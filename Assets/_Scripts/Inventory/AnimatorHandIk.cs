using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Applies hand IK from <see cref="WeaponGripResolver"/> cached targets only.
/// Must not write equipped weapon TRS — BASE/FINAL belong to <see cref="UnitEquippedWeaponPose"/>.
/// One <see cref="HandIkMode"/> drives target weights; Current eases toward Target (no SmoothDamp).
/// LateUpdate snap is left hand only, and only after left weight is high enough.
/// Right snap on Hand_R is a feedback loop. Visual recoil (order 200) runs before this snap.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(250)]
public class AnimatorHandIk : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение на корне юнита (родитель или сам юнит с CharacterInventory).")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Cached grip IK targets (equip-time).")]
	[SerializeField] private WeaponGripResolver m_GripResolver;
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
	[Tooltip("Драйвер клика для движения. На беге IK рук: Left/Right Hand Ik Weight While Running. Шаг в NotReady — левая на рукоятке, правая из Walk_F.")]
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[Tooltip("NavMesh драйвер локомоции. На беге IK рук: Left/Right Hand Ik Weight While Running. Шаг в NotReady — левая на рукоятке, правая из Walk_F.")]
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
	[Header("Бег (тест)")]
	[Tooltip("Вес левого IK на беге. 1 = полный, LateUpdate-snap включён.")]
	[FormerlySerializedAs("m_HandIkWeightWhileRunning")]
	[SerializeField, Range(0f, 1f)] private float m_LeftHandIkWeightWhileRunning = 1f;
	[Tooltip("Вес правого IK на беге. 0 = выкл.")]
	[SerializeField, Range(0f, 1f)] private float m_RightHandIkWeightWhileRunning = 0f;
	[Header("Экипировка")]
	[SerializeField, Min(0f)] private float m_EquipBlendDuration = 0.35f;
	[SerializeField] private AnimationCurve m_EquipBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	[Header("IK weight smoothing")]
	[Tooltip("Постоянная времени подъёма левого IK (сек). Меньше = быстрее.")]
	[SerializeField, Min(0.01f)] private float m_LeftIkRaiseSeconds = 0.07f;
	[Tooltip("Постоянная времени отпускания левого IK (сек).")]
	[SerializeField, Min(0.01f)] private float m_LeftIkReleaseSeconds = 0.12f;
	[Tooltip("Постоянная времени подъёма правого IK (сек).")]
	[SerializeField, Min(0.01f)] private float m_RightIkRaiseSeconds = 0.08f;
	[Tooltip("Постоянная времени отпускания правого IK (сек).")]
	[SerializeField, Min(0.01f)] private float m_RightIkReleaseSeconds = 0.1f;
	[Tooltip("LateUpdate left snap только если current left weight не ниже этого.")]
	[SerializeField, Range(0.5f, 1f)] private float m_LeftSnapWeightThreshold = 0.85f;
	[Tooltip("Дистанция кисть→LeftHandIK (м) для IK-GRIP-ERROR.")]
	[SerializeField, Min(0.01f)] private float m_MaxGripErrorMeters = 0.12f;
	[Tooltip("Скачок authored right dummy (м, local оружия) для IK-TARGET-JUMP.")]
	[SerializeField, Min(0.01f)] private float m_RightTargetJumpLogMeters = 0.08f;
	[Header("Локоть (подсказка IK)")]
	[SerializeField] private bool m_UseLeftElbowHint;
	[SerializeField] private Transform m_LeftElbowHint;
	[SerializeField, Range(0f, 1f)] private float m_LeftElbowHintWeight = 1f;
	[Header("Отладка")]
	[SerializeField] private bool m_DrawIkTargetGizmo;
	[SerializeField] private bool m_LogProximityIk;
	#endregion

	#region Private Fields
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);
	private const float c_MoveNavSpeedIkThreshold = 0.055f;

	private Animator m_Animator;
	private bool m_ClearHandIkOnNextAnimatorIkPass;
	private bool m_IsEquipBlendActive;
	private float m_EquipBlendElapsed;
	private int m_LastEquipBlendAdvanceFrame = -1;
	private HandIkMode m_CurrentMode = HandIkMode.Hold;
	private HandIkIntent m_LeftIntent = HandIkIntent.WeaponHold;
	private HandIkIntent m_RightIntent = HandIkIntent.WeaponHold;
	private float m_CurrentLeftWeight;
	private float m_CurrentRightWeight;
	private float m_TargetLeftWeight;
	private float m_TargetRightWeight;
	private float m_PreviousLeftWeight;
	private float m_PreviousRightWeight;
	private bool m_Reacquiring = true;
	private bool m_WasZeroIkMode = true;
	private float m_UnreachableLeftScale = 1f;
	private GripValidity m_LastGripValidity;
	private int m_LastWeightSmoothFrame = -1;
	private float m_NextProximityIkLogTime = -1f;
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

		if (m_Animator == null || !m_Animator.enabled)
			return;

		TickHandIkPipeline();

		if (m_CurrentMode == HandIkMode.Frozen && m_CurrentLeftWeight < 0.01f)
		{
			ValidateGrip();
			return;
		}

		// OnAnimatorIK can be skipped/overwritten by higher animator layers.
		// LateUpdate two-bone snap restores hand→target after animation + recoil.
		if (IsOperatingVehicleTurretIk())
		{
			if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadAnimationActive)
			{
				bool reloadUseNotReady = m_TurretReloadEvents.UseNotReadyIkTargets;
				bool skipHandleSnap = m_TurretReloadEvents.UseHandleNotReadyIkTargets;
				if (m_TurretReloadEvents.UseLeftHandIk && !skipHandleSnap)
					SnapHandBoneToTurretIk(HumanBodyBones.LeftHand, _leftHand: true, reloadUseNotReady);
				if (m_TurretReloadEvents.UseRightHandIk && !skipHandleSnap)
					SnapHandBoneToTurretIk(HumanBodyBones.RightHand, _leftHand: false, reloadUseNotReady);
			}
			else if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadBusy)
			{
				SnapHandBoneToTurretIk(HumanBodyBones.LeftHand, _leftHand: true, _useNotReady: false);
				SnapHandBoneToTurretIk(HumanBodyBones.RightHand, _leftHand: false, _useNotReady: false);
			}
			else
			{
				bool useNotReady = m_TurretReloadEvents != null && m_TurretReloadEvents.UseNotReadyIkTargets;
				SnapHandBoneToTurretIk(HumanBodyBones.LeftHand, _leftHand: true, useNotReady);
				SnapHandBoneToTurretIk(HumanBodyBones.RightHand, _leftHand: false, useNotReady);
			}

			ValidateGrip();
			return;
		}

		if (ShouldSnapLeftHand())
		{
			if (m_RocketLauncherOrder != null && m_RocketLauncherOrder.IsBusy)
			{
				if (m_RocketLauncherOrder.ShouldUseLeftHandIk)
				{
					Transform left = m_RocketLauncherOrder.GripLeftHandTarget;
					if (left != null && left.gameObject.activeInHierarchy)
						SnapHandBoneToWorldTarget(HumanBodyBones.LeftHand, _leftHand: true, left.position, left.rotation);
				}
			}
			else if (m_CurrentMode == HandIkMode.BoltHold)
			{
				Transform leftOnly = m_UnitEquipment != null ? m_UnitEquipment.GripLeftHandTarget : null;
				if (leftOnly != null && leftOnly.gameObject.activeInHierarchy)
					SnapHandBoneToWorldTarget(HumanBodyBones.LeftHand, _leftHand: true, leftOnly.position, leftOnly.rotation);
			}
			else
				SnapHandsToGripRigAfterAnimation();
		}

		ValidateGrip();
	}

	private void OnDrawGizmosSelected()
	{
		if (!m_DrawIkTargetGizmo || !Application.isPlaying)
			return;

		if (TryResolveLeftHandIkWorldPose(out Vector3 leftPos, out Quaternion leftRot) ||
		    TryGetGripWorldPose(_left: true, out leftPos, out leftRot))
		{
			Gizmos.color = new Color(0.2f, 0.95f, 1f, 0.95f);
			Gizmos.DrawSphere(leftPos, 0.015f);
			Gizmos.DrawLine(leftPos, leftPos + leftRot * Vector3.forward * 0.06f);
		}

		if (TryResolveRightHandIkWorldPose(out Vector3 rightPos, out Quaternion rightRot) ||
		    TryGetGripWorldPose(_left: false, out rightPos, out rightRot))
		{
			Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.95f);
			Gizmos.DrawSphere(rightPos, 0.015f);
			Gizmos.DrawLine(rightPos, rightPos + rightRot * Vector3.forward * 0.06f);
		}
	}

	private bool TryGetGripWorldPose(bool _left, out Vector3 _pos, out Quaternion _rot)
	{
		_pos = Vector3.zero;
		_rot = Quaternion.identity;
		EnsureGripResolver();
		if (m_GripResolver == null || !m_GripResolver.HasGripRig)
			return false;

		HandIkState state = m_GripResolver.CurrentState;
		Transform target = _left ? state.LeftTarget : state.RightTarget;
		if (target == null || !target.gameObject.activeInHierarchy)
			return false;
		_pos = target.position;
		_rot = target.rotation;
		return true;
	}
	#endregion

	#region Public Methods
	public HandIkMode CurrentMode => m_CurrentMode;
	public HandIkIntent LeftIkIntent => m_LeftIntent;
	public HandIkIntent RightIkIntent => m_RightIntent;
	public float CurrentLeftIkWeight => m_CurrentLeftWeight;
	public float CurrentRightIkWeight => m_CurrentRightWeight;
	public float TargetLeftIkWeight => m_TargetLeftWeight;
	public float TargetRightIkWeight => m_TargetRightWeight;
	public GripValidity LastGripValidity => m_LastGripValidity;

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

		TickHandIkPipeline();

		if (m_ClearHandIkOnNextAnimatorIkPass)
		{
			m_ClearHandIkOnNextAnimatorIkPass = false;
			StopEquipBlend();
			m_CurrentLeftWeight = 0f;
			m_CurrentRightWeight = 0f;
			ClearLeftHandIk();
			ClearRightHandIk();
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

	private bool IsSprintingNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.IsSprintMoveMode)
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.IsSprintMoveMode)
			return true;
		return false;
	}

	private bool IsStandingWalkNow()
	{
		if (IsInVehiclePassengerIkContext())
			return false;
		if (GetCurrentStance() != LocomotionStance.Standing)
			return false;
		if (IsRunningNow() || IsSprintingNow())
			return false;
		if (m_Animator == null)
			return false;
		return m_Animator.GetFloat(s_NavSpeed) >= c_MoveNavSpeedIkThreshold;
	}

	private bool IsPeacefulCarryIkPose()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
			return m_RuntimeTuner.ActiveWeaponPoseState.IsPeacefulCarryPose();
		if (m_EquippedWeaponPose == null)
			return false;
		return m_EquippedWeaponPose.GetEffectivePoseForIk().IsPeacefulCarryPose();
	}

	private bool ShouldSnapLeftHand()
	{
		if (m_CurrentLeftWeight < m_LeftSnapWeightThreshold)
			return false;
		if (m_LeftIntent == HandIkIntent.FullAnimation)
			return false;
		return m_CurrentMode == HandIkMode.Hold
		       || m_CurrentMode == HandIkMode.SoftHold
		       || m_CurrentMode == HandIkMode.BoltHold
		       || m_CurrentMode == HandIkMode.Transition;
	}

	private void TickHandIkPipeline()
	{
		EnsureGripResolver();
		bool poseBlending = m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating
		                    && m_EquippedWeaponPose.PoseBlend01 < 0.999f;
		bool stanceBlending = m_GripResolver != null && m_GripResolver.HoldContext.IsStanceBlending;
		bool stanceBusy = m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.StanceTransition);

		var query = new UnitHandIkModeResolver.Query
		{
			TunerHandsFrozen = ShouldDisableAllHandIkForTuning(),
			MagazineLoading = m_MagazineLoading != null && m_MagazineLoading.IsLoadingMagazine,
			Healing = (m_SelfStabilization != null && m_SelfStabilization.IsHealPresentationActive)
			          || (m_StabilizeOther != null && m_StabilizeOther.IsHealPresentationActive),
			DraggingFallen = m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.DraggingFallen),
			CarryingFallen = m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.CarryingFallen),
			GrenadeThrow = m_GrenadeThrow != null && m_GrenadeThrow.IsThrowAnimPlaying,
			ReloadingWeapon = m_WeaponReload != null && m_WeaponReload.IsReloadingWeapon,
			LoadingLmgBelt = m_WeaponReload != null && m_WeaponReload.IsLoadingLmgBelt,
			CyclingBolt = m_WeaponReload != null && m_WeaponReload.IsCyclingBolt,
			BoltHeld = ShouldUseBoltCycleLeftHandHoldIk(),
			PoseBlending = poseBlending,
			StanceBlending = stanceBlending,
			StanceBusy = stanceBusy,
			Running = IsRunningNow(),
			Walking = IsStandingWalkNow(),
			PeacefulCarry = IsPeacefulCarryIkPose(),
			Reacquiring = m_Reacquiring
		};

		HandIkState grip = m_GripResolver != null ? m_GripResolver.CurrentState : default;
		var weights = new UnitHandIkModeResolver.Weights
		{
			GripLeftDefault = grip.LeftWeight > 0.001f ? grip.LeftWeight : 0.9f,
			GripRightDefault = grip.RightWeight > 0.001f ? grip.RightWeight : 0.35f,
			RightNotReadyWeight = m_RightHandNotReadyIkWeight,
			ReadyBlend01 = GetEffectiveReadyBlend01(),
			RunLeft = m_LeftHandIkWeightWhileRunning,
			RunRight = m_RightHandIkWeightWhileRunning
		};

		UnitHandIkModeResolver.Result result = UnitHandIkModeResolver.Resolve(query, weights);
		m_CurrentMode = result.Mode;
		m_LeftIntent = result.LeftIntent;
		m_RightIntent = result.RightIntent;
		m_TargetLeftWeight = result.LeftWeightTarget * Mathf.Clamp01(m_UnreachableLeftScale);
		m_TargetRightWeight = result.RightWeightTarget;

		if (m_CurrentMode == HandIkMode.SoftHold &&
		    m_LeftIntent == HandIkIntent.WeaponHold &&
		    m_RightIntent == HandIkIntent.MovementRelaxation &&
		    m_TargetRightWeight <= 0.001f)
		{
			m_CurrentLeftWeight = m_TargetLeftWeight;
			m_CurrentRightWeight = 0f;
		}

		bool zeroMode = m_CurrentMode == HandIkMode.Disabled
		                || m_CurrentMode == HandIkMode.Frozen
		                || m_CurrentMode == HandIkMode.Reload;
		if (zeroMode)
		{
			m_WasZeroIkMode = true;
			m_Reacquiring = true;
		}
		else if (m_WasZeroIkMode)
		{
			m_Reacquiring = true;
			m_WasZeroIkMode = false;
		}

		if (m_Reacquiring && !zeroMode)
		{
			if (Mathf.Abs(m_CurrentLeftWeight - m_TargetLeftWeight) < 0.05f &&
			    Mathf.Abs(m_CurrentRightWeight - m_TargetRightWeight) < 0.05f)
				m_Reacquiring = false;
		}

		if (m_LastWeightSmoothFrame != Time.frameCount)
		{
			m_LastWeightSmoothFrame = Time.frameCount;
			m_PreviousLeftWeight = m_CurrentLeftWeight;
			m_PreviousRightWeight = m_CurrentRightWeight;
			float dt = Time.deltaTime;
			m_CurrentLeftWeight = SmoothExpWeight(
				m_CurrentLeftWeight,
				m_TargetLeftWeight,
				m_LeftIkRaiseSeconds,
				m_LeftIkReleaseSeconds,
				dt);
			m_CurrentRightWeight = SmoothExpWeight(
				m_CurrentRightWeight,
				m_TargetRightWeight,
				m_RightIkRaiseSeconds,
				m_RightIkReleaseSeconds,
				dt);
			if (m_UnreachableLeftScale < 0.99f)
				m_CurrentLeftWeight = Mathf.Min(m_CurrentLeftWeight, m_TargetLeftWeight);
		}

		if (query.GrenadeThrow)
			m_ClearHandIkOnNextAnimatorIkPass = true;
	}

	private static float SmoothExpWeight(float _current, float _target, float _raiseTau, float _releaseTau, float _dt)
	{
		float tau = _target >= _current ? Mathf.Max(0.01f, _raiseTau) : Mathf.Max(0.01f, _releaseTau);
		return Mathf.Lerp(_current, _target, 1f - Mathf.Exp(-_dt / tau));
	}

	private void ValidateGrip()
	{
		m_LastGripValidity = default;
		m_LastGripValidity.IsReachable = true;
		m_LastGripValidity.IsStable = true;

		if (m_Animator == null)
			return;

		EnsureGripResolver();
		HandIkState state = m_GripResolver != null ? m_GripResolver.CurrentState : default;
		Transform hand = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
		Transform shoulder = m_Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
		Transform elbow = m_Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);

		if (state.HasLeft && hand != null)
		{
			m_LastGripValidity.DistanceError = Vector3.Distance(hand.position, state.LeftTarget.position);
			m_LastGripValidity.AngleError = Quaternion.Angle(hand.rotation, state.LeftTarget.rotation);
			if (m_LastGripValidity.DistanceError > m_MaxGripErrorMeters &&
			    m_CurrentLeftWeight > 0.5f &&
			    (m_CurrentMode == HandIkMode.Hold || m_CurrentMode == HandIkMode.SoftHold ||
			     m_CurrentMode == HandIkMode.BoltHold))
			{
				m_LastGripValidity.IsStable = false;
				LogProximityIkThrottled($"[IK-GRIP-ERROR] unit={name} dist={m_LastGripValidity.DistanceError:F3}m");
			}
		}

		if (state.HasLeft && shoulder != null && elbow != null && m_CurrentLeftWeight > 0.15f)
		{
			float chain = Vector3.Distance(shoulder.position, elbow.position) +
			              Vector3.Distance(elbow.position, hand != null ? hand.position : elbow.position);
			float reach = Vector3.Distance(shoulder.position, state.LeftTarget.position);
			if (reach > chain + 0.02f)
			{
				m_LastGripValidity.IsReachable = false;
				m_LastGripValidity.LeftOutOfReach = true;
				m_UnreachableLeftScale = 1f;
				LogProximityIkThrottled($"[IK-GRIP-UNREACHABLE] unit={name} reach={reach:F3} chain={chain:F3}");
			}
			else
				m_UnreachableLeftScale = 1f;
		}

		if (m_GripResolver != null && m_GripResolver.LastRightTargetJumpMeters > m_RightTargetJumpLogMeters)
		{
			m_LastGripValidity.TargetJump = true;
			LogProximityIkThrottled($"[IK-TARGET-JUMP] unit={name} d={m_GripResolver.LastRightTargetJumpMeters:F3}m");
		}

		float leftJump = Mathf.Abs(m_CurrentLeftWeight - m_PreviousLeftWeight);
		float rightJump = Mathf.Abs(m_CurrentRightWeight - m_PreviousRightWeight);
		if (leftJump > 0.45f || rightJump > 0.45f)
		{
			m_LastGripValidity.WeightJump = true;
			LogProximityIkThrottled($"[IK-WEIGHT-JUMP] unit={name} L={leftJump:F2} R={rightJump:F2}");
		}
	}

	private void LogProximityIkThrottled(string _message)
	{
		if (!m_LogProximityIk)
			return;
		if (Time.unscaledTime < m_NextProximityIkLogTime)
			return;
		m_NextProximityIkLogTime = Time.unscaledTime + 2f;
		Debug.Log(_message, this);
	}

	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		if (m_GripResolver == null)
			m_GripResolver = GetComponentInParent<WeaponGripResolver>();
		if (m_GripResolver == null && m_UnitEquipment != null)
		{
			m_GripResolver = m_UnitEquipment.GetComponent<WeaponGripResolver>();
			if (m_GripResolver == null)
				m_GripResolver = m_UnitEquipment.gameObject.AddComponent<WeaponGripResolver>();
		}
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
		if (IsHandIkBlocked())
		{
			StopEquipBlend();
			return;
		}

		bool gripLeftReady = m_UnitEquipment != null
		                     && m_UnitEquipment.UsesWeaponGripRig
		                     && m_UnitEquipment.GripLeftHandTarget != null;
		if (!gripLeftReady && !TryResolveLeftHandIkWorldPose(out _, out _))
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

		// Rocket launcher gameplay defaults to Ready.
		if (m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose)
			return 1f;

		if (m_TurretReloadEvents != null && m_TurretReloadEvents.IsReloadBusy &&
		    (m_TurretReloadEvents.UseNotReadyIkTargets || m_TurretReloadEvents.UseHandleNotReadyIkTargets))
			return 0f;

		return m_EquippedWeaponPose != null
			? Mathf.Clamp01(m_EquippedWeaponPose.GripHoldBlend01)
			: 0f;
	}

	private float GetRightHandIkWeightMultiplier()
	{
		if (IsOperatingVehicleTurretIk())
			return 1f;

		if (m_EquippedWeaponPose == null && (m_UnitEquipment == null || !m_UnitEquipment.IsOperatingVehicleTurret))
			return 0f;

		if (IsStandingWalkNow() && IsPeacefulCarryIkPose())
			return 0f;

		float readyBlend = GetEffectiveReadyBlend01();
		if (IsRunningNow())
			return m_RightHandIkWeightWhileRunning;
		return Mathf.Lerp(m_RightHandNotReadyIkWeight, 1f, readyBlend);
	}

	private void ApplyLeftHandIkInternal()
	{
		if (m_CurrentLeftWeight <= 0.001f)
		{
			ClearLeftHandIk();
			return;
		}

		if (m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose)
		{
			if (TryApplyRocketLauncherLeftHandIk())
				return;
		}

		if (TryApplyGripRigLeftHandIk())
			return;

		if (!TryResolveLeftHandIkWorldPose(out Vector3 position, out Quaternion rotation))
		{
			StopEquipBlend();
			ClearLeftHandIk();
			return;
		}

		float blend = IsOperatingVehicleTurretIk() ? GetTurretHandIkBlendMultiplier() : GetEquipBlendMultiplier();
		float positionWeight = m_LeftHandPositionWeight * m_CurrentLeftWeight * blend;
		float rotationWeight = m_LeftHandRotationWeight * m_CurrentLeftWeight * blend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, rotation);

		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight * blend * m_CurrentLeftWeight);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
	}

	private bool TryApplyRocketLauncherLeftHandIk()
	{
		Transform left = m_RocketLauncherOrder != null ? m_RocketLauncherOrder.GripLeftHandTarget : null;
		if (left == null || !left.gameObject.activeInHierarchy)
			return false;

		float blend = GetEquipBlendMultiplier();
		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, m_LeftHandPositionWeight * m_CurrentLeftWeight * blend);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, m_LeftHandRotationWeight * m_CurrentLeftWeight * blend);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, left.position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, left.rotation);
		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight * blend * m_CurrentLeftWeight);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
		return true;
	}

	private bool TryApplyGripRigLeftHandIk()
	{
		if (IsOperatingVehicleTurretIk())
			return false;

		EnsureGripResolver();
		if (m_GripResolver == null || !m_GripResolver.HasGripRig)
			return false;

		HandIkState state = m_GripResolver.CurrentState;
		if (!state.HasLeft)
		{
			StopEquipBlend();
			ClearLeftHandIk();
			return true;
		}

		float blend = GetEquipBlendMultiplier();
		float positionWeight = m_LeftHandPositionWeight * m_CurrentLeftWeight * blend;
		float rotationWeight = m_LeftHandRotationWeight * m_CurrentLeftWeight * blend;

		m_Animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
		m_Animator.SetIKPosition(AvatarIKGoal.LeftHand, state.LeftTarget.position);
		m_Animator.SetIKRotation(AvatarIKGoal.LeftHand, state.LeftTarget.rotation);

		if (m_UseLeftElbowHint && m_LeftElbowHint != null)
		{
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, m_LeftElbowHintWeight * blend * m_CurrentLeftWeight);
			m_Animator.SetIKHintPosition(AvatarIKHint.LeftElbow, m_LeftElbowHint.position);
		}
		else
			m_Animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);

		return true;
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

		if (useRocketLauncher && TryApplyRocketLauncherRightHandIk())
			return;

		if (!useRocketLauncher && TryApplyGripRigRightHandIk())
			return;

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

		Vector3 worldPos = weaponRoot.TransformPoint(localPos);
		Quaternion worldRot = weaponRoot.rotation * localRot;

		ApplyRightHandIkDirect(worldPos, worldRot);
	}

	private bool TryApplyRocketLauncherRightHandIk()
	{
		if (m_RocketLauncherOrder == null)
			return false;

		WeaponStance stance = ResolveWeaponStanceForIk();
		Vector3 worldPos;
		Quaternion worldRot;
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			if (!m_RocketLauncherOrder.TryGetGripRightHandWorldPose(
				    stance,
				    m_RuntimeTuner.ActiveWeaponPoseState,
				    out worldPos,
				    out worldRot))
				return false;
		}
		else
		{
			float blend = GetEffectiveReadyBlend01();
			if (!m_RocketLauncherOrder.TryGetGripRightHandWorldPose(stance, blend, out worldPos, out worldRot))
				return false;
		}

		float ikBlend = m_CurrentRightWeight;
		if (ikBlend <= 0.001f)
		{
			ClearRightHandIk();
			return true;
		}

		m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, m_RightHandPositionWeight * ikBlend);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, m_RightHandRotationWeight * ikBlend);
		m_Animator.SetIKPosition(AvatarIKGoal.RightHand, worldPos);
		m_Animator.SetIKRotation(AvatarIKGoal.RightHand, worldRot);
		return true;
	}

	private WeaponStance ResolveWeaponStanceForIk()
	{
		if (m_VehiclePassengerState != null && m_VehiclePassengerState.IsFireCapable)
			return WeaponStance.Vehicle;
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive &&
		    m_RuntimeTuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Vehicle)
			return WeaponStance.Vehicle;
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive &&
		    m_RuntimeTuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch)
			return WeaponStance.Crouching;

		LocomotionStance loco = GetCurrentStance();
		return loco == LocomotionStance.Crouch ? WeaponStance.Crouching : WeaponStance.Standing;
	}

	private bool TryApplyGripRigRightHandIk()
	{
		EnsureGripResolver();
		if (m_GripResolver == null || !m_GripResolver.HasGripRig)
			return false;

		HandIkState state = m_GripResolver.CurrentState;
		if (!state.HasRight)
		{
			ClearRightHandIk();
			return true;
		}

		float ikBlend = m_CurrentRightWeight;
		if (ikBlend <= 0.001f)
		{
			ClearRightHandIk();
			return true;
		}

		float equipBlend = GetEquipBlendMultiplier();
		m_Animator.SetIKPositionWeight(AvatarIKGoal.RightHand, m_RightHandPositionWeight * ikBlend * equipBlend);
		m_Animator.SetIKRotationWeight(AvatarIKGoal.RightHand, m_RightHandRotationWeight * ikBlend * equipBlend);
		m_Animator.SetIKPosition(AvatarIKGoal.RightHand, state.RightTarget.position);
		m_Animator.SetIKRotation(AvatarIKGoal.RightHand, state.RightTarget.rotation);
		return true;
	}

	private void EnsureGripResolver()
	{
		if (m_GripResolver != null)
			return;
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		if (m_UnitEquipment == null)
			return;
		m_GripResolver = m_UnitEquipment.GetComponent<WeaponGripResolver>();
		if (m_GripResolver == null)
			m_GripResolver = m_UnitEquipment.gameObject.AddComponent<WeaponGripResolver>();
	}

	private void ApplyRightHandIkDirect(Vector3 worldPos, Quaternion worldRot)
	{
		float ikBlend = IsOperatingVehicleTurretIk() ? GetRightHandIkWeightMultiplier() : m_CurrentRightWeight;
		if (ikBlend <= 0.001f)
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

		Transform notReadyChild = GetRightHandIkTargetNotReadyTransform();
		Transform readyChild = GetRightHandIkTargetTransform();
		if (notReadyChild == null && readyChild == null)
			return false;

		Transform nr = notReadyChild != null ? notReadyChild : readyChild;
		Transform r = readyChild != null ? readyChild : notReadyChild;

		Vector3 notReadyLocalPosition = _weaponRoot.InverseTransformPoint(nr.position);
		Quaternion notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * nr.rotation;
		Vector3 readyLocalPosition = _weaponRoot.InverseTransformPoint(r.position);
		Quaternion readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * r.rotation;

		_localPosition = Vector3.Lerp(notReadyLocalPosition, readyLocalPosition, _readyBlend01);
		_localRotation = Quaternion.Slerp(notReadyLocalRotation, readyLocalRotation, _readyBlend01);
		return true;
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
		if (readyChild == null && notReadyChild == null)
			return false;

		Transform nr = notReadyChild != null ? notReadyChild : readyChild;
		Transform r = readyChild != null ? readyChild : notReadyChild;

		Vector3 notReadyLocalPosition = _weaponRoot.InverseTransformPoint(nr.position);
		Quaternion notReadyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * nr.rotation;
		Vector3 readyLocalPosition = _weaponRoot.InverseTransformPoint(r.position);
		Quaternion readyLocalRotation = Quaternion.Inverse(_weaponRoot.rotation) * r.rotation;

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

		SnapHandBoneToWorldTarget(_handBone, _leftHand, ikTarget.position, ikTarget.rotation);
	}

	/// <summary>
	/// Infantry GripRig: LateUpdate two-bone snap for LEFT hand only.
	/// Must not write equipped weapon TRS. Right hand must NOT snap to RightHandGrip / RightTarget —
	/// weapon is parented under Hand_R (right snap is a feedback loop).
	/// Visual recoil rotates Hand_R; left snap follows the kicked weapon.
	/// </summary>
	private void SnapHandsToGripRigAfterAnimation()
	{
		EnsureGripResolver();
		if (m_GripResolver == null || !m_GripResolver.HasGripRig)
			return;

		HandIkState state = m_GripResolver.CurrentState;
		if (!state.HasLeft)
			return;

		float leftBlend = GetEquipBlendMultiplier();
		if (m_LeftHandPositionWeight * m_CurrentLeftWeight * leftBlend <= 0.01f)
			return;

		// Left only — right LateUpdate snap is forbidden (Hand_R feedback loop).
		SnapHandBoneToWorldTarget(HumanBodyBones.LeftHand, _leftHand: true, state.LeftTarget.position, state.LeftTarget.rotation);
	}

	private void SnapHandBoneToWorldTarget(
		HumanBodyBones _handBone,
		bool _leftHand,
		Vector3 _targetPos,
		Quaternion _targetRot)
	{
		HumanBodyBones upperBone = _leftHand ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
		HumanBodyBones lowerBone = _leftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
		Transform upper = m_Animator.GetBoneTransform(upperBone);
		Transform lower = m_Animator.GetBoneTransform(lowerBone);
		Transform hand = m_Animator.GetBoneTransform(_handBone);
		if (upper == null || lower == null || hand == null)
			return;

		ApplyTwoBoneIk(upper, lower, hand, _targetPos, _targetRot);
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
