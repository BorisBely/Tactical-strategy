using UnityEngine;

/// <summary>
/// #14C.3 tactical position preference overlay. Decision support only.
/// Does not change CoverScore / PathScore / 0.60 / Reservation / Occupancy.
/// Does not Move / Reserve / Reposition.
/// </summary>
public static class ThreatDirectionPositionMath
{
	#region Constants
	public const float FacingWeight = 0.4f;
	public const float MaterialDirectionDegrees = 35f;
	public const float StandingProtectedHalfDegrees = 45f;
	public const float CrouchProtectedHalfDegrees = 40f;
	public const float PartialProtectedHalfDegrees = 70f;
	public const float CornerProtectedHalfDegrees = 18f;
	public const float UncertaintyBandDegrees = 15f;
	#endregion

	#region Public Methods
	public static void Clear(ref CoverPositionEvaluation _evaluation)
	{
		_evaluation.DirectionScore = 0f;
		_evaluation.FacingScore = 0f;
		_evaluation.ConfidenceWeight = 0f;
		_evaluation.SectorOverlap = 0f;
		_evaluation.PositionAdjustment = 0f;
	}

	public static void Stamp(ref CoverPositionEvaluation _evaluation, in CoverSituation _situation)
	{
		if (!_situation.HasThreatDirection)
		{
			Clear(ref _evaluation);
			return;
		}

		CoverCandidate candidate = _evaluation.Candidate;
		if (candidate == null)
		{
			Clear(ref _evaluation);
			return;
		}

		float direction = ThreatDirectionCoverMath.Adjustment(
			candidate.Normal,
			_situation.ThreatDirection);
		float facing = FacingScore(candidate.Normal, _situation.ThreatDirection);
		float weight = ThreatDirectionMath.CoverInfluence(_situation.ThreatConfidence);
		float overlap = SectorOverlap(
			candidate.Normal,
			ProtectedHalfAngleDegrees(candidate.CoverType),
			_situation.ThreatDirection,
			_situation.ThreatUncertaintyDegrees);

		_evaluation.DirectionScore = direction;
		_evaluation.FacingScore = facing;
		_evaluation.ConfidenceWeight = weight;
		_evaluation.SectorOverlap = overlap;
		_evaluation.PositionAdjustment = FinalAdjustment(direction, facing, weight, overlap);
	}

	public static float FacingScore(Vector3 _coverNormal, Vector3 _threatDirection)
	{
		return ThreatDirectionCoverMath.Alignment(_coverNormal, _threatDirection) * FacingWeight;
	}

	public static float ProtectedHalfAngleDegrees(CoverType _coverType)
	{
		switch (_coverType)
		{
			case CoverType.Corner:
				return CornerProtectedHalfDegrees;
			case CoverType.Partial:
				return PartialProtectedHalfDegrees;
			case CoverType.Crouch:
				return CrouchProtectedHalfDegrees;
			case CoverType.Standing:
				return StandingProtectedHalfDegrees;
			default:
				return StandingProtectedHalfDegrees;
		}
	}

	public static float SectorOverlap(
		Vector3 _coverNormal,
		float _coverHalfDegrees,
		Vector3 _threatDirection,
		float _threatHalfDegrees)
	{
		Vector3 normal = _coverNormal;
		Vector3 threat = _threatDirection;
		normal.y = 0f;
		threat.y = 0f;
		if (normal.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr ||
		    threat.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return 0f;

		float coverHalf = Mathf.Max(0f, _coverHalfDegrees);
		float threatHalf = Mathf.Max(0f, _threatHalfDegrees);
		float threatWidth = Mathf.Max(threatHalf * 2f, 0.01f);
		float delta = Mathf.Abs(Mathf.DeltaAngle(
			ThreatDirectionFacingController.YawFrom(normal),
			ThreatDirectionFacingController.YawFrom(threat)));
		float intersection = Mathf.Max(
			0f,
			Mathf.Min(coverHalf, delta + threatHalf) - Mathf.Max(-coverHalf, delta - threatHalf));
		return Mathf.Clamp01(intersection / threatWidth);
	}

	public static float FinalAdjustment(
		float _directionScore,
		float _facingScore,
		float _confidenceWeight,
		float _sectorOverlap)
	{
		float combined = _directionScore + _facingScore;
		float weight = Mathf.Clamp01(_confidenceWeight);
		if (combined >= 0f)
			return combined * weight * Mathf.Clamp01(_sectorOverlap);
		return combined * weight;
	}

	public static bool IsMaterialDirectionChange(Vector3 _from, Vector3 _to)
	{
		Vector3 a = _from;
		Vector3 b = _to;
		a.y = 0f;
		b.y = 0f;
		if (a.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr ||
		    b.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return true;
		return Vector3.Angle(a.normalized, b.normalized) >= MaterialDirectionDegrees;
	}

	public static int UncertaintyBand(float _uncertaintyDegrees)
	{
		return Mathf.Clamp(
			Mathf.RoundToInt(Mathf.Max(0f, _uncertaintyDegrees) / UncertaintyBandDegrees),
			0,
			8);
	}

	public static int ConsumerUncertaintyBand(in CoverSituation _situation)
	{
		if (!_situation.HasThreatDirection)
			return -1;
		return UncertaintyBand(_situation.ThreatUncertaintyDegrees);
	}
	#endregion
}
