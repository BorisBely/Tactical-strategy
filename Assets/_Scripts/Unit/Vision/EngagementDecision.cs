/// <summary>
/// What to do with the selected contact (G6). Not who to select, not a shot.
/// None ≠ Ignore. Observe / Suppress / Report are reserved; DefaultCombatPolicy never returns them.
/// </summary>
public enum EngagementDecision
{
	None = 0,
	Ignore = 1,
	Observe = 2,
	Track = 3,
	Aim = 4,
	Fire = 5,
	Suppress = 6,
	Report = 7
}
