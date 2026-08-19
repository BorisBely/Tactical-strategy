/// <summary>
/// Per-state enter / tick / exit. Navigation / Combat stay later actions, not new states.
/// </summary>
public interface IUnitAIStateHandler
{
	UnitAIState State { get; }

	void Enter(UnitAIController _controller, in UnitAIStateContext _context);

	void Tick(UnitAIController _controller, float _dt);

	void Exit(UnitAIController _controller);
}
