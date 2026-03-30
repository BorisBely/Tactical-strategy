using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// NavMesh: ПКМ — точка на поверхности, Shift+ПКМ — бег, двойной ПКМ — спринт, F — жёсткий стоп.
/// Переключение стоя ↔ присед (C): заказ скорости сбрасывается на шаг, чтобы после приседа не продолжался бег/спринт.
/// Shift+ПКМ / двойной ПКМ из приседа: встать и сразу бег/спринт — отдельный путь, сброс на шаг не применяется.
/// В приседе/лёжа — скорость агента по стойке; скорость приседа задаётся в м/с под клип.
/// Поворот по направлению движения, root motion у Animator выключен.
/// Параметры аниматора: NavSpeed, NavStrafe, NavForward, LocomotionTier, Stance (см. константы <see cref="UnitClickToMove"/>).
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

	private static readonly int s_NavSpeed = Animator.StringToHash(ParamNavSpeed);
	private static readonly int s_NavStrafe = Animator.StringToHash(ParamNavStrafe);
	private static readonly int s_NavForward = Animator.StringToHash(ParamNavForward);
	private static readonly int s_LocomotionTier = Animator.StringToHash(ParamLocomotionTier);

	[SerializeField] private Camera m_RayCamera;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField, Min(0.01f)] private float m_NavMeshSampleRadius = 2f;

	[Header("NavMeshAgent")]
	[SerializeField, Min(0f)] private float m_StoppingDistance = 0.15f;
	[SerializeField, Min(1f)] private float m_AgentAcceleration = 40f;
	[SerializeField] private bool m_WarpToNavMeshOnStart = true;
	[SerializeField, Min(0.5f)] private float m_WarpSearchRadius = 12f;

	[Header("Speeds")]
	[SerializeField, Min(0.1f)] private float m_WalkSpeed = 1.5f;
	[SerializeField, Min(0.1f)] private float m_RunSpeed = 3.5f;
	[SerializeField, Min(0.1f)] private float m_SprintSpeed = 7.25f;
	[Tooltip("Скорость NavMeshAgent в приседе (м/с). Подгоняй под шаг клипа Crouch_WalkFwdLoop (MovementAnimsetPro).")]
	[SerializeField, Min(0.1f)] private float m_CrouchWalkSpeed = 1.15f;
	[Tooltip("Скорость ползка лёжа (м/с).")]
	[SerializeField, Min(0.05f)] private float m_ProneCrawlSpeed = 0.5f;

	[Header("Стояние: анимация вставания при движении")]
	[Tooltip("После перехода лёжа → стоя при активном пути: время, когда NavSpeed для аниматора ограничен (ниже порога движения), чтобы тянуть граф через Stand_Idle. Из приседа при движении не используется — см. Entry в NavMeshLocomotion (StandUnarmed).")]
	[SerializeField, Min(0f)] private float m_AfterStandUpWalkAnimHoldSeconds = 0.32f;
	[Tooltip("Потолок NavSpeed на время удержания после вставания из лёжа (ниже порога движения в контроллере, обычно 0.055).")]
	[SerializeField, Range(0.01f, 0.054f)] private float m_StandUpNavSpeedAnimatorCeiling = 0.042f;

	[Header("Rotation")]
	[SerializeField, Min(0.1f)] private float m_RotateSpeed = 12f;

	[Header("Input")]
	[SerializeField, Min(0.05f)] private float m_DoubleClickSeconds = 0.25f;
	[SerializeField] private bool m_BlockClicksOverUi = true;
	[SerializeField] private bool m_HardStopEnabled = true;
	[SerializeField] private Key m_HardStopKey = Key.F;

	[Header("Animator smoothing")]
	[SerializeField, Min(0.01f)] private float m_SpeedSmoothTime = 0.1f;
	[Tooltip("Отдельное сглаживание при наборе NavSpeed (меньше — быстрее реакция старта).")]
	[SerializeField, Min(0.005f)] private float m_SpeedSmoothTimeAccelerate = 0.028f;
	[SerializeField, Min(0.01f)] private float m_DirectionSmoothTime = 0.06f;
	[Tooltip("Быстрее выравнивать NavForward/Strafe при почти нулевой скорости и активном заказе движения.")]
	[SerializeField, Min(0.005f)] private float m_DirectionSmoothTimeMoveStart = 0.02f;
	[SerializeField, Min(0.01f)] private float m_StopVelocityEpsilon = 0.08f;
	[Tooltip("Пока фактическая скорость агента ниже доли круиза, NavSpeed не ниже этого уровня от круиза — чтобы стартовые клипы не отставали от разгона NavMeshAgent.")]
	[SerializeField, Range(0.35f, 1f)] private float m_StartNavSpeedFloor = 0.88f;
	[Tooltip("За сколько метров до цели (поверх stopping distance) начинать снижать NavSpeed, чтобы клип остановки шёл во время замедления, а не после полной остановки.")]
	[SerializeField, Min(0f)] private float m_BrakeAnimLeadDistance = 0.9f;

	private NavMeshAgent m_Agent;
	private MoveTier m_Mode = MoveTier.Walk;
	private LocomotionStance m_LastStance = LocomotionStance.Standing;
	private float m_LastRightClickTime = -1f;

	private float m_SmoothSpeed01;
	private float m_SmoothSpeedVel;
	private Vector2 m_SmoothDir;
	private Vector2 m_SmoothDirVel;

	private float m_PostStandLowNavSpeedUntil = -1f;

	public bool IsSprintMoveMode => m_Mode == MoveTier.Sprint;

	public bool IsWalkOrRunMoveMode => m_Mode == MoveTier.Walk || m_Mode == MoveTier.Run;

	public bool TrySetDestination(Vector3 _worldPosition)
	{
		if (m_Agent == null)
			return false;

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return false;

		m_Agent.isStopped = false;
		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(hit.position);
		PrimeAnimatorForMoveStart();
		return true;
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

		ApplyTierSpeed();
		if (m_StanceSource != null)
			m_LastStance = m_StanceSource.CurrentStance;
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

		if (m_StanceSource != null)
		{
			LocomotionStance stance = m_StanceSource.CurrentStance;
			if (stance != m_LastStance)
			{
				// Только из лёжа: искусственно низкий NavSpeed, чтобы граф шёл через Stand_Idle.
				// Из приседа при движении NavSpeed не режем — иначе Entry (Walk/Run/Sprint) в StandUnarmed не сработает.
				if (m_AfterStandUpWalkAnimHoldSeconds > 0.001f &&
				    HasActiveMoveIntent() &&
				    stance == LocomotionStance.Standing &&
				    m_LastStance == LocomotionStance.Prone)
					m_PostStandLowNavSpeedUntil = Time.time + m_AfterStandUpWalkAnimHoldSeconds;

				// Стоя ↔ присед по C (или выход из приседа C/Z): всегда шаг. Иначе после бега/спринта → присед → стоя
				// сохранялся Run/Sprint. ПКМ+Shift/двойной клик не проходит сюда: там ForceStanding() и m_LastStance
				// выставлены в TryRightClick без пары «был присед» в этом кадре.
				if ((stance == LocomotionStance.Crouch && m_LastStance == LocomotionStance.Standing) ||
				    (stance == LocomotionStance.Standing && m_LastStance == LocomotionStance.Crouch))
					m_Mode = MoveTier.Walk;

				m_LastStance = stance;
				ApplyTierSpeed();
			}
		}

		if (m_HardStopEnabled && Keyboard.current != null &&
		    Keyboard.current[m_HardStopKey].wasPressedThisFrame &&
		    IsMovingOnNavMesh())
			HardStop();

		if (m_RayCamera != null)
			TryRightClick();

		UpdateFacing();
		PushAnimator();
	}

	private bool IsMovingOnNavMesh()
	{
		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > m_StopVelocityEpsilon * m_StopVelocityEpsilon)
			return true;

		return m_Agent.hasPath && m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.05f;
	}

	private void HardStop()
	{
		m_Agent.isStopped = true;
		m_Agent.ResetPath();
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

		m_Agent.isStopped = false;

		bool shift = Keyboard.current != null &&
		             (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
		bool doubleClick = m_LastRightClickTime >= 0f &&
		                   Time.time - m_LastRightClickTime <= m_DoubleClickSeconds;

		if (doubleClick)
			m_Mode = MoveTier.Sprint;
		else if (shift)
			m_Mode = MoveTier.Run;
		else
			m_Mode = MoveTier.Walk;

		m_LastRightClickTime = Time.time;

		if (m_StanceSource != null &&
		    (m_Mode == MoveTier.Run || m_Mode == MoveTier.Sprint) &&
		    m_StanceSource.CurrentStance != LocomotionStance.Standing)
		{
			LocomotionStance stanceBeforeStand = m_StanceSource.CurrentStance;
			if (m_AfterStandUpWalkAnimHoldSeconds > 0.001f && stanceBeforeStand == LocomotionStance.Prone)
				m_PostStandLowNavSpeedUntil = Time.time + m_AfterStandUpWalkAnimHoldSeconds;
			m_StanceSource.ForceStanding();
			m_LastStance = LocomotionStance.Standing;
		}

		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(navHit.position);
		PrimeAnimatorForMoveStart();
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

		m_Agent.speed = maxSpeed;
		m_Agent.acceleration = Mathf.Max(m_AgentAcceleration, maxSpeed * 4.5f);
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
		Vector3 dir = PlanarLocomotionDirection(out _, out _);
		if (dir.sqrMagnitude < 1e-6f)
			return;

		Quaternion q = Quaternion.LookRotation(dir, Vector3.up);
		transform.rotation = Quaternion.Slerp(transform.rotation, q, m_RotateSpeed * Time.deltaTime);
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

	private void PushAnimator()
	{
		if (m_Animator == null)
			return;

		Vector3 worldDir = PlanarLocomotionDirection(out float planarSpeed, out bool hasMoveIntent);
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
				target01 = Mathf.Max(target01, cruise01 * m_StartNavSpeedFloor);
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
		m_SmoothDir = Vector2.SmoothDamp(
			m_SmoothDir,
			moving ? targetDir : Vector2.zero,
			ref m_SmoothDirVel,
			dirSmooth,
			Mathf.Infinity,
			Time.deltaTime);

		float navSpeedOut = m_SmoothSpeed01;
		if (m_PostStandLowNavSpeedUntil > Time.time && HasActiveMoveIntent())
			navSpeedOut = Mathf.Min(navSpeedOut, m_StandUpNavSpeedAnimatorCeiling);

		m_Animator.SetFloat(s_NavSpeed, navSpeedOut);
		m_Animator.SetFloat(s_NavStrafe, m_SmoothDir.x);
		m_Animator.SetFloat(s_NavForward, m_SmoothDir.y);

		// Всегда отражаем m_Mode. В графе присед не ветвится по tier; принудительный 0 ломал
		// Entry/переходы в стойку, если UnitAnimatorStance меняет Stance после этого Update.
		m_Animator.SetInteger(s_LocomotionTier, (int)m_Mode);
	}

	private enum MoveTier
	{
		Walk,
		Run,
		Sprint
	}
}
