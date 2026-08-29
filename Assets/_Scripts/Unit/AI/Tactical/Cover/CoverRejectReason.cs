using UnityEngine;

/// <summary>
/// Why a sample was rejected: generation filter, or #13.4 below-threshold debug.
/// </summary>
public enum CoverRejectReason
{
	OutsideRegion = 0,
	OffNavMesh = 1,
	NoClearance = 2,
	Unanchored = 3,
	BelowThreshold = 4
}

/// <summary>
/// Debug-only rejected sample. Not stored in the shared cache.
/// </summary>
public struct CoverRejectedSample
{
	public Vector3 Position;
	public Vector3 Normal;
	public CoverRejectReason Reason;
}
