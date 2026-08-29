/// <summary>
/// Why <see cref="GameCommandService.Issue"/> rejected a request.
/// Transition / data reasons are forwarded from <see cref="UnitAIController.IssueCommand"/>.
/// </summary>
public enum GameCommandRejectReason
{
	None = 0,
	InvalidUnit = 1,
	NoAI = 2,
	InvalidStateTransition = 3,
	InvalidCommandData = 4,
	MissingDestination = 5,
	LowerPriority = 6
}
