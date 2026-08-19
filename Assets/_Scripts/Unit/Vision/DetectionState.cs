/// <summary>
/// Detection confidence lifecycle for a perceived contact (Stage G0+).
/// Not identity, threat, memory (RecentlyLost/Lost), or combat selection.
/// </summary>
public enum DetectionState
{
	Undetected = 0,
	Detecting = 1,
	Detected = 2
}
