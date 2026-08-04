using UnityEngine;

/// <summary>
/// IK левой и правой кисти к целям на экипированном оружии.
/// Правая рука: координаты relaxed/ready из <see cref="ItemDefinition"/> + вес по <see cref="UnitEquippedWeaponPose"/>.
/// Компонент на том же GameObject, что <see cref="Animator"/> (Humanoid). В Animator Controller нужен <b>IK Pass</b>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(250)]
public class AnimatorHandIk : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение на корне юнита (родитель или сам юнит с CharacterInventory).")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Play Mode pose/IK tuner on the unit.")]
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[Tooltip("Поза оружия relaxed/ready; вес IK правой руки берётся отсюда.")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[Tooltip("Пока идёт ручная зарядка магазина (T), IK рук отключается.")]
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoading;
	[Tooltip("Пока идёт перезарядка оружия (R), IK рук отключается.")]
	[SerializeField] private UnitWeaponReloadController m_WeaponReload;
	[Tooltip("Пока идёт самостабилизация IFAK, IK рук отключается.")]
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[Tooltip("Пока идёт стабилизация другого юнита, IK рук отключается.")]
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[Tooltip("Пока юнит тащит сражённого, IK рук отключается (рука уходит на drag-слой).")]
	[SerializeField] private UnitBusyState m_BusyState;
	[Tooltip("Пока идёт бросок гранаты, IK рук отключается.")]
	[SerializeField] private UnitGrenadeThrowController m_GrenadeThrow;
	[Tooltip("Пока идёт приказ гранатомёта, IK рук отключается.")]
	[SerializeField] private UnitRocketLauncherOrderController m_RocketLauncherOrder;
	[SerializeField] private UnitVehicleTurretReloadEvents m_TurretReloadEvents;
	[Tooltip("Контроллер отдачи — ApplyHandKick к IK правой кисти.")]
	[SerializeField] private UnitWeaponRecoil m_WeaponRecoil;
	[Tooltip("Драйвер клика для движения. При беге IK правой руки отключается.")]
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[Tooltip("NavMesh драйвер локомоции. При беге IK правой руки отключается.")]
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[Tooltip("Состояние пассажира в машине. На fire-capable месте — Vehicle поля ItemDefinition (NotReady/Ready через blend).")]
	[SerializeField] private VehiclePassengerState m_VehiclePassengerState;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_LeftHandRotationWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_RightHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float m_RightHandRotationWeight = 1f;
	[Tooltip("Right-hand IK weight in low ready (not ready). Use 1 so saved RightHandIkNotReady coords apply; 0 = animation only.")]
	[SerializeField, Range(0f, 1f)] private float m_RightHandNotReadyIkWeight = 1f;
	[Header("Экипировка")]
	[SerializeField, Min(0f)] private float m_EquipBlendDuration = 0.35f;
	[SerializeField] private AnimationCurve m_EquipBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	[Header("Локоть (подсказка IK)")]
	[SerializeField] private bool m_UseLeftElbowHint;
	[SerializeField] private Transform m_LeftElbowHint;
	[SerializeField, Range(0f, 1f)] private float m_LeftElbowHintWeight = 1f;
	[Header("Отладка")]
	[SerializeField] private bool m_DrawIkTargetGizmo;
	[SerializeField] private bool m_LogProximityIk;
	#endregion

	#region Private Fields
	private Animator m_Animator;
	private bool m_ClearHandIkOnNextAnimatorIkPass;
	private bool m_IsEquipBlendActive;
	private float m_EquipBlendElapsed;
	private int m_LastEquipBlendAdvanceFrame = -1;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Animator = GetComponent<Animator>();
		ResolveReferences();
	}

	private void OnEnable()
	{
		SubscribeEquipmentEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		StopEquipBlend();
	}

	private void LateUpdate()
	{
		if (m_TurretReloadEvents == null)
			m_TurretReloadEvents = GetComponentInParent<UnitVehicleTurretReloadEvents>();

		// OnAnimatorIK can be skipped/overwritten by higher animator layers.
		// Turret gunner: snap hands to pitch/gun IK empties after animation.
		if (!IsOperatingVehicleTurretIk() || m_Animator == null || !m_Animator.enabled)
			return;
		if (IsHandIkBlocked())
			return;

		if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadAnimationActive)
		{
			bool reloadUseNotReady = m_TurretReloadEvents.UseNotReadyIkTargets;
			bool skipHandleSnap = m_TurretReloadEvents.UseHandleNotReadyIkTargets;
			if (m_TurretReloadEvents.UseLeftHandIk && !skipHandleSnap)
				SnapHandBoneToTurretIk(HumanBodyBones.LeftHand, _leftHand: true, reloadUseNotReady);
			if (m_TurretReloadEvents.UseRightHandIk && !skipHandleSnap)
				SnapHandBoneToTurretIk(HumanBodyBones.RightHand, _leftHand: false, reloadUseNotReady);
			return;
		}

		if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadBusy)
		{
			SnapHandBoneToTurretIk(HumanBodyBones.LeftHand, _leftHand: true, _useNotReady: false);
			SnapHandBoneToTurretIk(HumanBodyBones.RightHand, _leftHand: false, _useNotReady: false);
			return;
		}

		bool useNotReady = m_TurretReloadEvents != null && m_TurretReloadEvents.UseNotReadyIkTargets;
		SnapHandBoneToTurretIk(HumanBodyBones.LeftHand, _leftHand: true, useNotReady);
		SnapHandBoneToTurretIk(HumanBodyBones.RightHand, _leftHand: false, useNotReady);
	}

	private void OnDrawGizmosSelected()
	{
		if (!m_DrawIkTargetGizmo || !Application.isPlaying)
			return;

		if (TryResolveLeftHandIkWorldPose(out Vector3 leftPos, out Quaternion leftRot))
		{
			Gizmos.color = new Color(0.2f, 0.95f, 1f, 0.95f);
			Gizmos.DrawSphere(leftPos, 0.015f);
			Gizmos.DrawLine(leftPos, leftPos + leftRot * Vector3.forward * 0.06f);
		}

		if (TryResolveRightHandIkWorldPose(out Vector3 rightPos, out Quaternion rightRot))
		{
			Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.95f);
			Gizmos.DrawSphere(rightPos, 0.015f);
			Gizmos.DrawLine(rightPos, rightPos + rightRot * Vector3.forward * 0.06f);
		}
	}
	#endregion

	#region Public Methods
	public void OnWeaponReadyStateChanged()
	{
		if (IsHandIkBlocked())
			m_ClearHandIkOnNextAnimatorIkPass = true;
	}

	public void OnWeaponReadyStateApplied()
	{
		OnWeaponReadyStateChanged();
	}

	public void RequestClearLeftHandIk()
	{
		StopEquipBlend();
		m_ClearHandIkOnNextAnimatorIkPass = true;
	}
	#endregion

	#region IK
	private void OnAnimatorIK(int _layerIndex)
	{
		if (m_Animator == null)
			return;

		if (m_TurretReloadEvents == null)
			m_TurretReloadEvents = GetComponentInParent<UnitVehicleTurretReloadEvents>();

		if (m_ClearHandIkOnNextAnimatorIkPass)
		{
			m_ClearHandIkOnNextAnimatorIkPass = false;
			StopEquipBlend();
			ClearLeftHandIk();
			ClearRightHandIk();
		}

		if (ShouldDisableAllHandIkForTuning())
		{
			StopEquipBlend();
			ClearLeftHandIk();
			ClearRightHandIk();
			return;
		}

		if (ShouldUseBoltCycleLeftHandHoldIk())
		{
			StopEquipBlend();
			ApplyLeftHandIkInternal();
			ClearRightHandIk();
			return;
		}

		if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadAnimationActive)
		{
			bool useLeft = m_TurretReloadEvents.UseLeftHandIk;
			bool useRight = m_TurretReloadEvents.UseRightHandIk;

			if (useLeft)
				ApplyLeftHandIkInternal();
			else
			{
				StopEquipBlend();
				ClearLeftHandIk();
			}

			if (useRight)
				ApplyRightHandIkInternal();
			else
				ClearRightHandIk();

			return;
		}

		if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadBusy)
		{
			ApplyLeftHandIkInternal();
			ApplyRightHandIkInternal();
			return;
		}

		if (IsHandIkBlocked())
		{
			StopEquipBlend();
			ClearLeftHandIk();
			ClearRightHandIk();
			return;
		}

		if (m_RocketLauncherOrder != null && m_RocketLauncherOrder.IsBusy)
		{
			if (m_RocketLauncherOrder.ShouldUseLeftHandIk)
				ApplyLeftHandIkInternal();
			else
			{
				StopEquipBlend();
				ClearLeftHandIk();
			}

			if (m_RocketLauncherOrder.ShouldUseRightHandIk)
				ApplyRightHandIkInternal();
			else
				ClearRightHandIk();

			return;
		}

		ApplyLeftHandIkInternal();
		if (IsRunningNow())
			ClearRightHandIk();
		else
			ApplyRightHandIkInternal();
	}

	private bool ShouldUseBoltCycleLeftHandHoldIk()
	{
		return m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle;
	}

	private bool ShouldDisableAllHandIkForTuning()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.ShouldDisableAllHandIk;
	}

	private bool IsRunningNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.IsRunMoveMode)
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.IsRunMoveMode)
			return true;
		return false;
	}

	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_MagazineLoading == null)
			m_MagazineLoading = GetComponentInParent<UnitMagazineLoadingController>();
		if (m_WeaponReload == null)
			m_WeaponReload = GetComponentInParent<UnitWeaponReloadController>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponentInParent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponentInParent<UnitStabilizeOtherController>();
		if (m_GrenadeThrow == null)
			m_GrenadeThrow = GetComponentInParent<UnitGrenadeThrowController>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponentInParent<UnitRocketLauncherOrderController>();
		if (m_TurretReloadEvents == null)
			m_TurretReloadEvents = GetComponentInParent<UnitVehicleTurretReloadEvents>();
		if (m_BusyState == null)
			m_BusyState = GetComponentInParent<UnitBusyState>();
		if (m_Stance == null)
			m_Stance = GetComponentInParent<UnitAnimatorStance>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponentInParent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponentInParent<UnitNavLocomotionDriver>();

		if (m_WeaponRecoil == null)
			m_WeaponRecoil = GetComponentInParent<UnitWeaponRecoil>();

		EnsureVehiclePassengerState();
	}

	private bool IsHandIkBlocked()
	{
		if (m_MagazineLoading != null && m_MagazineLoading.IsLoadingMagazine)
			return true;
		if (m_WeaponReload != null && (m_WeaponReload.IsReloadingWeapon || m_WeaponReload.IsCyclingBolt || m_WeaponReload.IsLoadingLmgBelt))
			return true;
		if (m_SelfStabilization != null && m_SelfStabilization.IsHealPresentationActive)
			return true;
		if (m_StabilizeOther != null && m_StabilizeOther.IsHealPresentationActive)
			return true;
		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.DraggingFallen))
			return true;
		if (m_GrenadeThrow != null && m_GrenadeThrow.IsThrowAnimPlaying)
		{
			m_ClearHandIkOnNextAnimatorIkPass = true;
			return true;
		}

		return false;
	}

	private void ClearLeftHandIk()
	{
		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
		m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private void ClearRightHandIk()
	{
		m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
	}

	private void SubscribeEquipmentEvents()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
	}

	private void UnsubscribeEquipmentEvents()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
	}

	private void HandleEquipmentChanged()
	{
		if (IsHandIkBlocked() || !TryResolveLeftHandIkWorldPose(out _, out _))
		{
			StopEquipBlend();
			return;
		}

		StartEquipBlend();
	}

	private void StartEquipBlend()
	{
		if (m_EquipBlendDuration <= 0f)
		{
			StopEquipBlend();
			return;
		}

		m_IsEquipBlendActive = true;
		m_EquipBlendElapsed = 0f;
		m_LastEquipBlendAdvanceFrame = -1;
	}

	private void StopEquipBlend()
	{
		m_IsEquipBlendActive = false;
		m_EquipBlendElapsed = 0f;
		m_LastEquipBlendAdvanceFrame = -1;
	}

	private float GetEquipBlendMultiplier()
	{
		if (!m_IsEquipBlendActive)
			return 1f;

		if (m_LastEquipBlendAdvanceFrame != Time.frameCount)
		{
			m_LastEquipBlendAdvanceFrame = Time.frameCount;
			m_EquipBlendElapsed += Time.deltaTime;
		}

		if (m_EquipBlendElapsed >= m_EquipBlendDuration)
		{
			StopEquipBlend();
			return 1f;
		}

		float normalizedTime = m_EquipBlendDuration > 0f
			? Mathf.Clamp01(m_EquipBlendElapsed / m_EquipBlendDuration)
			: 1f;

		if (m_EquipBlendCurve != null && m_EquipBlendCurve.length > 0)
			return m_EquipBlendCurve.Evaluate(normalizedTime);

		return Mathf.SmoothStep(0f, 1f, normalizedTime);
	}

	private float GetTurretHandIkBlendMultiplier() => 1f;

	private float GetEffectiveReadyBlend01()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
			return m_RuntimeTuner.ForcedReadyBlend01;

		// Rocket launcher in hands always uses Ready IK targets (Not Ready IK is unused for launchers).
		if (m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose)
			return 1f;

		if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadBusy &&
		    (m_TurretReloadEvents.UseNotReadyIkTargets || m_TurretReloadEvents.UseHandleNotReadyIkTargets))
			return 0f;

		return m_EquippedWeaponPose != null
			? Mathf.Clamp01(m_EquippedWeaponPose.ReadyPoseBlend01)
			: 0f;
	}

	private float GetRightHandIkWeightMultiplier()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.ForcesRightHandIk)
			return 1f;

		if (IsOperatingVehicleTurretIk())
			return 1f;

		if (m_EquippedWeaponPose == null && (m_UnitEquipment == null || !m_UnitEquipment.IsOperatingVehicleTurret))
			return 0f;

		float readyBlend = GetEffectiveReadyBlend01();
		return Mathf.Lerp(m_RightHandNotReadyIkWeight, 1f, readyBlend);
	}

	private void ApplyLeftHandIkInternal()
	{
		if (!TryResolveLeftHandIkWorldPose(out Vector3 position, out Quaternion rotation))
		{
			StopEquipBlend();
			ClearLeftHandIk();
			return;
		}

		float blend = IsOperatingVehicleTurretIk() ? GetTurretHandIkBlendMultiplier() : GetEquipBlendMultiplier();
		float positionWeight = m_LeftHandPositionWeight * blend;
		float rotationWeight = m_LeftHandRotationWeight * blend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, rotation);

		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight * blend);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private void ApplyRightHandIkInternal()
	{
		bool useRocketLauncher = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;

		if (!useRocketLauncher &&
		    TryResolveTurretHandIkWorldPose(
			    _leftHand: false,
			    _useNotReadyTargets: m_TurretReloadEvents != null && m_TurretReloadEvents.UseNotReadyIkTargets,
			    out Vector3 turretPos,
			    out Quaternion turretRot))
		{
			ApplyRightHandIkDirect(turretPos, turretRot);
			return;
		}

		Transform weaponRoot = useRocketLauncher
			? m_RocketLauncherOrder.HandLauncherRoot
			: m_UnitEquipment != null ? m_UnitEquipment.EffectiveWeaponRoot : null;
		if (weaponRoot == null || !weaponRoot.gameObject.activeInHierarchy)
		{
			ClearRightHandIk();
			return;
		}

		float readyBlend = GetEffectiveReadyBlend01();
		ItemDefinition equipped = useRocketLauncher
			? m_RocketLauncherOrder.ActiveLauncherDefinition
			: m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;
		if (equipped == null)
		{
			ClearRightHandIk();
			return;
		}

		if (!TryResolveRightHandIkLocalPose(equipped, weaponRoot, readyBlend, GetCurrentStance(), out Vector3 localPos, out Quaternion localRot))
		{
			ClearRightHandIk();
			return;
		}

		m_WeaponRecoil?.ApplyHandKick(ref localPos, ref localRot);

		Vector3 worldPos = weaponRoot.TransformPoint(localPos);
		Quaternion worldRot = weaponRoot.rotation * localRot;

		ApplyRightHandIkDirect(worldPos, worldRot);
	}

	private void ApplyRightHandIkDirect(Vector3 worldPos, Quaternion worldRot)
	{
		float ikBlend = GetRightHandIkWeightMultiplier();
		if (ikBlend <= 0f)
		{
			ClearRightHandIk();
			return;
		}

		float turretBlend = IsOperatingVehicleTurretIk() ? GetTurretHandIkBlendMultiplier() : 1f;
		float positionWeight = m_RightHandPositionWeight * ikBlend * turretBlend;
		float rotationWeight = m_RightHandRotationWeight * ikBlend * turretBlend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.RightHand, worldPos);
		m_Animator.SetIKRotation(AvatarIKGoal.RightHand, worldRot);
	}

	private bool TryResolveRightHandIkWorldPose(out Vector3 _position, out Quaternion _rotation)
	{
		_position = Vector3.zero;
		_rotation = Quaternion.identity;

		bool useRocketLauncher = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;
		if (!useRocketLauncher && m_UnitEquipment == null)
			return false;

		Transform weaponRoot = useRocketLauncher
			? m_RocketLauncherOrder.HandLauncherRoot
			: m_UnitEquipment.EffectiveWeaponRoot;
		if (weaponRoot == null || !weaponRoot.gameObject.activeInHierarchy)
			return false;

		ItemDefinition equipped = useRocketLauncher
			? m_RocketLauncherOrder.ActiveLauncherDefinition
			: m_UnitEquipment.EquippedDefinition;
		if (equipped == null)
			return false;

		float readyBlend = GetEffectiveReadyBlend01();

		if (!TryResolveRightHandIkLocalPose(equipped, weaponRoot, readyBlend, GetCurrentStance(), out Vector3 localPosition, out Quaternion localRotation))
			return false;

		_position = weaponRoot.TransformPoint(localPosition);
		_rotation = weaponRoot.rotation * localRotation;
		return true;
	}

	private bool TryResolveRightHandIkLocalPose(
		ItemDefinition _equipped,
		Transform _weaponRoot,
		float _readyBlend01,
		LocomotionStance _stance,
		out Vector3 _localPosition,
		out Quaternion _localRotation)
	{
		_localPosition = Vector3.zero;
		_localRotation = Quaternion.identity;

		bool inVehicle = IsInVehiclePassengerIkContext();

		Vector3 notReadyLocalPosition;
		Quaternion notReadyLocalRotation;
		Vector3 readyLocalPosition;
		Quaternion readyLocalRotation;

		if (inVehicle)
		{
			notReadyLocalPosition = _equipped.ResolveVehicleRightHandIkNotReadyLocalPosition();
			notReadyLocalRotation = _equipped.ResolveVehicleRightHandIkNotReadyLocalRotation();
			readyLocalPosition = _equipped.ResolveVehicleRightHandIkReadyLocalPosition();
			readyLocalRotation = _equipped.ResolveVehicleRightHandIkReadyLocalRotation();
		}
		else
		{
			notReadyLocalPosition = _equipped.ResolveRightHandIkNotReadyLocalPosition(_stance);
			notReadyLocalRotation = _equipped.ResolveRightHandIkNotReadyLocalRotation(_stance);
			readyLocalPosition = _equipped.ResolveRightHandIkReadyLocalPosition(_stance);
			readyLocalRotation = _equipped.ResolveRightHandIkReadyLocalRotation(_stance);
		}

		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			// Live Hierarchy transforms — user moves RightHandIkTarget* in Scene.
			Transform notReadyChild = GetRightHandIkTargetNotReadyTransform();
			if (notReadyChild != null)
			{
				notReadyLocalPosition = _weaponRoot.InverseTransformPoint(notReadyChild.position);
				notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * notReadyChild.rotation;
			}

			Transform readyChild = GetRightHandIkTargetTransform();
			if (readyChild != null)
			{
				readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
				readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
			}
		}
		else
		{
			Vector3 notReadyEulerCheck = inVehicle
				? _equipped.ResolveVehicleRightHandIkNotReadyLocalEulerAngles()
				: _equipped.ResolveRightHandIkNotReadyLocalEulerAngles(_stance);

			if (!HasConfiguredIkLocalPose(notReadyLocalPosition, notReadyEulerCheck))
			{
				Transform notReadyChild = GetRightHandIkTargetNotReadyTransform();
				if (notReadyChild != null)
				{
					notReadyLocalPosition = _weaponRoot.InverseTransformPoint(notReadyChild.position);
					notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * notReadyChild.rotation;
				}
			}

			Vector3 readyEulerCheck = inVehicle
				? _equipped.ResolveVehicleRightHandIkReadyLocalEulerAngles()
				: _equipped.ResolveRightHandIkReadyLocalEulerAngles(_stance);

			if (!HasConfiguredIkLocalPose(readyLocalPosition, readyEulerCheck))
			{
				Transform readyChild = GetRightHandIkTargetTransform();
				if (readyChild != null)
				{
					readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
					readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
				}
			}
		}

		_localPosition = Vector3.Lerp(notReadyLocalPosition, readyLocalPosition, _readyBlend01);
		_localRotation = Quaternion.Slerp(notReadyLocalRotation, readyLocalRotation, _readyBlend01);
		return true;
	}

	private static bool HasConfiguredIkLocalPose(Vector3 _localPosition, Vector3 _localEulerAngles)
	{
		return _localPosition != Vector3.zero || _localEulerAngles != Vector3.zero;
	}

	private bool TryResolveLeftHandIkWorldPose(out Vector3 _position, out Quaternion _rotation)
	{
		_position = Vector3.zero;
		_rotation = Quaternion.identity;

		if (TryResolveTurretHandIkWorldPose(
			    _leftHand: true,
			    _useNotReadyTargets: m_TurretReloadEvents != null && m_TurretReloadEvents.UseNotReadyIkTargets,
			    out _position,
			    out _rotation))
			return true;

		bool useRocketLauncher = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;
		if (!useRocketLauncher && m_UnitEquipment == null)
			return false;

		Transform weaponRoot = useRocketLauncher
			? m_RocketLauncherOrder.HandLauncherRoot
			: m_UnitEquipment.EffectiveWeaponRoot;
		if (weaponRoot == null || !weaponRoot.gameObject.activeInHierarchy)
			return false;

		ItemDefinition equipped = useRocketLauncher
			? m_RocketLauncherOrder.ActiveLauncherDefinition
			: m_UnitEquipment.EquippedDefinition;
		if (equipped == null)
			return false;

		EquippedWeapon equippedWeapon = !useRocketLauncher && m_UnitEquipment != null
			? m_UnitEquipment.EquippedWeapon
			: null;
		Transform foregripRoot = equippedWeapon != null ? equippedWeapon.UnderBarrelForegripVisualRoot : null;

		Transform readyChild = GetLeftHandIkTargetTransform();
		Transform notReadyChild = GetLeftHandIkTargetNotReadyTransform();

		float readyBlend = GetEffectiveReadyBlend01();

		// When foregrip provides IK targets, snap directly to world-space transforms —
		// no weapon-local roundtrip, no authored data interpolation.
		bool tuning = m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
		bool foregripHasTargets = foregripRoot != null
		                         && notReadyChild != null && IsUnderOrSame(foregripRoot, notReadyChild)
		                         && readyChild != null && IsUnderOrSame(foregripRoot, readyChild);

		if (foregripHasTargets && tuning)
		{
			_position = Vector3.Lerp(notReadyChild.position, readyChild.position, readyBlend);
			_rotation = Quaternion.Slerp(notReadyChild.rotation, readyChild.rotation, readyBlend);
			return true;
		}

		int fgIndex = GetForegripIndex(equippedWeapon);
		LocomotionStance stance = GetCurrentStance();
		bool inVehicleIk = IsInVehiclePassengerIkContext();

		if (fgIndex >= 1 && fgIndex <= 5 && foregripRoot != null)
		{
			bool useFg = false;
			Vector3 notReadyLocal = Vector3.zero, readyLocal = Vector3.zero;
			Quaternion notReadyLocalRot = Quaternion.identity, readyLocalRot = Quaternion.identity;

			if (inVehicleIk && equipped.HasVehicleForeGripIkConfigured(fgIndex))
			{
				notReadyLocal = equipped.GetVehicleForeGripLeftHandIkNotReadyLocalPosition(fgIndex);
				notReadyLocalRot = equipped.GetVehicleForeGripLeftHandIkNotReadyLocalRotation(fgIndex);
				readyLocal = equipped.GetVehicleForeGripLeftHandIkReadyLocalPosition(fgIndex);
				readyLocalRot = equipped.GetVehicleForeGripLeftHandIkReadyLocalRotation(fgIndex);
				useFg = true;
			}
			else if (stance == LocomotionStance.Crouch && equipped.HasCrouchForeGripIkConfigured(fgIndex))
			{
				notReadyLocal = equipped.GetCrouchForeGripLeftHandIkNotReadyLocalPosition(fgIndex);
				notReadyLocalRot = equipped.GetCrouchForeGripLeftHandIkNotReadyLocalRotation(fgIndex);
				readyLocal = equipped.GetCrouchForeGripLeftHandIkReadyLocalPosition(fgIndex);
				readyLocalRot = equipped.GetCrouchForeGripLeftHandIkReadyLocalRotation(fgIndex);
				useFg = true;
			}
			else if (equipped.HasForeGripIkConfigured(fgIndex))
			{
				notReadyLocal = equipped.GetForeGripLeftHandIkNotReadyLocalPosition(fgIndex);
				notReadyLocalRot = equipped.GetForeGripLeftHandIkNotReadyLocalRotation(fgIndex);
				readyLocal = equipped.GetForeGripLeftHandIkReadyLocalPosition(fgIndex);
				readyLocalRot = equipped.GetForeGripLeftHandIkReadyLocalRotation(fgIndex);
				useFg = true;
			}

			if (useFg)
			{
				Vector3 localPos = Vector3.Lerp(notReadyLocal, readyLocal, readyBlend);
				Quaternion localRot = Quaternion.Slerp(notReadyLocalRot, readyLocalRot, readyBlend);
				Transform fgRoot = foregripRoot;
				_position = fgRoot.TransformPoint(localPos);
				_rotation = fgRoot.rotation * localRot;
				return true;
			}
		}

		if (!TryResolveLeftHandIkLocalPose(equipped, weaponRoot, readyBlend, GetCurrentStance(), out Vector3 localPosition, out Quaternion localRotation))
			return false;

		_position = weaponRoot.TransformPoint(localPosition);
		_rotation = weaponRoot.rotation * localRotation;
		return true;
	}

	private bool TryResolveLeftHandIkLocalPose(
		ItemDefinition _equipped,
		Transform _weaponRoot,
		float _readyBlend01,
		LocomotionStance _stance,
		out Vector3 _localPosition,
		out Quaternion _localRotation)
	{
		_localPosition = Vector3.zero;
		_localRotation = Quaternion.identity;

		Transform readyChild = GetLeftHandIkTargetTransform();
		Transform notReadyChild = GetLeftHandIkTargetNotReadyTransform();

		bool inVehicle = IsInVehiclePassengerIkContext();

		Vector3 notReadyLocalPosition;
		Quaternion notReadyLocalRotation;
		Vector3 readyLocalPosition;
		Quaternion readyLocalRotation;

		if (inVehicle)
		{
			notReadyLocalPosition = _equipped.ResolveVehicleLeftHandIkNotReadyLocalPosition();
			notReadyLocalRotation = _equipped.ResolveVehicleLeftHandIkNotReadyLocalRotation();
			readyLocalPosition = _equipped.ResolveVehicleLeftHandIkReadyLocalPosition();
			readyLocalRotation = _equipped.ResolveVehicleLeftHandIkReadyLocalRotation();
		}
		else
		{
			notReadyLocalPosition = _equipped.ResolveLeftHandIkNotReadyLocalPosition(_stance);
			notReadyLocalRotation = _equipped.ResolveLeftHandIkNotReadyLocalRotation(_stance);
			readyLocalPosition = _equipped.ResolveLeftHandIkReadyLocalPosition(_stance);
			readyLocalRotation = _equipped.ResolveLeftHandIkReadyLocalRotation(_stance);
		}

		bool tuning = m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;

		if (tuning)
		{
			Transform nrChild = GetLeftHandIkTargetNotReadyTransform();
			if (nrChild != null)
			{
				notReadyLocalPosition = _weaponRoot.InverseTransformPoint(nrChild.position);
				notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * nrChild.rotation;
			}

			Transform rChild = GetLeftHandIkTargetTransform();
			if (rChild != null)
			{
				readyLocalPosition = _weaponRoot.InverseTransformPoint(rChild.position);
				readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * rChild.rotation;
			}
		}
		else
		{
			Vector3 notReadyEulerCheck = inVehicle
				? _equipped.ResolveVehicleLeftHandIkNotReadyLocalEulerAngles()
				: _equipped.ResolveLeftHandIkNotReadyLocalEulerAngles(_stance);

			if (!HasConfiguredIkLocalPose(notReadyLocalPosition, notReadyEulerCheck))
			{
				Transform fallback = notReadyChild != null ? notReadyChild : readyChild;
				if (fallback != null)
				{
					notReadyLocalPosition = _weaponRoot.InverseTransformPoint(fallback.position);
					notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * fallback.rotation;
				}
			}

			Vector3 readyEulerCheck = inVehicle
				? _equipped.ResolveVehicleLeftHandIkReadyLocalEulerAngles()
				: _equipped.ResolveLeftHandIkReadyLocalEulerAngles(_stance);

			if (!HasConfiguredIkLocalPose(readyLocalPosition, readyEulerCheck))
			{
				if (readyChild != null)
				{
					readyLocalPosition = _weaponRoot.InverseTransformPoint(readyChild.position);
					readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * readyChild.rotation;
				}
			}
		}

		if (readyChild == null && notReadyChild == null &&
		    !HasConfiguredIkLocalPose(
			    notReadyLocalPosition,
			    inVehicle
				    ? _equipped.ResolveVehicleLeftHandIkNotReadyLocalEulerAngles()
				    : _equipped.ResolveLeftHandIkNotReadyLocalEulerAngles(_stance)) &&
		    !HasConfiguredIkLocalPose(
			    readyLocalPosition,
			    inVehicle
				    ? _equipped.ResolveVehicleLeftHandIkReadyLocalEulerAngles()
				    : _equipped.ResolveLeftHandIkReadyLocalEulerAngles(_stance)))
			return false;

		_localPosition = Vector3.Lerp(notReadyLocalPosition, readyLocalPosition, _readyBlend01);
		_localRotation = Quaternion.Slerp(notReadyLocalRotation, readyLocalRotation, _readyBlend01);
		return true;
	}

	private bool UsesRocketLauncherPoseAndIk()
	{
		return m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;
	}

	private Transform GetRightHandIkTargetTransform()
	{
		return UsesRocketLauncherPoseAndIk()
			? m_RocketLauncherOrder.RightHandIkTargetTransform
			: m_UnitEquipment != null ? m_UnitEquipment.RightHandIkTargetTransform : null;
	}

	private Transform GetRightHandIkTargetNotReadyTransform()
	{
		return UsesRocketLauncherPoseAndIk()
			? m_RocketLauncherOrder.RightHandIkTargetNotReadyTransform
			: m_UnitEquipment != null ? m_UnitEquipment.RightHandIkTargetNotReadyTransform : null;
	}

	private Transform GetLeftHandIkTargetTransform()
	{
		return UsesRocketLauncherPoseAndIk()
			? m_RocketLauncherOrder.LeftHandIkTargetTransform
			: m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetTransform : null;
	}

	private Transform GetLeftHandIkTargetNotReadyTransform()
	{
		return UsesRocketLauncherPoseAndIk()
			? m_RocketLauncherOrder.LeftHandIkTargetNotReadyTransform
			: m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetNotReadyTransform : null;
	}

	private static bool IsUnderOrSame(Transform _root, Transform _child)
	{
		return _root != null && _child != null && (_child == _root || _child.IsChildOf(_root));
	}

	private static int GetForegripIndex(EquippedWeapon _equippedWeapon)
	{
		Transform root = _equippedWeapon != null ? _equippedWeapon.UnderBarrelForegripVisualRoot : null;
		if (root == null)
			return 0;

		string name = root.name;
		for (int i = 5; i >= 1; i--)
			if (name.Contains("ForeGrip" + i))
				return i;
		return 0;
	}

	private LocomotionStance GetCurrentStance()
	{
		if (m_Stance != null)
			return m_Stance.CurrentStance;

		if (m_Animator != null)
		{
			int stance = m_Animator.GetInteger(Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance));
			if (stance == (int)LocomotionStance.Crouch)
				return LocomotionStance.Crouch;
			if (stance == (int)LocomotionStance.Prone)
				return LocomotionStance.Prone;
		}

		return LocomotionStance.Standing;
	}

	private bool IsOperatingVehicleTurretIk()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		return m_UnitEquipment != null && m_UnitEquipment.IsOperatingVehicleTurret;
	}

	private bool TryResolveTurretHandIkWorldPose(
		bool _leftHand,
		bool _useNotReadyTargets,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		_position = Vector3.zero;
		_rotation = Quaternion.identity;

		if (!IsOperatingVehicleTurretIk())
			return false;

		Transform ikTarget = ResolveTurretIkTargetTransform(_leftHand, _useNotReadyTargets);
		if (ikTarget == null)
			return false;

		_position = ikTarget.position;
		_rotation = ikTarget.rotation;
		return true;
	}

	private Transform ResolveTurretIkTargetTransform(bool _leftHand, bool _useNotReadyTargets)
	{
		if (m_TurretReloadEvents != null && m_TurretReloadEvents.UseHandleNotReadyIkTargets)
		{
			Transform handleIk = _leftHand
				? m_TurretReloadEvents.LeftHandHandleIkTarget
				: m_TurretReloadEvents.RightHandHandleIkTarget;
			if (handleIk != null)
				return handleIk;
		}

		if (m_UnitEquipment == null)
			return null;

		Transform ikTarget = _leftHand
			? (_useNotReadyTargets
				? m_UnitEquipment.LeftHandIkTargetNotReadyTransform
				: m_UnitEquipment.LeftHandIkTargetTransform)
			: (_useNotReadyTargets
				? m_UnitEquipment.RightHandIkTargetNotReadyTransform
				: m_UnitEquipment.RightHandIkTargetTransform);
		if (ikTarget == null && _useNotReadyTargets)
		{
			ikTarget = _leftHand
				? m_UnitEquipment.LeftHandIkTargetTransform
				: m_UnitEquipment.RightHandIkTargetTransform;
		}

		if (ikTarget != null)
			return ikTarget;

		// Live re-resolve if cache was empty (weapon shown after bind).
		EquippedWeapon weapon = m_UnitEquipment.EquippedWeapon;
		if (weapon == null)
			return null;

		string readyName = _leftHand ? "LeftHandIkTarget" : "RightHandIkTarget";
		string notReadyName = _leftHand ? "LeftHandIkTarget_NotReady" : "RightHandIkTarget_NotReady";
		string name = _useNotReadyTargets ? notReadyName : readyName;
		Transform found = _leftHand
			? weapon.ResolveLeftHandIkTargetTransform(name)
			: weapon.ResolveRightHandIkTargetTransform(name);
		if (found == null && _useNotReadyTargets)
		{
			found = _leftHand
				? weapon.ResolveLeftHandIkTargetTransform(readyName)
				: weapon.ResolveRightHandIkTargetTransform(readyName);
		}

		return found;
	}

	private void SnapHandBoneToTurretIk(HumanBodyBones _handBone, bool _leftHand, bool _useNotReady)
	{
		Transform ikTarget = ResolveTurretIkTargetTransform(_leftHand, _useNotReady);
		if (ikTarget == null)
			return;

		HumanBodyBones upperBone = _leftHand ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
		HumanBodyBones lowerBone = _leftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
		Transform upper = m_Animator.GetBoneTransform(upperBone);
		Transform lower = m_Animator.GetBoneTransform(lowerBone);
		Transform hand = m_Animator.GetBoneTransform(_handBone);
		if (upper == null || lower == null || hand == null)
			return;

		ApplyTwoBoneIk(upper, lower, hand, ikTarget.position, ikTarget.rotation);
	}

	private static void ApplyTwoBoneIk(
		Transform _upper,
		Transform _lower,
		Transform _hand,
		Vector3 _targetPos,
		Quaternion _targetRot)
	{
		Vector3 upperPos = _upper.position;
		float lenUpper = Vector3.Distance(upperPos, _lower.position);
		float lenLower = Vector3.Distance(_lower.position, _hand.position);
		if (lenUpper < 1e-4f || lenLower < 1e-4f)
		{
			_hand.SetPositionAndRotation(_targetPos, _targetRot);
			return;
		}

		float maxReach = lenUpper + lenLower;
		Vector3 toTarget = _targetPos - upperPos;
		float dist = Mathf.Clamp(toTarget.magnitude, 0.001f, maxReach - 0.001f);
		Vector3 dir = toTarget / Mathf.Max(toTarget.magnitude, 0.001f);

		// Bend plane from current elbow offset.
		Vector3 pole = Vector3.Cross(dir, _lower.position - upperPos);
		if (pole.sqrMagnitude < 1e-6f)
			pole = Vector3.Cross(dir, _upper.up);
		if (pole.sqrMagnitude < 1e-6f)
			pole = Vector3.up;
		pole.Normalize();

		float cosAngle = (lenUpper * lenUpper + dist * dist - lenLower * lenLower) / (2f * lenUpper * dist);
		cosAngle = Mathf.Clamp(cosAngle, -1f, 1f);
		float angle = Mathf.Acos(cosAngle);

		Vector3 elbowDir = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, pole) * dir;
		Vector3 elbowPos = upperPos + elbowDir * lenUpper;

		Vector3 upperAxis = (_lower.position - upperPos).normalized;
		Vector3 desiredUpper = (elbowPos - upperPos).normalized;
		if (upperAxis.sqrMagnitude > 1e-6f && desiredUpper.sqrMagnitude > 1e-6f)
			_upper.rotation = Quaternion.FromToRotation(upperAxis, desiredUpper) * _upper.rotation;

		Vector3 lowerAxis = (_hand.position - _lower.position).normalized;
		Vector3 desiredLower = (_targetPos - _lower.position).normalized;
		if (lowerAxis.sqrMagnitude > 1e-6f && desiredLower.sqrMagnitude > 1e-6f)
			_lower.rotation = Quaternion.FromToRotation(lowerAxis, desiredLower) * _lower.rotation;

		_hand.SetPositionAndRotation(_targetPos, _targetRot);
	}

	private bool IsInVehiclePassengerIkContext()
	{
		if (IsOperatingVehicleTurretIk())
			return true;

		EnsureVehiclePassengerState();
		if (m_VehiclePassengerState == null)
			return false;

		// Match UnitEquippedWeaponPose: any fire-capable seat uses vehicle IK fields;
		// NotReady↔Ready is handled by ReadyPoseBlend01 (VehiclePassengerState.WantsReadyPose).
		if (m_VehiclePassengerState.IsFireCapable)
			return true;

		// Tuner can edit vehicle buffers while the unit is not mounted.
		return m_RuntimeTuner != null
		       && m_RuntimeTuner.IsTuningActive
		       && m_RuntimeTuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle;
	}

	private VehiclePassengerState EnsureVehiclePassengerState()
	{
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponent<VehiclePassengerState>();
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponentInParent<VehiclePassengerState>();
		return m_VehiclePassengerState;
	}
	#endregion
}
