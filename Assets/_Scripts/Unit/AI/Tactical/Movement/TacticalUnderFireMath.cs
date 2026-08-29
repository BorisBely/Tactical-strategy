using UnityEngine;

/// <summary>
/// #14.6 under-fire matrix. ImmediateThreat ≠ always EmergencyCover. Not Flee.
/// Weights are prototype, not freeze.
/// </summary>
public static class TacticalUnderFireMath
{
	#region Constants
	public const float NearbyCoverMeters = 2.5f;
	public const float ShortHopMeters = 5f;
	public const float LongHopMeters = 12f;
	public const float HighExposure = 0.55f;
	public const float SaferDelta = 0.25f;
	public const float FireAlignmentDot = 0.35f;
	public const float NearbySearchMeters = 8f;
	#endregion

	#region Public Methods
	public static TacticalUnderFireDecision Decide(in TacticalUnderFireSituation _situation)
	{
		var decision = new TacticalUnderFireDecision
		{
			RemainingHopMeters = _situation.RemainingHopMeters,
			CurrentExposure01 = _situation.CurrentExposure01,
			CoverAheadMeters = _situation.CoverAheadMeters,
			AlternativeExposure01 = _situation.AlternativeExposure01
		};

		if (!_situation.ImmediateThreat)
		{
			decision.Reason = TacticalUnderFireReason.NoThreat;
			return decision;
		}

		if (_situation.MissionOverride)
		{
			decision.Reason = TacticalUnderFireReason.CommandOverride;
			return decision;
		}

		if (_situation.CurrentPositionProtected)
		{
			decision.Action = TacticalUnderFireAction.Hold;
			decision.Reason = TacticalUnderFireReason.AlreadyProtected;
			return decision;
		}

		if (_situation.RouteBlocked)
			return Blocked(in _situation, in decision);

		bool coverSoon = _situation.CoverAheadProtected &&
		                 _situation.CoverAheadMeters >= 0f &&
		                 _situation.CoverAheadMeters <= NearbyCoverMeters;
		bool shortDash = _situation.CoverAheadProtected &&
		                 _situation.RemainingHopMeters <= ShortHopMeters;
		if (coverSoon || shortDash)
		{
			decision.Action = TacticalUnderFireAction.Continue;
			decision.Reason = coverSoon
				? TacticalUnderFireReason.CoverAhead
				: TacticalUnderFireReason.ShortDash;
			return decision;
		}

		bool highExposure = _situation.CurrentExposure01 >= HighExposure;
		bool longOpen = _situation.RemainingHopMeters >= LongHopMeters && highExposure;
		bool altSafer = _situation.HasSaferAlternative &&
		                (_situation.CurrentExposure01 - _situation.AlternativeExposure01) >= SaferDelta;

		if (altSafer && (longOpen || highExposure))
			return Replan(in decision, TacticalUnderFireReason.AlternativeSafer);

		if ((longOpen || (highExposure && !_situation.CoverAheadProtected)) &&
		    _situation.HasNearbyEmergencyCover)
		{
			decision.Action = TacticalUnderFireAction.EmergencyCover;
			decision.Reason = longOpen
				? TacticalUnderFireReason.RouteTooExposed
				: TacticalUnderFireReason.NearbyCoverSafer;
			decision.NeedsEmergencyCover = true;
			return decision;
		}

		if (MovingIntoFire(in _situation) &&
		    _situation.RemainingHopMeters > NearbyCoverMeters)
		{
			if (_situation.HasSaferAlternative)
				return Replan(in decision, TacticalUnderFireReason.AlternativeSafer);
			if (_situation.HasNearbyEmergencyCover)
			{
				decision.Action = TacticalUnderFireAction.EmergencyCover;
				decision.Reason = TacticalUnderFireReason.NearbyCoverSafer;
				decision.NeedsEmergencyCover = true;
				return decision;
			}
		}

		if (_situation.HasSaferAlternative)
			return Replan(in decision, TacticalUnderFireReason.AlternativeSafer);

		decision.Action = TacticalUnderFireAction.Continue;
		decision.Reason = TacticalUnderFireReason.NoAlternativeFallback;
		return decision;
	}

