using UnityEngine;

/// <summary>
/// Простейший ИИ врага: идёт по списку точек и включает ready, когда видит цель игрока.
/// Стрельбу и более сложные решения можно наращивать поверх этого контроллера.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyPatrolAI : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;

	[Header("Patrol")]
	[SerializeField] private Transform[] m_PatrolPoints;
	[SerializeField] private bool m_LoopPatrol = true;
	[SerializeField, Min(0.05f)] private float m_WaypointReachDistance = 0.45f;
	[SerializeField] private bool m_StartFromClosestWaypoint = true;
	[SerializeField] private UnitNavLocomotionDriver.MoveTier m_PatrolMoveTier = UnitNavLocomotionDriver.MoveTier.Walk;

	private int m_CurrentPatrolIndex;
	private bool m_HasIssuedCurrentPatrolOrder;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
	}

	private void OnEnable()
	{
		SelectInitialPatrolIndex();
		m_HasIssuedCurrentPatrolOrder = false;
	}

	private void Update()
	{
		UpdateReadyState();
		UpdatePatrol();
	}
	#endregion

	#region Private Methods
	private void UpdateReadyState()
	{
		if (m_ReadyHands == null)
			return;

		bool wantsReady = m_Vision != null && m_Vision.VisibleTarget != null;
		if (m_ReadyHands.WantsReady == wantsReady)
			return;

		m_ReadyHands.SetReadyWanted(wantsReady);
	}

	private void UpdatePatrol()
	{
		if (m_LocomotionDriver == null)
			return;

		if (!TryGetCurrentPatrolPoint(out Transform patrolPoint))
		{
			m_LocomotionDriver.HardStop();
			return;
		}

		Vector3 toPoint = patrolPoint.position - transform.position;
		toPoint.y = 0f;
		float reachDistanceSq = m_WaypointReachDistance * m_WaypointReachDistance;
		bool reachedCurrentPoint = toPoint.sqrMagnitude <= reachDistanceSq;

		if (reachedCurrentPoint)
		{
			if (!TryAdvancePatrolIndex())
			{
				m_HasIssuedCurrentPatrolOrder = false;
				m_LocomotionDriver.HardStop();
				return;
			}

			patrolPoint = m_PatrolPoints[m_CurrentPatrolIndex];
			m_HasIssuedCurrentPatrolOrder = false;
		}

		if (m_HasIssuedCurrentPatrolOrder && m_LocomotionDriver.HasMoveIntent)
			return;

		if (m_LocomotionDriver.IssueNavOrder(patrolPoint.position, m_PatrolMoveTier))
			m_HasIssuedCurrentPatrolOrder = true;
	}

	private void SelectInitialPatrolIndex()
	{
		if (m_PatrolPoints == null || m_PatrolPoints.Length == 0)
		{
			m_CurrentPatrolIndex = -1;
			return;
		}

		if (!m_StartFromClosestWaypoint)
		{
			m_CurrentPatrolIndex = FindNextValidPatrolIndex(0);
			return;
		}

		int bestIndex = -1;
		float bestDistanceSq = float.MaxValue;
		for (int i = 0; i < m_PatrolPoints.Length; i++)
		{
			Transform patrolPoint = m_PatrolPoints[i];
			if (patrolPoint == null)
				continue;

			Vector3 delta = patrolPoint.position - transform.position;
			delta.y = 0f;
			float distanceSq = delta.sqrMagnitude;
			if (distanceSq < bestDistanceSq)
			{
				bestDistanceSq = distanceSq;
				bestIndex = i;
			}
		}

		m_CurrentPatrolIndex = bestIndex;
	}

	private bool TryGetCurrentPatrolPoint(out Transform _patrolPoint)
	{
		_patrolPoint = null;
		if (m_PatrolPoints == null || m_PatrolPoints.Length == 0)
			return false;
		if (m_CurrentPatrolIndex < 0 || m_CurrentPatrolIndex >= m_PatrolPoints.Length)
			return false;

		_patrolPoint = m_PatrolPoints[m_CurrentPatrolIndex];
		if (_patrolPoint != null)
			return true;

		int nextIndex = FindNextValidPatrolIndex(m_CurrentPatrolIndex + 1);
		if (nextIndex < 0)
			return false;

		m_CurrentPatrolIndex = nextIndex;
		_patrolPoint = m_PatrolPoints[m_CurrentPatrolIndex];
		return _patrolPoint != null;
	}

	private bool TryAdvancePatrolIndex()
	{
		if (m_PatrolPoints == null || m_PatrolPoints.Length <= 1)
			return false;

		int nextStartIndex = m_CurrentPatrolIndex + 1;
		int nextIndex = FindNextValidPatrolIndex(nextStartIndex);
		if (nextIndex >= 0)
		{
			m_CurrentPatrolIndex = nextIndex;
			return true;
		}

		if (!m_LoopPatrol)
			return false;

		nextIndex = FindNextValidPatrolIndex(0);
		if (nextIndex < 0 || nextIndex == m_CurrentPatrolIndex)
			return false;

		m_CurrentPatrolIndex = nextIndex;
		return true;
	}

	private int FindNextValidPatrolIndex(int _startIndex)
	{
		if (m_PatrolPoints == null || m_PatrolPoints.Length == 0)
			return -1;

		for (int i = Mathf.Max(0, _startIndex); i < m_PatrolPoints.Length; i++)
		{
			if (m_PatrolPoints[i] != null)
				return i;
		}

		return -1;
	}
	#endregion
}
