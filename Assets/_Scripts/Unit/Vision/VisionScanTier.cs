/// <summary>
/// G8 compute-budget scan tier. Not DetectionProgress and not a confidence penalty.
/// </summary>
public enum VisionScanTier
{
	Idle = 0,
	Cheap = 1,
	RangeFov = 2,
	Detail = 3
}
