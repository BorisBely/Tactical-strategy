using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Why a route was rejected before scoring.
/// </summary>
public enum TacticalRouteRejectReason
{
	None = 0,
	InvalidDestination = 1,
	Unreachable = 2,
	Blocked = 3,
	Duplicate = 4,
	Capped = 5
}

/// <summary>
/// Why a viable route was selected.
/// </summary>
public enum TacticalRouteSelectReason
{
	None = 0,
	HighestScore = 1,
	ShorterDistance = 2,
	CandidateOrder = 3,
	OnlyViable = 4,
	DirectBaseline = 5
}

/// <summary>
/// #14.1 query. Not a cover situation. Formation is unused.
/// </summary>
public struct TacticalRouteSituation
{
	public Vector3 Origin;
	public Vector3 Destination;
	public bool HasDestination;
	public TacticalMovementMode Mode;
	public Vector3 Objective;
	public bool HasObjective;
	public Vector3 HostileDirection;
	public bool HasKnownThreat;
	public IReadOnlyList<Vector3> CoverHints;
	public float WalkSpeedMetersPerSecond;
	public IReadOnlyList<CoverCandidate> CoverCandidates;
	public SharedCoverSpatialCache CoverCache;
	public CoverOccupancyBoard Occupancy;
	public int OccupancyUnitId;
	public float Now;
	public int FinalCoverCandidateId;
	public IReadOnlyList<TacticalWallAnchor> WallAnchors;
	public IReadOnlyList<Vector3> HostilePositions;
	public int GeometryVersion;
	public int KnowledgeVersion;
	public TacticalUnderFireSituation UnderFire;
}

/// <summary>
/// One way to reach Destination. Metrics may be authored (tests) or computed.
/// </summary>
public sealed class TacticalRouteCandidate
{
	public int CandidateId;
	public TacticalRouteKind Kind;
	public Vector3 Origin;
	public Vector3 Destination;
	public readonly List<TacticalRouteWaypoint> Intermediates = new List<TacticalRouteWaypoint>(4);
	public float DistanceMeters;
	public float TravelTimeSeconds;
	public float Exposure01;
	public float Cover01;
	public float Danger01;
	public float MissionProgress01;
	public float WallProximity01;
	public float OpenExposure01;
	public float PeakExposure01;
	public float ExposureCost;
	public float TimeAboveThresholdSeconds;
	public float TimeExposedSeconds;
	public readonly List<TacticalExposureSample> ExposureSamples = new List<TacticalExposureSample>(8);
	public bool UseAuthoredMetrics;
	public bool UseAuthoredExposureProfile;
	public bool Viable;
	public TacticalRouteRejectReason RejectReason;

	public Vector3 CurrentHop =>
		Intermediates.Count > 0 ? Intermediates[0].Position : Destination;

	public void SetDirect(int _id, Vector3 _origin, Vector3 _destination)
	{
		CandidateId = _id;
		Kind = TacticalRouteKind.Direct;
		Origin = _origin;
		Destination = _destination;
		Intermediates.Clear();
		ExposureSamples.Clear();
		UseAuthoredMetrics = false;
		UseAuthoredExposureProfile = false;
		PeakExposure01 = 0f;
		ExposureCost = 0f;
		TimeAboveThresholdSeconds = 0f;
		TimeExposedSeconds = 0f;
		Viable = false;
		RejectReason = TacticalRouteRejectReason.None;
	}

	public void SetWaypoint(int _id, Vector3 _origin, Vector3 _destination, Vector3 _hop)
	{
		SetCoverHops(_id, _origin, _destination, new[] { TacticalRouteWaypoint.At(_hop) });
	}

	public void SetCoverHops(
		int _id,
		Vector3 _origin,
		Vector3 _destination,
		IReadOnlyList<TacticalRouteWaypoint> _hops)
	{
		CandidateId = _id;
		Origin = _origin;
		Destination = _destination;
		Intermediates.Clear();
		if (_hops != null)
		{
			for (int i = 0; i < _hops.Count; i++)
				Intermediates.Add(_hops[i]);
		}

		Kind = Intermediates.Count > 0 ? TacticalRouteKind.Waypoint : TacticalRouteKind.Direct;
		UseAuthoredMetrics = false;
		UseAuthoredExposureProfile = false;
		ExposureSamples.Clear();
		PeakExposure01 = 0f;
		ExposureCost = 0f;
		TimeAboveThresholdSeconds = 0f;
		TimeExposedSeconds = 0f;
		Viable = false;
		RejectReason = TacticalRouteRejectReason.None;
	}
}

/// <summary>
/// Explainable route score. Weights are prototype, not freeze.
/// Total = Mission + Cover + WallBias − Distance − TravelTime − Exposure − Danger − OpenExposure
///         − PeakHold − TimeAbove − TimeExposed.
/// 14.4 extras are Tactical/Emergency only. 14.1 average Exposure is unchanged.
/// </summary>
public struct TacticalRouteScoreFactors
{
	public float MissionProgress;
	public float Cover;
	public float Distance;
	public float TravelTime;
	public float Exposure;
	public float Danger;
	public float WallBias;
	public float OpenExposure;
	public float PeakHold;
	public float TimeAbove;
	public float TimeExposed;
	public float Total;

	public float RebuiltTotal =>
		MissionProgress + Cover + WallBias
		- Distance - TravelTime - Exposure - Danger - OpenExposure
		- PeakHold - TimeAbove - TimeExposed;
}

/// <summary>
/// One candidate after viability + score. Overlay does not Move.
/// </summary>
public struct TacticalRouteEvaluation
{
	public TacticalRouteCandidate Candidate;
	public TacticalRouteScoreFactors Factors;
	public bool Viable;
	public TacticalRouteRejectReason RejectReason;
	public float Score;
}

/// <summary>
/// Evaluator outcome. Selected route is how; Destination stays the goal.
/// </summary>
public struct TacticalRouteDecision
{
	public bool HasSelection;
	public TacticalRouteEvaluation Selected;
	public TacticalRouteSelectReason Reason;
	public int CandidateCount;
	public int ViableCount;
	public bool FromCache;
	public IReadOnlyList<TacticalRouteEvaluation> Evaluations;
}

/// <summary>
/// Optional reachability. Null probe = finite destination is valid and reachable.
/// </summary>
public interface ITacticalRoutePathProbe
{
	bool IsDestinationValid(Vector3 _destination);
	bool IsReachable(
		Vector3 _origin,
		Vector3 _destination,
		IReadOnlyList<TacticalRouteWaypoint> _intermediates);
}
