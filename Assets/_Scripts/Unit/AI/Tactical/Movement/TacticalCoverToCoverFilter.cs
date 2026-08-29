using UnityEngine;

/// <summary>
/// #14.2 cheap filter before cover-to-cover combinations. Not a full planner.
/// </summary>
public static class TacticalCoverToCoverFilter
{
	#region Constants
	public const float MinProgress01 = 0.12f;
	public const float MaxLateralMeters = 10f;
	public const float NearDestination01 = 0.94f;
	#endregion

	#region Public Methods
	public static TacticalCoverHopRejectReason Classify(
		CoverCandidate _cover,
		in TacticalRouteSituation _situation,
		ITacticalRoutePathProbe _probe)
	{
		if (_cover == null || !TacticalRouteViability.IsFinitePoint(_cover.Position))
			return TacticalCoverHopRejectReason.Unreachable;
		if (_cover.CandidateId == _situation.FinalCoverCandidateId && _situation.FinalCoverCandidateId != 0)
			return TacticalCoverHopRejectReason.IsFinalDestination;
		if (!_cover.NavMeshValid)
			return TacticalCoverHopRejectReason.Unreachable;
		if (_situation.Occupancy != null &&
		    _situation.OccupancyUnitId != 0 &&
		    !_situation.Occupancy.IsUsable(_cover, _situation.OccupancyUnitId, _situation.Now))
			return TacticalCoverHopRejectReason.Occupied;
		if (!_cover.StandingValid && !_cover.CrouchValid)
			return TacticalCoverHopRejectReason.NoExposureReduction;
		if (_probe != null &&
		    !_probe.IsReachable(_situation.Origin, _cover.Position, null))
			return TacticalCoverHopRejectReason.Unreachable;

		Vector3 origin = _situation.Origin;
		Vector3 dest = _situation.Destination;
		Vector3 span = dest - origin;
		span.y = 0f;
		float pathMeters = Mathf.Sqrt(span.x * span.x + span.z * span.z);
		if (pathMeters < 0.05f)
			return TacticalCoverHopRejectReason.NoProgress;
		Vector3 forward = span / pathMeters;
		Vector3 toCover = _cover.Position - origin;
		toCover.y = 0f;
		float along = Vector3.Dot(toCover, forward);
		if (along < -0.5f)
			return TacticalCoverHopRejectReason.Behind;
		float progress = along / pathMeters;
		if (progress < MinProgress01)
			return TacticalCoverHopRejectReason.NoProgress;
		if (progress > NearDestination01)
			return TacticalCoverHopRejectReason.NoProgress;

		float lateral = Mathf.Sqrt(Mathf.Max(0f, toCover.sqrMagnitude - along * along));
		if (lateral > MaxLateralMeters)
			return TacticalCoverHopRejectReason.TooFarFromRoute;

		if (_situation.HasKnownThreat && lateral < 0.4f)
			return TacticalCoverHopRejectReason.NoExposureReduction;

		return TacticalCoverHopRejectReason.None;
	}

	public static float IntermediateValue(CoverCandidate _cover, in TacticalRouteSituation _situation)
	{
		if (_cover == null)
			return 0f;
		Vector3 origin = _situation.Origin;
		Vector3 dest = _situation.Destination;
		Vector3 span = dest - origin;
		span.y = 0f;
		float pathMeters = Mathf.Max(0.05f, Mathf.Sqrt(span.x * span.x + span.z * span.z));
		Vector3 forward = span / pathMeters;
		Vector3 toCover = _cover.Position - origin;
		toCover.y = 0f;
		float along = Vector3.Dot(toCover, forward);
		float progress = Mathf.Clamp01(along / pathMeters);
		float lateral = Mathf.Sqrt(Mathf.Max(0f, toCover.sqrMagnitude - along * along));
		float extra = Mathf.Max(0f, Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(origin, _cover.Position)) +
		                        Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(_cover.Position, dest)) -
		                        pathMeters);
		float coverQuality = _cover.StandingValid
			? Mathf.Clamp01(_cover.StandingProfile.Average)
			: Mathf.Clamp01(_cover.CrouchProfile.Average) * 0.7f;
		float exposureReduction = _situation.HasKnownThreat
			? Mathf.Clamp01(lateral / 8f)
			: 0.25f;
		return exposureReduction + coverQuality + progress - lateral / 12f - extra / 20f;
	}
	#endregion
}
