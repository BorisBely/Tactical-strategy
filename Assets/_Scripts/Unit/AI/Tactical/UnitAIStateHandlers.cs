/// <summary>
/// Empty tactical handlers. Engage is resolved on UnitAIController, not a new state.
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

public sealed class UnitAIDefenseHandler : IUnitAIStateHandler
{
	public UnitAIState State => UnitAIState.Defense;

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

public sealed class UnitAIAttackHandler : IUnitAIStateHandler
{
	public UnitAIState State => UnitAIState.Attack;

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

public sealed class UnitAISearchHandler : IUnitAIStateHandler
{
	public UnitAIState State => UnitAIState.Search;

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

public sealed class UnitAIRetreatHandler : IUnitAIStateHandler
{
	public UnitAIState State => UnitAIState.Retreat;

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

public sealed class UnitAIFleeHandler : IUnitAIStateHandler
{
	public UnitAIState State => UnitAIState.Flee;

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
