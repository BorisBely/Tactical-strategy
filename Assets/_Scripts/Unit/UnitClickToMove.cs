using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// NavMesh: ПКМ — точка на поверхности, двойной ПКМ — спринт, F — жёсткий стоп.
/// Переключение стоя ↔ присед (C): заказ скорости сбрасывается на шаг, чтобы после приседа не продолжался бег/спринт.
/// Двойной ПКМ из приседа/лёжа: встать и сразу спринт — отдельный путь, сброс на шаг не применяется.
/// В приседе/лёжа — скорость агента по стойке; скорость приседа задаётся в м/с под клип.
/// Поворот на цель: yaw через <see cref="m_FacingTargetYawSmoothTime"/>; при engage горизонталь от <see cref="UnitVision.GetEngageFacingOriginWorld"/> если активен прицел в UnitVision, иначе от корня. NavStrafe/NavForward сглаживаются (<see cref="m_DirectionSmoothTime"/>, при engage — <see cref="m_EngageDirectionSmoothTime"/>). В UnitVision — расширение конуса при удержании цели.
/// Root motion у Animator выключен.
/// В лёже <c>LocomotionTier</c> на аниматоре всегда 0 (ползок). Параметры: NavSpeed, NavStrafe, NavForward, LocomotionTier, Stance.
/// На время клипов смены стойки с лёжа (без оружия и с винтовкой, см. <see cref="IsStanceTransitionMovementBlocked"/>) NavMesh и очередь ПКМ замирают до конца клипа.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public sealed class UnitClickToMove : MonoBehaviour
{
	/// <summary>0..1 — интенсивность локомоции (скорость относительно спринта).</summary>
	public const string ParamNavSpeed = "NavSpeed";

	/// <summary>Локальная ось X направления движения (−1..1), для стрейфа в blend tree.</summary>
	public const string ParamNavStrafe = "NavStrafe";

	/// <summary>Локальная ось Z направления движения (−1..1), вперёд/назад.</summary>
	public const string ParamNavForward = "NavForward";

	/// <summary>Ярус скорости заказа: 0 walk, 1 run, 2 sprint — выбор loop/stop в стоячей локомоции (и start/loop в приседе и т.д.).</summary>
	public const string ParamLocomotionTier = "LocomotionTier";
	public const string ParamLocomotionTierBlend = "LocomotionTierBlend";

	private static readonly int s_NavSpeed = Animator.StringToHash(ParamNavSpeed);
	private static readonly int s_NavStrafe = Animator.StringToHash(ParamNavStrafe);
	private static readonly int s_NavForward = Animator.StringToHash(ParamNavForward);
	private static readonly int s_LocomotionTier = Animator.StringToHash(ParamLocomotionTier);
	private static readonly int s_LocomotionTierBlend = Animator.StringToHash(ParamLocomotionTierBlend);
	private static readonly int s_WeaponMode = Animator.StringToHash(UnitAnimatorWeaponMode.ParamWeaponMode);
	private static readonly int s_IsDraggingNotReady = Animator.StringToHash("IsDraggingNotReady");

	/// <summary>Слой 0, безоружная ветка: вставание из лёжа и укладка в лёжа (стоя или из приседа).</summary>
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

	[SerializeField] private Camera m_RayCamera;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField, Min(0.01f)] private float m_NavMeshSampleRadius = 2f;

	[Header("NavMeshAgent")]
	[SerializeField, Min(0f)] private float m_StoppingDistance = 0.15f;
	[SerializeField, Min(1f)] private float m_AgentAcceleration = 40f;
	[SerializeField] private bool m_WarpToNavMeshOnStart = true;
	[SerializeField, Min(0.5f)] private float m_WarpSearchRadius = 12f;

	[Header("Speeds")]
	[SerializeField, Min(0.1f)] private float m_WalkSpeed = 1.5f;
	[SerializeField, Min(0.1f)] private float m_RunSpeed = 3f;
	[SerializeField, Min(0.1f)] private float m_SprintSpeed = 7.25f;
	[Tooltip("Скорость NavMeshAgent в приседе (м/с). Подгоняй под шаг клипа Crouch_WalkFwdLoop (MovementAnimsetPro).")]
	[SerializeField, Min(0.1f)] private float m_CrouchWalkSpeed = 1.15f;
	[Tooltip("Скорость ползка лёжа (м/с).")]
	[SerializeField, Min(0.05f)] private float m_ProneCrawlSpeed = 0.5f;

	[Header("Speed smoothing (NavMeshAgent)")]
	[Tooltip("Плавность смены реальной скорости NavMeshAgent при переключении Walk/Run/Sprint. 0 = мгновенно.")]
	[SerializeField, Min(0f)] private float m_AgentSpeedSmoothSeconds = 0.15f;
	[Tooltip("Отдельная плавность при снижении скорости (обычно чуть больше). 0 = мгновенно.")]
	[SerializeField, Min(0f)] private float m_AgentSpeedSmoothSecondsDecel = 0.2f;

	[Header("Стояние: анимация вставания при движении")]
	[Tooltip("После перехода лёжа → стоя при активном пути: время, когда NavSpeed для аниматора ограничен, чтобы граф успел пройти через Stand_Relaxed_Rifle_Idle.")]
	[SerializeField, Min(0f)] private float m_AfterStandUpWalkAnimHoldSeconds = 0.32f;
	[Tooltip("Потолок NavSpeed на время удержания после вставания из лёжа (ниже порога движения в контроллере, обычно 0.055).")]
	[SerializeField, Range(0.01f, 0.054f)] private float m_StandUpNavSpeedAnimatorCeiling = 0.042f;

	[Header("Rotation")]
	[Tooltip("Скорость разворота корня (на путь или по velocity), коэффициент Slerp.")]
	[SerializeField, Min(0.1f)] private float m_RotateSpeed = 6f;
	[Tooltip("Сглаживание только yaw при развороте на видимую цель (сек).")]
	[SerializeField, Min(0.02f)] private float m_FacingTargetYawSmoothTime = 0.18f;
	[Tooltip("Не разворачивать корень на видимую цель (engage) во время перезарядки и передёргивания затвора.")]
	[SerializeField] private bool m_BlockEngageFacingDuringReload = true;

	[Header("Боёвка: стабильная стойка при стрельбе")]
	[Tooltip("При engage и активной команде огня, пока агент почти стоит — жёстко выставить NavSpeed=0 и NavForward=1, NavStrafe=0, чтобы blend tree не подмешивал шаг/страф к прицелу.")]
	[SerializeField] private bool m_SteadyAnimatorLocomotionWhileEngagingAndFiring = true;
	[Tooltip("Если выключено — залипание idle при любом engage без проверки команды огня.")]
	[SerializeField] private bool m_SteadyLocomotionRequiresFireCommand = true;
	[Tooltip("Не отключать анимацию движения, пока агент реально едет или есть незавершённый заказ пути.")]
	[SerializeField] private bool m_SteadyLocomotionOnlyWhenNearlyStationary = true;

	[Header("Input")]
	[SerializeField] private bool m_EnableDirectInput = true;
	[SerializeField, Min(0.05f)] private float m_DoubleClickSeconds = 0.25f;
	[Tooltip("Гибрид одиночного/двойного ПКМ: одиночный клик откладывается на небольшой интервал, чтобы Unity успела распознать двойной клик.\nЕсли второй клик пришёл в окно double-click — одиночная команда отменяется и сразу идёт Sprint.")]
	[SerializeField, Min(0.01f)] private float m_SingleClickCommitDelaySeconds = 0.12f;
	[SerializeField] private bool m_BlockClicksOverUi = true;
	[SerializeField] private bool m_HardStopEnabled = true;
	[SerializeField] private Key m_HardStopKey = Key.F;

	[Header("Animator smoothing")]
	[SerializeField, Min(0.01f)] private float m_SpeedSmoothTime = 0.12f;
	[Tooltip("Отдельное сглаживание при наборе NavSpeed (меньше — быстрее реакция старта).")]
	[SerializeField, Min(0.005f)] private float m_SpeedSmoothTimeAccelerate = 0.035f;
	[Tooltip("Сглаживание NavStrafe/NavForward (2D blend tree). Больше — плавнее смена направления шага/бега.")]
	[SerializeField, Min(0.01f)] private float m_DirectionSmoothTime = 0.14f;
	[Tooltip("Быстрее выравнивать NavForward/Strafe при почти нулевой скорости и активном заказе движения.")]
	[SerializeField, Min(0.005f)] private float m_DirectionSmoothTimeMoveStart = 0.055f;
	[Tooltip("Сглаживание направления blend tree при движении к видимой цели (engage). Меньше — острее; 0 — как раньше (мгновенно).")]
	[SerializeField, Min(0f)] private float m_EngageDirectionSmoothTime = 0.055f;
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;
	[Tooltip("Пока фактическая скорость агента ниже доли круиза, NavSpeed не ниже этого уровня от круиза — чтобы стартовые клипы не отставали от разгона NavMeshAgent.")]
	[SerializeField, Range(0.35f, 1f)] private float m_StartNavSpeedFloor = 0.88f;
	[SerializeField, Range(0f, 0.25f)] private float m_StartNavSpeedFloorMaxLeadOverVelocity = 0.1f;
	[Tooltip("За сколько метров до цели (поверх stopping distance) начинать снижать NavSpeed, чтобы клип остановки шёл во время замедления, а не после полной остановки.")]
	[SerializeField, Min(0f)] private float m_BrakeAnimLeadDistance = 0.9f;

	[Header("Animator playback sync")]
	[SerializeField] private bool m_SyncAnimatorPlaybackToGroundSpeed = true;
	[SerializeField, Range(0.4f, 1.5f)] private float m_PlaybackSyncMin = 0.55f;
	[SerializeField, Range(0.5f, 2f)] private float m_PlaybackSyncMax = 1.45f;

	private NavMeshAgent m_Agent;
	private MoveTier m_Mode = MoveTier.Walk;
	private LocomotionStance m_LastStance = LocomotionStance.Standing;
	private float m_LastRightClickTime = -1f;

	private float m_SmoothSpeed01;
	private float m_SmoothSpeedVel;
	private Vector2 m_SmoothDir;
	private Vector2 m_SmoothDirVel;
	private float m_EngageYawVelocity;

	private float m_PostStandLowNavSpeedUntil = -1f;

	private bool m_HasPendingNavOrder;
	private Vector3 m_PendingNavDestination;
	private bool m_PendingNavOverridesMode;
	private MoveTier m_PendingNavMode;
	private float m_TargetAgentSpeed;

	// Single vs double right-click debounce (ПКМ и RTS-команда «шаг»):
	// одиночный клик откладывается, пока юнит уже бежит/спринтует — чтобы не сбрасывать скорость до двойного ПКМ.
	private bool m_HasPendingRightClick;
	private float m_PendingRightClickTime = -1f;
	private Vector3 m_PendingRightClickDestination;

	/// <summary>Предыдущий кадр: шла блокировка движения из‑за клипа смены стойки (для однократного снятия isStopped).</summary>
	private bool m_StanceMovementWasBlocked;
	private RtsUnitMember m_CachedRtsMember;

	public bool IsSprintMoveMode => IsSprintActive();

	/// <summary>Множитель скорости проигрывания клипов (1 = без подстройки).</summary>
	public float AnimatorPlaybackSpeedMultiplier { get; private set; } = 1f;

	public bool IsWalkOrRunMoveMode => m_Mode == MoveTier.Walk || m_Mode == MoveTier.Run;

	public bool DirectInputEnabled => m_EnableDirectInput;

	public bool HasMoveIntent => HasActiveMoveIntent();

	private bool IsSprintActive()
	{
		if (m_Mode != MoveTier.Sprint)
			return false;
		if (HasActiveMoveIntent() || NavAgentHasIncompletePath())
			return true;
		if (m_Agent == null)
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
		if (m_Agent == null)
			return false;

		Vector3 velocity = m_Agent.velocity;
		velocity.y = 0f;
		return velocity.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon;
	}

	/// <summary>
	/// Принудительно сбросить заказ скорости на шаг (например, для механик, которым нельзя оставаться в спринте).
	/// </summary>
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

	public bool TrySetDestination(Vector3 _worldPosition)
	{
		if (m_Agent == null)
			return false;
		if (!IsConscious())
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

	public bool IssueNavOrder(Vector3 _worldPosition, MoveTier _mode)
	{
		if (m_Agent == null)
			return false;
		if (!IsConscious())
			return false;

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return false;

		if (_mode != MoveTier.Walk)
		{
			m_HasPendingRightClick = false;
			m_PendingRightClickTime = -1f;
			IssueNavOrderInternal(hit.position, _mode);
			return true;
		}

		// RTS / внешний одиночный клик во время бега/спринта: отложить шаг, чтобы успеть распознать двойной ПКМ.
		if (m_Mode != MoveTier.Walk)
		{
			m_HasPendingRightClick = true;
			m_PendingRightClickTime = Time.time;
			m_PendingRightClickDestination = hit.position;
			return true;
		}

		IssueNavOrderInternal(hit.position, _mode);
		return true;
	}

	public void SetDirectInputEnabled(bool _enabled)
	{
		m_EnableDirectInput = _enabled;
		if (_enabled)
			return;

		m_HasPendingRightClick = false;
		m_PendingRightClickTime = -1f;
	}

	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();

		if (m_RayCamera == null)
			m_RayCamera = Camera.main;

		m_Agent.updatePosition = true;
		m_Agent.updateRotation = false;
		m_Agent.stoppingDistance = m_StoppingDistance;

		if (m_Animator != null)
			m_Animator.applyRootMotion = false;

		m_TargetAgentSpeed = m_Agent != null ? m_Agent.speed : 0f;
		ApplyTierSpeed();
		if (m_StanceSource != null)
			m_LastStance = m_StanceSource.CurrentStance;
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponent<UnitStabilizeOtherController>();
		m_CachedRtsMember = GetComponent<RtsUnitMember>();
	}

	private void Start()
	{
		if (m_RayCamera == null)
		{
#if UNITY_2023_1_OR_NEWER
			m_RayCamera = Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Exclude);
#else
			m_RayCamera = Object.FindObjectOfType<Camera>();
#endif
			if (m_RayCamera == null)
				Debug.LogError("UnitClickToMove: нет камеры для луча ПКМ.", this);
		}

		if (m_Agent != null && m_WarpToNavMeshOnStart && !m_Agent.isOnNavMesh)
		{
			if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, m_WarpSearchRadius, NavMesh.AllAreas))
			{
				m_Agent.Warp(hit.position);
				Debug.LogWarning("UnitClickToMove: юнит не на NavMesh — перенос к ближайшей точке.", this);
			}
			else
				Debug.LogError("UnitClickToMove: нет точки на NavMesh в радиусе Warp Search Radius.", this);
		}
	}

	private void Update()
	{
		if (m_Agent == null)
			return;
		if (!IsConscious())
		{
			HardStop();
			PushAnimator();
			return;
		}

		if (m_HasPendingNavOrder && !IsStanceTransitionMovementBlocked())
			ConsumePendingNavOrder();

		bool stanceMovementBlocked = IsStanceTransitionMovementBlocked();
		if (stanceMovementBlocked)
			m_Agent.isStopped = true;
		else if (m_StanceMovementWasBlocked && m_Agent.isStopped && NavAgentHasIncompletePath())
			m_Agent.isStopped = false;

		m_StanceMovementWasBlocked = stanceMovementBlocked;

		if (m_StanceSource != null)
		{
			LocomotionStance stance = m_StanceSource.CurrentStance;
			if (stance != m_LastStance)
			{
				// Только из лёжа: искусственно низкий NavSpeed, чтобы граф успел пройти через Stand_Relaxed_Rifle_Idle.
				// Из приседа при движении NavSpeed не режем — иначе Entry в Locomotion_Unarmed не сработает.
				if (m_AfterStandUpWalkAnimHoldSeconds > 0.001f &&
				    HasActiveMoveIntent() &&
				    stance == LocomotionStance.Standing &&
				    m_LastStance == LocomotionStance.Prone)
					m_PostStandLowNavSpeedUntil = Time.time + m_AfterStandUpWalkAnimHoldSeconds;

				// Стоя ↔ присед / стоя ↔ лёжа / присед ↔ лёжа (Z, C): заказ скорости — шаг. ПКМ+Shift/двойной клик из приседа/лёжа
				// не проходит сюда (TryRightClick + ForceStanding).
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
		}

		if (CanUseDirectInput() &&
		    m_HardStopEnabled && Keyboard.current != null &&
		    Keyboard.current[m_HardStopKey].wasPressedThisFrame)
		{
			if (TryGetComponent(out UnitSelfStabilizationController selfStabilization))
				selfStabilization.StopSelfStabilization();

			if (TryGetComponent(out UnitStabilizeOtherController stabilizeOther))
				stabilizeOther.StopStabilizeOther();

			if (TryGetComponent(out UnitFiremanCarryController firemanCarry))
				firemanCarry.RequestRelease();

			HardStop();
		}

		TickPendingSingleRightClick();

		if (CanUseDirectInput() && m_RayCamera != null)
			TryRightClick();

		UpdateAgentSpeedToTarget();
		UpdateFacing();
		PushAnimator();
		TryRestoreReadyAfterSprintWhenStopped();
		TryRestoreReadyAfterRunWhenStopped();
	}

	private bool IsMovingOnNavMesh()
	{
		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon)
			return true;

		return m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f;
	}

	private bool CanUseDirectInput()
	{
		if (!m_EnableDirectInput)
			return false;
		if (!IsConscious())
			return false;
		if (m_Team == null)
			return true;

		return m_Team.Team == UnitTeamId.Player;
	}

	public void HardStop()
	{
		m_HasPendingNavOrder = false;
		m_HasPendingRightClick = false;
		m_PendingRightClickTime = -1f;

		if (m_Agent != null && m_Agent.enabled && m_Agent.isOnNavMesh)
		{
			m_Agent.isStopped = true;
			m_Agent.ResetPath();
		}

		m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	private void TickPendingSingleRightClick()
	{
		if (!m_HasPendingRightClick)
			return;

		if (m_PendingRightClickTime < 0f)
		{
			m_HasPendingRightClick = false;
			return;
		}

		// Hybrid: commit single click after short delay.
		// IMPORTANT: when the unit is already in a faster tier (Run/Sprint), committing "Walk" too early causes a visible jerk
		// if the player actually intended a double click. In that case we wait the full double-click window.
		float dt = Time.time - m_PendingRightClickTime;
		float commitDelay = m_Mode != MoveTier.Walk
			? m_DoubleClickSeconds
			: Mathf.Min(m_SingleClickCommitDelaySeconds, m_DoubleClickSeconds);
		commitDelay = Mathf.Max(0.01f, commitDelay);
		if (dt < commitDelay)
			return;

		m_HasPendingRightClick = false;
		m_PendingRightClickTime = -1f;
		IssueNavOrderInternal(m_PendingRightClickDestination, MoveTier.Walk);
	}

	private void IssueNavOrderInternal(Vector3 _destination, MoveTier _mode)
	{
		if (m_Agent == null)
			return;
		if (!IsConscious())
			return;
		if (IsHealingBlocked())
			return;

		if (_mode == MoveTier.Sprint)
			m_ReadyHands?.SuppressReadyForSprintIfNeeded();
		if (_mode == MoveTier.Run)
			m_ReadyHands?.SuppressReadyForRunIfNeeded();

		if (IsStanceTransitionMovementBlocked())
		{
			m_PendingNavDestination = _destination;
			m_PendingNavOverridesMode = true;
			m_PendingNavMode = _mode;
			m_HasPendingNavOrder = true;
			if (_mode != MoveTier.Sprint)
				m_ReadyHands?.TryRestoreReadyAfterSprint(false);
			if (_mode != MoveTier.Run)
				m_ReadyHands?.TryRestoreReadyAfterRun(false);
			return;
		}

		m_Agent.isStopped = false;
		m_Mode = _mode;
		EnsureStandingForFastMoveIfNeeded();
		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(_destination);
		PrimeAnimatorForMoveStart();
		if (_mode != MoveTier.Sprint)
			m_ReadyHands?.TryRestoreReadyAfterSprint(false);
		if (_mode != MoveTier.Run)
			m_ReadyHands?.TryRestoreReadyAfterRun(false);
	}

	private void TryRightClick()
	{
		if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame)
			return;

		if (m_BlockClicksOverUi && EventSystem.current != null &&
		    EventSystem.current.IsPointerOverGameObject())
			return;

		Ray ray = m_RayCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (!Physics.Raycast(ray, out RaycastHit hit, 500f, m_GroundMask, QueryTriggerInteraction.Ignore))
			return;

		if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return;

		bool doubleClick = m_LastRightClickTime >= 0f &&
		                   Time.time - m_LastRightClickTime <= m_DoubleClickSeconds;

		m_LastRightClickTime = Time.time;

		// Double click commits immediately as Run (and cancels pending single click).
		if (doubleClick)
		{
			m_HasPendingRightClick = false;
			m_PendingRightClickTime = -1f;
			IssueNavOrderInternal(navHit.position, MoveTier.Run);
			return;
		}

		// Single click: delay; actual commit happens in TickPendingSingleRightClick.
		m_HasPendingRightClick = true;
		m_PendingRightClickTime = Time.time;
		m_PendingRightClickDestination = navHit.position;
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

		// Как PushAnimator:moving — в покое (нет заказа пути до цели, скорость почти ноль) срезаем NavMeshAgent.speed,
		// чтобы не висел хвост сглаживания между тирами Walk/Run/Sprint.
		Vector3 planarVel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		bool moving = planarVel.magnitude > m_StopVelocityEpsilon || HasActiveMoveIntent();
		if (!moving)
		{
			m_Agent.speed = 0f;
			float targetStopped = Mathf.Max(0.01f, m_TargetAgentSpeed);
			float accelStopped = Mathf.Max(m_Agent.speed, targetStopped);
			m_Agent.acceleration = Mathf.Max(m_AgentAcceleration, accelStopped * 4.5f);
			return;
		}

		float target = Mathf.Max(0.01f, m_TargetAgentSpeed);

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
				// Time-to-target style: reach 'target' from 0 in approximately smoothSeconds.
				float maxDelta = Time.deltaTime * (target / smoothSeconds);
				m_Agent.speed = Mathf.MoveTowards(current, target, maxDelta);
			}
		}

		float accelBasis = Mathf.Max(m_Agent.speed, target);
		m_Agent.acceleration = Mathf.Max(m_AgentAcceleration, accelBasis * 4.5f);
	}

	private float GetStandingTierSpeed()
	{
		MoveTier tierForSpeed = m_Mode;
		switch (tierForSpeed)
		{
			case MoveTier.Walk:
				return m_WalkSpeed;
			case MoveTier.Run:
				return m_RunSpeed;
			case MoveTier.Sprint:
				return m_SprintSpeed;
			default:
				return m_WalkSpeed;
		}
	}

	/// <summary>Есть заказ двигаться: путь считается или уже есть и до цели дальше чем stopping distance.</summary>
	private bool HasActiveMoveIntent()
	{
		if (m_Agent == null)
			return false;

		if (m_Agent.isStopped)
			return false;
		if (m_Agent.pathPending)
			return true;
		if (!m_Agent.hasPath)
			return false;
		if (float.IsPositiveInfinity(m_Agent.remainingDistance))
			return false;
		return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
	}

	/// <summary>Есть незавершённый путь к цели; <c>isStopped</c> не учитывается (для снятия паузы ровно при выходе из блокировки стойки).</summary>
	private bool NavAgentHasIncompletePath()
	{
		if (m_Agent == null)
			return false;

		if (m_Agent.pathPending)
			return true;
		if (!m_Agent.hasPath)
			return false;
		if (float.IsPositiveInfinity(m_Agent.remainingDistance))
			return false;
		return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
	}

	private Vector3 PlanarLocomotionDirection(out float _planarSpeed, out bool _hasGoalAhead)
	{
		Vector3 vel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		_planarSpeed = vel.magnitude;
		_hasGoalAhead = HasActiveMoveIntent();

		if (_planarSpeed > m_StopVelocityEpsilon)
			return vel.normalized;

		if (_hasGoalAhead)
		{
			Vector3 to = m_Agent.steeringTarget - transform.position;
			to.y = 0f;
			return to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;
		}

		return transform.forward;
	}

	private void UpdateFacing()
	{
		Vector3 dir = Vector3.zero;
		bool visionFacing = false;

		if (IsEngagingVisibleTarget())
		{
			if (m_Vision == null ||
			    !m_Vision.TryGetEngageFacingOriginWorld(out Vector3 origin) ||
			    !m_Vision.TryGetEngageFacingForwardXZ(out Vector3 facingForwardXZ))
				return;

			Vector3 aimPoint = m_Vision.GetVisibleTargetAimPointWorld();
			Vector3 toTarget = aimPoint - origin;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude < 1e-6f)
				return;
			dir = toTarget.normalized;
			visionFacing = true;

			float yawError = Vector3.SignedAngle(facingForwardXZ, dir, Vector3.up);
			float currentYaw = transform.eulerAngles.y;
			float targetYaw = currentYaw + yawError;
			float newYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref m_EngageYawVelocity, m_FacingTargetYawSmoothTime);
			transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
			return;
		}
		else
		{
			m_EngageYawVelocity = 0f;
			Vector3 vel = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
			float planarSpeed = vel.magnitude;

			if (planarSpeed > m_StopVelocityEpsilon)
				dir = vel.normalized;
			else if (NavAgentHasIncompletePath())
			{
				Vector3 toSteer = m_Agent.steeringTarget - transform.position;
				toSteer.y = 0f;
				if (toSteer.sqrMagnitude < 1e-6f)
					return;
				dir = toSteer.normalized;
			}
			else
				return;
		}

		if (dir.sqrMagnitude < 1e-6f)
			return;

		Quaternion q = Quaternion.LookRotation(dir, Vector3.up);
		if (!visionFacing)
			transform.rotation = Quaternion.Slerp(transform.rotation, q, m_RotateSpeed * Time.deltaTime);
	}

	private bool IsEngagingVisibleTarget()
	{
		return m_Vision != null && m_Vision.VisibleTarget != null && ShouldRotateRootTowardVisionTarget();
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

	/// <summary>
	/// Разворот на <see cref="UnitVision.VisibleTarget"/>: только оружие + «готов», без спринта.
	/// </summary>
	private bool ShouldRotateRootTowardVisionTarget()
	{
		if (IsSprintActive() || IsRunActive())
			return false;
		if (m_BlockEngageFacingDuringReload)
		{
			if (m_ReloadController == null)
				m_ReloadController = GetComponent<UnitWeaponReloadController>();
			if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
				return false;
		}
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		return m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
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

	/// <summary>
	/// Пока на слое 0 играет переход в/из лёжа для текущего <c>WeaponMode</c> (без оружия или винтовка/пистолет в том же графе).
	/// </summary>
	private bool IsStanceTransitionMovementBlocked()
	{
		if (m_Animator == null)
			return false;

		string[] names = GetStanceBlockingStateNamesForWeaponMode(m_Animator.GetInteger(s_WeaponMode));
		if (names == null || names.Length == 0)
			return false;

		if (m_Animator.IsInTransition(0))
		{
			AnimatorStateInfo next = m_Animator.GetNextAnimatorStateInfo(0);
			if (AnimatorStateMatchesAnyName(ref next, names))
				return true;
			AnimatorStateInfo cur = m_Animator.GetCurrentAnimatorStateInfo(0);
			if (AnimatorStateMatchesAnyName(ref cur, names))
				return true;
			return false;
		}

		AnimatorStateInfo info = m_Animator.GetCurrentAnimatorStateInfo(0);
		if (!AnimatorStateMatchesAnyName(ref info, names))
			return false;

		float nt = info.normalizedTime;
		if (info.loop)
			nt %= 1f;
		return nt < 0.999f;
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

	private static bool AnimatorStateMatchesAnyName(ref AnimatorStateInfo _info, string[] _names)
	{
		for (int i = 0; i < _names.Length; i++)
		{
			if (_info.IsName(_names[i]))
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
		bool unarmedStandFwdOnly = wm == (int)LocomotionWeaponMode.Unarmed && stance == LocomotionStance.Standing && axisFwdOnly;
		bool rifleSprintFwdOnly = (wm == (int)LocomotionWeaponMode.Rifle || wm == (int)LocomotionWeaponMode.Pistol) &&
		                          stance == LocomotionStance.Standing &&
		                          locomotionTier == (int)MoveTier.Sprint &&
		                          axisFwdOnly;

		if (unarmedStandFwdOnly || rifleSprintFwdOnly)
		{
			m_SmoothDir = new Vector2(0f, 1f);
			m_SmoothDirVel = Vector2.zero;
		}
	}

	private void PushAnimator()
	{
		if (m_Animator == null)
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

		Vector3 worldDir = PlanarLocomotionDirection(out float planarSpeed, out bool hasMoveIntent);
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
				worldDir = toSteer.normalized;
		}

		bool moving = planarSpeed > m_StopVelocityEpsilon || hasMoveIntent;

		Vector3 local = moving
			? transform.InverseTransformDirection(worldDir)
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
			float rem = m_Agent.remainingDistance;
			float sd = Mathf.Max(m_Agent.stoppingDistance, 0.02f);
			float bandEnd = sd + m_BrakeAnimLeadDistance;
			if (rem < bandEnd)
			{
				inBrakeZone = true;
				float goalCap = Mathf.Clamp01((rem - sd) / m_BrakeAnimLeadDistance);
				target01 = Mathf.Min(target01, goalCap);
			}
		}

		bool pathCommitted = HasActiveMoveIntent();
		if (pathCommitted && !inBrakeZone && moving && m_SprintSpeed > 0.01f)
		{
			float cruise01 = Mathf.Clamp01(m_Agent.speed / m_SprintSpeed);
			float vel01 = Mathf.Clamp01(planarSpeed / m_SprintSpeed);
			if (cruise01 > 0.004f && vel01 < cruise01 * 0.55f)
			{
				float floorTarget = cruise01 * m_StartNavSpeedFloor;
				float cappedFloor = Mathf.Min(floorTarget, vel01 + m_StartNavSpeedFloorMaxLeadOverVelocity);
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

		Vector2 targetDir = new Vector2(local.x, local.z);
		if (targetDir.sqrMagnitude > 1e-6f)
			targetDir.Normalize();

		float dirSmooth = moving && planarSpeed < m_StopVelocityEpsilon * 1.25f
			? m_DirectionSmoothTimeMoveStart
			: m_DirectionSmoothTime;

		bool engageMove = IsEngagingVisibleTarget() && moving;
		float dirSmoothUse = engageMove && m_EngageDirectionSmoothTime > 0.0001f
			? m_EngageDirectionSmoothTime
			: dirSmooth;
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
				dirSmoothUse,
				Mathf.Infinity,
				Time.deltaTime);
		}

		float navSpeedOut = m_SmoothSpeed01;
		if (m_PostStandLowNavSpeedUntil > Time.time && HasActiveMoveIntent())
			navSpeedOut = Mathf.Min(navSpeedOut, m_StandUpNavSpeedAnimatorCeiling);

		int tier = (int)m_Mode;
		if (m_StanceSource != null && m_StanceSource.CurrentStance == LocomotionStance.Prone)
			tier = 0;
		ApplyAnimatorLocomotionDirectionConstraints(ref tier, navSpeedOut);

		m_Animator.SetFloat(s_NavSpeed, navSpeedOut);
		m_Animator.SetFloat(s_NavStrafe, m_SmoothDir.x);
		m_Animator.SetFloat(s_NavForward, m_SmoothDir.y);

		SetAnimatorLocomotionTier(tier);
		m_Animator.SetBool(s_IsDraggingNotReady, false);
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

		m_Animator.speed = Mathf.Max(0f, _multiplier);
	}

	public enum MoveTier
	{
		Walk,
		Run,
		Sprint
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

		int locomotionTier = (int)m_Mode;
		if (m_StanceSource != null && m_StanceSource.CurrentStance == LocomotionStance.Prone)
			locomotionTier = 0;
		ApplyAnimatorLocomotionTierCap(ref locomotionTier);
		SetAnimatorLocomotionTier(locomotionTier);
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
}
