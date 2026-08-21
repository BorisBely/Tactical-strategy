/// <summary>
/// Who issued <see cref="TacticalCommand"/>. Stage 6.1 uses Test / Debug / Scenario.
/// <see cref="Game"/> is reserved for 6.2 — do not wire RTS here.
/// </summary>
public enum TacticalCommandSource
{
	Test = 0,
	Debug = 1,
	Scenario = 2,
	Game = 3
}
