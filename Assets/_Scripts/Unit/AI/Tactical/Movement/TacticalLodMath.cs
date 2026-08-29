using UnityEngine;

/// <summary>
/// #14.9 when to run closed 14.0–14.8 work. Does not change scores, wall bias, or exposure formula.
/// Intervals are prototype, not freeze.
/// </summary>
public static class TacticalLodMath
{
	#region Constants
	public const float DefaultFarMeters = 40f;
	public const float DefaultNearMeters = 12f;
	public const float QuietToReducedSeconds = 3f;
	public const float QuietToBackgroundSeconds = 8f;
	public const float FullIntervalSeconds = 0.05f;
	public const float ReducedIntervalSeconds = 0.25f;
	public const float BackgroundIntervalSeconds = 1.5f;
	public const float BackgroundRouteValiditySeconds = 2f;
	public const float ReducedExposureIntervalSeconds = 0.5f;
	#endregion

	#region Public Methods
	public static TacticalLodDecision Select(in TacticalLodSituation _situation)
	{
		TacticalLodReason reason;
		TacticalCriticality criticality;
		TacticalLodTier tier = SelectTier(in _situation, out reason, out criticality);
		return new TacticalLodDecision
		{
			Tier = tier,
			PreviousTier = _situation.PreviousTier,
			Criticality = criticality,
			Reason = reason,
			Now = _situation.Now
		};
	}

	public static bool Allows(
		TacticalLodTier _tier,
		TacticalLodOperation _operation,
		in TacticalLodGate _gate)
	{
		if (_operation == TacticalLodOperation.MovementExecution ||
		    _operation == TacticalLodOperation.ArrivalValidation)
			return true;
		if (_tier == TacticalLodTier.None || _tier == TacticalLodTier.Full)
			return true;
		if (_gate.Mandatory || _gate.FirstEvaluation)
			return true;

		switch (_operation)
		{
			case TacticalLodOperation.Replanning:
				return _gate.HasEvent;
			case TacticalLodOperation.RouteEvaluation:
				return _gate.HasEvent || (_tier == TacticalLodTier.Reduced && _gate.TickDue);
			case TacticalLodOperation.Exposure:
				if (_tier == TacticalLodTier.Background)
					return _gate.HasEvent;
				return _tier == TacticalLodTier.Reduced && _gate.TickDue;
			case TacticalLodOperation.CoverEvaluation:
				return _gate.HasEvent;
			case TacticalLodOperation.MovingLean:
				return _gate.HasEvent ||
				       _gate.ApproachingCorner ||
				       _gate.CurrentlyLeaning;
			case TacticalLodOperation.RouteValidity:
				return _tier == TacticalLodTier.Reduced || _gate.TickDue;
			default:
				return false;
		}
	}

	public static float IntervalSeconds(TacticalLodTier _tier)
	{
		switch (_tier)
		{
			case TacticalLodTier.Reduced:
				return ReducedIntervalSeconds;
			case TacticalLodTier.Background:
				return BackgroundIntervalSeconds;
			default:
				return FullIntervalSeconds;
		}
	}

	public static bool TickDue(float _now, float _lastTime, TacticalLodTier _tier)
	{
		if (_lastTime < 0f)
			return true;
		return _now - _lastTime >= IntervalSeconds(_tier);
	}

	public static bool RouteCacheValid(
		in TacticalLodCacheStamp _cached,
		int _routeVersion,
		int _geometryVersion,
		int _knowledgeVersion)
	{
		return _cached.Present &&
		       _cached.RouteVersion == _routeVersion &&
		       _cached.GeometryVersion == _geometryVersion &&
		       _cached.KnowledgeVersion == _knowledgeVersion;
	}

	public static bool ExposureCacheValid(
		in TacticalLodCacheStamp _cached,
		int _routeVersion,
		int _geometryVersion,
		int _knowledgeVersion)
	{
		return RouteCacheValid(in _cached, _routeVersion, _geometryVersion, _knowledgeVersion);
	}

	public static TacticalLodCacheStamp Stamp(
		int _routeVersion,
		int _geometryVersion,
		int _knowledgeVersion,
		float _score,
		int _candidateId)
	{
		return new TacticalLodCacheStamp
		{
			Present = true,
			RouteVersion = _routeVersion,
			GeometryVersion = _geometryVersion,
			KnowledgeVersion = _knowledgeVersion,
			Score = _score,
			CandidateId = _candidateId
		};
	}

