using UnityEngine;

/// <summary>
/// #14.6 movement reaction. Not a <see cref="UnitAIState"/>. Not Flee.
/// Speed / lean / suppression / wounds are not this layer.
/// </summary>
public enum TacticalUnderFireAction
{
	None = 0,
	Continue = 1,
	Replan = 2,
	EmergencyCover = 3,
	Hold = 4
}

/// <summary>
/// Explainable under-fire reason. Thresholds are prototype, not freeze.
/// </summary>
public enum TacticalUnderFireReason
{
	None = 0,
	NoThreat = 1,
	CommandOverride = 2,
	AlreadyProtected = 3,
	CoverAhead = 4,
	ShortDash = 5,
	AlternativeSafer = 6,
	RouteTooExposed = 7,
	NearbyCoverSafer = 8,
	RouteBlocked = 9,
	NoAlternativeFallback = 10,
	Cooldown = 11
}

/// <summary>
/// Authored / inferred snapshot for one under-fire decision.
/// Overlay does not Move. #13 still owns WHERE if EmergencyCover.
/// </summary>
public struct TacticalUnderFireSituation
{
	public bool Present;
	public bool ImmediateThreat;
	public bool Moving;
	public float RemainingHopMeters;
	public float RemainingRouteMeters;
	public float CurrentExposure01;
	public float AlternativeExposure01;
	public bool HasSaferAlternative;
	public float CoverAheadMeters;
	public bool CoverAheadProtected;
	public bool CurrentPositionProtected;
	public bool HasNearbyEmergencyCover;
	public bool HasCoverCandidates;
	public bool RouteBlocked;
	public bool MissionOverride;
	public Vector3 ThreatDirection;
	public Vector3 MoveDirection;
	public int EmergencyCoverCandidateId;
	public Vector3 EmergencyDestination;
	public bool HasEmergencyDestination;
}

/// <summary>
/// One under-fire choice. Action is Continue / Replan / EmergencyCover / Hold.
/// </summary>
public struct TacticalUnderFireDecision
{
	public TacticalUnderFireAction Action;
	public TacticalUnderFireReason Reason;
	public float RemainingHopMeters;
	public float CurrentExposure01;
	public float CoverAheadMeters;
	public float AlternativeExposure01;
	public bool NeedsEmergencyCover;
}
