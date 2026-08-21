/// <summary>
/// Combat reads this, not the tactical FSM. Tactical AI publishes Hold / Engage only.
/// Stage 2 FROZEN.
/// </summary>
public interface ICombatIntentSource
{
	CombatIntent CurrentCombatIntent { get; }
}