	public static int ComparePriority(TacticalCriticality _left, TacticalCriticality _right)
	{
		return ((int)_right).CompareTo((int)_left);
	}

	public static bool IsFar(in TacticalLodSituation _situation)
	{
		return _situation.HasPlayerDistance &&
		       _situation.DistanceToPlayerMeters >= DefaultFarMeters;
	}

	public static bool IsNear(in TacticalLodSituation _situation)
	{
		return _situation.HasPlayerDistance &&
		       _situation.DistanceToPlayerMeters <= DefaultNearMeters;
	}
	#endregion

	#region Private Methods
	private static TacticalLodTier SelectTier(
		in TacticalLodSituation _situation,
		out TacticalLodReason _reason,
		out TacticalCriticality _criticality)
	{
		if (_situation.HasImmediateThreat || _situation.IncomingFire)
		{
			_reason = _situation.IncomingFire && !_situation.HasImmediateThreat
				? TacticalLodReason.IncomingFire
				: TacticalLodReason.ImmediateThreat;
			_criticality = TacticalCriticality.Emergency;
			return TacticalLodTier.Full;
		}

		if (_situation.UnderFire || _situation.InCombat)
		{
			_reason = TacticalLodReason.Combat;
			_criticality = TacticalCriticality.High;
			return TacticalLodTier.Full;
		}

		if (_situation.SeesHostile && _situation.HasPendingSignificantEvent)
		{
			_reason = TacticalLodReason.NewHostile;
			_criticality = TacticalCriticality.High;
			return TacticalLodTier.Full;
		}

		if (_situation.HasPendingSignificantEvent)
		{
			_reason = TacticalLodReason.EventWake;
			_criticality = TacticalCriticality.High;
			return TacticalLodTier.Full;
		}

		if (_situation.ApproachingCorner)
		{
			_reason = TacticalLodReason.CornerApproaching;
			_criticality = TacticalCriticality.Medium;
			return TacticalLodTier.Full;
		}

		if (_situation.InComplexGeometry && _situation.HasActiveTacticalMovement)
		{
			_reason = TacticalLodReason.ComplexGeometry;
			_criticality = TacticalCriticality.Medium;
			return TacticalLodTier.Full;
		}

		if (_situation.HasActiveTacticalMovement)
		{
			_reason = TacticalLodReason.ActiveMovement;
			_criticality = TacticalCriticality.Medium;
			return TacticalLodTier.Reduced;
		}

		if (_situation.Idle && IsFar(in _situation))
		{
			_reason = TacticalLodReason.IdleFar;
			_criticality = TacticalCriticality.Low;
			return TacticalLodTier.Background;
		}

		if (_situation.Idle && IsNear(in _situation))
		{
			_reason = TacticalLodReason.NearIdle;
			_criticality = TacticalCriticality.Low;
			return TacticalLodTier.Reduced;
		}

		if (_situation.PreviousTier == TacticalLodTier.Full &&
		    _situation.SecondsSinceSignificantEvent >= QuietToReducedSeconds)
		{
			_reason = TacticalLodReason.Quiet;
			_criticality = TacticalCriticality.Medium;
			return TacticalLodTier.Reduced;
		}

		if ((_situation.PreviousTier == TacticalLodTier.Reduced ||
		     _situation.PreviousTier == TacticalLodTier.Full) &&
		    _situation.SecondsSinceSignificantEvent >= QuietToBackgroundSeconds &&
		    !_situation.HasActiveTacticalMovement)
		{
			_reason = TacticalLodReason.Quiet;
			_criticality = TacticalCriticality.Low;
			return TacticalLodTier.Background;
		}

		if (_situation.PreviousTier == TacticalLodTier.Background)
		{
			_reason = TacticalLodReason.IdleFar;
			_criticality = TacticalCriticality.Low;
			return TacticalLodTier.Background;
		}

		if (IsFar(in _situation) && !_situation.SeesHostile)
		{
			_reason = TacticalLodReason.IdleFar;
			_criticality = TacticalCriticality.Low;
			return TacticalLodTier.Background;
		}

		_reason = TacticalLodReason.Quiet;
		_criticality = TacticalCriticality.Low;
		return TacticalLodTier.Reduced;
	}
	#endregion
}
