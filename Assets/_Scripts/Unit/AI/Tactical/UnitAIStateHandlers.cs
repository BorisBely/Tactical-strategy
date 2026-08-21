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
	#endregion

	public UnitAIState State => UnitAIState.Search;

	public void Enter(UnitAIController _controller, in UnitAIStateContext _context)
	{
		_controller.ClearSearchNavigationDebug();
		m_Nav.Begin();
		Step(_controller);
	}

	public void Tick(UnitAIController _controller, float _dt)
	{
		if (m_Nav.IssueFailed)
		{
			_controller.TryResumeSearchBecauseNavigationFailed();
			return;
		}

		Step(_controller);
	}

	public void Exit(UnitAIController _controller)
	{
		m_Nav.Cancel(_controller);
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
			context.AreaRadius,
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
/// Attack / Defense / Retreat / Flee: one Walk to <see cref="UnitAIStateContext.Destination"/>.
/// Reached stops the unit. Only Flee then goes Idle.
/// </summary>
public abstract class UnitAIPointNavigationHandler : IUnitAIStateHandler
{
	#region Private Fields
	private readonly TacticalNavigationExecutor m_Nav = new TacticalNavigationExecutor();
	#endregion

	public abstract UnitAIState State { get; }

	protected virtual bool CompleteToIdleOnReached => false;

	public void Enter(UnitAIController _controller, in UnitAIStateContext _context)
	{
		_controller.ClearTacticalNavigationDebug();
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
		m_Nav.Tick(
			_controller,
			hasDestination,
			destination,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			TacticalNavigationMath.ReasonFor(State));
		if (m_Nav.Issued)
			_controller.NotifyTacticalNavigationIssued();
		if (m_Nav.Reached)
			_controller.NotifyTacticalDestinationReached();
	}
	#endregion
}
