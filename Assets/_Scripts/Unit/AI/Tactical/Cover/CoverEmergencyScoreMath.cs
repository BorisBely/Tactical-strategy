using UnityEngine;

/// <summary>
/// #13.4 emergency score. Not <see cref="CoverScoreMath.PositionScore"/>. Prototype weights, not freeze.
/// Primary: Protection, TravelCost, Danger, NavMeshValid. Secondary: Visibility, FireLane, Mission, Weapon.
/// </summary>
public static class CoverEmergencyScoreMath
{
	#region Constants
	public const float EmergencyAcceptableThreshold = 2f;
	public const float AcceptableThreshold = EmergencyAcceptableThreshold;
	public const float AcceptableProtection = 1.45f;
	private const float c_ProtectionWeight = 1f;
	private const float c_TravelWeight = 0.45f;
	private const float c_DangerWeight = 1.15f;
	private const float c_VisibilityWeight = 0.15f;
	private const float c_FireLaneWeight = 0.1f;
	private const float c_MissionWeight = 0.1f;
	private const float c_WeaponWeight = 0.1f;
	#endregion

	#region Public Methods
	public static float Score(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		return Evaluate(_candidate, in _situation, _los).Score;
	}

	public static CoverEmergencyEvaluation Evaluate(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		CoverScoreFactors factors = CoverScoreMath.EvaluateFactors(_candidate, in _situation, _los);
		float score =
			factors.Protection * c_ProtectionWeight
			- factors.TravelCost * c_TravelWeight
			- factors.Danger * c_DangerWeight
			+ factors.Visibility * c_VisibilityWeight
			+ factors.FireLane * c_FireLaneWeight
			+ factors.MissionRelevance * c_MissionWeight
			+ factors.WeaponSuitability * c_WeaponWeight;
		bool valid = CoverScoreMath.IsSelectable(_candidate);
		float travelMeters = 0f;
		if (_candidate != null)
			travelMeters = Mathf.Sqrt(
				CoverSpatialMath.PlanarDistanceSqr(_situation.UnitPosition, _candidate.Position));
		return new CoverEmergencyEvaluation
		{
			Candidate = _candidate,
			Score = score,
			Protection = factors.Protection,
			TravelCost = factors.TravelCost,
			Danger = factors.Danger,
			TravelMeters = travelMeters,
			Valid = valid,
			Acceptable = IsAcceptable(_candidate, score, in _situation)
		};
	}

	public static bool IsAcceptable(
		CoverCandidate _candidate,
		float _score,
		in CoverSituation _situation)
	{
		if (_candidate == null || _candidate.CoverType == CoverType.None || !_candidate.NavMeshValid)
			return false;
		if (!CoverScoreMath.IsSelectable(_candidate))
			return false;
		if (CoverScoreMath.ProtectionScore(_candidate, in _situation) < AcceptableProtection)
			return false;
		return _score >= AcceptableThreshold;
	}

	public static bool IsCurrentSufficient(
		CoverCandidate _occupying,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		if (_occupying == null)
			return false;
		CoverEmergencyEvaluation evaluation = Evaluate(_occupying, in _situation, _los);
		return evaluation.Acceptable;
	}
	#endregion
}
