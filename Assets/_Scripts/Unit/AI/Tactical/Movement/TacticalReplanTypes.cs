using UnityEngine;

/// <summary>
/// #14.5 why the world asked the route to look at itself. Not Attack.
/// </summary>
public enum TacticalReplanEventKind
{
	None = 0,
	NewHostile = 1,
	EnemyMoved = 2,
	ImmediateThreat = 3,
	GeometryChanged = 4,
	RouteBlocked = 5,
	DestinationInvalid = 6,
	MissionChanged = 7,
	CoverInvalid = 8,
	Sound = 9
}

/// <summary>
/// Movement/route status. Not a <see cref="UnitAIState"/>.
/// </summary>
public enum TacticalRouteCommitStatus
{
	None = 0,
	Committed = 1,
	Replanning = 2
}

/// <summary>
/// Reevaluate can keep the same route. Replacement is separate.
/// </summary>
public enum TacticalReplanAction
{
	None = 0,
	Keep = 1,
	Replace = 2
}

/// <summary>
/// Explainable gate / overlay outcome. Weights are prototype, not freeze.
/// </summary>
public enum TacticalReplanReason
{
	None = 0,
	NoEvent = 1,
	DeltaTooSmall = 2,
	Cooldown = 3,
	ExposureWorsened = 4,
	RouteInvalid = 5,
	GeometryOnRoute = 6,
	GeometryOffRoute = 7,
	ImmediateThreat = 8,
	MissionChanged = 9,
	SameRoute = 10,
	AdvantageTooSmall = 11,
	CoverInvalid = 12
}

/// <summary>
/// One world cue. Overlay coalesces a window into a single check.
/// </summary>
public struct TacticalReplanEvent
{
	public TacticalReplanEventKind Kind;
	public float Delta;
	public int GeometryVersion;
	public bool OnRoute;

	public static TacticalReplanEvent Of(TacticalReplanEventKind _kind, float _delta = 0f)
	{
		return new TacticalReplanEvent { Kind = _kind, Delta = _delta };
	}

	public static TacticalReplanEvent Geometry(bool _onRoute, int _version)
	{
		return new TacticalReplanEvent
		{
			Kind = TacticalReplanEventKind.GeometryChanged,
			OnRoute = _onRoute,
			GeometryVersion = _version,
			Delta = _onRoute ? 1f : 0f
		};
	}
}

/// <summary>
/// Snapshot of the committed plan. Used by the gate, not by NavMesh.
/// </summary>
public struct TacticalCommittedRoute
{
	public int CandidateId;
	public float Score;
	public float Exposure01;
	public int GeometryVersion;
	public Vector3 Origin;
	public Vector3 Destination;
	public TacticalRouteKind Kind;
	public TacticalMovementMode Mode;
	public int IntermediateCount;
	public float Progress01;
	public bool Present;
}

/// <summary>
/// Gate outcome. ShouldReevaluate ≠ ShouldReplace.
/// </summary>
public struct TacticalReplanCheck
{
	public bool ShouldReevaluate;
	public bool ShouldReplace;
	public bool Mandatory;
	public bool EmergencyBypass;
	public bool FromCooldown;
	public TacticalReplanReason Reason;
	public TacticalReplanEventKind EventKind;
	public float Delta;
	public float ReplanningCost;
	public float NewAdvantage;
	public int CoalescedCount;
}
