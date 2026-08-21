/// <summary>
/// Why infantry issued a move. Does not change Walk mechanics. Diagnostic only.
/// </summary>
public enum UnitNavigationReason
{
	None = 0,
	Search = 1,
	Attack = 2,
	Retreat = 3,
	Flee = 4,
	Defense = 5
}
