using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared Walk / Reached / Cancel for Search, Attack, Defense, Retreat, Flee.
/// Does not change <see cref="UnitAIState"/>, Combat, Vision, or Memory.
/// </summary>
public sealed class TacticalNavigationExecutor
{
	#region Private Fields
	private IUnitMoveCommand m_Move;
	private bool m_Issued;
	private bool m_Reached;
	private bool m_IssueFailed;
	private bool m_Cancelled;
	#endregion

	#region Public Properties
	public bool Issued => m_Issued;
	public bool Reached => m_Reached;
	public bool IssueFailed => m_IssueFailed;
	public bool Cancelled => m_Cancelled;
	#endregion

	#region Public Methods
	public void Begin()
	{
		m_Issued = false;
		m_Reached = false;
		m_IssueFailed = false;
		m_Cancelled = false;
		m_Move = null;
	}

	/// <summary>
	/// 14.7: keep the mover bound and walk the next hop after an intermediate traverse.
	/// Does not issue Walk by itself.
	/// </summary>
	public void ContinueToNextHop()
	{
		m_Issued = false;
		m_Reached = false;
		m_IssueFailed = false;
		m_Cancelled = false;
	}

	public void Cancel(UnitAIController _controller)
	{
		BindMove(_controller);
		if (m_Move != null)
			m_Move.Stop();
		m_Issued = false;
		m_Reached = false;
		m_IssueFailed = false;
		m_Cancelled = true;
		m_Move = null;
	}

	public void Tick(
		UnitAIController _controller,
		bool _hasDestination,
		Vector3 _destination,
		float _arrivalRadius,
		UnitNavigationReason _reason)
	{
		if (_controller == null || m_Reached || m_IssueFailed || m_Cancelled)
			return;
		if (!_hasDestination)
			return;

		if (TacticalNavigationMath.IsInsideArrival(
			    _controller.transform.position,
			    _destination,
			    _arrivalRadius))
		{
			if (m_Issued)
			{
				BindMove(_controller);
				if (m_Move != null)
					m_Move.Stop();
			}

			m_Reached = true;
			return;
		}

		if (m_Issued && ShouldReissueWalk(_controller, _destination, _arrivalRadius))
			m_Issued = false;

		if (m_Issued)
			return;

		BindMove(_controller);
		if (m_Move == null || !m_Move.CanIssue)
			return;

		if (!m_Move.TryMoveTo(_destination, _reason))
		{
			m_IssueFailed = true;
			return;
		}

		m_Issued = true;
	}
	#endregion

	#region Private Methods
	private void BindMove(UnitAIController _controller)
	{
		if (m_Move != null || _controller == null)
			return;
		if (_controller.TryGetComponent(out IUnitMoveCommand existing))
		{
			m_Move = existing;
			return;
		}

		if (_controller.TryGetComponent(out UnitNavLocomotionDriver driver) && driver.enabled)
			m_Move = _controller.gameObject.AddComponent<UnitNavMoveCommand>();
	}

	private static bool ShouldReissueWalk(
		UnitAIController _controller,
		Vector3 _destination,
		float _arrivalRadius)
	{
		if (_controller == null)
			return false;
		if (TacticalNavigationMath.IsInsideArrival(
			    _controller.transform.position, _destination, _arrivalRadius))
			return false;
		if (!_controller.TryGetComponent(out NavMeshAgent agent) || !agent.enabled || !agent.isOnNavMesh)
			return false;
		float remaining = float.PositiveInfinity;
		if (!float.IsPositiveInfinity(agent.remainingDistance))
			remaining = agent.remainingDistance;
		return TacticalNavigationMath.ShouldReissueStuckWalk(
			false,
			agent.pathPending,
			agent.hasPath,
			remaining);
	}
	#endregion
}
