using UnityEngine;

/// <summary>
/// Why a CoverCandidate was not used as an intermediate hop. Not a #13 score reject.
/// </summary>
public enum TacticalCoverHopRejectReason
{
	None = 0,
	NoProgress = 1,
	TooFarFromRoute = 2,
	TooMuchExposure = 3,
	Occupied = 4,
	Unreachable = 5,
	NoExposureReduction = 6,
	Behind = 7,
	Capped = 8,
	IsFinalDestination = 9
}

/// <summary>
/// Why the planner kept Direct or built a cover chain. Prototype, not freeze.
/// </summary>
public enum TacticalCoverPlanReason
{
	None = 0,
	DirectAcceptable = 1,
	DirectBaseline = 2,
	CoverChain = 3,
	NoUsefulCover = 4
}

/// <summary>
/// One filtered-out cover for debug overlay.
/// </summary>
public struct TacticalCoverFilterRejection
{
	public int CandidateId;
	public Vector3 Position;
	public TacticalCoverHopRejectReason Reason;
}

/// <summary>
/// One walk segment. Executor still Walks the hop end.
/// </summary>
public struct TacticalCoverHop
{
	public Vector3 Start;
	public Vector3 End;
	public float DistanceMeters;
	public float Exposure01;
	public int CoverCandidateId;
	public CoverRegionId CoverRegion;
	public bool IsFinal;
}
