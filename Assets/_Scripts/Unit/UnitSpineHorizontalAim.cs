using UnityEngine;

/// <summary>
/// Target follow on Spine_01 / Spine_02: yaw at idle (limit ±35°), plus pitch while combat-shoot walking.
/// Does not write equipped weapon local TRS — that is <see cref="UnitEquippedWeaponPose"/> BASE.
/// Within the yaw limit only the torso turns; at the limit the root recenters on the target and spine returns toward 0°.
/// Combat shoot walk (HipFire / PointAim / Aiming) leaves yaw to barrel-centric root facing; spine yaw goes to 0°.
/// HipFire walk pitch is a closed loop on the uncompensated clip (animator overwrites spine).
/// PointAim / Aiming walk: no spine pitch (vertical = AimPitch). HipFire walk holds a locomotion-neutral
/// barrel pitch, not the target elevation. Not Aim_Point and not weapon local.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
public sealed class UnitSpineHorizontalAim : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitGrenadeThrowController m_GrenadeThrowController;
	[SerializeField] private UnitFallenDragController m_FallenDragController;
	[SerializeField] private UnitFiremanCarryController m_FiremanCarryController;
	[SerializeField] private VehiclePassengerState m_VehiclePassengerState;
	[SerializeField] private RtsUnitMember m_RtsMember;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;

	[Header("Bones")]
	[SerializeField] private Transform m_Spine01;
	[SerializeField] private Transform m_Spine02;
	[Tooltip("Доля суммарного yaw на Spine_01 (остальное — Spine_02).")]
	[SerializeField, Range(0f, 1f)] private float m_Spine01Weight = 0.35f;

	[Header("Limits")]
	[Tooltip("Максимальный суммарный yaw торса относительно корня (градусы).")]
	[SerializeField, Range(5f, 70f)] private float m_MaxAbsorbDegrees = 35f;
	[Tooltip("Сглаживание поворота спины (сек). Экспонента без перелёта — SmoothDamp давал рывок туда-обратно.")]
	[SerializeField, Min(0.01f)] private float m_YawSmoothTime = 0.13f;
	[Tooltip("Пока |угол корень→цель| больше лимита — корень выравнивается на цель до этого порога.")]
	[SerializeField, Range(0.5f, 10f)] private float m_RecenterCompleteDegrees = 2.5f;
	[Tooltip("Не крутить спину во время StanceTransition.")]
	[SerializeField] private bool m_BlockDuringStanceTransition = true;

	[Header("Combat walk pitch")]
	[Tooltip("Максимальный pitch торса на шаге HipFireWalk (closed-loop к neutral pitch). PointAim/Aiming walk не крутят spine pitch.")]
	[SerializeField, Range(5f, 30f)] private float m_MaxHipFireWalkPitchDegrees = 18f;
	[Tooltip("Neutral world barrel pitch on HipFire crouch walk. Closed-loop holds this, not the target elevation.")]
	[SerializeField, Range(0f, 12f)] private float m_HipFireWalkBarrelLiftCrouch = 5f;
	[Tooltip("Neutral world barrel pitch on HipFire standing walk. Closed-loop holds this, not the target elevation.")]
	[SerializeField, Range(0f, 12f)] private float m_HipFireWalkBarrelLiftStand = 4f;
	[Tooltip("Максимальный yaw торса на стоячем HipFire-шаге, чтобы выровнять ствол с телом.")]
	[SerializeField, Range(5f, 25f)] private float m_MaxHipFireWalkYawDegrees = 14f;
	[SerializeField, Min(0.01f)] private float m_PitchSmoothTime = 0.13f;

	[Header("Debug")]
	[SerializeField] private bool m_DebugIsActive;
	[SerializeField] private bool m_DebugWantsRootRecenter;
	[SerializeField] private float m_DebugBodyToTargetYaw;
	[SerializeField] private float m_DebugSmoothedSpineYaw;
	[SerializeField] private float m_DebugSmoothedSpinePitch;
	#endregion

	#region Private Fields
	private float m_SmoothedSpineYaw;
	private float m_SmoothedSpinePitch;
	private bool m_IsRecentering;
	private bool m_BonesResolved;
	private float m_LastRootYaw;
	private bool m_HasLastRootYaw;
	#endregion

	#region Public Properties
	/// <summary>Спина управляет горизонталью engage (корень крутится только при recenter).</summary>
	public bool IsActive => m_DebugIsActive;

	/// <summary>Нужно выровнять корень на цель, чтобы спина вернулась к 0°.</summary>
	public bool WantsRootRecenter => m_DebugIsActive && m_IsRecentering;

	public float MaxAbsorbDegrees => m_MaxAbsorbDegrees;
	public float CurrentAbsorbedYawDegrees => m_SmoothedSpineYaw;
	public float CurrentAbsorbedPitchDegrees => m_SmoothedSpinePitch;
	public float BodyToTargetYawDegrees => m_DebugBodyToTargetYaw;
	public bool IsSaturated =>
		m_DebugIsActive && Mathf.Abs(m_SmoothedSpineYaw) >= m_MaxAbsorbDegrees - 0.5f;

	public string FormatFacingDebugLine()
	{
		if (m_DebugIsActive)
		{
			return $"spine=ON absorb={m_SmoothedSpineYaw:F1}/{m_MaxAbsorbDegrees:F0} " +
			       $"pitch={m_SmoothedSpinePitch:F1}/{m_MaxHipFireWalkPitchDegrees:F0} " +
			       $"recenter={(m_IsRecentering ? 1 : 0)} body↔target={m_DebugBodyToTargetYaw:F1}°";
		}

		TryGetSpineSkipReason(out string reason);
		return $"spine=off reason={reason} body↔target={m_DebugBodyToTargetYaw:F1}°";
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		ResolveBones();
	}

	private void OnDisable()
	{
		m_SmoothedSpineYaw = 0f;
		m_SmoothedSpinePitch = 0f;
		m_IsRecentering = false;
		m_HasLastRootYaw = false;
		m_DebugIsActive = false;
		m_DebugWantsRootRecenter = false;
		m_DebugBodyToTargetYaw = 0f;
		m_DebugSmoothedSpineYaw = 0f;
		m_DebugSmoothedSpinePitch = 0f;
	}

	private void Update()
	{
		// ClickToMove читает WantsRootRecenter в Update — флаг считаем до поворота корня.
		EvaluateRecenterFlag();
	}

	private void LateUpdate()
	{
		if (!m_BonesResolved)
			ResolveBones();
		if (!m_BonesResolved)
			return;

		// Корень уже повернулся в Update: снимаем этот yaw со спины, чтобы грудь не улетела вместе с ногами.
		CompensateSpineForRootYaw();
		EvaluateSpineYawAfterRootFacing();
		ApplySpineYaw(m_SmoothedSpineYaw);
		EvaluateSpinePitchForCombatWalk();
		ApplySpinePitch(m_SmoothedSpinePitch);
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		ResolveTargetSelector();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
		if (m_GrenadeThrowController == null)
			m_GrenadeThrowController = GetComponent<UnitGrenadeThrowController>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = GetComponent<UnitFiremanCarryController>();
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponent<VehiclePassengerState>();
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
	}

	private void ResolveTargetSelector()
	{
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
	}

	private void ResolveBones()
	{
		m_BonesResolved = false;
		if (m_Animator == null)
			return;

		if (m_Spine01 == null)
		{
			m_Spine01 = m_Animator.GetBoneTransform(HumanBodyBones.Spine);
			if (m_Spine01 == null)
				m_Spine01 = FindChildRecursive(transform, "Spine_01");
		}

		if (m_Spine02 == null)
		{
			m_Spine02 = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
			if (m_Spine02 == null)
				m_Spine02 = FindChildRecursive(transform, "Spine_02");
		}

		m_BonesResolved = m_Spine01 != null && m_Spine02 != null;
	}

	private void EvaluateRecenterFlag()
	{
		bool canAim = CanUseSpineAim(out float bodyToTargetYaw);
		m_DebugBodyToTargetYaw = bodyToTargetYaw;
		if (!canAim)
		{
			m_DebugIsActive = false;
			m_IsRecentering = false;
			m_DebugWantsRootRecenter = false;
			return;
		}

		m_DebugIsActive = true;
		// Barrel-centric walk keeps the body offset from the target on purpose.
		// Recenter would stick forever at ~18° body↔target and starve aimQuality.
		if (IsCombatShootWalkNow())
		{
			m_IsRecentering = false;
			m_DebugWantsRootRecenter = false;
			return;
		}

		UpdateRecenterState(bodyToTargetYaw);
		m_DebugWantsRootRecenter = m_IsRecentering;
	}

	private void EvaluateSpineYawAfterRootFacing()
	{
		bool canAim = CanUseSpineAim(out float bodyToTargetYaw);
		m_DebugBodyToTargetYaw = bodyToTargetYaw;
		if (!canAim || IsCombatShootWalkNow())
		{
			float walkSpineTarget = 0f;
			// Standing HipFire walk without a target: cancel clip yaw so the bore tracks the body.
			// Crouch keeps 0 — root already barrel-aligns when a target exists.
			// PointAim / Aiming walk with a target: spine stays 0; root owns barrel-centric yaw.
			if (IsHipFireWalkNow() &&
			    IsStandingNow() &&
			    !canAim &&
			    TryGetBodyBarrelYawOffset(out float bodyBarrelYaw))
			{
				float maxYaw = Mathf.Max(1f, m_MaxHipFireWalkYawDegrees);
				walkSpineTarget = Mathf.Clamp(-bodyBarrelYaw, -maxYaw, maxYaw);
			}

			SmoothSpineYawToward(walkSpineTarget);
			m_DebugSmoothedSpineYaw = m_SmoothedSpineYaw;
			return;
		}

		float maxAbsorb = Mathf.Max(1f, m_MaxAbsorbDegrees);
		m_SmoothedSpineYaw = Mathf.Clamp(m_SmoothedSpineYaw, -maxAbsorb, maxAbsorb);
		float spineTarget = Mathf.Clamp(bodyToTargetYaw, -maxAbsorb, maxAbsorb);
		SmoothSpineYawToward(spineTarget);
		m_DebugSmoothedSpineYaw = m_SmoothedSpineYaw;
	}

	private void CompensateSpineForRootYaw()
	{
		float rootYaw = transform.eulerAngles.y;
		if (!m_HasLastRootYaw)
		{
			m_LastRootYaw = rootYaw;
			m_HasLastRootYaw = true;
			return;
		}

		float rootDelta = Mathf.DeltaAngle(m_LastRootYaw, rootYaw);
		m_LastRootYaw = rootYaw;
		if (Mathf.Abs(rootDelta) < 0.0001f)
			return;

		// Peel root yaw off the torso only during idle recenter. Combat shoot walk yaws the
		// whole unit on purpose; without a target the peel lags ~8° and looks like a side pull.
		if (!m_DebugIsActive || IsCombatShootWalkNow())
			return;

		m_SmoothedSpineYaw = Mathf.DeltaAngle(0f, m_SmoothedSpineYaw - rootDelta);
	}

	private bool IsHipFireWalkNow()
	{
		return UnitHorizontalFacingUtility.IsHipFireWalk(
			m_ReadyHands,
			m_Animator,
			IsRunOrSprintActive());
	}

	private bool IsCombatShootWalkNow()
	{
		return UnitHorizontalFacingUtility.IsCombatShootWalk(
			m_ReadyHands,
			m_Animator,
			IsRunOrSprintActive());
	}

	private bool CanUseSpineAim(out float _bodyToTargetYaw)
	{
		_bodyToTargetYaw = 0f;
		if (TryGetSpineSkipReason(out _))
			return false;

		if (m_TargetSelector == null ||
		    !m_TargetSelector.TryGetEngageableAimPointWorld(out Vector3 aimPoint))
			return false;

		Vector3 toTarget = aimPoint - transform.position;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude < 1e-6f)
			return false;

		Vector3 bodyFwd = transform.forward;
		bodyFwd.y = 0f;
		if (bodyFwd.sqrMagnitude < 1e-6f)
			return false;

		bodyFwd.Normalize();
		_bodyToTargetYaw = Vector3.SignedAngle(bodyFwd, toTarget.normalized, Vector3.up);
		return true;
	}

	private bool TryGetSpineSkipReason(out string _reason)
	{
		ResolveTargetSelector();
		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
		{
			_reason = "ragdoll";
			return true;
		}
		if (m_VehiclePassengerState != null && m_VehiclePassengerState.IsVehicleReady)
		{
			_reason = "vehicle";
			return true;
		}
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
		{
			_reason = "drag";
			return true;
		}
		if (m_FiremanCarryController != null && m_FiremanCarryController.IsCarryingFallen)
		{
			_reason = "carry";
			return true;
		}
		if (m_GrenadeThrowController != null &&
		    (m_GrenadeThrowController.IsAiming || m_GrenadeThrowController.IsThrowAnimPlaying))
		{
			_reason = "grenade";
			return true;
		}
		if (m_BlockDuringStanceTransition &&
		    m_BusyState != null &&
		    m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
		{
			_reason = "stance";
			return true;
		}
		if (m_Stance != null && m_Stance.CurrentStance == LocomotionStance.Prone)
		{
			_reason = "prone";
			return true;
		}
		if (m_RtsMember != null &&
		    m_RtsMember.IsRotatingToRouteFacing &&
		    !m_RtsMember.ShouldYieldRouteFacingToCombatTarget)
		{
			_reason = "routeFacing";
			return true;
		}
		if (m_RtsMember != null && m_RtsMember.IsManualBarrelFacingActive)
		{
			_reason = "manualBarrel";
			return true;
		}
		if (IsRunOrSprintActive())
		{
			_reason = "runSprint";
			return true;
		}
		if (m_ReadyHands == null)
		{
			_reason = "noReadyHands";
			return true;
		}
		if (!m_ReadyHands.WantsCombatTargetFacing())
		{
			_reason = $"pose={m_ReadyHands.EffectivePoseState}(notRaised)";
			return true;
		}
		if (m_TargetSelector == null)
		{
			_reason = "noSelector";
			return true;
		}
		if (m_TargetSelector.SelectedTarget == null)
		{
			_reason = "noTarget";
			return true;
		}
		if (!m_BonesResolved && m_Animator != null)
			ResolveBones();
		if (!m_BonesResolved)
		{
			_reason = "noBones";
			return true;
		}

		if (m_TargetSelector == null ||
		    !m_TargetSelector.TryGetEngageableAimPointWorld(out Vector3 aimPoint))
		{
			_reason = "noAimPoint";
			return true;
		}

		Vector3 toTarget = aimPoint - transform.position;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude < 1e-6f)
		{
			_reason = "noAimPoint";
			return true;
		}

		Vector3 bodyFwd = transform.forward;
		bodyFwd.y = 0f;
		if (bodyFwd.sqrMagnitude < 1e-6f)
		{
			_reason = "noBodyFwd";
			return true;
		}

		_reason = "ok";
		return false;
	}

	private void UpdateRecenterState(float _bodyToTargetYaw)
	{
		float maxAbsorb = Mathf.Max(1f, m_MaxAbsorbDegrees);
		float complete = Mathf.Clamp(m_RecenterCompleteDegrees, 0.5f, maxAbsorb);

		if (m_IsRecentering)
		{
			if (Mathf.Abs(_bodyToTargetYaw) <= complete)
				m_IsRecentering = false;
			return;
		}

		if (Mathf.Abs(_bodyToTargetYaw) > maxAbsorb)
			m_IsRecentering = true;
	}

	private void SmoothSpineYawToward(float _targetYaw)
	{
		float smooth = Mathf.Max(0.0001f, m_YawSmoothTime);
		float t = 1f - Mathf.Exp(-Time.deltaTime / smooth);
		m_SmoothedSpineYaw = Mathf.LerpAngle(m_SmoothedSpineYaw, _targetYaw, t);
	}

	private void ApplySpineYaw(float _totalYawDegrees)
	{
		if (Mathf.Abs(_totalYawDegrees) < 0.0001f)
			return;

		float w1 = Mathf.Clamp01(m_Spine01Weight);
		float yaw1 = _totalYawDegrees * w1;
		float yaw2 = _totalYawDegrees * (1f - w1);

		if (m_Spine01 != null && Mathf.Abs(yaw1) > 0.0001f)
			m_Spine01.rotation = Quaternion.AngleAxis(yaw1, Vector3.up) * m_Spine01.rotation;
		if (m_Spine02 != null && Mathf.Abs(yaw2) > 0.0001f)
			m_Spine02.rotation = Quaternion.AngleAxis(yaw2, Vector3.up) * m_Spine02.rotation;
	}

	private void EvaluateSpinePitchForCombatWalk()
	{
		float targetPitch = 0f;
		// PointAim/Aiming walk: spine pitch stays 0 (vertical is AimPitch). Pose blend skip
		// keeps PreAim muzzle-down from fighting the Aiming raise.
		if (!IsReloadBusyNow() && !IsPoseBlendAnimatingNow() &&
		    IsHipFireWalkNow() && TryGetBarrelWorldPitchDegrees(out float barrelPitch))
		{
			float maxPitch = Mathf.Max(1f, m_MaxHipFireWalkPitchDegrees);
			float desiredPitch = GetHipFireWalkNeutralPitchDegrees();
			targetPitch = Mathf.Clamp(desiredPitch - barrelPitch, -maxPitch, maxPitch);
		}

		SmoothSpinePitchToward(targetPitch);
		m_DebugSmoothedSpinePitch = m_SmoothedSpinePitch;
	}

	private bool IsPoseBlendAnimatingNow()
	{
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		return m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;
	}

	private float GetHipFireWalkNeutralPitchDegrees()
	{
		return IsCrouchNow() ? m_HipFireWalkBarrelLiftCrouch : m_HipFireWalkBarrelLiftStand;
	}

	private bool IsStandingNow() =>
		m_Stance == null || m_Stance.CurrentStance == LocomotionStance.Standing;

	private bool IsCrouchNow() =>
		m_Stance != null && m_Stance.CurrentStance == LocomotionStance.Crouch;

	private bool TryGetBodyBarrelYawOffset(out float _offsetDegrees)
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		return UnitHorizontalFacingUtility.TryGetBodyBarrelYawOffset(transform, m_Equipment, out _offsetDegrees);
	}

	private bool TryGetBarrelWorldPitchDegrees(out float _barrelPitch)
	{
		_barrelPitch = 0f;
		if (!TryGetBarrelTransform(out Transform barrel))
			return false;

		Vector3 barrelFwd = barrel.forward;
		if (barrelFwd.sqrMagnitude < 1e-6f)
			return false;

		float barrelHoriz = Mathf.Sqrt(barrelFwd.x * barrelFwd.x + barrelFwd.z * barrelFwd.z);
		_barrelPitch = Mathf.Atan2(barrelFwd.y, Mathf.Max(1e-6f, barrelHoriz)) * Mathf.Rad2Deg;
		return true;
	}

	private bool TryGetBarrelTransform(out Transform _barrel)
	{
		_barrel = null;
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_Equipment == null)
			return false;

		EquippedWeapon weapon = m_Equipment.EquippedWeapon;
		if (weapon == null)
			return false;

		_barrel = weapon.BarrelTransform != null ? weapon.BarrelTransform : weapon.FireOriginTransform;
		return _barrel != null;
	}

	private void SmoothSpinePitchToward(float _targetPitch)
	{
		float smooth = Mathf.Max(0.0001f, m_PitchSmoothTime);
		float t = 1f - Mathf.Exp(-Time.deltaTime / smooth);
		m_SmoothedSpinePitch = Mathf.Lerp(m_SmoothedSpinePitch, _targetPitch, t);
	}

	private void ApplySpinePitch(float _totalPitchDegrees)
	{
		if (Mathf.Abs(_totalPitchDegrees) < 0.0001f)
			return;

		Vector3 pitchAxis = transform.right;
		pitchAxis.y = 0f;
		if (pitchAxis.sqrMagnitude < 1e-6f)
			return;
		pitchAxis.Normalize();

		float w1 = Mathf.Clamp01(m_Spine01Weight);
		float pitch1 = _totalPitchDegrees * w1;
		float pitch2 = _totalPitchDegrees * (1f - w1);

		if (m_Spine01 != null && Mathf.Abs(pitch1) > 0.0001f)
			m_Spine01.rotation = Quaternion.AngleAxis(pitch1, pitchAxis) * m_Spine01.rotation;
		if (m_Spine02 != null && Mathf.Abs(pitch2) > 0.0001f)
			m_Spine02.rotation = Quaternion.AngleAxis(pitch2, pitchAxis) * m_Spine02.rotation;
	}

	private bool IsReloadBusyNow()
	{
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		return m_ReloadController != null && m_ReloadController.IsReloadBusy;
	}

	private bool IsRunOrSprintActive()
	{
		if (m_ClickToMove != null && m_ClickToMove.isActiveAndEnabled)
			return m_ClickToMove.IsRunMoveMode || m_ClickToMove.IsSprintMoveMode;
		if (m_LocomotionDriver != null && m_LocomotionDriver.isActiveAndEnabled)
			return m_LocomotionDriver.IsRunMoveMode || m_LocomotionDriver.IsSprintMoveMode;
		return false;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}
