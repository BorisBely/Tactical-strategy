/// <summary>
/// Why the current Search ended. Search does not invent Under Fire or command-priority (#11).
/// </summary>
public enum UnitAISearchCompletionReason
{
	None = 0,
	Found = 1,
	Exhausted = 2,
	Expired = 3,
	Cancelled = 4,
	NewOrder = 5,
	Threat = 6
}
