using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Serialization;

/// <summary>
/// Weapon pose mode selection (LowReady / HighReady / PreAim / HipFire / PointAim / Aiming / Auto).
/// Does not write equipped weapon local TRS — BASE is <see cref="UnitEquippedWeaponPose"/>.
/// NotReady and NotReadyPatrol are peaceful carry flags (Ctrl+E), not WeaponPoseMode values.
/// E cycles LowReady → HighReady → PreAim → Aiming → PointAim. Ctrl+E cycles NotReady ↔ NotReadyPatrol.
/// Run/sprint: non-combat stays; HipFire → NotReady; combat → HighReady; then restore wanted mode.
/// Turn >90°: NotReady/Patrol stay; LowReady/PreAim → LowReady; PointAim/Aiming/HighReady → HighReady; HipFire → NotReady.
/// Animator bool <c>WeaponReady</c> picks locomotion clips (Jog_Aim vs Run_F), not the pose slot.
/// LowReady and HighReady: on the move WeaponReady stays on so walk uses Walk_Aim / RifleCrouch_Move, not Walk_F.
/// Standing idle uses the IK-delayed pose so HighReady does not flip WeaponReady mid E-cycle.
/// Run: LowReady and HighReady → Jog_Aim_F_Loop. Sprint: Sprint_F.
/// Int <c>WeaponStandIdle</c>: NotReady/NotReadyPatrol/HipFire → relaxed body
/// (Stand_Relaxed_Idle / RifleCrouch_Idle); HipFireWalk / HipFireCrouchWalk use aim-walk clips;
/// LowReady and combat poses → aim body (Stand_Aim_Idle / RifleCrouch_Idle_Ready).
/// HighReady and Aiming share the aim clip.
/// Vehicle seat: HipFire and combat → Seat_Aim; Frozen / NotReady / Patrol → Seat_relax.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public sealed class UnitWeaponReadyHandsLayer : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitAnimatorWeaponMode m_AnimatorWeaponMode;
	[SerializeField] private AnimatorHandIk m_LeftHandIk;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;

	[Header("Ввод")]
	[SerializeField] private bool m_EnableKeyboardInput = true;
	[SerializeField] private Key m_ToggleReadyKey = Key.E;

	[Header("Звук перехода позы")]
	[FormerlySerializedAs("m_EnterNotReadyClip")]
	[Tooltip("Один клип на вход и выход из позы. PreAim ↔ Aiming без звука.")]
	[SerializeField] private AudioClip m_EnterLowReadyClip;
	[SerializeField, Range(0f, 1f)] private float m_ReadyTransitionVolume = 0.55f;
	[SerializeField, Min(0.1f)] private float m_ReadyTransitionSpatialMaxDistance = 18f;

	[Header("Auto bake")]
	[SerializeField, Min(0.05f)] private float m_AcceptableHitRadiusMeters = 0.35f;

	[Header("Отладка поз")]
	[Tooltip("Консоль: переходы позы, run/sprint suppress, restore, луч ствола вверх/вниз. Только выбранный юнит.")]
	[SerializeField] private bool m_LogPoseTransitions;

	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	private static readonly int s_LocomotionTier = Animator.StringToHash(UnitClickToMove.ParamLocomotionTier);
	private static readonly int s_WeaponReady = Animator.StringToHash(UnitAnimatorWeaponMode.ParamWeaponReady);
	private static readonly int s_WeaponStandIdle = Animator.StringToHash(UnitAnimatorWeaponMode.ParamWeaponStandIdle);
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);

	private WeaponPoseMode m_WantedMode = WeaponPoseMode.LowReady;
	private WeaponPoseMode m_ModeBeforeSuppression = WeaponPoseMode.LowReady;
	private WeaponPoseState m_EffectivePose = WeaponPoseState.NotReady;
	private WeaponPoseAutoCapabilityCache m_CapabilityCache;
	private ItemDefinition m_LastEquipped;
	private bool m_BlockToggleInput;
	private bool m_RestoreReadyAfterSprint;
	private bool m_RestoreReadyAfterRun;
	private bool m_RestoreReadyAfterTurn;
	private bool m_ProximityBlocksReady;
	private bool m_RocketLauncherFireReadyOverride;
	private bool m_ReadyTransitionAudioArmed;
	private bool m_HasStarted;
	private bool m_LastPushedWeaponReady;
	private int m_LastPushedStandIdle = (int)UnitAnimatorWeaponMode.WeaponStandIdleStyle.AimIdle;
	private WeaponPoseState m_LastAudioPose = WeaponPoseState.NotReady;
	private bool m_PendingLowReady;
	private bool m_IsPeacefulNotReady = true;
	private WeaponPoseState m_PeacefulCarryPose = WeaponPoseState.NotReady;
	private float m_CombatAlertUntilTime;
	private int m_LastAttachmentFingerprint;
	private RtsUnitMember m_RtsMember;
	private string m_LastPoseLogKey;
	private int m_PoseLogSilence;
	private WeaponPoseState m_CachedAutoPose = WeaponPoseState.LowReady;
	private float m_NextAutoPoseTime;
	private bool m_CachedAutoHasTarget;
	private Transform m_CachedAutoTarget;
	private bool m_CachedAutoAlert;
	private const float c_CombatAlertHoldSeconds = 4f;
	private const float c_BarrelPitchLevelDeadzoneDeg = 2f;
	#endregion

	#region Public Properties
	public WeaponPoseMode WantedMode => m_WantedMode;
	public WeaponPoseState EffectivePoseState => ResolveEffectivePose();
	public WeaponPoseAutoCapabilityCache PoseCapabilityCache => m_CapabilityCache;
	/// <summary>
	/// Fire-capable hold (PreAim / HipFire / PointAim / Aiming). Not the facing gate —
	/// HighReady is raised combat but cannot shoot.
	/// </summary>
	public bool WantsReady => ResolveEffectivePose().CanFireFromPose();
	public bool IsWeaponInCombatPose => IsWeaponEquipped() && ResolveEffectivePose().IsCombatPose();
	public bool IsPeacefulNotReady => m_IsPeacefulNotReady;
	public WeaponPoseState PeacefulCarryPose => m_PeacefulCarryPose;
	public bool HasCombatAlert => Time.time < m_CombatAlertUntilTime;
	public bool IsKeyboardInputEnabled => m_EnableKeyboardInput;
	public bool HasPendingReadyRestore =>
		m_RestoreReadyAfterSprint || m_RestoreReadyAfterRun || m_RestoreReadyAfterTurn;
	#endregion

	#region Public Methods
	public bool IsStandingIdleNow() =>
		m_Animator == null || m_Animator.GetFloat(s_NavSpeed) < 0.055f;

	public bool ShouldIkFollowPoseTargetImmediately() =>
		HasPendingReadyRestore && !IsStandingIdleNow();

	public string FormatStandingPoseDebug()
	{
		bool followEffective = ShouldWeaponReadyFollowEffectivePose();
		return $"effective={ResolveEffectivePose()} ikSide={ResolveStandIdlePoseSource()} " +
		       $"weaponReady={ResolveWeaponReadyParameter()} followEffective={followEffective} " +
		       $"pendingRestore={HasPendingReadyRestore} " +
		       $"run={m_RestoreReadyAfterRun} sprint={m_RestoreReadyAfterSprint} turn={m_RestoreReadyAfterTurn} " +
		       $"{FormatLocomotionBits()}";
	}

	public bool IsEquippedWeaponUserNotReady()
	{
		if (m_Equipment == null)
			return false;
		ItemDefinition current = m_Equipment.EquippedDefinition;
		if (current == null || !current.IsEquipment || current.EquipmentKind != EquipmentKind.Weapon)
			return false;
		return !GetEffectiveIsReady();
	}

	public bool ShouldUseUnarmedLocomotionBranch() => false;

	/// <summary>
	/// Fire/aim-progress ready: CanFireFromPose (PreAim+) or turret/rocket.
	/// Body/spine facing uses <see cref="WantsCombatTargetFacing"/> — HighReady must still turn onto a target.
	/// </summary>
	public bool IsWeaponEquippedAndReady()
	{
		if (m_RocketLauncherFireReadyOverride && GetEffectiveIsReady())
			return true;
		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret)
			return true;
		return IsWeaponEquipped() && GetEffectiveIsReady();
	}

	/// <summary>
	/// Raised combat hold for root yaw, spine aim, and barrel-centric facing:
	/// HighReady, PreAim, HipFire, PointAim, Aiming. Not the trigger-pull gate.
	/// </summary>
	public bool WantsCombatTargetFacing()
	{
		if (m_RocketLauncherFireReadyOverride && GetEffectiveIsReady())
			return true;
		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret)
			return true;
		return IsWeaponEquipped() && ResolveEffectivePose().IsWeaponRaised();
	}

	public bool IsWeaponReadyToFire()
	{
		if (!IsWeaponEquipped() && !m_RocketLauncherFireReadyOverride)
			return false;
		if (IsSprintingNow())
			return false;
		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret)
			return true;
		return CanFireFromSettledCombatPose();
	}

	/// <summary>
	/// Огонь только из Aiming / HipFire / PointAim.
	/// PreAim, HighReady, LowReady, NotReady — выстрел запрещён.
	/// Во время бленда оба конца перехода должны разрешать выстрел.
	/// </summary>
	public bool CanFireFromSettledCombatPose()
	{
		if (m_EquippedWeaponPose != null)
		{
			if (m_EquippedWeaponPose.IsPoseBlendAnimating && m_EquippedWeaponPose.PoseBlend01 < 0.999f)
				return m_EquippedWeaponPose.CurrentPose.CanShootFromPose()
				       && m_EquippedWeaponPose.TargetPose.CanShootFromPose();
			return m_EquippedWeaponPose.TargetPose.CanShootFromPose();
		}

		return ResolveEffectivePose().CanShootFromPose();
	}

	public void EnableReadyFromStanceZInput()
	{
		if (!IsWeaponEquipped())
			return;
		if (m_WantedMode != WeaponPoseMode.LowReady || m_IsPeacefulNotReady)
			return;
		SetPoseModeWanted(WeaponPoseMode.HighReady, true);
	}

	public void SetReadyWanted(bool _ready, bool _forceWalkIfNeeded = true)
	{
		if (!_ready)
			CancelDeferredReadyRestores();
		SetPoseModeWanted(_ready ? WeaponPoseMode.HighReady : WeaponPoseMode.LowReady, _forceWalkIfNeeded);
	}

	public void SetPoseModeWanted(WeaponPoseMode _mode, bool _forceWalkIfNeeded = true)
	{
		m_IsPeacefulNotReady = false;
		m_PeacefulCarryPose = WeaponPoseState.NotReady;
		WeaponPoseMode clamped = ClampModeToCapabilities(_mode);
		ApplyPoseModeWanted(clamped, _forceWalkIfNeeded, true);
	}

	/// <summary>
	/// Game default: armed units enter Aiming. Unarmed stay in peaceful NotReady.
	/// </summary>
	public void ApplyDefaultEquippedPose()
	{
		if (IsWeaponEquipped())
		{
			SetPoseModeWanted(WeaponPoseMode.Aiming, false);
			return;
		}

		ApplyPeacefulCarry(WeaponPoseState.NotReady);
	}

	/// <summary>Ctrl+E / non-combat menu: NotReady ↔ NotReadyPatrol. From combat (including LowReady) enters NotReady.</summary>
	public void TogglePeacefulNotReady()
	{
		if (m_IsPeacefulNotReady)
		{
			EnterPeacefulCarry(m_PeacefulCarryPose == WeaponPoseState.NotReady
				? WeaponPoseState.NotReadyPatrol
				: WeaponPoseState.NotReady);
			return;
		}

		EnterPeacefulCarry(WeaponPoseState.NotReady);
	}

	/// <summary>Force a peaceful carry pose (tuner / debug). Same fire and idle rules as NotReady.</summary>
	public void SetPeacefulCarryPose(WeaponPoseState _pose)
	{
		if (!_pose.IsPeacefulCarryPose())
			return;
		ApplyPeacefulCarry(_pose);
	}

	/// <summary>E / combat menu: peaceful/HipFire/Auto → LowReady, then LowReady → HighReady → PreAim → Aiming → PointAim → LowReady.</summary>
	public void CycleCombatPose()
	{
		if (!IsWeaponEquipped() && !m_RocketLauncherFireReadyOverride)
			return;
		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.CarryingFallen))
			return;

		WeaponPoseMode next = GetNextCombatCycleMode(m_WantedMode);
		if (next == WeaponPoseMode.LowReady
		    && m_Animator != null
		    && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return;

		if (next == WeaponPoseMode.LowReady && IsWeaponActionBusy())
		{
			m_PendingLowReady = true;
			return;
		}

		m_PendingLowReady = false;
		if (next == WeaponPoseMode.LowReady)
			CancelDeferredReadyRestores();

		ApplyPoseModeWanted(next, next != WeaponPoseMode.LowReady, true);
	}

	public void NotifyCombatAlert()
	{
		m_CombatAlertUntilTime = Time.time + c_CombatAlertHoldSeconds;
	}

	public void CancelDeferredReadyRestores()
	{
		if (m_RestoreReadyAfterSprint || m_RestoreReadyAfterRun || m_RestoreReadyAfterTurn)
		{
			LogPoseTransition(
				$"CANCEL restore flags run={m_RestoreReadyAfterRun} sprint={m_RestoreReadyAfterSprint} turn={m_RestoreReadyAfterTurn} " +
				$"pose={ResolveEffectivePose()} wanted={m_WantedMode} saveWanted={m_ModeBeforeSuppression}",
				debounce: false);
		}

		m_RestoreReadyAfterSprint = false;
		m_RestoreReadyAfterRun = false;
		m_RestoreReadyAfterTurn = false;
	}

	public void SetProximityReadyBlock(bool _blocked)
	{
		if (m_ProximityBlocksReady == _blocked)
			return;
		m_ProximityBlocksReady = _blocked;
		LogPoseTransition(
			$"proximity block={_blocked} pose={ResolveEffectivePose()} wanted={m_WantedMode} restoreRun={m_RestoreReadyAfterRun}",
			debounce: false);
		RefreshEffectivePose(true);
		PushAnimatorPoseParameters();
		m_Vision?.NotifyWeaponReadyChanged(GetEffectiveIsReady());
	}

	public void SuppressReadyForSprintIfNeeded()
	{
		if (m_RestoreReadyAfterSprint)
			return;
		if (!IsWeaponEquipped())
		{
			m_RestoreReadyAfterSprint = false;
			return;
		}

		if (!TryBeginLocomotionPoseSuppress(LocomotionPoseSuppressReason.Run, "sprint"))
			return;
		m_RestoreReadyAfterSprint = true;
	}

	public void SuppressReadyForRunIfNeeded()
	{
		if (m_RestoreReadyAfterRun)
			return;
		if (!IsWeaponEquipped())
		{
			m_RestoreReadyAfterRun = false;
			return;
		}

		if (!TryBeginLocomotionPoseSuppress(LocomotionPoseSuppressReason.Run, "run"))
			return;
		m_RestoreReadyAfterRun = true;
	}

	public void TryRestoreReadyAfterSprint(bool _isStillSprinting)
	{
		if (_isStillSprinting || !m_RestoreReadyAfterSprint)
			return;
		m_RestoreReadyAfterSprint = false;
		WeaponPoseState from = ResolveEffectivePose();
		LogPoseTransition(
			$"RESTORE sprint {from} → wanted={m_ModeBeforeSuppression} stillSprint={_isStillSprinting} runPending={m_RestoreReadyAfterRun}",
			debounce: false);
		m_LastPoseLogKey = null;
		if (IsWeaponEquipped())
		{
			m_PoseLogSilence++;
			ApplyPoseModeWanted(m_ModeBeforeSuppression, false, true);
			m_PoseLogSilence--;
		}
	}

	public void TryRestoreReadyAfterRun(bool _isStillRunning)
	{
		if (_isStillRunning || !m_RestoreReadyAfterRun)
			return;
		m_RestoreReadyAfterRun = false;
		WeaponPoseState from = ResolveEffectivePose();
		LogPoseTransition(
			$"RESTORE run {from} → wanted={m_ModeBeforeSuppression} stillRun={_isStillRunning} sprintPending={m_RestoreReadyAfterSprint}",
			debounce: false);
		m_LastPoseLogKey = null;
		if (IsWeaponEquipped())
		{
			m_PoseLogSilence++;
			ApplyPoseModeWanted(m_ModeBeforeSuppression, false, true);
			m_PoseLogSilence--;
		}
	}

	/// <summary>Returns true when a turn suppress is active (applied now or already pending restore).</summary>
	public bool SuppressReadyForTurnIfNeeded()
	{
		if (m_RestoreReadyAfterTurn)
			return true;
		if (!IsWeaponEquipped())
			return false;
		if (IsWeaponReloadBusy())
			return false;
		if (!TryBeginLocomotionPoseSuppress(LocomotionPoseSuppressReason.Turn, "turn"))
			return false;
		m_RestoreReadyAfterTurn = true;
		return true;
	}

	public void TryRestoreReadyAfterTurn(bool _isStillTurning)
	{
		if (_isStillTurning || !m_RestoreReadyAfterTurn)
			return;
		if (IsWeaponReloadBusy())
			return;
		m_RestoreReadyAfterTurn = false;
		WeaponPoseState from = ResolveEffectivePose();
		LogPoseTransition(
			$"RESTORE turn {from} → wanted={m_ModeBeforeSuppression} stillTurn={_isStillTurning}",
			debounce: false);
		m_LastPoseLogKey = null;
		if (IsWeaponEquipped())
		{
			m_PoseLogSilence++;
			ApplyPoseModeWanted(m_ModeBeforeSuppression, false, true);
			m_PoseLogSilence--;
		}
	}

	public void SetToggleInputBlocked(bool _blocked) => m_BlockToggleInput = _blocked;
	public void SetKeyboardInputEnabled(bool _enabled) => m_EnableKeyboardInput = _enabled;

	public void BeginRocketLauncherFireReadyOverride()
	{
		if (m_RocketLauncherFireReadyOverride)
			return;
		m_RocketLauncherFireReadyOverride = true;
		ApplyPoseModeWanted(WeaponPoseMode.PointAim, true, true);
	}

	public void ForceNotReadyAfterRocketLauncherFire()
	{
		bool hadOverride = m_RocketLauncherFireReadyOverride;
		m_RocketLauncherFireReadyOverride = false;
		m_IsPeacefulNotReady = false;
		m_PeacefulCarryPose = WeaponPoseState.NotReady;
		if (!hadOverride && m_WantedMode == WeaponPoseMode.LowReady)
		{
			PushAnimatorPoseParameters();
			return;
		}

		bool didChange = m_WantedMode != WeaponPoseMode.LowReady;
		m_WantedMode = WeaponPoseMode.LowReady;
		m_EffectivePose = WeaponPoseState.LowReady;
		CancelDeferredReadyRestores();
		PushAnimatorPoseParameters();
		if (didChange)
		{
			NotifyPoseConsumers();
			m_Vision?.NotifyWeaponReadyChanged(false);
			ApplyVisualRefreshAfterReadyToggle();
		}
	}

	public void RebuildPoseCapabilityCache()
	{
		EnsureCombatRefs();
		WeaponDefinition weapon = null;
		WeaponAttachmentDefinition[] attachments = null;
		if (m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null)
		{
			weapon = m_WeaponRuntime.RuntimeState.WeaponDefinition;
			attachments = m_WeaponRuntime.RuntimeState.EquippedAttachments;
		}

		m_CapabilityCache = WeaponPoseAutoCapabilityBaker.Bake(
			weapon,
			attachments,
			m_CombatStats,
			m_IndividualTraits,
			m_CombatCondition,
			m_AcceptableHitRadiusMeters);

		m_NextAutoPoseTime = 0f;
		m_WantedMode = ClampModeToCapabilities(m_WantedMode);
		RefreshEffectivePose(true);
		m_LastAttachmentFingerprint = ComputeAttachmentFingerprint();
	}

	public void RefreshAnimatorPoseParameters() => PushAnimatorPoseParameters();

	public bool IsWeaponEquipped()
	{
		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		return current != null && current.IsEquipment &&
		       (current.EquipmentKind == EquipmentKind.Weapon || current.EquipmentKind == EquipmentKind.TurretWeapon);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		EnsureCombatRefs();

		if (m_Animator != null && m_LeftHandIk == null)
			m_LeftHandIk = m_Animator.GetComponent<AnimatorHandIk>();
		if (m_Animator != null && m_LeftHandIk == null)
			m_LeftHandIk = m_Animator.gameObject.AddComponent<AnimatorHandIk>();

		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = gameObject.AddComponent<UnitEquippedWeaponPose>();
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();

		if (GetComponent<UnitProximityReadyController>() == null)
			gameObject.AddComponent<UnitProximityReadyController>();
		if (GetComponent<UnitEquippedWeaponPoseRuntimeTuner>() == null)
			gameObject.AddComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
	}

	private void OnEnable()
	{
		m_LastEquipped = null;
		m_RestoreReadyAfterSprint = false;
		m_RestoreReadyAfterRun = false;
		m_ReadyTransitionAudioArmed = false;
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged += HandleEquipmentChanged;
		RebuildPoseCapabilityCache();
		ApplyDefaultEquippedPose();
		if (m_HasStarted)
			ArmReadyTransitionAudioBaseline();
	}

	private void OnDisable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
	}

	private void Start()
	{
		m_HasStarted = true;
		ArmReadyTransitionAudioBaseline();
	}

	private void Update()
	{
		ItemDefinition current = m_Equipment != null ? m_Equipment.EquippedDefinition : null;
		if (!ReferenceEquals(current, m_LastEquipped))
		{
			m_LastEquipped = current;
			if (!m_RocketLauncherFireReadyOverride)
			{
				if (m_RestoreReadyAfterRun || m_RestoreReadyAfterSprint)
				{
					LogPoseTransition(
						$"EQUIP reset cancels restore run={m_RestoreReadyAfterRun} sprint={m_RestoreReadyAfterSprint}",
						debounce: false);
				}

				m_RestoreReadyAfterSprint = false;
				m_RestoreReadyAfterRun = false;
				ApplyDefaultEquippedPose();
			}

			RebuildPoseCapabilityCache();
			PushAnimatorPoseParameters();
		}
		else if (ComputeAttachmentFingerprint() != m_LastAttachmentFingerprint)
			RebuildPoseCapabilityCache();

		RefreshEffectivePose(false);

		TryApplyPendingLowReady();

		if (!CanUseDirectKeyboardInput() || !IsWeaponEquipped())
			return;
		if (m_BlockToggleInput)
			return;
		if (!WasKeyPressedThisFrame(m_ToggleReadyKey))
			return;

		if (IsControlHeld())
		{
			HandleCtrlEToggle();
			return;
		}

		CycleCombatPose();
	}
	#endregion

	#region Private Methods
	private void HandleEquipmentChanged() => RebuildPoseCapabilityCache();

	private int ComputeAttachmentFingerprint()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		WeaponAttachmentDefinition[] attachments = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.EquippedAttachments
			: null;
		if (attachments == null || attachments.Length == 0)
			return 0;

		int hash = attachments.Length;
		for (int i = 0; i < attachments.Length; i++)
			hash = unchecked(hash * 31 + (attachments[i] != null ? attachments[i].GetEntityId().GetHashCode() : 0));
		return hash;
	}

	private void EnsureCombatRefs()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_IndividualTraits == null)
			m_IndividualTraits = GetComponent<UnitIndividualTraits>();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
	}

	private enum LocomotionPoseSuppressReason
	{
		Run,
		Turn,
	}

	private WeaponPoseMode ClampModeToCapabilities(WeaponPoseMode _mode) => _mode;

	private bool TryBeginLocomotionPoseSuppress(LocomotionPoseSuppressReason _reason, string _source)
	{
		WeaponPoseState current = ResolveEffectivePose();
		if (!TryGetLocomotionSuppressPose(_reason, current, out WeaponPoseState target, out _))
			return false;
		if (current == target)
			return false;

		m_ModeBeforeSuppression = m_WantedMode;
		m_PoseLogSilence++;
		ApplyLocomotionSuppressPose(target);
		m_PoseLogSilence--;
		LogPoseTransition(
			$"APPLY {_source} {current}→{ResolveEffectivePose()} saveWanted={m_ModeBeforeSuppression} nowWanted={m_WantedMode} " +
			$"peaceful={m_IsPeacefulNotReady} {FormatLocomotionBits()}",
			debounce: false);
		return true;
	}

	private static bool TryGetLocomotionSuppressPose(
		LocomotionPoseSuppressReason _reason,
		WeaponPoseState _current,
		out WeaponPoseState _target,
		out string _skipReason)
	{
		_target = _current;
		_skipReason = null;
		if (_current.IsPeacefulCarryPose())
		{
			_skipReason = "peaceful-stay";
			return false;
		}

		if (_reason == LocomotionPoseSuppressReason.Run)
		{
			if (_current == WeaponPoseState.LowReady)
			{
				_skipReason = "LowReady-no-downgrade";
				return false;
			}

			if (_current.IsHipFireHold())
			{
				_target = WeaponPoseState.NotReady;
				return true;
			}

			if (_current.IsCombatPose())
			{
				_target = WeaponPoseState.HighReady;
				return true;
			}

			_skipReason = "no-run-mapping";
			return false;
		}

		switch (_current)
		{
			case WeaponPoseState.LowReady:
				_target = WeaponPoseState.LowReady;
				return true;
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
				_target = WeaponPoseState.NotReady;
				return true;
			case WeaponPoseState.PreAim:
			case WeaponPoseState.PointAim:
			case WeaponPoseState.Aiming:
			case WeaponPoseState.HighReady:
				_target = WeaponPoseState.HighReady;
				return true;
			default:
				_skipReason = "no-turn-mapping";
				return false;
		}
	}

	private void ApplyLocomotionSuppressPose(WeaponPoseState _target)
	{
		if (_target == WeaponPoseState.NotReady)
		{
			ApplyPeacefulCarry(WeaponPoseState.NotReady);
			return;
		}

		WeaponPoseMode mode = _target == WeaponPoseState.HighReady
			? WeaponPoseMode.HighReady
			: WeaponPoseMode.LowReady;
		ApplyPoseModeWanted(mode, false, true);
	}

	private WeaponPoseMode GetNextCombatCycleMode(WeaponPoseMode _current)
	{
		if (m_IsPeacefulNotReady || !_current.IsManualCombatMode())
			return WeaponPoseMode.LowReady;

		switch (_current)
		{
			case WeaponPoseMode.LowReady:
				return WeaponPoseMode.HighReady;
			case WeaponPoseMode.HighReady:
				return WeaponPoseMode.PreAim;
			case WeaponPoseMode.PreAim:
				return WeaponPoseMode.Aiming;
			case WeaponPoseMode.Aiming:
				return WeaponPoseMode.PointAim;
			case WeaponPoseMode.PointAim:
				return WeaponPoseMode.LowReady;
			default:
				return WeaponPoseMode.LowReady;
		}
	}

	private void HandleCtrlEToggle() => TogglePeacefulNotReady();

	private void EnterPeacefulCarry(WeaponPoseState _pose)
	{
		if (!_pose.IsPeacefulCarryPose())
			return;
		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.CarryingFallen))
			return;
		if (IsWeaponActionBusy())
			return;

		ApplyPeacefulCarry(_pose);
	}

	private void ApplyPeacefulCarry(WeaponPoseState _pose)
	{
		CancelDeferredReadyRestores();
		m_IsPeacefulNotReady = true;
		m_PeacefulCarryPose = _pose;
		m_WantedMode = WeaponPoseMode.LowReady;
		WeaponPoseState previous = m_EffectivePose;
		m_EffectivePose = _pose;
		if (previous != m_EffectivePose)
		{
			PlayPoseEnterSoundIfNeeded(previous, m_EffectivePose);
			NotifyPoseConsumers();
			m_Vision?.NotifyWeaponReadyChanged(false);
			if (m_PoseLogSilence == 0)
			{
				LogPoseTransition(
					$"TRANSITION peaceful {previous}→{m_EffectivePose} wanted={m_WantedMode}",
					debounce: false);
			}
		}

		PushAnimatorPoseParameters();
	}

	private void ExitPeacefulNotReady()
	{
		m_IsPeacefulNotReady = false;
		m_PeacefulCarryPose = WeaponPoseState.NotReady;
		ApplyPoseModeWanted(WeaponPoseMode.LowReady, false, true);
	}

	private void TryApplyPendingLowReady()
	{
		if (!m_PendingLowReady || IsWeaponActionBusy())
			return;
		m_PendingLowReady = false;
		CancelDeferredReadyRestores();
		ApplyPoseModeWanted(WeaponPoseMode.LowReady, false, true);
	}

	private bool IsWeaponActionBusy()
	{
		if (m_MagazineLoadingController != null &&
		    (m_MagazineLoadingController.IsLoadingMagazine || m_MagazineLoadingController.IsLoadingAllMagazines))
			return true;
		if (m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			return true;
		return false;
	}

	private bool CanUseDirectKeyboardInput()
	{
		if (!m_EnableKeyboardInput)
			return false;
		if (m_Team == null)
			return true;
		if (m_Team.Team != UnitTeamId.Player)
			return false;
		if (TryGetComponent(out UnitVehicleMountState mount) && mount.IsMounted)
			return false;
		return true;
	}

	private static bool WasKeyPressedThisFrame(Key _key)
	{
		for (int i = 0; i < InputSystem.devices.Count; i++)
		{
			if (InputSystem.devices[i] is not Keyboard kb)
				continue;
			KeyControl key = kb[_key];
			if (key != null && key.wasPressedThisFrame)
				return true;
		}

		return false;
	}

	private static bool IsControlHeld()
	{
		for (int i = 0; i < InputSystem.devices.Count; i++)
		{
			if (InputSystem.devices[i] is not Keyboard kb)
				continue;
			if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)
				return true;
		}

		return false;
	}

	private bool GetEffectiveIsReady()
	{
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return true;
		if (m_ProximityBlocksReady)
			return false;
		return ResolveEffectivePose().CanFireFromPose();
	}

	private WeaponPoseState ResolveEffectivePose()
	{
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return WeaponPoseState.Aiming;
		if (m_IsPeacefulNotReady)
			return m_PeacefulCarryPose.IsPeacefulCarryPose()
				? m_PeacefulCarryPose
				: WeaponPoseState.NotReady;
		if (m_ProximityBlocksReady)
			return WeaponPoseState.HighReady;
		return m_EffectivePose;
	}

	private void RefreshEffectivePose(bool _forceNotify)
	{
		WeaponPoseState previous = m_EffectivePose;
		m_EffectivePose = m_IsPeacefulNotReady
			? (m_PeacefulCarryPose.IsPeacefulCarryPose() ? m_PeacefulCarryPose : WeaponPoseState.NotReady)
			: ComputePoseFromWantedMode(m_WantedMode);
		if (_forceNotify || previous != m_EffectivePose)
		{
			PlayPoseEnterSoundIfNeeded(previous, m_EffectivePose);
			NotifyPoseConsumers();
			PushAnimatorPoseParameters();
			if (previous != m_EffectivePose && m_PoseLogSilence == 0)
			{
				string conflict = HasPendingReadyRestore
					? $" CONFLICT restore pending run={m_RestoreReadyAfterRun} sprint={m_RestoreReadyAfterSprint} turn={m_RestoreReadyAfterTurn} saveWanted={m_ModeBeforeSuppression}"
					: string.Empty;
				LogPoseTransition(
					$"TRANSITION refresh {previous}→{m_EffectivePose} wanted={m_WantedMode} auto={m_WantedMode == WeaponPoseMode.Auto}{conflict}",
					debounce: false);
			}
		}
	}

	private WeaponPoseState ComputePoseFromWantedMode(WeaponPoseMode _mode)
	{
		switch (_mode)
		{
			case WeaponPoseMode.HighReady:
				return WeaponPoseState.HighReady;
			case WeaponPoseMode.PreAim:
				return WeaponPoseState.PreAim;
			case WeaponPoseMode.HipFire:
				return ResolveHipFireHoldPose();
			case WeaponPoseMode.PointAim:
				return WeaponPoseState.PointAim;
			case WeaponPoseMode.Aiming:
				return WeaponPoseState.Aiming;
			case WeaponPoseMode.Auto:
				WeaponPoseState autoPose = ResolveAutoPose();
				return autoPose == WeaponPoseState.HipFire ? ResolveHipFireHoldPose() : autoPose;
			default:
				return WeaponPoseState.LowReady;
		}
	}

	private WeaponPoseState ResolveHipFireHoldPose()
	{
		if (!ShouldUseHipFireWalkHold())
			return WeaponPoseState.HipFire;
		if (IsCrouchHoldNow())
			return WeaponPoseState.HipFireCrouchWalk;
		return WeaponPoseState.HipFireWalk;
	}

	private bool ShouldUseHipFireWalkHold()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			return m_RuntimeTuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireWalk
			       || m_RuntimeTuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk;
		}
		if (IsFastMoveModeNow())
			return false;
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return false;
		return m_Animator != null && m_Animator.GetFloat(s_NavSpeed) >= 0.055f;
	}

	private bool IsCrouchHoldNow()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
			return m_RuntimeTuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HipFireCrouchWalk
			       || m_RuntimeTuner.ActivePosture == UnitEquippedWeaponPoseRuntimeTuner.TuningPosture.Crouch;
		return m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Crouch;
	}

	private WeaponPoseState ResolveAutoPose()
	{
		if (!m_CapabilityCache.IsValid)
			RebuildPoseCapabilityCache();

		bool hasTarget = false;
		float distance = -1f;
		Transform target = null;
		if (m_TargetSelector != null)
		{
			target = m_TargetSelector.SelectedTarget;
			if (target != null)
			{
				hasTarget = true;
				distance = Vector3.Distance(transform.position, target.position);
				NotifyCombatAlert();
			}
		}

		bool hasAlert = HasCombatAlert;
		bool due = Time.time >= m_NextAutoPoseTime;
		bool identityChanged = hasTarget != m_CachedAutoHasTarget
			|| target != m_CachedAutoTarget
			|| hasAlert != m_CachedAutoAlert;
		if (!due && !identityChanged)
			return m_CachedAutoPose;

		m_CachedAutoPose = m_CapabilityCache.ResolveAutoPose(new WeaponAutoPoseContext
		{
			HasTarget = hasTarget,
			DistanceMeters = distance,
			HasCombatAlert = hasAlert,
			CurrentPose = m_EffectivePose,
		});
		m_CachedAutoHasTarget = hasTarget;
		m_CachedAutoTarget = target;
		m_CachedAutoAlert = hasAlert;
		m_NextAutoPoseTime = Time.time + PerceptionWorkStagger.NextIntervalSeconds(
			gameObject.GetEntityId().GetHashCode());
		return m_CachedAutoPose;
	}

	private void ApplyPoseModeWanted(WeaponPoseMode _mode, bool _forceWalkIfNeeded, bool _refreshImmediately)
	{
		_mode = ClampModeToCapabilities(_mode);
		if (!IsWeaponEquipped() && !(m_RocketLauncherFireReadyOverride && _mode != WeaponPoseMode.LowReady))
		{
			m_WantedMode = WeaponPoseMode.LowReady;
			m_EffectivePose = WeaponPoseState.LowReady;
			m_IsPeacefulNotReady = false;
			m_PeacefulCarryPose = WeaponPoseState.NotReady;
			m_RestoreReadyAfterSprint = false;
			m_RestoreReadyAfterRun = false;
			PushAnimatorPoseParameters();
			return;
		}

		m_IsPeacefulNotReady = false;
		m_PeacefulCarryPose = WeaponPoseState.NotReady;

		WeaponPoseState previousPose = m_EffectivePose;
		bool modeChanged = m_WantedMode != _mode;
		if (modeChanged && _mode == WeaponPoseMode.Auto)
			m_NextAutoPoseTime = 0f;
		m_WantedMode = _mode;
		m_EffectivePose = ComputePoseFromWantedMode(_mode);
		bool poseChanged = previousPose != m_EffectivePose;

		if ((modeChanged || poseChanged) && m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			m_WeaponReloadController.SyncAimReloadClipForWeaponReadyChange();

		if (m_EffectivePose.CanFireFromPose() && _forceWalkIfNeeded && IsFastMoveModeNow())
		{
			LogPoseTransition(
				$"CONFLICT ForceWalk pose={m_EffectivePose} canFire during run/sprint wanted={_mode} restoreRun={m_RestoreReadyAfterRun} restoreSprint={m_RestoreReadyAfterSprint}",
				debounce: false);
			ForceWalkMoveModeOnAllLocomotionDrivers();
		}

		if (poseChanged)
		{
			PlayPoseEnterSoundIfNeeded(previousPose, m_EffectivePose);
			NotifyPoseConsumers();
			m_Vision?.NotifyWeaponReadyChanged(m_EffectivePose.CanFireFromPose());
		}

		if ((modeChanged || poseChanged) && m_PoseLogSilence == 0)
		{
			string conflict = HasPendingReadyRestore
				? $" CONFLICT restore pending run={m_RestoreReadyAfterRun} sprint={m_RestoreReadyAfterSprint} turn={m_RestoreReadyAfterTurn} saveWanted={m_ModeBeforeSuppression}"
				: string.Empty;
			LogPoseTransition(
				$"TRANSITION {previousPose}→{m_EffectivePose} wanted={_mode} modeChanged={modeChanged} forceWalk={_forceWalkIfNeeded}{conflict}",
				debounce: false);
		}

		PushAnimatorPoseParameters();
	}

	private void NotifyPoseConsumers()
	{
		m_EquippedWeaponPose?.OnWeaponReadyStateChanged();
		m_LeftHandIk?.OnWeaponReadyStateChanged();
	}

	private void ApplyVisualRefreshAfterReadyToggle()
	{
		if (ShouldReplayLocomotionCrossfadeAfterReadyChange())
			m_AnimatorWeaponMode.ReplayLocomotionIdleCrossfade();
	}

	private bool ShouldReplayLocomotionCrossfadeAfterReadyChange()
	{
		if (m_Animator == null || m_AnimatorWeaponMode == null)
			return false;
		if (m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine)
			return false;
		if (m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			return false;
		return m_Animator.GetFloat(s_NavSpeed) < 0.055f;
	}

	private void ForceWalkMoveModeOnAllLocomotionDrivers()
	{
		m_ClickToMove?.ForceWalkMoveMode();
		m_LocomotionDriver?.ForceWalkMoveMode();
	}

	private bool IsSprintingNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.IsSprintMoveMode)
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.IsSprintMoveMode)
			return true;
		if (m_ClickToMove == null && m_LocomotionDriver == null && m_Animator != null)
			return m_Animator.GetInteger(s_LocomotionTier) == 2;
		return false;
	}

	private bool IsRunningNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.IsRunMoveMode)
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.IsRunMoveMode)
			return true;
		return false;
	}

	private bool IsFastMoveModeNow() => IsSprintingNow() || IsRunningNow();

	private bool IsWeaponReloadBusy()
	{
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		return m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy;
	}

	private void PushAnimatorPoseParameters()
	{
		bool nextReady = ResolveWeaponReadyParameter();
		int nextStandIdle = ResolveWeaponStandIdleStyle(ResolveStandIdlePoseSource());
		bool standIdleChanged = m_LastPushedStandIdle != nextStandIdle;
		bool wasReady = m_LastPushedWeaponReady;
		m_LastPushedWeaponReady = nextReady;
		m_LastPushedStandIdle = nextStandIdle;

		if (m_Animator == null)
			return;

		m_Animator.SetBool(s_WeaponReady, nextReady);
		m_Animator.SetInteger(s_WeaponStandIdle, nextStandIdle);

		if (m_EquippedWeaponPose != null && m_EquippedWeaponPose.ShouldLogHighReadyToPreAim)
		{
			Debug.Log(
				$"[HR→PreAim ANIM] unit={name} push WeaponReady={nextReady} StandIdle={nextStandIdle} " +
				$"(wasReady={wasReady} idleChanged={standIdleChanged}) " +
				$"effective={ResolveEffectivePose()} ikSide={ResolveStandIdlePoseSource()} " +
				$"canFire={ResolveEffectivePose().CanFireFromPose()} blending={m_EquippedWeaponPose.IsPoseBlendAnimating} " +
				$"{m_EquippedWeaponPose.CurrentPose}→{m_EquippedWeaponPose.TargetPose}",
				this);
		}

		if ((standIdleChanged || wasReady != nextReady) && ShouldReplayBaseLayerForPoseChange())
			m_AnimatorWeaponMode?.ReplayLocomotionIdleCrossfade();
	}

	private bool ShouldReplayBaseLayerForPoseChange()
	{
		if (m_Animator == null)
			return false;
		if (m_RuntimeTuner != null && m_RuntimeTuner.ShouldFreezeWalkAnimator)
			return false;
		if (m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine)
			return false;
		if (m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy)
			return false;
		return true;
	}

	private WeaponPoseState ResolveStandIdlePoseSource()
	{
		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			if (m_RuntimeTuner.ActiveTarget == UnitEquippedWeaponPoseRuntimeTuner.TuningTarget.HandsFrozen)
				return WeaponPoseState.NotReady;
			return m_RuntimeTuner.ActiveWeaponPoseState;
		}

		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.GetEffectivePoseForIk();

		return ResolveEffectivePose();
	}

	private static int ResolveWeaponStandIdleStyle(WeaponPoseState _pose) =>
		_pose.UsesRelaxedStandIdle()
			? (int)UnitAnimatorWeaponMode.WeaponStandIdleStyle.RelaxedIdle
			: (int)UnitAnimatorWeaponMode.WeaponStandIdleStyle.AimIdle;

	private void PushWeaponReadyParameter() => PushAnimatorPoseParameters();

	private bool ResolveWeaponReadyParameter()
	{
		bool allow = IsWeaponEquipped() || m_RocketLauncherFireReadyOverride;
		if (!allow)
			return false;
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return true;
		WeaponPoseState pose = ShouldWeaponReadyFollowEffectivePose()
			? ResolveEffectivePose()
			: ResolveStandIdlePoseSource();

		if (IsSprintingNow())
			return pose.CanFireFromPose();

		if (!IsStandingIdleNow() &&
		    (pose == WeaponPoseState.LowReady || pose == WeaponPoseState.HighReady))
			return true;

		return pose.CanFireFromPose();
	}

	/// <summary>
	/// Run/sprint (and pending restore) must switch locomotion clips with the gameplay pose.
	/// Standing E-cycle must not: HighReady is !CanFire, and flipping WeaponReady at blend start
	/// CrossFades Aim_Point → RelaxedIdle while IK still interpolates the authored slot.
	/// </summary>
	private bool ShouldWeaponReadyFollowEffectivePose() =>
		HasPendingReadyRestore || !IsStandingIdleNow();

	private void ArmReadyTransitionAudioBaseline()
	{
		m_LastPushedWeaponReady = ResolveWeaponReadyParameter();
		m_LastAudioPose = m_EffectivePose;
		m_ReadyTransitionAudioArmed = true;
	}

	private void PlayPoseEnterSoundIfNeeded(WeaponPoseState _from, WeaponPoseState _to)
	{
		if (!m_ReadyTransitionAudioArmed || _from == _to)
			return;
		m_LastAudioPose = _to;
		if (IsSilentPreAimAimingBlend(_from, _to))
			return;
		AudioClip clip = m_EnterLowReadyClip;
		if (clip == null || m_ReadyTransitionVolume <= 0f)
			return;
		UnitNonFireAudioUtility.PlayAtPoint(
			clip,
			transform.position,
			m_ReadyTransitionVolume,
			m_ReadyTransitionSpatialMaxDistance);
	}

	private static bool IsSilentPreAimAimingBlend(WeaponPoseState _from, WeaponPoseState _to)
	{
		if (_from.IsHipFireHold() && _to.IsHipFireHold())
			return true;
		return (_from == WeaponPoseState.PreAim && _to == WeaponPoseState.Aiming)
			|| (_from == WeaponPoseState.Aiming && _to == WeaponPoseState.PreAim);
	}

	private bool ShouldLogPoseTransitions()
	{
		if (!m_LogPoseTransitions)
			return false;
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		return m_RtsMember == null || m_RtsMember.IsSelected;
	}

	private void LogPoseTransition(string _message, bool debounce, string debounceKey = null)
	{
		if (!ShouldLogPoseTransitions())
			return;
		if (debounce)
		{
			string key = debounceKey ?? _message;
			if (m_LastPoseLogKey == key)
				return;
			m_LastPoseLogKey = key;
		}

		Debug.Log($"[PoseRun] {name} {_message} | {FormatBarrelPitch()} ik={FormatIkPose()}", this);
		DrawBarrelDebugRay(1.2f);
	}

	private string FormatIkPose()
	{
		if (m_EquippedWeaponPose == null)
			return "none";
		return $"{m_EquippedWeaponPose.CurrentPose}→{m_EquippedWeaponPose.TargetPose} blend={m_EquippedWeaponPose.IsPoseBlendAnimating}";
	}

	private string FormatLocomotionBits()
	{
		int tier = m_Animator != null ? m_Animator.GetInteger(s_LocomotionTier) : -1;
		float nav = m_Animator != null ? m_Animator.GetFloat(s_NavSpeed) : -1f;
		bool weaponReady = m_Animator != null && m_Animator.GetBool(s_WeaponReady);
		return $"tier={tier} nav={nav:F2} animReady={weaponReady} clickRun={IsRunningNow()} clickSprint={IsSprintingNow()} proximity={m_ProximityBlocksReady}";
	}

	private string FormatBarrelPitch()
	{
		if (!TryGetBarrelPitch(out _, out float pitchDeg, out string dir))
			return "barrel=none";
		return $"луч {dir} pitch={pitchDeg:F1}°";
	}

	private void DrawBarrelDebugRay(float _durationSeconds)
	{
		if (!TryGetBarrelPitch(out Transform barrel, out _, out string dir))
			return;
		Color color = dir == "ВВЕРХ"
			? new Color(1f, 0.45f, 0.1f)
			: dir == "ВНИЗ"
				? new Color(0.25f, 1f, 0.3f)
				: Color.cyan;
		Debug.DrawRay(barrel.position, barrel.forward * 5f, color, _durationSeconds, false);
	}

	private bool TryGetBarrelPitch(out Transform _barrel, out float _pitchDeg, out string _dir)
	{
		_barrel = null;
		_pitchDeg = 0f;
		_dir = "none";
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon != null)
			_barrel = weapon.FireOriginTransform != null ? weapon.FireOriginTransform : weapon.BarrelTransform;
		if (_barrel == null)
			return false;

		Vector3 fwd = _barrel.forward;
		float horiz = Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z);
		_pitchDeg = Mathf.Atan2(fwd.y, Mathf.Max(horiz, 0.0001f)) * Mathf.Rad2Deg;
		_dir = _pitchDeg > c_BarrelPitchLevelDeadzoneDeg
			? "ВВЕРХ"
			: _pitchDeg < -c_BarrelPitchLevelDeadzoneDeg
				? "ВНИЗ"
				: "ГОРИЗОНТ";
		return true;
	}
	#endregion
}
