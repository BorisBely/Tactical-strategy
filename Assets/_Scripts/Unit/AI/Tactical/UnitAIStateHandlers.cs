using UnityEngine;

/// <summary>
/// Tactical handlers. Engage is resolved on UnitAIController, not a new state.
/// Search / Attack / Defense / Retreat / Flee walk through <see cref="TacticalNavigationExecutor"/>.
/// Combat stays on CombatIntent.
/// </summary>
public sealed class UnitAIIdleHandler : IUnitAIStateHandler
{
	public UnitAIState State => UnitAIState.Idle;

	public void Enter(UnitAIController _controller, in UnitAIStateContext _context)
	{
	}

	public void Tick(UnitAIController _controller, float _dt)
	{
	}

	public void Exit(UnitAIController _controller)
	{
	}
}

public sealed class UnitAIDefenseHandler : UnitAIPointNavigationHandler
{
	public override UnitAIState State => UnitAIState.Defense;
}

public sealed class UnitAIAttackHandler : UnitAIPointNavigationHandler
{
	public override UnitAIState State => UnitAIState.Attack;
}

public sealed class UnitAISearchHandler : IUnitAIStateHandler
{
	#region Private Fields
	private readonly TacticalNavigationExecutor m_Nav = new TacticalNavigationExecutor();
	private float m_InspectTime;
	private bool m_Inspecting;
	#endregion

	public UnitAIState State => UnitAIState.Search;

	public void Enter(UnitAIController _controller, in UnitAIStateContext _context)
	{
		_controller.ClearSearchNavigationDebug();
		m_Nav.Begin();
		m_InspectTime = 0f;
		m_Inspecting = false;
		_controller.BeginSearchSession();
		Step(_controller);
	}

	public void Tick(UnitAIController _controller, float _dt)
	{
		if (m_Nav.IssueFailed)
		{
			_controller.TryResumeSearchBecauseNavigationFailed();
			return;
		}

		if (m_Inspecting)
		{
			m_InspectTime += Mathf.Max(0f, _dt);
			if (m_InspectTime < UnitAISearchDecision.InspectDuration)
				return;

			if (!_controller.TryAdvanceSearchCandidate())
			{
				_controller.TryCompleteSearchBecauseExhausted();
				return;
			}

			m_Nav.Begin();
			m_Inspecting = false;
			m_InspectTime = 0f;
			_controller.ClearSearchArrival();
			Step(_controller);
			return;
		}

		Step(_controller);
		if (m_Nav.Reached && !m_Inspecting)
		{
			_controller.NotifySearchAreaReached();
			_controller.NotifySearchCandidateInspected();
			m_Inspecting = true;
			m_InspectTime = 0f;
		}
	}

	public void Exit(UnitAIController _controller)
	{
		m_Nav.Cancel(_controller);
		_controller.FinishSearchSessionIfOpen();
		_controller.ClearSearchNavigationDebug();
	}

	#region Private Methods
	private void Step(UnitAIController _controller)
	{
		UnitAIStateContext context = _controller.CurrentContext;
		m_Nav.Tick(
			_controller,
			true,
			context.SearchPosition,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			UnitNavigationReason.Search);
		if (m_Nav.Issued)
			_controller.NotifySearchNavigationIssued();
		if (m_Nav.Reached)
			_controller.NotifySearchAreaReached();
	}
	#endregion
}

public sealed class UnitAIRetreatHandler : UnitAIPointNavigationHandler
{
	public override UnitAIState State => UnitAIState.Retreat;
}

public sealed class UnitAIFleeHandler : UnitAIPointNavigationHandler
{
	public override UnitAIState State => UnitAIState.Flee;

	protected override bool CompleteToIdleOnReached => true;
}

/// <summary>
/// Attack / Defense / Retreat / Flee: Walk current #14 hop (14.0 Direct = Destination).
/// Reached stops the unit. Only Flee then goes Idle. Overlay does not call IUnitMoveCommand.
/// </summary>
public abstract class UnitAIPointNavigationHandler : IUnitAIStateHandler
{
	#region Private Fields
	private readonly TacticalNavigationExecutor m_Nav = new TacticalNavigationExecutor();
	private bool m_ArrivalHandled;
	#endregion

	public abstract UnitAIState State { get; }

	protected virtual bool CompleteToIdleOnReached => false;

	public void Enter(UnitAIController _controller, in UnitAIStateContext _context)
	{
		_controller.ClearTacticalNavigationDebug();
		m_ArrivalHandled = false;
		m_Nav.Begin();
		Step(_controller);
	}

	public void Tick(UnitAIController _controller, float _dt)
	{
		Step(_controller);
		if (m_Nav.Reached && CompleteToIdleOnReached)
			_controller.TryCompleteFleeBecauseReached();
	}

	public void Exit(UnitAIController _controller)
	{
		m_Nav.Cancel(_controller);
		_controller.ClearTacticalNavigationDebug();
	}

	#region Private Methods
	private void Step(UnitAIController _controller)
	{
		bool hasDestination = TacticalNavigationMath.TryGetPointDestination(
			State,
			_controller.CurrentContext,
			out Vector3 destination);
		Vector3 hop = _controller.ResolvePointMovementHop(hasDestination, destination);
		m_Nav.Tick(
			_controller,
			hasDestination,
			hop,
			TacticalArrivalMath.WalkArrivalRadius(
				State,
				_controller.TacticalMovement.CurrentHopRequiresCoverAcquire),
			TacticalNavigationMath.ReasonFor(State));
		if (m_Nav.Issued)
			_controller.NotifyTacticalNavigationIssued();
		if (m_Nav.Reached)
		{
			if (!m_ArrivalHandled)
			{
				TacticalArrivalDecision arrival = _controller.NotifyTacticalArrival();
				bool keepApproaching =
					TacticalArrivalMath.IsTransientAcquireMiss(arrival.Reason) &&
					(_controller.TacticalMovement.CurrentHopRequiresCoverAcquire ||
					 State == UnitAIState.Attack ||
					 State == UnitAIState.Defense);
				if (arrival.Result == TacticalArrivalResult.Traversed || keepApproaching)
					m_Nav.ContinueToNextHop();
				else
					m_ArrivalHandled = true;
			}
		}
	}
	#endregion
}
