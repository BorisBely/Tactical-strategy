using UnityEngine;
using UnityEngine.AI;

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

	private static readonly string[] s_PistolStanceBlockingStateNames =
	{
		"Pistol_Prone2Idle",
		"Pistol_Prone2Crouch",
		"Pistol_Idle2Prone",
		"Pistol_Crouch2Prone",
	};
	#endregion

	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitAnimatorStance m_StanceSource;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitWeaponFireController m_FireController;
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
	[SerializeField, Min(0.1f)] private float m_CrouchWalkSpeed = 1.15f;
	[SerializeField, Min(0.05f)] private float m_ProneCrawlSpeed = 0.5f;

	[Header("Stand Up")]
	[SerializeField, Min(0f)] private float m_AfterStandUpWalkAnimHoldSeconds = 0.32f;
	[SerializeField, Range(0.01f, 0.054f)] private float m_StandUpNavSpeedAnimatorCeiling = 0.042f;

	[Header("Rotation")]
	[SerializeField, Min(0.1f)] private float m_RotateSpeed = 6f;
	[SerializeField, Min(0.02f)] private float m_FacingTargetYawSmoothTime = 0.18f;

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
	[SerializeField, Min(0f)] private float m_BrakeAnimLeadDistance = 0.9f;
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
	private float m_PostStandLowNavSpeedUntil = -1f;
	private bool m_HasPendingNavOrder;
	private Vector3 m_PendingNavDestination;
	private bool m_PendingNavOverridesMode;
	private MoveTier m_PendingNavMode;
	private bool m_StanceMovementWasBlocked;
	#endregion

	#region Public Properties
	public bool IsSprintMoveMode => m_Mode == MoveTier.Sprint;
	public bool IsWalkOrRunMoveMode => m_Mode == MoveTier.Walk || m_Mode == MoveTier.Run;
	public bool HasMoveIntent => HasActiveMoveIntent();
	#endregion

	#region Public Methods
	public void ForceWalkMoveMode()
	{
		if (m_Mode == MoveTier.Walk)
			return;

		m_Mode = MoveTier.Walk;
		if (m_Agent != null)
			ApplyTierSpeed();
	}

	public void SetMoveTier(MoveTier _moveTier)
	{
		if (m_Mode == _moveTier)
			return;

		m_Mode = _moveTier;
		if (m_Agent != null)
			ApplyTierSpeed();
	}

	public bool TrySetDestination(Vector3 _worldPosition)
	{
		if (m_Agent == null)
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
			return false;

		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, m_NavMeshSampleRadius, NavMesh.AllAreas))
			return false;

		IssueNavOrderInternal(hit.position, _moveTier);
		return true;
	}

	public void HardStop()
	{
		if (m_Agent == null)
			return;

		m_HasPendingNavOrder = false;
		m_Agent.isStopped = true;
		m_Agent.ResetPath();
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();

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

		if (m_HasPendingNavOrder && !IsStanceTransitionMovementBlocked())
			ConsumePendingNavOrder();

		bool stanceMovementBlocked = IsStanceTransitionMovementBlocked();
		if (stanceMovementBlocked)
			m_Agent.isStopped = true;
		else if (m_StanceMovementWasBlocked && m_Agent.isStopped && NavAgentHasIncompletePath())
			m_Agent.isStopped = false;

		m_StanceMovementWasBlocked = stanceMovementBlocked;
		UpdateMoveTierForStanceChanges();
		UpdateFacing();
		PushAnimator();
	}
	#endregion

	#region Private Methods
	private void IssueNavOrderInternal(Vector3 _destination, MoveTier _moveTier)
	{
		if (IsStanceTransitionMovementBlocked())
		{
			m_PendingNavDestination = _destination;
			m_PendingNavOverridesMode = true;
			m_PendingNavMode = _moveTier;
			m_HasPendingNavOrder = true;
			return;
		}

		m_Agent.isStopped = false;
		m_Mode = _moveTier;
		EnsureStandingForFastMoveIfNeeded();
		ApplyTierSpeed();
		m_Agent.ResetPath();
		m_Agent.SetDestination(_destination);
		PrimeAnimatorForMoveStart();
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
			m_Mode = MoveTier.Walk;

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

		m_Agent.speed = maxSpeed;
		m_Agent.acceleration = Mathf.Max(m_AgentAcceleration, maxSpeed * 4.5f);
	}

	private float GetStandingTierSpeed()
	{
		switch (m_Mode)
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

	private bool NavAgentHasIncompletePath()
	{
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

	private void UpdateFacing()
	{
		Vector3 direction = Vector3.zero;

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

			direction = toTarget.normalized;
			float yawError = Vector3.SignedAngle(facingForwardXZ, direction, Vector3.up);
			float currentYaw = transform.eulerAngles.y;
			float targetYaw = currentYaw + yawError;
			float newYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref m_EngageYawVelocity, m_FacingTargetYawSmoothTime);
			transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
			return;
		}

		m_EngageYawVelocity = 0f;
		Vector3 velocity = new Vector3(m_Agent.velocity.x, 0f, m_Agent.velocity.z);
		float planarSpeed = velocity.magnitude;

		if (planarSpeed > m_StopVelocityEpsilon)
			direction = velocity.normalized;
		else if (NavAgentHasIncompletePath())
		{
			Vector3 toSteer = m_Agent.steeringTarget - transform.position;
			toSteer.y = 0f;
			if (toSteer.sqrMagnitude < 1e-6f)
				return;
			direction = toSteer.normalized;
		}
		else
			return;

		Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
		transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, m_RotateSpeed * Time.deltaTime);
	}

	private bool IsEngagingVisibleTarget()
	{
		return m_Vision != null && m_Vision.VisibleTarget != null && ShouldRotateRootTowardVisionTarget();
	}

	private bool ShouldRotateRootTowardVisionTarget()
	{
		if (m_Mode == MoveTier.Sprint)
			return false;
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
			(int)LocomotionWeaponMode.Pistol => s_PistolStanceBlockingStateNames,
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
			m_Animator.SetInteger(s_LocomotionTier, locomotionTier);
			return;
		}

		Vector3 worldDirection = PlanarLocomotionDirection(out float planarSpeed, out bool hasMoveIntent);
		if (ShouldSnapEngageSteadyLocomotion(planarSpeed))
		{
			ApplyEngageSteadyLocomotionAnimatorOutputs();
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

		m_Animator.SetFloat(s_NavSpeed, navSpeedOut);
		m_Animator.SetFloat(s_NavStrafe, m_SmoothDir.x);
		m_Animator.SetFloat(s_NavForward, m_SmoothDir.y);

		int locomotionTierOut = (int)m_Mode;
		if (m_StanceSource != null && m_StanceSource.CurrentStance == LocomotionStance.Prone)
			locomotionTierOut = 0;
		m_Animator.SetInteger(s_LocomotionTier, locomotionTierOut);
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
		m_Animator.SetInteger(s_LocomotionTier, locomotionTierOut);
	}
	#endregion
}
