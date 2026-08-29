using UnityEngine;

/// <summary>
/// #14.7 pure arrival validation. Local checks only. Does not generate cover. Does not replan.
/// </summary>
public static class TacticalArrivalMath
{
	#region Constants
	/// <summary>
	/// Prototype envelope. Not a freeze. Same magnitude as #13 arrival snap.
	/// </summary>
	public const float DefaultAcquireToleranceMeters = CoverScoreMath.ArrivalSnapMeters;

	/// <summary>
	/// Nav Reached for a cover hop must land inside the acquire disk.
	/// Attack/Defense dest-only Walk uses the same disk so remaining=0 can still acquire.
	/// Search / Retreat / Flee dest-only keep <see cref="TacticalNavigationMath.DefaultPointArrivalRadius"/>.
	/// Prototype, not a freeze.
	/// </summary>
	public const float CoverHopArrivalRadiusMeters = DefaultAcquireToleranceMeters;
	#endregion

	#region Public Methods
	public static float ResolveTolerance(float _acquireToleranceMeters)
	{
		return _acquireToleranceMeters > 0f ? _acquireToleranceMeters : DefaultAcquireToleranceMeters;
	}

	public static float ArrivalRadiusForHop(bool _coverHop)
	{
		return _coverHop ? CoverHopArrivalRadiusMeters : TacticalNavigationMath.DefaultPointArrivalRadius;
	}

	public static float WalkArrivalRadius(UnitAIState _state, bool _coverHop)
	{
		if (_coverHop)
			return CoverHopArrivalRadiusMeters;
		if (_state == UnitAIState.Attack || _state == UnitAIState.Defense)
			return CoverHopArrivalRadiusMeters;
		return TacticalNavigationMath.DefaultPointArrivalRadius;
	}

	public static bool IsTransientAcquireMiss(TacticalArrivalFailureReason _reason)
	{
		return _reason == TacticalArrivalFailureReason.OutOfTolerance ||
		       _reason == TacticalArrivalFailureReason.NavigationStopped;
	}

	public static bool IsWithinTolerance(Vector3 _current, Vector3 _target, float _toleranceMeters)
	{
		float tolerance = ResolveTolerance(_toleranceMeters);
		return CoverSpatialMath.PlanarDistanceSqr(_current, _target) <= tolerance * tolerance;
	}

