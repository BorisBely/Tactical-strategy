using UnityEngine;

/// <summary>
/// #13.3 factor scores. Weights are prototype, not freeze. Does not Fire / Move / Lean.
/// Primary slice: Protection + Visibility − TravelCost. Other factors are thin baselines.
/// </summary>
public static class CoverScoreMath
{
	#region Constants
	public const float EyeHeightMeters = 1.5f;
	public const float ArrivalSnapMeters = 0.6f;
	#endregion

	#region Public Methods
	public static CoverProtectionProfile ProfileForStance(CoverCandidate _candidate, CoverStance _stance)
	{
		if (_candidate == null)
			return default;
		return _stance == CoverStance.Crouch ? _candidate.CrouchProfile : _candidate.StandingProfile;
	}

	public static float ProtectionScore(CoverCandidate _candidate, in CoverSituation _situation)
	{
		if (_candidate == null)
			return 0f;

		CoverProtectionProfile profile = ProfileForStance(_candidate, _situation.Stance);
		float body = profile.Head * 0.25f + profile.Torso * 0.4f + profile.Pelvis * 0.2f + profile.Legs * 0.15f;
		float stanceFit = 0f;
		if (_situation.Stance == CoverStance.Crouch && _candidate.CrouchValid)
			stanceFit = 0.2f;
		else if (_situation.Stance == CoverStance.Standing && _candidate.StandingValid)
			stanceFit = 0.2f;

		return Mathf.Clamp(body * 3f + stanceFit, 0f, 3.2f);
	}

	public static float VisibilityScore(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		if (_candidate == null)
			return 0f;
		if (!TryLookTarget(_candidate, in _situation, out Vector3 from, out Vector3 to))
			return 0.4f;
		if (_los != null && !_los.HasClearLook(from, to))
			return 0.2f;

		float facing = FacingBonus(_candidate, to - from);
		return 2f + facing;
	}

