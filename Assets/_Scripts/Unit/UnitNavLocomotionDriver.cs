using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.AI;
#pragma warning disable CS0414

/// <summary>
/// Общий NavMesh/animator/facing-драйвер без пользовательского ввода.
/// Подходит для ИИ и для будущего переиспользования логики игрока.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public sealed class UnitNavLocomotionDriver : MonoBehaviour
{
	#region Types
	public enum MoveTier
	{
		Walk,
		Run,
		Sprint
	}
	#endregion

	#region Constants
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);
	private static readonly int s_NavStrafe = Animator.StringToHash(UnitClickToMove.ParamNavStrafe);
	private static readonly int s_NavForward = Animator.StringToHash(UnitClickToMove.ParamNavForward);
	private static readonly int s_LocomotionTier = Animator.StringToHash(UnitClickToMove.ParamLocomotionTier);
	private static readonly int s_LocomotionTierBlend = Animator.StringToHash(UnitClickToMove.ParamLocomotionTierBlend);
	private static readonly int s_WeaponMode = Animator.StringToHash(UnitAnimatorWeaponMode.ParamWeaponMode);

	private static readonly string[] s_UnarmedStanceBlockingStateNames =
	{
		"Unarmed_Prone2Idle",
		"Unarmed_Prone2Crouch",
		"Unarmed_Idle2Prone",
		"Unarmed_Crouch2Prone",
	};

	private static readonly string[] s_RifleStanceBlockingStateNames =
	{
		"Rifle_Prone2Idle",
		"Rifle_Prone2Crouch",
		"Rifle_Idle2Prone",
		"Rifle_Crouch2Prone",
	};

	#endregion

	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitGrenadeThrowController m_GrenadeThrowController;
	[SerializeField] private UnitWeaponAiming m_WeaponAiming;
	[SerializeField] private UnitSpineHorizontalAim m_SpineHorizontalAim;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[SerializeField, Min(0.01f)] private float m_NavMeshSampleRadius = 2f;

	[Header("NavMeshAgent")]
	[SerializeField, Min(0f)] private float m_StoppingDistance = 0.05f;
	[SerializeField, Min(1f)] private float m_AgentAcceleration = 40f;
	[SerializeField] private bool m_WarpToNavMeshOnStart = true;
	[SerializeField, Min(0.5f)] private float m_WarpSearchRadius = 12f;

	[Header("Speeds")]
	[SerializeField, Min(0.1f)] private float m_WalkSpeed = 1.5f;
	[SerializeField, Min(0.1f)] private float m_RunSpeed = 4.6f;
	[Tooltip("Скорость бега без оружия.")]
	[SerializeField, Min(0.1f)] private float m_RunSpeedNoWeapon = 3.4f;
	[SerializeField, Min(0.1f)] private float m_SprintSpeed = 7.25f;
	[SerializeField, Min(0.1f)] private float m_CrouchWalkSpeed = 1.15f;
	[SerializeField, Min(0.05f)] private float m_ProneCrawlSpeed = 0.5f;

	[Header("Speed smoothing (NavMeshAgent)")]
	[Tooltip("Плавность смены реальной скорости NavMeshAgent при переключении Walk/Run/Sprint. 0 = мгновенно.")]
	[SerializeField, Min(0f)] private float m_AgentSpeedSmoothSeconds = 0.15f;
	[Tooltip("Отдельная плавность при снижении скорости (обычно чуть больше). 0 = мгновенно.")]
	[SerializeField, Min(0f)] private float m_AgentSpeedSmoothSecondsDecel = 0.2f;

	[Header("Stand Up")]
	[SerializeField, Min(0f)] private float m_AfterStandUpWalkAnimHoldSeconds = 0.32f;
	[SerializeField, Range(0.01f, 0.054f)] private float m_StandUpNavSpeedAnimatorCeiling = 0.042f;

	[Header("Rotation")]
	[SerializeField, Min(0.1f)] private float m_RotateSpeed = 6f;
	public float RotateSpeed { get => m_RotateSpeed; set => m_RotateSpeed = value; }
	[SerializeField, Min(0.02f)] private float m_FacingTargetYawSmoothTime = 0.18f;
	[Tooltip("Legacy: раньше ограничивал engage внутри конуса стрелки. Сейчас цель всегда приоритетнее стрелки; поле не используется.")]
	[SerializeField, Range(5f, 90f)] private float m_ManualFacingTargetConeHalfAngle = 30f;
	[Tooltip("Legacy: раньше handoff стрелка→engage. Сейчас цель всегда приоритетнее стрелки; поле не используется.")]
	[SerializeField, Range(0.5f, 15f)] private float m_ManualFacingEngageHandoffDegrees = 3f;

	[Header("Combat: steady stance while firing")]
	[Tooltip("При engage и активной команде огня, пока агент почти стоит — NavSpeed=0, NavForward=1, NavStrafe=0, чтобы не мешались клипы шага с прицелом.")]
	[SerializeField] private bool m_SteadyAnimatorLocomotionWhileEngagingAndFiring = true;
	[SerializeField] private bool m_SteadyLocomotionRequiresFireCommand = true;
	[SerializeField] private bool m_SteadyLocomotionOnlyWhenNearlyStationary = true;

	[Header("Animator smoothing")]
	[SerializeField, Min(0.01f)] private float m_SpeedSmoothTime = 0.12f;
	[SerializeField, Min(0.005f)] private float m_SpeedSmoothTimeAccelerate = 0.035f;
	[SerializeField, Min(0.01f)] private float m_DirectionSmoothTime = 0.14f;
	[SerializeField, Min(0.005f)] private float m_DirectionSmoothTimeMoveStart = 0.055f;
	[SerializeField, Min(0f)] private float m_EngageDirectionSmoothTime = 0.055f;
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;
	[SerializeField, Range(0.35f, 1f)] private float m_StartNavSpeedFloor = 0.88f;
	[Tooltip("StartNavSpeedFloor не выше фактической скорости + этот запас (0–1 по шкале Sprint). Убирает скольжение, когда параметр «убегает» вперёд тела.")]
	[SerializeField, Range(0f, 0.25f)] private float m_StartNavSpeedFloorMaxLeadOverVelocity = 0.1f;
	[SerializeField, Min(0f)] private float m_BrakeAnimLeadDistance = 0.9f;

	[Header("Stopping")]
	[Tooltip("Если от точки назначения осталось меньше этого расстояния и скорость почти ноль — принудительный стоп.")]
	[SerializeField, Min(0.05f)] private float m_EarlyArrivalDistance = 0.15f;

	[Header("Animator playback sync")]
	[Tooltip("Множитель Animator.speed ≈ (скорость тела / NavSpeed в blend tree). Компонент RtsUnitMember умножает свой вариационный speed на это значение.")]
	[SerializeField] private bool m_SyncAnimatorPlaybackToGroundSpeed = true;
	[SerializeField, Range(0.4f, 1.5f)] private float m_PlaybackSyncMin = 0.55f;
	[SerializeField, Range(0.5f, 2f)] private float m_PlaybackSyncMax = 1.45f;

	[Header("Debug: facing")]
	[Tooltip("Консоль [Facing]: корень, спина, barrel-centric, коррекция оружия. Только выбранный юнит.")]
	[SerializeField] private bool m_LogFacingSystem;
	[SerializeField, Min(0.05f)] private float m_LogFacingIntervalSeconds = 0.2f;

	[Header("Engage pose settle")]
	[Tooltip("If |body↔barrel| exceeds this, engage turns the root toward the target (not the bore) until the ready pose settles.")]
	[SerializeField, Range(10f, 90f)] private float m_EngageRootFacingWhenBarrelOffsetExceeds = 25f;

	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;
	private MoveTier m_Mode = MoveTier.Walk;
	private LocomotionStance m_LastStance = LocomotionStance.Standing;
	private float m_SmoothSpeed01;
	private float m_SmoothSpeedVel;
	private Vector2 m_SmoothDir;
	private Vector2 m_SmoothDirVel;
	private float m_EngageYawVelocity;
	private bool m_TurnSuppressedReady;
	private Vector3 m_LastLocomotionWorldDirection;
	private float m_PostStandLowNavSpeedUntil = -1f;
	private bool m_HasPendingNavOrder;
	private Vector3 m_PendingNavDestination;
	private bool m_PendingNavOverridesMode;
	private MoveTier m_PendingNavMode;
	private float m_TargetAgentSpeed;
	public float FormationSpeedMultiplier = 1f;
	public float StaminaSpeedMultiplier = 1f;
	[System.NonSerialized] public float? OverrideFacingAngle;
	/// <summary>В high ready — world yaw линии огня (ствол); иначе yaw корня.</summary>
	public bool SuppressEarlyArrivalStop { get; set; }
	private bool m_StanceMovementWasBlocked;
	private RtsUnitMember m_CachedRtsMember;
	private UnitClickToMove m_ClickToMove;
	private float m_NextFacingLogTime;
	private string m_FacingLogMode = "none";
	private string m_FacingLogDetail = "";
	private float m_FacingLogRootDelta;
	private string m_LastEmittedFacingMode;
	#endregion

	#region Public Properties
	public bool IsSprintMoveMode => IsSprintActive();
	public bool IsRunMoveMode => IsRunActive();
	public bool IsWalkOrRunMoveMode => m_Mode == MoveTier.Walk || m_Mode == MoveTier.Run;
	public bool HasMoveIntent => IsConscious() && IsNavAgentOperational() && HasActiveMoveIntent();
	/// <summary>Множитель скорости проигрывания клипов (1 = без подстройки). См. <see cref="RtsUnitMember"/>.</summary>
	public float AnimatorPlaybackSpeedMultiplier { get; private set; } = 1f;
	#endregion

	private bool IsSprintActive()
	{
		if (m_Mode != MoveTier.Sprint)
			return false;
		if (HasActiveMoveIntent() || NavAgentHasIncompletePath())
			return true;
		if (!IsNavAgentOperational())
			return false;

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		return velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon;
	}

	private bool IsRunActive()
	{
		if (m_Mode != MoveTier.Run)
			return false;
		if (HasActiveMoveIntent() || NavAgentHasIncompletePath())
			return true;
		if (!IsNavAgentOperational())
			return false;

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		return velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon;
	}

	#region Public Methods
	public void ForceWalkMoveMode()
	{
		if (m_Mode == MoveTier.Walk)
			return;

		m_Mode = MoveTier.Walk;
		SnapAnimatorNavSpeedToCurrentVelocity();
		if (m_Agent != null)
			ApplyTierSpeed();
		m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	/// <summary>
	/// After releasing a manual facing override in low ready: smooth root rotation via normal UpdateFacing.
	/// </summary>
	public void BeginNotReadyMovementFacingRealign()
	{
		m_TurnSuppressedReady = false;
		m_EngageYawVelocity = 0f;
		m_SmoothDirVel = Vector2.zero;

		if (!TryGetMovementFacingDirection(out Vector3 worldDirection))
		{
			m_SmoothDir = new Vector2(0f, 1f);
			return;
		}

		Vector3 localDirection = transform.InverseTransformDirection(worldDirection);
		Vector2 targetDir = new Vector2(localDirection.x, localDirection.z);
		m_SmoothDir = targetDir.sqrMagnitude > 1e-6f ? targetDir.normalized : new Vector2(0f, 1f);
	}

	public void SetMoveTier(MoveTier _moveTier)
	{
		if (_moveTier == MoveTier.Sprint)
			m_ReadyHands?.SuppressReadyForSprintIfNeeded();
		if (_moveTier == MoveTier.Run)
			m_ReadyHands?.SuppressReadyForRunIfNeeded();

		if (m_Mode == _moveTier)
			return;

		m_Mode = _moveTier;
		if (m_Agent != null)
			ApplyTierSpeed();
		if (_moveTier != MoveTier.Sprint)
			m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		if (_moveTier != MoveTier.Run)
			m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	public bool TrySetDestination(Vector3 _worldPosition)
	{
		if (m_Agent == null)
			return false;
		if (!IsConscious())
			return false;
		if (IsHealingBlocked())
			return false;

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return false;

		if (IsStanceTransitionMovementBlocked())
		{
			m_PendingNavDestination = hit.position;
			m_PendingNavOverridesMode = false;
			m_HasPendingNavOrder = true;
			return true;
		}

		m_Agent.isStopped = false;
		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(hit.position);
		PrimeAnimatorForMoveStart();
		return true;
	}

	public bool IssueNavOrder(Vector3 _worldPosition, MoveTier _moveTier)
	{
		if (m_Agent == null)
		{
			LogNavMove("fail", _worldPosition, _worldPosition, _moveTier, false, "no_agent");
			return false;
		}
		if (!IsConscious())
		{
			LogNavMove("fail", _worldPosition, _worldPosition, _moveTier, false, "unconscious");
			return false;
		}

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, m_NavMeshSampleRadius, NavMesh.AllAreas))
		{
			LogNavMove("fail", _worldPosition, _worldPosition, _moveTier, false, "SamplePosition");
			return false;
		}

		IssueNavOrderInternal(hit.position, _moveTier);
		if (UnitActionLogSession.ShouldLogMove(this, hit.position, false))
			LogNavMove("issue", _worldPosition, hit.position, _moveTier, true, null);
		return true;
	}

	public bool IssueNavOrderContinuous(Vector3 _worldPosition, MoveTier _moveTier)
	{
		if (m_Agent == null)
		{
			LogNavMove("fail", _worldPosition, _worldPosition, _moveTier, false, "no_agent");
			return false;
		}
		if (!IsConscious())
		{
			LogNavMove("fail", _worldPosition, _worldPosition, _moveTier, false, "unconscious");
			return false;
		}

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, m_NavMeshSampleRadius, NavMesh.AllAreas))
		{
			LogNavMove("fail", _worldPosition, _worldPosition, _moveTier, false, "SamplePosition");
			return false;
		}

		IssueNavOrderContinuousInternal(hit.position, _moveTier);
		if (UnitActionLogSession.ShouldLogMove(this, hit.position, true))
			LogNavMove("continuous", _worldPosition, hit.position, _moveTier, true, null);
		return true;
	}

	public void HardStop()
	{
		bool hadMove = m_HasPendingNavOrder ||
		               (IsNavAgentOperational() && m_Agent.hasPath && !m_Agent.isStopped);
		m_HasPendingNavOrder = false;
		if (!IsNavAgentOperational())
		{
			m_ReadyHands?.TryRestoreReadyAfterSprint(false);
			m_ReadyHands?.TryRestoreReadyAfterRun(false);
			if (hadMove)
				LogNavMove("stop", transform.position, transform.position, m_Mode, true, "no_agent");
			return;
		}

		m_Agent.isStopped = true;
		m_Agent.ResetPath();
		m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		m_ReadyHands?.TryRestoreReadyAfterRun(false);
		if (hadMove)
			LogNavMove("stop", transform.position, transform.position, m_Mode, true, null);
	}

	private void TryEarlyArrivalStop()
	{
		if (SuppressEarlyArrivalStop)
			return;
		if (!IsNavAgentOperational() || m_Agent.isStopped)
			return;
		if (m_Agent.pathPending)
			return;
		if (!m_Agent.hasPath)
			return;
		if (float.IsPositiveInfinity(m_Agent.remainingDistance))
			return;

		if (m_Agent.remainingDistance > m_EarlyArrivalDistance + m_Agent.stoppingDistance)
			return;

		Vector3 planarVel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		if (planarVel.magnitude > m_StopVelocityEpsilon)
			return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		m_CachedRtsMember?.NotifyRouteDebugEarlyStop(m_Agent.remainingDistance);
#endif

		Vector3 reachedDest = m_Agent.destination;
		float reachedRem = m_Agent.remainingDistance;
		m_Agent.isStopped = true;
		m_Agent.ResetPath();
		if (UnitActionLog.Enabled)
			LogNavMove("reached", reachedDest, reachedDest, m_Mode, true, "earlyArrival rem=" + UnitActionLog.F2(reachedRem));
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_GrenadeThrowController == null)
			m_GrenadeThrowController = GetComponent<UnitGrenadeThrowController>();
		if (m_SpineHorizontalAim == null)
			m_SpineHorizontalAim = GetComponent<UnitSpineHorizontalAim>();
		if (m_WeaponAiming == null)
			m_WeaponAiming = GetComponent<UnitWeaponAiming>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponent<UnitStabilizeOtherController>();
		m_CachedRtsMember = GetComponent<RtsUnitMember>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();

		m_Agent.updatePosition = true;
		m_Agent.updateRotation = false;
		m_Agent.stoppingDistance = m_StoppingDistance;

		if (m_Animator != null)
			m_Animator.applyRootMotion = false;

		m_TargetAgentSpeed = m_Agent != null ? m_Agent.speed : 0f;
		ApplyTierSpeed();
		if (m_StanceSource != null)
			m_LastStance = m_StanceSource.CurrentStance;
	}

	private void ResolveTargetSelector()
	{
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
	}

	private void Start()
	{
		if (m_Agent != null && m_WarpToNavMeshOnStart && !m_Agent.isOnNavMesh)
		{
			if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, m_WarpSearchRadius, NavMesh.AllAreas))
			{
				m_Agent.Warp(hit.position);
				Debug.LogWarning("UnitNavLocomotionDriver: юнит не на NavMesh — перенос к ближайшей точке.", this);
			}
			else
				Debug.LogError("UnitNavLocomotionDriver: нет точки на NavMesh в радиусе Warp Search Radius.", this);
		}
	}

	private void Update()
	{
		if (m_Agent == null)
			return;
		if (!IsConscious())
		{
			m_HasPendingNavOrder = false;
			HardStop();
			PushAnimator();
			return;
		}

		if (!IsNavAgentOperational())
			return;

		if (m_HasPendingNavOrder && !IsStanceTransitionMovementBlocked())
			ConsumePendingNavOrder();

		if (IsClickToMoveDrivingAgent())
			return;

		bool stanceMovementBlocked = IsStanceTransitionMovementBlocked();
		if (stanceMovementBlocked)
			m_Agent.isStopped = true;
		else if (m_StanceMovementWasBlocked && m_Agent.isStopped && NavAgentHasIncompletePath())
			m_Agent.isStopped = false;

		m_StanceMovementWasBlocked = stanceMovementBlocked;

		if (!stanceMovementBlocked)
			TryEarlyArrivalStop();

		UpdateMoveTierForStanceChanges();
		UpdateAgentSpeedToTarget();
		UpdateFacing();
		PushAnimator();
		TryRestoreReadyAfterSprintWhenStopped();
		TryRestoreReadyAfterRunWhenStopped();
	}

	private void LateUpdate()
	{
		LogFacingSystemIfNeeded();
	}
	#endregion

	#region Private Methods
	private void LogNavMove(string _verb, Vector3 _dest, Vector3 _snapped, MoveTier _tier, bool _ok, string _fail)
	{
		if (!UnitActionLog.Enabled)
			return;
		string reason = "None";
		if (TryGetComponent(out IUnitMoveCommand move) && move != null && move.Reason != UnitNavigationReason.None)
			reason = move.Reason.ToString();
		string rem = UnitActionLog.AgentRemaining(m_Agent);
		string payload =
			_verb +
			" dest=" + UnitActionLog.Vec(_dest) +
			" snapped=" + UnitActionLog.Vec(_snapped) +
			" tier=" + _tier +
			" reason=" + reason +
			" ok=" + (_ok ? "1" : "0") +
			" remaining=" + rem +
			" path=" + UnitActionLog.AgentPath(m_Agent) +
			" source=NavDriver";
		if (!string.IsNullOrEmpty(_fail))
			payload += " fail=" + _fail;
		UnitActionLog.Write(this, UnitActionLog.Move, payload);
		if (_verb == "issue" || _verb == "stop" || _verb == "reached" || !_ok)
			UnitActionLog.Timeline(UnitActionLog.Move, "actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private void IssueNavOrderInternal(Vector3 _destination, MoveTier _moveTier)
	{
		if (!IsConscious())
			return;
		if (IsHealingBlocked())
			return;

		if (_moveTier == MoveTier.Sprint)
			m_ReadyHands?.SuppressReadyForSprintIfNeeded();
		if (_moveTier == MoveTier.Run)
			m_ReadyHands?.SuppressReadyForRunIfNeeded();

		if (IsStanceTransitionMovementBlocked())
		{
			m_PendingNavDestination = _destination;
			m_PendingNavOverridesMode = true;
			m_PendingNavMode = _moveTier;
			m_HasPendingNavOrder = true;
			if (_moveTier != MoveTier.Sprint)
				m_ReadyHands?.TryRestoreReadyAfterSprint(false);
			if (_moveTier != MoveTier.Run)
				m_ReadyHands?.TryRestoreReadyAfterRun(false);
			return;
		}

		m_Agent.isStopped = false;
		m_Mode = _moveTier;
		EnsureStandingForFastMoveIfNeeded();
		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(_destination);
		PrimeAnimatorForMoveStart();
		if (_moveTier != MoveTier.Sprint)
			m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		if (_moveTier != MoveTier.Run)
			m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	private void IssueNavOrderContinuousInternal(Vector3 _destination, MoveTier _moveTier)
	{
		if (!IsConscious())
			return;
		if (IsHealingBlocked())
			return;

		if (_moveTier == MoveTier.Sprint)
			m_ReadyHands?.SuppressReadyForSprintIfNeeded();
		if (_moveTier == MoveTier.Run)
			m_ReadyHands?.SuppressReadyForRunIfNeeded();

		if (IsStanceTransitionMovementBlocked())
		{
			m_PendingNavDestination = _destination;
			m_PendingNavOverridesMode = true;
			m_PendingNavMode = _moveTier;
			m_HasPendingNavOrder = true;
			if (_moveTier != MoveTier.Sprint)
				m_ReadyHands?.TryRestoreReadyAfterSprint(false);
			if (_moveTier != MoveTier.Run)
				m_ReadyHands?.TryRestoreReadyAfterRun(false);
			return;
		}

		m_Agent.isStopped = false;
		if (_moveTier != m_Mode)
		{
			m_Mode = _moveTier;
			EnsureStandingForFastMoveIfNeeded();
			ApplyTierSpeed();
		}

		m_Agent.SetDestination(_destination);
		if (_moveTier != MoveTier.Sprint)
			m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		if (_moveTier != MoveTier.Run)
			m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	private void UpdateMoveTierForStanceChanges()
	{
		if (m_StanceSource == null)
			return;

		LocomotionStance stance = m_StanceSource.CurrentStance;
		if (stance == m_LastStance)
			return;

		if (m_AfterStandUpWalkAnimHoldSeconds > 0.001f &&
		    HasActiveMoveIntent() &&
		    stance == LocomotionStance.Standing &&
		    m_LastStance == LocomotionStance.Prone)
			m_PostStandLowNavSpeedUntil = Time.time + m_AfterStandUpWalkAnimHoldSeconds;

		if ((stance == LocomotionStance.Crouch && m_LastStance == LocomotionStance.Standing) ||
		    (stance == LocomotionStance.Standing && m_LastStance == LocomotionStance.Crouch) ||
		    (stance == LocomotionStance.Prone && m_LastStance == LocomotionStance.Standing) ||
		    (stance == LocomotionStance.Standing && m_LastStance == LocomotionStance.Prone) ||
		    (stance == LocomotionStance.Prone && m_LastStance == LocomotionStance.Crouch) ||
		    (stance == LocomotionStance.Crouch && m_LastStance == LocomotionStance.Prone))
		{
			m_Mode = MoveTier.Walk;
			if (!HasPendingSprintOrder())
				m_ReadyHands?.TryRestoreReadyAfterSprint(false);
			if (!HasPendingRunOrder())
				m_ReadyHands?.TryRestoreReadyAfterRun(false);
		}

		m_LastStance = stance;
		ApplyTierSpeed();
	}

	private void ApplyTierSpeed()
	{
		float maxSpeed;

		if (m_StanceSource != null)
		{
			switch (m_StanceSource.CurrentStance)
			{
				case LocomotionStance.Crouch:
					maxSpeed = m_CrouchWalkSpeed;
					break;
				case LocomotionStance.Prone:
					maxSpeed = m_ProneCrawlSpeed;
					break;
				default:
					maxSpeed = GetStandingTierSpeed();
					break;
			}
		}
		else
			maxSpeed = GetStandingTierSpeed();

		m_TargetAgentSpeed = maxSpeed;
		UpdateAgentSpeedToTarget();
	}

	private void UpdateAgentSpeedToTarget()
	{
		if (m_Agent == null)
			return;

		// Idle (same semantics as PushAnimator:moving): clear NavMeshAgent.speed smoothing leftovers.
		Vector3 planarVelocity = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		bool moving = planarVelocity.magnitude > m_StopVelocityEpsilon || HasActiveMoveIntent();
		if (!moving)
		{
			m_Agent.speed = 0f;
			float targetWhileStopped = Mathf.Max(0.01f, m_TargetAgentSpeed);
			float accelBasisWhileStopped = Mathf.Max(m_Agent.speed, targetWhileStopped);
			m_Agent.acceleration = Mathf.Max(m_AgentAcceleration, accelBasisWhileStopped * 4.5f);
			return;
		}

		float target = Mathf.Max(0.01f, m_TargetAgentSpeed * FormationSpeedMultiplier * StaminaSpeedMultiplier);

		if (m_AgentSpeedSmoothSeconds <= 0.0001f)
		{
			m_Agent.speed = target;
		}
		else
		{
			float current = m_Agent.speed;
			float smoothSeconds = target >= current ? m_AgentSpeedSmoothSeconds : m_AgentSpeedSmoothSecondsDecel;
			if (smoothSeconds <= 0.0001f)
				m_Agent.speed = target;
			else
			{
				float maxDelta = Time.deltaTime * (target / smoothSeconds);
				m_Agent.speed = Mathf.MoveTowards(current, target, maxDelta);
			}
		}

		float accelBasis = Mathf.Max(m_Agent.speed, target);
		m_Agent.acceleration = Mathf.Max(m_AgentAcceleration, accelBasis * 4.5f);
	}

	private float GetStandingTierSpeed()
	{
		switch (m_Mode)
		{
			case MoveTier.Walk:
				return m_WalkSpeed;
			case MoveTier.Run:
				bool hasWeapon = m_ReadyHands != null && m_ReadyHands.IsWeaponEquipped();
				return hasWeapon ? m_RunSpeed : m_RunSpeedNoWeapon;
			case MoveTier.Sprint:
				return m_SprintSpeed;
			default:
				return m_WalkSpeed;
		}
	}

	private bool HasActiveMoveIntent()
	{
		if (!IsNavAgentOperational())
			return false;

		if (m_Agent.isStopped)
			return false;
		if (m_Agent.pathPending)
			return true;
		if (m_Agent.pathStatus == NavMeshPathStatus.PathPartial)
			return true;
		if (!m_Agent.hasPath)
			return false;
		if (float.IsPositiveInfinity(m_Agent.remainingDistance))
			return true;
		return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
	}

	private bool NavAgentHasIncompletePath()
	{
		if (!IsNavAgentOperational())
			return false;

		if (m_Agent.pathPending)
			return true;
		if (m_Agent.pathStatus == NavMeshPathStatus.PathPartial)
			return true;
		if (!m_Agent.hasPath)
			return false;
		if (float.IsPositiveInfinity(m_Agent.remainingDistance))
			return true;
		return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
	}

	private Vector3 PlanarLocomotionDirection(out float _planarSpeed, out bool _hasGoalAhead)
	{
		if (!IsNavAgentOperational())
		{
			_planarSpeed = 0f;
			_hasGoalAhead = false;
			return transform.forward;
		}

		Vector3 velocity = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		_planarSpeed = velocity.magnitude;
		_hasGoalAhead = HasActiveMoveIntent();

		if (_planarSpeed > m_StopVelocityEpsilon)
			return velocity.normalized;

		if (_hasGoalAhead)
		{
			Vector3 toGoal = m_Agent.steeringTarget - transform.position;
			toGoal.y = 0f;
			return toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : transform.forward;
		}

		return transform.forward;
	}

	private bool TryGetMovementFacingDirection(out Vector3 _direction)
	{
		_direction = Vector3.zero;

		Vector3 velocity = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		if (velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon)
		{
			_direction = velocity.normalized;
			return true;
		}

		if (!NavAgentHasIncompletePath())
			return false;

		Vector3 toSteer = m_Agent.steeringTarget - transform.position;
		toSteer.y = 0f;
		if (toSteer.sqrMagnitude < 1e-6f)
			return false;

		_direction = toSteer.normalized;
		return true;
	}

	private void ApplyFacingDirection(Vector3 _direction)
	{
		if (_direction.sqrMagnitude < 1e-6f)
			return;

		Quaternion targetRotation = Quaternion.LookRotation(_direction, Vector3.up);
		float angleDiff = Quaternion.Angle(transform.rotation, targetRotation);
		HandleTurnReady(angleDiff);
		transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, m_RotateSpeed * Time.deltaTime);
	}

	private void UpdateFacing()
	{
		ResolveTargetSelector();
		float yawBefore = transform.eulerAngles.y;
		string mode = "none";
		string detail = "";
		try
		{
			if (m_GrenadeThrowController != null && (m_GrenadeThrowController.IsAiming || m_GrenadeThrowController.IsThrowAnimPlaying))
			{
				mode = "blocked";
				detail = "grenade";
				return;
			}

			if (m_CachedRtsMember != null &&
			    m_CachedRtsMember.IsRotatingToRouteFacing &&
			    !m_CachedRtsMember.ShouldYieldRouteFacingToCombatTarget)
			{
				mode = "blocked";
				detail = "routeFacing";
				return;
			}

			if (ShouldApplyManualFacingOverride())
			{
				m_EngageYawVelocity = 0f;
				mode = "manual";
				detail = $"arrowYaw={OverrideFacingAngle.Value:F1}";
				float bodyYaw = ResolveHorizontalFacingBodyYaw(OverrideFacingAngle.Value);
				Vector3 overrideDir = UnitHorizontalFacingUtility.YawDegreesToForwardXZ(bodyYaw);
				ApplyFacingDirection(overrideDir);
				return;
			}

			if (IsRunActive() || IsSprintActive())
			{
				m_EngageYawVelocity = 0f;
				mode = "move";
				detail = IsSprintActive() ? "sprint" : "run";
				if (TryGetMovementFacingDirection(out Vector3 moveDirection))
					ApplyFacingDirection(moveDirection);
				return;
			}

			if (IsEngagingVisibleTarget())
			{
				if (TryApplyCombatWalkBarrelFacing(ref mode, ref detail))
					return;

				if (m_SpineHorizontalAim == null)
					m_SpineHorizontalAim = GetComponent<UnitSpineHorizontalAim>();

				if (m_SpineHorizontalAim != null && m_SpineHorizontalAim.IsActive)
				{
					if (!TryGetRootBodyToTargetYaw(out float bodyToTargetYaw))
					{
						mode = "engageSpine";
						detail = "noBodyYaw";
						return;
					}

					HandleTurnReady(Mathf.Abs(bodyToTargetYaw));

					if (!m_SpineHorizontalAim.WantsRootRecenter)
					{
						m_EngageYawVelocity = 0f;
						mode = "engageSpine";
						detail = $"hold body↔target={bodyToTargetYaw:F1}° absorb={m_SpineHorizontalAim.CurrentAbsorbedYawDegrees:F1}°";
						return;
					}

					mode = "engageSpine";
					detail = $"recenter body↔target={bodyToTargetYaw:F1}°";
					float currentYaw = transform.eulerAngles.y;
					float targetYaw = currentYaw + bodyToTargetYaw;
					float newYaw = Mathf.SmoothDampAngle(
						currentYaw,
						targetYaw,
						ref m_EngageYawVelocity,
						m_FacingTargetYawSmoothTime);
					transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
					return;
				}

				if (!TryResolveEngageFacing(out Vector3 origin, out Vector3 facingForwardXZ))
				{
					mode = "engageRoot";
					detail = "noOrigin";
					return;
				}

				Vector3 aimPoint = m_TargetSelector != null
					? m_TargetSelector.GetEngageableAimPointWorld()
					: Vector3.zero;
				Vector3 toTarget = aimPoint - origin;
				toTarget.y = 0f;
				if (toTarget.sqrMagnitude < 1e-6f)
				{
					mode = "engageRoot";
					detail = "noAimPoint";
					return;
				}

				Vector3 engageDir = toTarget.normalized;
				float yawError = Vector3.SignedAngle(facingForwardXZ, engageDir, Vector3.up);
				HandleTurnReady(Mathf.Abs(yawError));
				mode = "engageRoot";
				detail = $"yawErr={yawError:F1}°";
				float fallbackCurrentYaw = transform.eulerAngles.y;
				float fallbackTargetYaw = fallbackCurrentYaw + yawError;
				float fallbackNewYaw = Mathf.SmoothDampAngle(
					fallbackCurrentYaw,
					fallbackTargetYaw,
					ref m_EngageYawVelocity,
					m_FacingTargetYawSmoothTime);
				transform.rotation = Quaternion.Euler(0f, fallbackNewYaw, 0f);
				return;
			}

			string engageGate = UnitFacingDebugLog.DiagnoseEngageGate(
				IsRunActive(),
				IsSprintActive(),
				false,
				m_ReadyHands,
				m_TargetSelector);
			m_EngageYawVelocity = 0f;

			bool readyIdleHoldFacing = m_ReadyHands != null && m_ReadyHands.WantsCombatTargetFacing();
			Vector3 direction = Vector3.zero;
			Vector3 velocity = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
			float planarSpeed = velocity.magnitude;

			if (planarSpeed > m_StopVelocityEpsilon)
			{
				mode = "path";
				detail = $"vel engage={engageGate}";
				direction = velocity.normalized;
			}
			else if (NavAgentHasIncompletePath())
			{
				if (readyIdleHoldFacing)
				{
					m_ReadyHands?.TryRestoreReadyAfterTurn(false);
					m_TurnSuppressedReady = false;
					mode = "idleHold";
					detail = $"pathPending engage={engageGate}";
					return;
				}

				Vector3 toSteer = m_Agent.steeringTarget - transform.position;
				toSteer.y = 0f;
				if (toSteer.sqrMagnitude < 1e-6f)
				{
					mode = "path";
					detail = $"steerEmpty engage={engageGate}";
					return;
				}

				mode = "path";
				detail = $"steer engage={engageGate}";
				direction = toSteer.normalized;
			}
			else
			{
				m_ReadyHands?.TryRestoreReadyAfterTurn(false);
				m_TurnSuppressedReady = false;
				mode = "idle";
				detail = $"engage={engageGate}";
				return;
			}

			ApplyFacingDirection(direction);
		}
		finally
		{
			m_FacingLogMode = mode;
			m_FacingLogDetail = detail;
			m_FacingLogRootDelta = Mathf.DeltaAngle(yawBefore, transform.eulerAngles.y);
		}
	}

	private void HandleTurnReady(float _angleDegrees)
	{
		if (_angleDegrees > 90f)
		{
			if (!m_TurnSuppressedReady)
			{
				m_ReadyHands?.SuppressReadyForTurnIfNeeded();
				m_TurnSuppressedReady = true;
				if (m_LogFacingSystem && UnitFacingDebugLog.ShouldLog(m_CachedRtsMember))
				{
					UnitFacingDebugLog.EmitEvent(
						this,
						"turnSuppress",
						$"angle={_angleDegrees:F1}° pose={m_ReadyHands?.EffectivePoseState}");
				}
			}
		}
		else if (_angleDegrees < 20f && m_TurnSuppressedReady)
		{
			m_ReadyHands?.TryRestoreReadyAfterTurn(false);
			m_TurnSuppressedReady = false;
			if (m_LogFacingSystem && UnitFacingDebugLog.ShouldLog(m_CachedRtsMember))
			{
				UnitFacingDebugLog.EmitEvent(
					this,
					"turnRestore",
					$"angle={_angleDegrees:F1}° pose={m_ReadyHands?.EffectivePoseState}");
			}
		}
	}

	private bool IsClickToMoveDrivingAgent()
	{
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		return m_ClickToMove != null && m_ClickToMove.isActiveAndEnabled;
	}

	private void LogFacingSystemIfNeeded()
	{
		if (IsClickToMoveDrivingAgent())
			return;

		if (!m_LogFacingSystem)
			return;
		if (!UnitFacingDebugLog.ShouldLog(m_CachedRtsMember))
			return;

		ResolveTargetSelector();

		bool modeChanged = m_LastEmittedFacingMode != m_FacingLogMode;
		if (!modeChanged && Time.unscaledTime < m_NextFacingLogTime)
			return;

		if (m_WeaponAiming == null)
			m_WeaponAiming = GetComponent<UnitWeaponAiming>();

		m_LastEmittedFacingMode = m_FacingLogMode;
		m_NextFacingLogTime = Time.unscaledTime + Mathf.Max(0.05f, m_LogFacingIntervalSeconds);
		UnitFacingDebugLog.EmitSnapshot(
			this,
			m_FacingLogMode,
			m_FacingLogDetail,
			m_FacingLogRootDelta,
			m_TurnSuppressedReady,
			m_ReadyHands,
			m_SpineHorizontalAim,
			m_Equipment,
			m_WeaponAiming,
			m_TargetSelector);
	}

	private bool TryApplyCombatWalkBarrelFacing(ref string _mode, ref string _detail)
	{
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (!UnitHorizontalFacingUtility.IsCombatShootWalk(
			    m_ReadyHands,
			    m_Animator,
			    IsRunActive() || IsSprintActive()))
			return false;

		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return false;

		ResolveTargetSelector();
		if (m_TargetSelector == null || m_TargetSelector.SelectedTarget == null)
			return false;

		Vector3 aimPoint = m_TargetSelector.GetEngageableAimPointWorld();
		if (!UnitHorizontalFacingUtility.TryGetTargetWorldYaw(transform, aimPoint, out float desiredBarrelYaw))
			return false;

		float bodyYaw = ResolveHorizontalFacingBodyYaw(desiredBarrelYaw);
		float currentYaw = transform.eulerAngles.y;
		float yawError = Mathf.DeltaAngle(currentYaw, bodyYaw);
		HandleTurnReady(Mathf.Abs(yawError));
		_mode = "engageAimWalk";
		_detail = $"barrelYaw={desiredBarrelYaw:F1}° bodyYaw={bodyYaw:F1}° err={yawError:F1}°";
		float newYaw = Mathf.SmoothDampAngle(
			currentYaw,
			bodyYaw,
			ref m_EngageYawVelocity,
			m_FacingTargetYawSmoothTime);
		transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
		return true;
	}

	private bool IsEngagingVisibleTarget()
	{
		ResolveTargetSelector();
		return m_TargetSelector != null && m_TargetSelector.SelectedTarget != null && ShouldRotateRootTowardVisionTarget();
	}

	private bool TryGetRootBodyToTargetYaw(out float _bodyToTargetYaw)
	{
		_bodyToTargetYaw = 0f;
		ResolveTargetSelector();
		if (m_TargetSelector == null || m_TargetSelector.SelectedTarget == null)
			return false;

		Vector3 aimPoint = m_TargetSelector.GetEngageableAimPointWorld();
		Vector3 toTarget = aimPoint - transform.position;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude < 1e-6f)
			return false;

		Vector3 bodyFwd = transform.forward;
		bodyFwd.y = 0f;
		if (bodyFwd.sqrMagnitude < 1e-6f)
			return false;

		_bodyToTargetYaw = Vector3.SignedAngle(bodyFwd.normalized, toTarget.normalized, Vector3.up);
		return true;
	}

	private bool TryResolveEngageFacing(out Vector3 _origin, out Vector3 _facingForwardXZ)
	{
		_origin = default;
		_facingForwardXZ = default;

		if (m_Vision == null)
			return false;

		bool largeBarrelOffset =
			UnitHorizontalFacingUtility.TryGetBodyBarrelYawOffset(transform, m_Equipment, out float bodyBarrel) &&
			Mathf.Abs(bodyBarrel) >= m_EngageRootFacingWhenBarrelOffsetExceeds;

		if (largeBarrelOffset)
		{
			_origin = transform.position;
			_facingForwardXZ = transform.forward;
			_facingForwardXZ.y = 0f;
			if (_facingForwardXZ.sqrMagnitude < 1e-6f)
				return false;
			_facingForwardXZ.Normalize();
			return true;
		}

		return m_Vision.TryGetEngageFacingOriginWorld(out _origin) &&
		       m_Vision.TryGetEngageFacingForwardXZ(out _facingForwardXZ);
	}

	private bool ShouldApplyManualFacingOverride()
	{
		if (!OverrideFacingAngle.HasValue)
			return false;

		if (m_CachedRtsMember != null)
		{
			RtsUnitMember.ArrowPriorityPhase phase = m_CachedRtsMember.CurrentArrowPriorityPhase;
			if (phase == RtsUnitMember.ArrowPriorityPhase.BlueHold ||
			    phase == RtsUnitMember.ArrowPriorityPhase.GreenHold)
				return true;
		}

		// Цель всегда приоритетнее жёлтой стрелки: пока есть engage — крутим корень на цель.
		// После потери/убийства цели OverrideFacingAngle остаётся и юнит возвращается к стрелке.
		if (IsEngagingVisibleTarget())
			return false;

		if (m_CachedRtsMember != null)
		{
			RtsUnitMember.ArrowPriorityPhase phase = m_CachedRtsMember.CurrentArrowPriorityPhase;
			if (phase == RtsUnitMember.ArrowPriorityPhase.Turning ||
			    phase == RtsUnitMember.ArrowPriorityPhase.YellowReturning)
				return true;
		}

		bool moving = IsPlanarMoving();
		bool hasIntent = m_CachedRtsMember != null && m_CachedRtsMember.HasActiveMovementIntent;
		if (m_CachedRtsMember != null && !m_CachedRtsMember.WantsReady &&
		    (moving || hasIntent) &&
		    !m_CachedRtsMember.AllowsInMovementManualFacingOverride)
		{
			return false;
		}

		return true;
	}

	private bool IsConscious()
	{
		return m_Consciousness == null || m_Consciousness.IsConscious;
	}

	private bool IsHealingBlocked()
	{
		if (m_SelfStabilization != null &&
		    (m_SelfStabilization.IsSelfHealing || m_SelfStabilization.IsHealPresentationActive))
			return true;
		if (m_StabilizeOther != null &&
		    (m_StabilizeOther.IsStabilizingOther || m_StabilizeOther.IsHealPresentationActive))
			return true;
		return false;
	}

	private bool IsNavAgentOperational()
	{
		return m_Agent != null && m_Agent.enabled && m_Agent.isOnNavMesh;
	}

	private bool ShouldRotateRootTowardVisionTarget()
	{
		if (IsSprintActive() || IsRunActive())
			return false;

		UnitRocketLauncherOrderController rocketOrder = GetComponent<UnitRocketLauncherOrderController>();
		if (rocketOrder != null &&
		    rocketOrder.IsBusy &&
		    (rocketOrder.CurrentPhase == RocketLauncherOrderPhase.Aiming ||
		     rocketOrder.CurrentPhase == RocketLauncherOrderPhase.Firing))
			return true;

		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		return m_ReadyHands != null && m_ReadyHands.WantsCombatTargetFacing();
	}

	private float ResolveHorizontalFacingBodyYaw(float _desiredWorldYaw)
	{
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();

		return UnitHorizontalFacingUtility.ResolveHorizontalFacingBodyYaw(
			transform,
			m_Equipment,
			m_ReadyHands,
			_desiredWorldYaw);
	}

	private void PrimeAnimatorForMoveStart()
	{
		if (m_Animator == null || m_SprintSpeed < 0.01f)
			return;

		float cruise01 = Mathf.Clamp01(m_Agent.speed / m_SprintSpeed);
		float floor = cruise01 * m_StartNavSpeedFloor;
		m_SmoothSpeed01 = Mathf.Max(m_SmoothSpeed01, floor);
		m_SmoothSpeedVel = 0f;
	}

	private void ConsumePendingNavOrder()
	{
		m_HasPendingNavOrder = false;
		m_Agent.isStopped = false;

		if (m_PendingNavOverridesMode)
			m_Mode = m_PendingNavMode;

		EnsureStandingForFastMoveIfNeeded();
		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(m_PendingNavDestination);
		PrimeAnimatorForMoveStart();
		if (m_Mode != MoveTier.Sprint)
			m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		if (m_Mode != MoveTier.Run)
			m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	private void TryRestoreReadyAfterSprintWhenStopped()
	{
		if (m_Mode != MoveTier.Sprint || m_ReadyHands == null || m_Agent == null)
			return;
		if (HasPendingSprintOrder())
			return;
		if (NavAgentHasIncompletePath())
			return;

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		if (velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon || HasActiveMoveIntent())
			return;

		ForceWalkMoveMode();
	}

	private void TryRestoreReadyAfterRunWhenStopped()
	{
		if (m_Mode != MoveTier.Run || m_ReadyHands == null || m_Agent == null)
			return;
		if (HasPendingRunOrder())
			return;
		if (NavAgentHasIncompletePath())
			return;

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		if (velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon || HasActiveMoveIntent())
			return;

		ForceWalkMoveMode();
	}

	private bool HasPendingSprintOrder()
	{
		return m_HasPendingNavOrder && m_PendingNavOverridesMode && m_PendingNavMode == MoveTier.Sprint;
	}

	private bool HasPendingRunOrder()
	{
		return m_HasPendingNavOrder && m_PendingNavOverridesMode && m_PendingNavMode == MoveTier.Run;
	}

	private void EnsureStandingForFastMoveIfNeeded()
	{
		if (m_StanceSource == null)
			return;
		if (m_Mode != MoveTier.Run && m_Mode != MoveTier.Sprint)
			return;
		if (m_StanceSource.CurrentStance == LocomotionStance.Standing)
			return;

		LocomotionStance stanceBeforeStand = m_StanceSource.CurrentStance;
		if (m_AfterStandUpWalkAnimHoldSeconds > 0.001f && stanceBeforeStand == LocomotionStance.Prone)
			m_PostStandLowNavSpeedUntil = Time.time + m_AfterStandUpWalkAnimHoldSeconds;
		m_StanceSource.ForceStanding();
		m_LastStance = LocomotionStance.Standing;
	}

	private bool IsStanceTransitionMovementBlocked()
	{
		if (m_Animator == null)
			return false;

		string[] stateNames = GetStanceBlockingStateNamesForWeaponMode(m_Animator.GetInteger(s_WeaponMode));
		if (stateNames == null || stateNames.Length == 0)
			return false;

		if (m_Animator.IsInTransition(0))
		{
			AnimatorStateInfo next = m_Animator.GetNextAnimatorStateInfo(0);
			if (AnimatorStateMatchesAnyName(ref next, stateNames))
				return true;
			AnimatorStateInfo current = m_Animator.GetCurrentAnimatorStateInfo(0);
			if (AnimatorStateMatchesAnyName(ref current, stateNames))
				return true;
			return false;
		}

		AnimatorStateInfo info = m_Animator.GetCurrentAnimatorStateInfo(0);
		if (!AnimatorStateMatchesAnyName(ref info, stateNames))
			return false;

		float normalizedTime = info.normalizedTime;
		if (info.loop)
			normalizedTime %= 1f;
		return normalizedTime < 0.999f;
	}

	private static string[] GetStanceBlockingStateNamesForWeaponMode(int _weaponMode)
	{
		return _weaponMode switch
		{
			(int)LocomotionWeaponMode.Unarmed => s_UnarmedStanceBlockingStateNames,
			(int)LocomotionWeaponMode.Rifle => s_RifleStanceBlockingStateNames,
			(int)LocomotionWeaponMode.Pistol => s_RifleStanceBlockingStateNames,
			_ => null
		};
	}

	private static bool AnimatorStateMatchesAnyName(ref AnimatorStateInfo _info, string[] _stateNames)
	{
		for (int i = 0; i < _stateNames.Length; i++)
		{
			if (_info.IsName(_stateNames[i]))
				return true;
		}

		return false;
	}

	private void ApplyAnimatorLocomotionTierCap(ref int locomotionTier)
	{
		if (m_StanceSource == null || m_Animator == null)
			return;

		if (m_StanceSource.CurrentStance == LocomotionStance.Prone)
			return;

		int wm = m_Animator.GetInteger(s_WeaponMode);
		if (wm == (int)LocomotionWeaponMode.Unarmed && m_StanceSource.CurrentStance == LocomotionStance.Crouch)
			locomotionTier = Mathf.Min(locomotionTier, (int)MoveTier.Walk);
	}

	private bool IsBodyAlignedWithLocomotionDirection(float _maxAngleDegrees = 20f)
	{
		if (m_LastLocomotionWorldDirection.sqrMagnitude < 1e-6f)
			return true;

		Vector3 flatForward = transform.forward;
		flatForward.y = 0f;
		if (flatForward.sqrMagnitude < 1e-6f)
			return true;

		float angle = Vector3.Angle(flatForward.normalized, m_LastLocomotionWorldDirection);
		return angle < _maxAngleDegrees;
	}

	private void ApplyAnimatorLocomotionDirectionConstraints(ref int locomotionTier, float navSpeedOut)
	{
		ApplyAnimatorLocomotionTierCap(ref locomotionTier);

		if (m_StanceSource == null || m_Animator == null)
			return;

		LocomotionStance stance = m_StanceSource.CurrentStance;
		if (stance == LocomotionStance.Prone)
			return;

		int wm = m_Animator.GetInteger(s_WeaponMode);
		bool axisFwdOnly = navSpeedOut > 0.02f;
		bool rtsMarchIntent = m_CachedRtsMember != null && m_CachedRtsMember.HasActiveMovementIntent;
		bool rtsNotReadyFwdOnly = m_CachedRtsMember != null && !m_CachedRtsMember.WantsReady &&
		                          (axisFwdOnly || rtsMarchIntent) &&
		                          IsBodyAlignedWithLocomotionDirection();
		bool unarmedStandFwdOnly = wm == (int)LocomotionWeaponMode.Unarmed && stance == LocomotionStance.Standing && axisFwdOnly;
		bool rifleSprintFwdOnly = (wm == (int)LocomotionWeaponMode.Rifle || wm == (int)LocomotionWeaponMode.Pistol) &&
		                          stance == LocomotionStance.Standing &&
		                          locomotionTier == (int)MoveTier.Sprint &&
		                          axisFwdOnly;

		if (rtsNotReadyFwdOnly || unarmedStandFwdOnly || rifleSprintFwdOnly)
		{
			m_SmoothDir = new Vector2(0f, 1f);
			m_SmoothDirVel = Vector2.zero;
		}
	}

	private bool IsPlanarMoving()
	{
		if (!IsNavAgentOperational())
			return HasActiveMoveIntent();

		Vector3 planarVel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		return planarVel.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon || HasActiveMoveIntent();
	}

	private void PushAnimator()
	{
		if (m_Animator == null || !m_Animator.enabled)
			return;

		if (!IsNavAgentOperational())
			return;

		if (IsStanceTransitionMovementBlocked())
		{
			m_SmoothSpeed01 = 0f;
			m_SmoothSpeedVel = 0f;
			m_SmoothDir = Vector2.zero;
			m_SmoothDirVel = Vector2.zero;

			m_Animator.SetFloat(s_NavSpeed, 0f);
			m_Animator.SetFloat(s_NavStrafe, 0f);
			m_Animator.SetFloat(s_NavForward, 1f);
			int locomotionTier = (int)m_Mode;
			if (m_StanceSource != null && m_StanceSource.CurrentStance == LocomotionStance.Prone)
				locomotionTier = 0;
			ApplyAnimatorLocomotionTierCap(ref locomotionTier);
			SetAnimatorLocomotionTier(locomotionTier);
			ApplyAnimatorPlaybackSpeedOutputs(0f, 0f);
			return;
		}

		Vector3 worldDirection = PlanarLocomotionDirection(out float planarSpeed, out bool hasMoveIntent);
		if (ShouldSnapEngageSteadyLocomotion(planarSpeed))
		{
			ApplyEngageSteadyLocomotionAnimatorOutputs();
			ApplyAnimatorPlaybackSpeedOutputs(0f, 0f);
			return;
		}
		if (IsEngagingVisibleTarget() && NavAgentHasIncompletePath())
		{
			Vector3 toSteer = m_Agent.steeringTarget - transform.position;
			toSteer.y = 0f;
			if (toSteer.sqrMagnitude > 1e-4f)
				worldDirection = toSteer.normalized;
		}

		bool moving = planarSpeed > m_StopVelocityEpsilon || hasMoveIntent;
		m_LastLocomotionWorldDirection = moving ? worldDirection : Vector3.zero;

		Vector3 localDirection = moving
			? transform.InverseTransformDirection(worldDirection)
			: Vector3.forward;

		float target01 = 0f;
		if (moving && m_SprintSpeed > 0.01f)
			target01 = Mathf.Clamp01(planarSpeed / m_SprintSpeed);

		bool inBrakeZone = false;
		if (!m_Agent.pathPending &&
		    m_BrakeAnimLeadDistance > 0.01f &&
		    m_Agent.hasPath &&
		    !float.IsPositiveInfinity(m_Agent.remainingDistance))
		{
			float remainingDistance = m_Agent.remainingDistance;
			float stoppingDistance = Mathf.Max(m_Agent.stoppingDistance, 0.02f);
			float bandEnd = stoppingDistance + m_BrakeAnimLeadDistance;
			if (remainingDistance < bandEnd)
			{
				inBrakeZone = true;
				float goalCap = Mathf.Clamp01((remainingDistance - stoppingDistance) / m_BrakeAnimLeadDistance);
				target01 = Mathf.Min(target01, goalCap);
			}
		}

		bool pathCommitted = HasActiveMoveIntent();
		if (pathCommitted && !inBrakeZone && moving && m_SprintSpeed > 0.01f)
		{
			float cruise01 = Mathf.Clamp01(m_Agent.speed / m_SprintSpeed);
			float velocity01 = Mathf.Clamp01(planarSpeed / m_SprintSpeed);
			if (cruise01 > 0.004f && velocity01 < cruise01 * 0.55f)
			{
				float floorTarget = cruise01 * m_StartNavSpeedFloor;
				float cappedFloor = Mathf.Min(
					floorTarget,
					velocity01 + m_StartNavSpeedFloorMaxLeadOverVelocity);
				target01 = Mathf.Max(target01, cappedFloor);
			}
		}

		float speedSmooth = target01 > m_SmoothSpeed01 + 0.002f
			? m_SpeedSmoothTimeAccelerate
			: m_SpeedSmoothTime;
		m_SmoothSpeed01 = Mathf.SmoothDamp(
			m_SmoothSpeed01,
			target01,
			ref m_SmoothSpeedVel,
			speedSmooth,
			Mathf.Infinity,
			Time.deltaTime);

		Vector2 targetDir = new Vector2(localDirection.x, localDirection.z);
		if (targetDir.sqrMagnitude > 1e-6f)
			targetDir.Normalize();

		float directionSmooth = moving && planarSpeed < m_StopVelocityEpsilon * 1.25f
				? m_DirectionSmoothTimeMoveStart
				: m_DirectionSmoothTime;

			bool engageMove = IsEngagingVisibleTarget() && moving;
			float directionSmoothUse = engageMove && m_EngageDirectionSmoothTime > 0.0001f
				? m_EngageDirectionSmoothTime
				: directionSmooth;
			if (engageMove && m_EngageDirectionSmoothTime <= 0.0001f)
			{
				m_SmoothDir = targetDir;
				m_SmoothDirVel = Vector2.zero;
			}
			else
			{
				m_SmoothDir = Vector2.SmoothDamp(
					m_SmoothDir,
					moving ? targetDir : Vector2.zero,
					ref m_SmoothDirVel,
					directionSmoothUse,
					Mathf.Infinity,
					Time.deltaTime);
			}

		float navSpeedOut = m_SmoothSpeed01;
		if (m_PostStandLowNavSpeedUntil > Time.time && HasActiveMoveIntent())
			navSpeedOut = Mathf.Min(navSpeedOut, m_StandUpNavSpeedAnimatorCeiling);

		int locomotionTierOut = (int)m_Mode;
		if (m_StanceSource != null && m_StanceSource.CurrentStance == LocomotionStance.Prone)
			locomotionTierOut = 0;
		ApplyAnimatorLocomotionDirectionConstraints(ref locomotionTierOut, navSpeedOut);

		m_Animator.SetFloat(s_NavSpeed, navSpeedOut);
		m_Animator.SetFloat(s_NavStrafe, m_SmoothDir.x);
		m_Animator.SetFloat(s_NavForward, m_SmoothDir.y);

		SetAnimatorLocomotionTier(locomotionTierOut);
		ApplyAnimatorPlaybackSpeedOutputs(navSpeedOut, planarSpeed);
	}

	private void ApplyAnimatorPlaybackSpeedOutputs(float _navSpeedOut, float _planarSpeed)
	{
		AnimatorPlaybackSpeedMultiplier = 1f;
		if (!m_SyncAnimatorPlaybackToGroundSpeed || m_SprintSpeed < 0.01f)
		{
			ApplyAnimatorPlaybackSpeedDirect(1f);
			return;
		}

		const float moveThreshold = 0.055f;
		if (_navSpeedOut < moveThreshold || _planarSpeed < m_StopVelocityEpsilon)
		{
			ApplyAnimatorPlaybackSpeedDirect(1f);
			return;
		}

		float ground01 = Mathf.Clamp01(_planarSpeed / m_SprintSpeed);
		float ratio = ground01 / Mathf.Max(_navSpeedOut, 0.02f);
		AnimatorPlaybackSpeedMultiplier = Mathf.Clamp(ratio, m_PlaybackSyncMin, m_PlaybackSyncMax);
		ApplyAnimatorPlaybackSpeedDirect(AnimatorPlaybackSpeedMultiplier);
	}

	private void ApplyAnimatorPlaybackSpeedDirect(float _multiplier)
	{
		if (m_Animator == null || m_CachedRtsMember != null)
			return;

		if (TryGetComponent(out UnitEquippedWeaponPoseRuntimeTuner tuner) && tuner.ShouldFreezeWalkAnimator)
		{
			m_Animator.speed = 0f;
			return;
		}

		m_Animator.speed = _multiplier;
	}

	private bool ShouldSnapEngageSteadyLocomotion(float _planarSpeed)
	{
		if (!m_SteadyAnimatorLocomotionWhileEngagingAndFiring || !IsEngagingVisibleTarget())
			return false;

		if (m_SteadyLocomotionRequiresFireCommand)
		{
			if (m_FireController == null)
				m_FireController = GetComponent<UnitWeaponFireController>();
			if (m_FireController == null || !m_FireController.IsFiringCommandActive)
				return false;
		}

		if (m_SteadyLocomotionOnlyWhenNearlyStationary)
		{
			if (_planarSpeed > m_StopVelocityEpsilon || HasActiveMoveIntent())
				return false;
		}

		return true;
	}

	private void ApplyEngageSteadyLocomotionAnimatorOutputs()
	{
		m_SmoothSpeed01 = 0f;
		m_SmoothSpeedVel = 0f;
		m_SmoothDir = new Vector2(0f, 1f);
		m_SmoothDirVel = Vector2.zero;

		m_Animator.SetFloat(s_NavSpeed, 0f);
		m_Animator.SetFloat(s_NavStrafe, 0f);
		m_Animator.SetFloat(s_NavForward, 1f);

		int locomotionTierOut = (int)m_Mode;
		if (m_StanceSource != null && m_StanceSource.CurrentStance == LocomotionStance.Prone)
			locomotionTierOut = 0;
		ApplyAnimatorLocomotionTierCap(ref locomotionTierOut);
		SetAnimatorLocomotionTier(locomotionTierOut);
	}

	private void SetAnimatorLocomotionTier(int _tier)
	{
		m_Animator.SetInteger(s_LocomotionTier, _tier);
		m_Animator.SetFloat(s_LocomotionTierBlend, _tier);
	}

	private void SnapAnimatorNavSpeedToCurrentVelocity()
	{
		if (m_Agent == null || m_SprintSpeed < 0.01f)
		{
			m_SmoothSpeed01 = 0f;
			m_SmoothSpeedVel = 0f;
			return;
		}

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		m_SmoothSpeed01 = Mathf.Clamp01(velocity.magnitude / m_SprintSpeed);
		m_SmoothSpeedVel = 0f;
	}
	#endregion
}