	public static bool ShouldSuppressReplan(in TacticalUnderFireDecision _decision, bool _explicitSituation)
	{
		if (_decision.Action == TacticalUnderFireAction.Hold)
			return true;
		if (_decision.Action == TacticalUnderFireAction.EmergencyCover)
			return true;
		if (_decision.Action != TacticalUnderFireAction.Continue)
			return false;
		if (_explicitSituation)
			return true;
		return _decision.Reason == TacticalUnderFireReason.CoverAhead ||
		       _decision.Reason == TacticalUnderFireReason.ShortDash;
	}

	public static bool MovingIntoFire(in TacticalUnderFireSituation _situation)
	{
		return FireAlignment(in _situation) >= FireAlignmentDot;
	}

	public static float FireAlignment(in TacticalUnderFireSituation _situation)
	{
		Vector3 threat = _situation.ThreatDirection;
		Vector3 move = _situation.MoveDirection;
		threat.y = 0f;
		move.y = 0f;
		if (threat.sqrMagnitude < 0.0001f || move.sqrMagnitude < 0.0001f)
			return 0f;
		return Vector3.Dot(threat.normalized, move.normalized);
	}

	public static TacticalUnderFireSituation FromCommitted(
		in TacticalRouteSituation _situation,
		in TacticalCommittedRoute _committed,
		TacticalRoute _route)
	{
		float hopMeters = 0f;
		Vector3 hop = _situation.Destination;
		TacticalRouteWaypoint waypoint = default;
		if (_route != null && _route.HasDestination)
		{
			hop = _route.CurrentHop;
			waypoint = _route.CurrentWaypoint;
			hopMeters = Mathf.Sqrt(
				CoverSpatialMath.PlanarDistanceSqr(_situation.Origin, hop));
		}

		bool hopCover = waypoint.CoverCandidateId != 0 ||
		                (_route != null &&
		                 _route.IsOnFinalHop &&
		                 _situation.FinalCoverCandidateId != 0);
		bool nearby = false;
		bool hasCovers = _situation.CoverCandidates != null &&
		                 _situation.CoverCandidates.Count > 0;
		if (hasCovers)
		{
			float nearbySqr = NearbySearchMeters * NearbySearchMeters;
			for (int i = 0; i < _situation.CoverCandidates.Count; i++)
			{
				CoverCandidate cover = _situation.CoverCandidates[i];
				if (cover == null || cover.CandidateId == waypoint.CoverCandidateId)
					continue;
				if (CoverSpatialMath.PlanarDistanceSqr(_situation.Origin, cover.Position) <= nearbySqr)
				{
					nearby = true;
					break;
				}
			}
		}

		return new TacticalUnderFireSituation
		{
			Present = false,
			ImmediateThreat = true,
			Moving = true,
			RemainingHopMeters = hopMeters,
			RemainingRouteMeters = Mathf.Sqrt(
				CoverSpatialMath.PlanarDistanceSqr(_situation.Origin, _situation.Destination)),
			CurrentExposure01 = _committed.Exposure01,
			CoverAheadMeters = hopMeters,
			CoverAheadProtected = hopCover,
			HasNearbyEmergencyCover = nearby,
			HasCoverCandidates = hasCovers,
			ThreatDirection = _situation.HostileDirection,
			MoveDirection = hop - _situation.Origin
		};
	}
	#endregion

	#region Private Methods
	private static TacticalUnderFireDecision Blocked(
		in TacticalUnderFireSituation _situation,
		in TacticalUnderFireDecision _seed)
	{
		TacticalUnderFireDecision decision = _seed;
		if (_situation.HasSaferAlternative)
			return Replan(in decision, TacticalUnderFireReason.RouteBlocked);
		if (_situation.HasNearbyEmergencyCover)
		{
			decision.Action = TacticalUnderFireAction.EmergencyCover;
			decision.Reason = TacticalUnderFireReason.RouteBlocked;
			decision.NeedsEmergencyCover = true;
			return decision;
		}

		decision.Action = TacticalUnderFireAction.Continue;
		decision.Reason = TacticalUnderFireReason.NoAlternativeFallback;
		return decision;
	}

	private static TacticalUnderFireDecision Replan(
		in TacticalUnderFireDecision _seed,
		TacticalUnderFireReason _reason)
	{
		TacticalUnderFireDecision decision = _seed;
		decision.Action = TacticalUnderFireAction.Replan;
		decision.Reason = _reason;
		return decision;
	}
	#endregion
}
