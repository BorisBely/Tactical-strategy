using System.Collections.Generic;

/// <summary>
/// Per-unit opinion of one shared candidate. Score lives here, not on the candidate.
/// </summary>
public struct CoverScoreFactors
{
	public float Protection;
	public float Visibility;
	public float FireLane;
	public float MissionRelevance;
	public float WeaponSuitability;
	public float EscapeOptions;
	public float Exposure;
	public float TravelCost;
	public float Danger;

	public float Total =>
		Protection + Visibility + FireLane + MissionRelevance + WeaponSuitability + EscapeOptions
		- Exposure - TravelCost - Danger;
}

/// <summary>
/// One scored candidate for one soldier. Valid ≠ selected. Selected ≠ Move.
/// </summary>
public struct CoverPositionEvaluation
{
	public CoverCandidate Candidate;
	public float Score;
	public bool Valid;
	public CoverScoreFactors Factors;
	public float ThreatDirectionAdjustment;
	public float DirectionScore;
	public float FacingScore;
	public float ConfidenceWeight;
	public float SectorOverlap;
	public float PositionAdjustment;

	/// <summary>#14C.1 overlay. CoverScore itself stays frozen.</summary>
	public float PreferenceScore => Score + ThreatDirectionAdjustment;

	/// <summary>#14C.3 overlay. Preference only. Not CoverScore. Not Move.</summary>
	public float TacticalPositionPreference => Score + PositionAdjustment;
}

/// <summary>
/// Result of one individual evaluation pass. Does not issue navigation or Fire.
/// </summary>
public sealed class CoverEvaluationResult
{
	public IReadOnlyList<CoverPositionEvaluation> Evaluations;
	public CoverPositionEvaluation Best;
	public CoverPositionEvaluation Current;
	public bool HasBest;
	public bool RepositionRecommended;
	public bool FromCache;
}
