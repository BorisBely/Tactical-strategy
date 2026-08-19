/// <summary>
/// AI-1 tactical task. Not Observe/Track/Engage/Patrol — those are actions inside a state.
/// Only <see cref="UnitAIController"/> changes this.
/// </summary>
public enum UnitAIState
{
	Idle = 0,
	Defense = 1,
	Attack = 2,
	Search = 3,
	Retreat = 4,
	Flee = 5
}
