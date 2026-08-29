using UnityEngine;

/// <summary>
/// Shared geometric cover class. Not an individual score. #13.
/// </summary>
public enum CoverType
{
	None = 0,
	Crouch = 1,
	Standing = 2,
	Partial = 3,
	Corner = 4
}

/// <summary>
/// Occupancy states for the #13.6 runtime board. Not stored as shared geometry truth.
/// Rank / squad / formation are not occupancy.
/// </summary>
public enum CoverOccupancy
{
	Available = 0,
	Reserved = 1,
	Occupied = 2
}

/// <summary>
/// Diagnostic slot lifecycle. Not a score. Not CoverOccupancy.
/// Board stores Available / Reserved / Occupied. Approaching and Acquired are observed, not occupancy.
/// Acquired ≠ Occupied.
/// </summary>
public enum CoverSlotPhase
{
	None = 0,
	Reserved = 1,
	Approaching = 2,
	Acquired = 3,
	Occupied = 4,
	Released = 5
}
