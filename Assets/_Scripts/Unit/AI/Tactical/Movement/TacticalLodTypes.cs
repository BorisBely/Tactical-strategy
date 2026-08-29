/// <summary>
/// #14.9 tactical update depth. Changes when work runs, not which route wins.
/// </summary>
public enum TacticalLodTier
{
	None = 0,
	Full = 1,
	Reduced = 2,
	Background = 3
}

/// <summary>
/// Scheduler priority. Emergency is never starved by background work.
/// </summary>
public enum TacticalCriticality
{
	None = 0,
	Low = 1,
	Medium = 2,
	High = 3,
	Emergency = 4
}

/// <summary>
/// Expensive tactical operation the scheduler may admit or defer.
/// </summary>
public enum TacticalLodOperation
{
	None = 0,
	MovementExecution = 1,
	RouteValidity = 2,
	RouteEvaluation = 3,
	Replanning = 4,
	Exposure = 5,
	CoverEvaluation = 6,
	MovingLean = 7,
	ArrivalValidation = 8
}

/// <summary>
/// Why a tier was chosen or an evaluation was skipped. Thresholds are prototype, not freeze.
/// </summary>
public enum TacticalLodReason
{
	None = 0,
	IdleFar = 1,
	ActiveMovement = 2,
	Combat = 3,
	ImmediateThreat = 4,
	IncomingFire = 5,
	NewHostile = 6,
	Quiet = 7,
	CornerApproaching = 8,
	EventWake = 9,
	ComplexGeometry = 10,
	TickDue = 11,
	BudgetDenied = 12,
	CacheHit = 13,
	FirstEvaluation = 14,
	NearIdle = 15
}

/// <summary>
/// Snapshot used to pick a tier. Overlay does not Move.
/// </summary>
public struct TacticalLodSituation
{
	public float Now;
	public TacticalLodTier PreviousTier;
	public bool Idle;
	public bool HasActiveTacticalMovement;
	public bool UnderFire;
	public bool InCombat;
	public bool SeesHostile;
	public bool HasImmediateThreat;
	public bool IncomingFire;
	public bool HasPendingSignificantEvent;
	public bool InComplexGeometry;
	public bool ApproachingCorner;
	public bool Arriving;
	public bool CurrentlyLeaning;
	public bool HasPlayerDistance;
	public float DistanceToPlayerMeters;
	public float SecondsSinceSignificantEvent;
	public int GeometryVersion;
	public int KnowledgeVersion;
	public int RouteVersion;
}

/// <summary>
/// Per-operation gate flags. LOD does not rewrite 14.0–14.8 scoring.
/// </summary>
public struct TacticalLodGate
{
	public bool HasEvent;
	public bool FirstEvaluation;
	public bool TickDue;
	public bool ApproachingCorner;
	public bool CurrentlyLeaning;
	public bool Mandatory;
}

/// <summary>
/// Route / exposure reuse stamp. Shared spatial cache stays #13.
/// </summary>
public struct TacticalLodCacheStamp
{
	public bool Present;
	public int RouteVersion;
	public int GeometryVersion;
	public int KnowledgeVersion;
	public float Score;
	public int CandidateId;
}

/// <summary>
/// One LOD classification. Not a <see cref="UnitAIState"/>.
/// </summary>
public struct TacticalLodDecision
{
	public TacticalLodTier Tier;
	public TacticalLodTier PreviousTier;
	public TacticalCriticality Criticality;
	public TacticalLodReason Reason;
	public bool FromCache;
	public float Now;
}

/// <summary>
/// One scheduler admission. Scheduler does not pick routes.
/// </summary>
public struct TacticalSchedulerAdmission
{
	public int UnitId;
	public TacticalLodOperation Operation;
	public TacticalCriticality Criticality;
	public int Tick;
}

/// <summary>
/// Queued request before <see cref="TacticalUpdateScheduler.Dispatch"/>.
/// </summary>
public struct TacticalSchedulerRequest
{
	public int UnitId;
	public TacticalLodOperation Operation;
	public TacticalCriticality Criticality;
}
