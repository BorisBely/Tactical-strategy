/// <summary>
/// Explicit order into the AI state machine. Not Perception, not Combat, not Navigation.
/// </summary>
public readonly struct UnitAICommand
{
	public readonly UnitAIState State;
	public readonly UnitAIStateContext Context;

	public UnitAICommand(UnitAIState _state, UnitAIStateContext _context)
	{
		State = _state;
		Context = _context;
	}

	public static UnitAICommand Idle()
	{
		return new UnitAICommand(UnitAIState.Idle, UnitAIStateContext.Empty);
	}

	public static UnitAICommand Defense(UnitAIStateContext _context)
	{
		return new UnitAICommand(UnitAIState.Defense, _context);
	}

	public static UnitAICommand Attack(UnitAIStateContext _context)
	{
		return new UnitAICommand(UnitAIState.Attack, _context);
	}

	public static UnitAICommand Search(UnitAIStateContext _context)
	{
		return new UnitAICommand(UnitAIState.Search, _context);
	}

	public static UnitAICommand Retreat(UnitAIStateContext _context)
	{
		return new UnitAICommand(UnitAIState.Retreat, _context);
	}

	public static UnitAICommand Flee(UnitAIStateContext _context)
	{
		return new UnitAICommand(UnitAIState.Flee, _context);
	}
}
