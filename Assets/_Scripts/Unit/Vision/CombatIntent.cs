/// <summary>
/// External combat mode from Tactical AI. Not <see cref="UnitAIState"/>, not Fire.
/// Missing source = Combat keeps working as before (no veto).
/// Stage 2 FROZEN. Do not retune G6 / identity / Q.
/// </summary>
public enum CombatIntent
{
	Hold = 0,
	Engage = 1
}
