using UnityEngine;

/// <summary>
/// #14C.1 overlay on frozen CoverScore. #14C.2: adjustment scaled by confidence.
/// #14C.3 stamps TacticalPositionPreference beside ThreatDirectionAdjustment.
/// Does not change Protection / Visibility / 0.60 / Reservation.
/// CoverNormal in this project faces the fire/look side (wall behind). Alignment is
/// <c>dot(CoverNormal, ThreatDirection)</c>.
/// </summary>
public static class ThreatDirectionCoverMath
{
	#region Constants
	public const float GoodDot = 0.5f;
	public const float BadDot = -0.5f;
	public const float GoodBonus = 0.85f;
	public const float SideBonus = 0.12f;
	public const float Penalty = -0.75f;
	#endregion

	#region Public Methods
	public static void Bind(ref CoverSituation _situation, in ThreatDirectionKnowledge _knowledge)
	{
		if (!_knowledge.HasValue)
		{
			_situation.HasThreatDirection = false;
			_situation.ThreatDirection = Vector3.zero;
			_situation.ThreatSource = ThreatDirectionSource.InitialEstimate;
			_situation.ThreatState = ThreatDirectionState.None;
			_situation.ThreatConfidence = 0f;
			_situation.ThreatUncertaintyDegrees = 0f;
			return;
		}

		_situation.HasThreatDirection = true;
		_situation.ThreatDirection = _knowledge.Direction;
		_situation.ThreatSource = _knowledge.Source;
		_situation.ThreatState = _knowledge.State;
		_situation.ThreatConfidence = _knowledge.Confidence;
		_situation.ThreatUncertaintyDegrees = _knowledge.UncertaintyDegrees;
	}

	public static float Alignment(Vector3 _coverNormal, Vector3 _threatDirection)
	{
		Vector3 normal = _coverNormal;
		Vector3 threat = _threatDirection;
		normal.y = 0f;
		threat.y = 0f;
		if (normal.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr ||
		    threat.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return 0f;
		return Vector3.Dot(normal.normalized, threat.normalized);
	}

	public static float Adjustment(Vector3 _coverNormal, Vector3 _threatDirection)
	{
		float dot = Alignment(_coverNormal, _threatDirection);
		if (dot >= GoodDot)
			return GoodBonus;
		if (dot <= BadDot)
			return Penalty;
		return SideBonus;
	}

	public static float Adjustment(CoverCandidate _candidate, in ThreatDirectionKnowledge _knowledge)
	{
		if (_candidate == null || !_knowledge.HasValue)
			return 0f;
		return WeightedAdjustment(_candidate.Normal, _knowledge.Direction, _knowledge.Confidence);
	}

	public static float WeightedAdjustment(Vector3 _coverNormal, Vector3 _threatDirection, float _confidence)
	{
		return Adjustment(_coverNormal, _threatDirection) * ThreatDirectionMath.CoverInfluence(_confidence);
	}

	public static void Stamp(ref CoverPositionEvaluation _evaluation, in CoverSituation _situation)
	{
		if (!_situation.HasThreatDirection)
		{
			_evaluation.ThreatDirectionAdjustment = 0f;
			ThreatDirectionPositionMath.Clear(ref _evaluation);
			return;
		}

		CoverCandidate candidate = _evaluation.Candidate;
		_evaluation.ThreatDirectionAdjustment = candidate == null
			? 0f
			: WeightedAdjustment(candidate.Normal, _situation.ThreatDirection, _situation.ThreatConfidence);
		ThreatDirectionPositionMath.Stamp(ref _evaluation, in _situation);
	}

	public static CoverPositionEvaluation Stamp(CoverPositionEvaluation _evaluation, in CoverSituation _situation)
	{
		Stamp(ref _evaluation, in _situation);
		return _evaluation;
	}

	public static bool IsBetterPreference(in CoverPositionEvaluation _a, in CoverPositionEvaluation _b)
	{
		const float epsilon = 0.0001f;
		if (_a.TacticalPositionPreference > _b.TacticalPositionPreference + epsilon)
			return true;
		if (_a.TacticalPositionPreference < _b.TacticalPositionPreference - epsilon)
			return false;
		if (_a.PreferenceScore > _b.PreferenceScore + epsilon)
			return true;
		if (_a.PreferenceScore < _b.PreferenceScore - epsilon)
			return false;
		if (_a.Score > _b.Score + epsilon)
			return true;
		if (_a.Score < _b.Score - epsilon)
			return false;
		float travelA = _a.Factors.TravelCost;
		float travelB = _b.Factors.TravelCost;
		if (travelA + 0.05f < travelB)
			return true;
		if (travelB + 0.05f < travelA)
			return false;
		int idA = _a.Candidate != null ? _a.Candidate.CandidateId : 0;
		int idB = _b.Candidate != null ? _b.Candidate.CandidateId : 0;
		return idA < idB;
	}
	#endregion
}
