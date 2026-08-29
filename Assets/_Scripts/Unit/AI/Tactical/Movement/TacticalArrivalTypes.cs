using UnityEngine;

/// <summary>
/// #14.7 tactical acquisition outcome. Navigation Reached is not this.
/// </summary>
public enum TacticalArrivalResult
{
	None = 0,
	Acquired = 1,
	Traversed = 2,
	Invalid = 3,
	Occupied = 4,
	OutOfTolerance = 5,
	Reevaluate = 6,
	Rejected = 7
}

/// <summary>
/// Why acquisition failed. Prototype names; do not freeze thresholds.
/// </summary>
public enum TacticalArrivalFailureReason
{
	None = 0,
	InvalidPosition = 1,
	Occupied = 2,
	ReservationLost = 3,
	GeometryChanged = 4,
	OutOfTolerance = 5,
	NavigationStopped = 6,
	RouteStale = 7,
	CandidateMissing = 8,
	NotReservedByUnit = 9,
	PathInvalid = 10
}

/// <summary>
/// One arrival check. Overlay does not Move. #14.5 owns replan.
/// Orientation is an extension point; 14.7 does not acquire facing.
/// </summary>
public struct TacticalArrivalSituation
{
	public bool NavigationReached;
	public Vector3 CurrentPosition;
	public Vector3 TargetPosition;
	public bool HasTargetPosition;
	public Vector3 MoveDestination;
	public bool HasMoveDestination;
	public Vector3 AgentPosition;
	public bool HasAgentPosition;
	public float NavRemainingDistance;
	public bool HasNavRemaining;
	public Vector3 Velocity;
	public bool HasVelocity;
	public CoverCandidate Candidate;
	public int CandidateId;
	public CoverRegionId CandidateRegion;
	public CoverType RequiredCoverType;
	public bool IntermediateHop;
	public bool RouteStale;
	public bool DestinationInvalid;
	public CoverOccupancyBoard Occupancy;
	public int UnitId;
	public int GeometryVersion;
	public float AcquireToleranceMeters;
	public float Now;
	public UnitAIState MissionState;
	public string PathStatus;
	public float StoppingDistance;
	public bool HasStoppingDistance;
	public float AgentRadius;
	public bool HasAgentRadius;
}

/// <summary>
/// Tactical arrival decision. Acquired ≠ NavMesh Reached.
/// </summary>
public struct TacticalArrivalDecision
{
	public TacticalArrivalResult Result;
	public TacticalArrivalFailureReason Reason;
	public int CandidateId;
	public float DistanceMeters;
	public int GeometryVersion;
	public int CurrentGeometryVersion;
	public CoverOccupancy OccupancyState;
	public int ReservationOwnerUnitId;
	public bool OrientationPending;
	public bool OrientationValid;
	public CurrentTacticalPosition Position;
	public UnitAIState MissionState;
	public Vector3 AcquirePosition;
	public Vector3 MoveDestination;

	public bool IsAcquired => Result == TacticalArrivalResult.Acquired;

	public bool IsTraversed => Result == TacticalArrivalResult.Traversed;

	public bool IsFinal => Result != TacticalArrivalResult.None && Result != TacticalArrivalResult.Traversed;

	public bool NeedsReevaluate =>
		Result == TacticalArrivalResult.Reevaluate ||
		Result == TacticalArrivalResult.Invalid ||
		Result == TacticalArrivalResult.Occupied;
}
