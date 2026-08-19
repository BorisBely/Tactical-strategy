/// <summary>
/// Explicit order-driven transitions. Search may also be requested by UnitAIController from LastKnown.
/// </summary>
public static class UnitAITransitionTable
{
	public static bool CanTransition(UnitAIState _from, UnitAIState _to)
	{
		if (_from == _to)
			return true;

		switch (_from)
		{
			case UnitAIState.Idle:
				return _to == UnitAIState.Defense ||
				       _to == UnitAIState.Attack ||
				       _to == UnitAIState.Search ||
				       _to == UnitAIState.Flee;
			case UnitAIState.Defense:
				return _to == UnitAIState.Attack ||
				       _to == UnitAIState.Retreat ||
				       _to == UnitAIState.Idle ||
				       _to == UnitAIState.Search ||
				       _to == UnitAIState.Flee;
			case UnitAIState.Attack:
				return _to == UnitAIState.Defense ||
				       _to == UnitAIState.Retreat ||
				       _to == UnitAIState.Idle ||
				       _to == UnitAIState.Search ||
				       _to == UnitAIState.Flee;
			case UnitAIState.Search:
				return _to == UnitAIState.Attack ||
				       _to == UnitAIState.Defense ||
				       _to == UnitAIState.Idle ||
				       _to == UnitAIState.Flee;
			case UnitAIState.Retreat:
				return _to == UnitAIState.Defense ||
				       _to == UnitAIState.Idle ||
				       _to == UnitAIState.Flee;
			case UnitAIState.Flee:
				return _to == UnitAIState.Idle;
			default:
				return false;
		}
	}
}
