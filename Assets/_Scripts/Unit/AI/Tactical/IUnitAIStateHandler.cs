/// <summary>
/// Per-state enter / tick / exit. Navigation uses <see cref="IUnitMoveCommand"/>; Combat stays on CombatIntent.
/// </summary>
public interface IUnitAIStateHandler
{
	UnitAIState State { get; }

	void Enter(UnitAIController _controller, in UnitAIStateContext _context);

	void Tick(UnitAIController _controller, float _dt);

	void Exit(UnitAIController _controller);
}
