/// <summary>
/// AI-1 FROZEN. In-state action. Not a <see cref="UnitAIState"/>.
/// Engage does not create DefenseEnemyDetected / AttackEngage — the task stays Defense or Attack.
/// </summary>
public enum UnitAIAction
{
	None = 0,
	Hold = 1,
	Engage = 2
}
