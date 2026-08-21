/// <summary>
/// Input latch while waiting for a world point. Not <see cref="UnitAIState"/>.
/// </summary>
public enum GameCommandInputMode
{
	Normal = 0,
	AttackPending = 1,
	DefensePending = 2,
	RetreatPending = 3,
	SearchPending = 4,
	FleePending = 5
}