	public static float TravelCost(CoverCandidate _candidate, in CoverSituation _situation)
	{
		if (_candidate == null)
			return 0f;
		float meters = Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(_situation.UnitPosition, _candidate.Position));
		float cost = Mathf.Min(3f, meters / 5f);
		if (_situation.Rank == CoverRankClass.Veteran)
			cost *= 0.85f;
		return cost;
	}

	public static float FireLaneScore(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		if (_candidate == null || _candidate.CoverType == CoverType.None)
			return 0f;
		if (!TryLookTarget(_candidate, in _situation, out Vector3 from, out Vector3 to))
			return 0.4f;
		if (_los != null && !_los.HasClearLook(from, to))
			return 0f;
		if (FacingBonus(_candidate, to - from) <= 0f)
			return 0.35f;
		return 1.2f;
	}

	public static float MissionScore(CoverCandidate _candidate, in CoverSituation _situation)
	{
		if (_candidate == null || !_situation.HasTarget || _situation.Mission == CoverMissionIntent.Hold)
			return 0f;

		float unitToTarget = Mathf.Sqrt(
			CoverSpatialMath.PlanarDistanceSqr(_situation.UnitPosition, _situation.TargetPosition));
		float candToTarget = Mathf.Sqrt(
			CoverSpatialMath.PlanarDistanceSqr(_candidate.Position, _situation.TargetPosition));
		float delta = (unitToTarget - candToTarget) / 10f;
		if (_situation.Mission == CoverMissionIntent.Attack)
			return Mathf.Clamp(delta, -0.6f, 0.8f);
		return Mathf.Clamp(-delta, -0.6f, 0.8f);
	}

	public static float WeaponScore(CoverCandidate _candidate, in CoverSituation _situation)
	{
		if (_candidate == null)
			return 0f;

		float fromUnit = Mathf.Sqrt(
			CoverSpatialMath.PlanarDistanceSqr(_situation.UnitPosition, _candidate.Position));
		switch (_situation.Weapon)
		{
			case CoverWeaponClass.Sniper:
				float sniper = 0f;
				if (_candidate.StandingValid)
					sniper += 0.3f;
				if (fromUnit > 10f)
					sniper += 1.2f;
				else if (fromUnit < 6f)
					sniper -= 0.35f;
				return sniper;
			case CoverWeaponClass.Lmg:
				float lmg = _candidate.StandingValid ? 0.45f : 0f;
				if (fromUnit < 10f)
					lmg += 0.55f;
				return lmg;
			default:
				float rifle = 0.15f;
				if (fromUnit < 6f)
					rifle += 0.65f;
				return rifle;
		}
	}

	public static float EscapeScore(CoverCandidate _candidate, in CoverSituation _situation)
	{
		return 0f;
	}

	public static float ExposureScore(CoverCandidate _candidate, in CoverSituation _situation)
	{
		float protection = ProtectionScore(_candidate, in _situation);
		float open = 1f - Mathf.Clamp01(protection / 3.2f);
		if (_candidate != null && _candidate.CoverType == CoverType.None)
			open = Mathf.Max(open, 0.7f);
		return open * 0.8f;
	}

	public static float DangerScore(CoverCandidate _candidate, in CoverSituation _situation)
	{
		if (_candidate == null)
			return 0f;

		float danger = _candidate.CoverType == CoverType.None ? 0.7f : 0f;
		Vector3 hostile = _situation.HostileDirection;
		hostile.y = 0f;
		if (hostile.sqrMagnitude > 0.01f)
		{
			hostile.Normalize();
			Vector3 offset = _candidate.Position - _situation.UnitPosition;
			offset.y = 0f;
			if (offset.sqrMagnitude > 0.01f && Vector3.Dot(offset.normalized, hostile) > 0.35f &&
			    ProtectionScore(_candidate, in _situation) < 1.2f)
				danger += 0.4f;
		}

		if (_situation.Rank == CoverRankClass.Recruit)
			danger *= 1.15f;
		else if (_situation.Rank == CoverRankClass.Veteran)
			danger *= 0.8f;
		return danger;
	}

	public static CoverScoreFactors EvaluateFactors(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		return new CoverScoreFactors
		{
			Protection = ProtectionScore(_candidate, in _situation),
			Visibility = VisibilityScore(_candidate, in _situation, _los),
			FireLane = FireLaneScore(_candidate, in _situation, _los),
			MissionRelevance = MissionScore(_candidate, in _situation),
			WeaponSuitability = WeaponScore(_candidate, in _situation),
			EscapeOptions = EscapeScore(_candidate, in _situation),
			Exposure = ExposureScore(_candidate, in _situation),
			TravelCost = TravelCost(_candidate, in _situation),
			Danger = DangerScore(_candidate, in _situation)
		};
	}

	public static float PositionScore(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		return EvaluateFactors(_candidate, in _situation, _los).Total;
	}

	public static CoverPositionEvaluation EvaluateOne(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		CoverScoreFactors factors = EvaluateFactors(_candidate, in _situation, _los);
		return new CoverPositionEvaluation
		{
			Candidate = _candidate,
			Factors = factors,
			Score = factors.Total,
			Valid = IsSelectable(_candidate)
		};
	}

	public static bool IsSelectable(CoverCandidate _candidate)
	{
		return _candidate != null &&
		       _candidate.NavMeshValid &&
		       _candidate.CoverType != CoverType.None;
	}

	public static CoverCandidate CreateCurrentPlaceholder(in CoverSituation _situation)
	{
		Vector3 normal = _situation.SectorForward;
		normal.y = 0f;
		if (normal.sqrMagnitude < 0.0001f && _situation.HasTarget)
		{
			normal = _situation.TargetPosition - _situation.UnitPosition;
			normal.y = 0f;
		}

		if (normal.sqrMagnitude < 0.0001f)
			normal = Vector3.forward;
		else
			normal.Normalize();

		return new CoverCandidate
		{
			CandidateId = 0,
			Position = _situation.UnitPosition,
			Normal = normal,
			CoverType = CoverType.None,
			NavMeshValid = true,
			GeometryVersion = _situation.GeometryVersion,
			RegionId = _situation.RegionId
		};
	}

	public static bool IsAtCandidate(in CoverSituation _situation, CoverCandidate _candidate)
	{
		if (_candidate == null)
			return false;
		return CoverSpatialMath.PlanarDistanceSqr(_situation.UnitPosition, _candidate.Position) <=
		       ArrivalSnapMeters * ArrivalSnapMeters;
	}
	#endregion

	#region Private Methods
	private static bool TryLookTarget(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		out Vector3 _from,
		out Vector3 _to)
	{
		_from = _candidate.Position + Vector3.up * EyeHeightMeters;
		if (_situation.HasTarget)
		{
			_to = _situation.TargetPosition;
			if (_to.y < 0.2f)
				_to.y = EyeHeightMeters;
			return true;
		}

		Vector3 forward = _situation.SectorForward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = _candidate.Normal;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
		{
			_to = _from + Vector3.forward;
			return false;
		}

		_to = _from + forward.normalized * 12f;
		return true;
	}

	private static float FacingBonus(CoverCandidate _candidate, Vector3 _toTarget)
	{
		Vector3 n = _candidate.Normal;
		n.y = 0f;
		_toTarget.y = 0f;
		if (n.sqrMagnitude < 0.0001f || _toTarget.sqrMagnitude < 0.0001f)
			return 0f;
		float dot = Vector3.Dot(n.normalized, _toTarget.normalized);
		return dot > 0.15f ? 0.4f : 0f;
	}
	#endregion
}