	public static float DistanceMeters(Vector3 _current, Vector3 _target)
	{
		return Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(_current, _target));
	}

	public static bool MatchesRequiredType(CoverCandidate _candidate, CoverType _required)
	{
		if (_candidate == null)
			return false;
		CoverType required = _required != CoverType.None ? _required : _candidate.CoverType;
		if (required == CoverType.None)
			return CoverScoreMath.IsSelectable(_candidate);
		switch (required)
		{
			case CoverType.Standing:
				return _candidate.StandingValid;
			case CoverType.Crouch:
				return _candidate.CrouchValid;
			case CoverType.Partial:
				return _candidate.PartialValid;
			case CoverType.Corner:
				return _candidate.CornerValid;
			default:
				return CoverScoreMath.IsSelectable(_candidate);
		}
	}

	public static bool IsCandidateValid(CoverCandidate _candidate, CoverType _required)
	{
		return CoverScoreMath.IsSelectable(_candidate) && MatchesRequiredType(_candidate, _required);
	}

	public static TacticalArrivalDecision Evaluate(in TacticalArrivalSituation _situation)
	{
		int candidateId = _situation.Candidate != null
			? _situation.Candidate.CandidateId
			: _situation.CandidateId;
		Vector3 target = ResolveTarget(in _situation);
		float distance = DistanceMeters(_situation.CurrentPosition, target);
		int candidateGeometry = _situation.Candidate != null ? _situation.Candidate.GeometryVersion : 0;
		TacticalArrivalDecision seed = Seed(in _situation, candidateId, distance, candidateGeometry);
		seed.AcquirePosition = target;
		seed.MoveDestination = _situation.HasMoveDestination ? _situation.MoveDestination : target;

		if (!_situation.NavigationReached)
			return Fail(in seed, TacticalArrivalResult.Rejected, TacticalArrivalFailureReason.NavigationStopped);

		if (!string.IsNullOrEmpty(_situation.PathStatus) &&
		    string.Equals(_situation.PathStatus, "PathInvalid", System.StringComparison.Ordinal))
			return Fail(in seed, TacticalArrivalResult.Rejected, TacticalArrivalFailureReason.PathInvalid);

		if (candidateId != 0 && _situation.Candidate == null)
			return Fail(in seed, TacticalArrivalResult.Rejected, TacticalArrivalFailureReason.CandidateMissing);

		if (!IsWithinTolerance(_situation.CurrentPosition, target, _situation.AcquireToleranceMeters))
			return Fail(in seed, TacticalArrivalResult.OutOfTolerance, TacticalArrivalFailureReason.OutOfTolerance);

		if (candidateId == 0 || _situation.Candidate == null)
		{
			if (_situation.RouteStale && _situation.DestinationInvalid)
				return Fail(in seed, TacticalArrivalResult.Reevaluate, TacticalArrivalFailureReason.RouteStale);
			return Success(in seed, TacticalArrivalResult.Acquired, CurrentTacticalPosition.Invalid);
		}

		CoverCandidate candidate = _situation.Candidate;
		CoverType required = _situation.RequiredCoverType != CoverType.None
			? _situation.RequiredCoverType
			: candidate.CoverType;

		if (HasGeometryMismatch(in _situation, candidate))
		{
			if (!IsCandidateValid(candidate, required) ||
			    (_situation.GeometryVersion != 0 && candidate.GeometryVersion != _situation.GeometryVersion))
				return Fail(in seed, TacticalArrivalResult.Reevaluate, TacticalArrivalFailureReason.GeometryChanged);
		}

		if (!IsCandidateValid(candidate, required))
			return Fail(in seed, TacticalArrivalResult.Invalid, TacticalArrivalFailureReason.InvalidPosition);

		if (_situation.RouteStale && _situation.DestinationInvalid)
			return Fail(in seed, TacticalArrivalResult.Reevaluate, TacticalArrivalFailureReason.RouteStale);

		TacticalArrivalDecision occupancyFail;
		if (TryOccupancyFail(in _situation, candidate, in seed, out occupancyFail))
			return occupancyFail;

		if (_situation.IntermediateHop)
			return Success(in seed, TacticalArrivalResult.Traversed, CurrentTacticalPosition.Invalid);

		return Success(in seed, TacticalArrivalResult.Acquired, CurrentTacticalPosition.FromCandidate(candidate, false));
	}
	#endregion

	#region Private Methods
	private static Vector3 ResolveTarget(in TacticalArrivalSituation _situation)
	{
		if (_situation.Candidate != null)
			return _situation.Candidate.Position;
		return _situation.TargetPosition;
	}

	private static TacticalArrivalDecision Seed(
		in TacticalArrivalSituation _situation,
		int _candidateId,
		float _distance,
		int _candidateGeometry)
	{
		CoverOccupancy state = CoverOccupancy.Available;
		int owner = 0;
		if (_situation.Occupancy != null && _candidateId != 0)
		{
			CoverReservation reservation;
			if (_situation.Occupancy.TryGetReservation(
				    _situation.Candidate != null ? _situation.Candidate.RegionId : _situation.CandidateRegion,
				    _candidateId,
				    _situation.Now,
				    out reservation))
			{
				state = reservation.State;
				owner = reservation.UnitId;
			}
		}

		return new TacticalArrivalDecision
		{
			CandidateId = _candidateId,
			DistanceMeters = _distance,
			GeometryVersion = _candidateGeometry,
			CurrentGeometryVersion = _situation.GeometryVersion,
			OccupancyState = state,
			ReservationOwnerUnitId = owner,
			OrientationPending = true,
			OrientationValid = true,
			MissionState = _situation.MissionState
		};
	}

	private static bool HasGeometryMismatch(in TacticalArrivalSituation _situation, CoverCandidate _candidate)
	{
		if (_situation.GeometryVersion == 0)
			return false;
		if (_candidate.GeometryVersion != _situation.GeometryVersion)
			return true;
		if (_situation.Occupancy == null || _situation.CandidateId == 0 && _candidate.CandidateId == 0)
			return false;
		CoverReservation reservation;
		if (!_situation.Occupancy.TryGetReservation(
			    _candidate.RegionId,
			    _candidate.CandidateId,
			    _situation.Now,
			    out reservation))
			return false;
		return reservation.GeometryVersion != 0 && reservation.GeometryVersion != _situation.GeometryVersion;
	}

	private static bool TryOccupancyFail(
		in TacticalArrivalSituation _situation,
		CoverCandidate _candidate,
		in TacticalArrivalDecision _seed,
		out TacticalArrivalDecision _failed)
	{
		_failed = default;
		if (_situation.Occupancy == null || _situation.UnitId == 0)
			return false;

		CoverReservation reservation;
		bool hasSlot = _situation.Occupancy.TryGetReservation(
			_candidate.RegionId,
			_candidate.CandidateId,
			_situation.Now,
			out reservation);
		if (!hasSlot)
		{
			_failed = Fail(in _seed, TacticalArrivalResult.Reevaluate, TacticalArrivalFailureReason.ReservationLost);
			return true;
		}

		if (reservation.UnitId == _situation.UnitId)
			return false;

		TacticalArrivalFailureReason reason = reservation.State == CoverOccupancy.Occupied
			? TacticalArrivalFailureReason.Occupied
			: TacticalArrivalFailureReason.NotReservedByUnit;
		TacticalArrivalResult result = reason == TacticalArrivalFailureReason.Occupied
			? TacticalArrivalResult.Occupied
			: TacticalArrivalResult.Rejected;
		_failed = Fail(in _seed, result, reason);
		return true;
	}

	private static TacticalArrivalDecision Success(
		in TacticalArrivalDecision _seed,
		TacticalArrivalResult _result,
		CurrentTacticalPosition _position)
	{
		TacticalArrivalDecision decision = _seed;
		decision.Result = _result;
		decision.Reason = TacticalArrivalFailureReason.None;
		decision.Position = _position;
		decision.OrientationPending = true;
		decision.OrientationValid = true;
		return decision;
	}

	private static TacticalArrivalDecision Fail(
		in TacticalArrivalDecision _seed,
		TacticalArrivalResult _result,
		TacticalArrivalFailureReason _reason)
	{
		TacticalArrivalDecision decision = _seed;
		decision.Result = _result;
		decision.Reason = _reason;
		decision.Position = CurrentTacticalPosition.Invalid;
		decision.OrientationPending = true;
		decision.OrientationValid = true;
		return decision;
	}
	#endregion
}
