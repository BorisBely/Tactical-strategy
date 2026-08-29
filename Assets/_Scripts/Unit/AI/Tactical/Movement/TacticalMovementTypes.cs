using UnityEngine;

/// <summary>
/// #14 movement mode. Policy (when to use which) is not 14.0.
/// </summary>
public enum TacticalMovementMode
{
	Normal = 0,
	Tactical = 1,
	Emergency = 2
}

/// <summary>
/// How the route reaches Destination. Direct is the baseline candidate.
/// </summary>
public enum TacticalRouteKind
{
	None = 0,
	Direct = 1,
	Waypoint = 2
}

/// <summary>
/// Meaning of an intermediate hop. Not a cover score.
/// </summary>
public enum TacticalWaypointKind
{
	None = 0,
	Destination = 1,
	IntermediateCover = 2,
	Corridor = 3
}

/// <summary>
/// One hop. Destination of the route is stored on <see cref="TacticalRoute"/>, not only here.
/// </summary>
public struct TacticalRouteWaypoint
{
	public Vector3 Position;
	public TacticalWaypointKind Kind;
	public int CoverCandidateId;

	public CoverRegionId CoverRegion;

	public static TacticalRouteWaypoint At(
		Vector3 _position,
		TacticalWaypointKind _kind = TacticalWaypointKind.IntermediateCover,
		int _coverCandidateId = 0)
	{
		return CoverHop(_position, _coverCandidateId, default, _kind);
	}

	public static TacticalRouteWaypoint CoverHop(
		Vector3 _position,
		int _coverCandidateId,
		CoverRegionId _region,
		TacticalWaypointKind _kind = TacticalWaypointKind.IntermediateCover)
	{
		return new TacticalRouteWaypoint
		{
			Position = _position,
			Kind = _kind,
			CoverCandidateId = _coverCandidateId,
			CoverRegion = _region
		};
	}
}

/// <summary>
/// Future group slot. #16. 14.0 always <see cref="Present"/> = false.
/// </summary>
public struct TacticalFormationContext
{
	public bool Present;

	public static TacticalFormationContext None => default;
}

/// <summary>
/// Per-query movement context. Formation is an extension point, not behaviour.
/// </summary>
public struct TacticalRouteContext
{
	public TacticalMovementMode Mode;
	public TacticalFormationContext Formation;

	public static TacticalRouteContext Single(TacticalMovementMode _mode)
	{
		return new TacticalRouteContext
		{
			Mode = _mode,
			Formation = TacticalFormationContext.None
		};
	}
}

/// <summary>
/// Destination is the tactical goal. Route is how to get there.
/// </summary>
public struct TacticalMovementGoal
{
	public Vector3 Origin;
	public Vector3 Destination;
	public bool HasDestination;
	public TacticalRouteContext Context;
	public float Now;
}

/// <summary>
/// Overlay outcome. Not Move. Not a new <see cref="UnitAIState"/>.
/// </summary>
public struct TacticalMovementDecision
{
	public bool HasRoute;
	public TacticalRouteKind Kind;
	public TacticalMovementMode Mode;
	public Vector3 Origin;
	public Vector3 Destination;
	public Vector3 CurrentHop;
	public int IntermediateCount;
	public bool FromCache;
	public TacticalRoute Route;
	public int SelectedCandidateId;
	public float SelectedScore;
	public TacticalRouteSelectReason SelectReason;
	public int CandidateCount;
	public int ViableCount;
	public int CurrentHopIndex;
	public bool NeedsReroute;
	public int ReservedCoverCandidateId;
	public TacticalRouteCommitStatus CommitStatus;
	public TacticalReplanAction ReplanAction;
	public TacticalReplanReason ReplanReason;
	public TacticalReplanEventKind LastEventKind;
	public TacticalUnderFireAction UnderFireAction;
	public TacticalUnderFireReason UnderFireReason;
	public bool NeedsEmergencyCover;
	public TacticalArrivalResult ArrivalResult;
	public TacticalArrivalFailureReason ArrivalReason;
	public float ArrivalDistanceMeters;
	public CurrentTacticalPosition CurrentTacticalPosition;
	public TacticalMovingLeanAction MovingLeanAction;
	public CoverPeekDirection MovingLeanDirection;
	public CoverLeanLevel MovingLeanDepth;
	public TacticalMovingLeanReason MovingLeanReason;
	public TacticalLodTier LodTier;
	public TacticalLodReason LodReason;
}
