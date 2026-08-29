using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Overlay outcome. Not a <see cref="UnitAIState"/>. Selected ≠ Move.
/// </summary>
public enum EmergencyCoverResult
{
	None = 0,
	Stay = 1,
	Selected = 2,
	Fallback = 3
}

/// <summary>
/// Why the overlay stayed, picked, or skipped a destination.
/// </summary>
public enum EmergencyCoverReason
{
	None = 0,
	ImmediateThreat = 1,
	CurrentCoverSufficient = 2,
	NoAcceptableCandidate = 3,
	NoCandidates = 4
}

/// <summary>
/// Per-candidate emergency opinion. Score is not <see cref="CoverScoreMath.PositionScore"/>.
/// </summary>
public struct CoverEmergencyEvaluation
{
	public CoverCandidate Candidate;
	public float Score;
	public float Protection;
	public float TravelCost;
	public float Danger;
	public float TravelMeters;
	public bool Acceptable;
	public bool Valid;
}

/// <summary>
/// One ImmediateThreat overlay pass. Does not issue navigation or Fire.
/// </summary>
public struct EmergencyCoverDecision
{
	public bool Active;
	public EmergencyCoverResult Result;
	public EmergencyCoverReason Reason;
	public Vector3 Destination;
	public bool HasDestination;
	public int SelectedCandidateId;
	public CoverCandidate Selected;
	public float SelectedScore;
	public bool FromCache;
	public IReadOnlyList<CoverEmergencyEvaluation> Evaluations;
}
