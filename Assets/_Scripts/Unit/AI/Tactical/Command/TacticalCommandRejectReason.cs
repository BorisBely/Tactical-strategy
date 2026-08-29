/// <summary>
/// Why <see cref="UnitAIController.IssueCommand"/> rejected a request.
/// </summary>
public enum TacticalCommandRejectReason
{
	None = 0,
	InvalidStateTransition = 1,
	InvalidCommandData = 2,
	MissingDestination = 3,
	LowerPriority = 4,
	UnitUnavailable = 5
}
