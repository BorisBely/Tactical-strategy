using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stay or request a better position. Not Move. Not a <see cref="UnitAIState"/>.
/// </summary>
public enum TacticalCoverDecisionKind
{
	None = 0,
	Stay = 1,
	Reposition = 2
}

/// <summary>
/// Why Stay / Reposition.
/// </summary>
public enum TacticalCoverReason
{
	None = 0,
	ImprovementTooSmall = 1,
	BetterTacticalPosition = 2,
	CurrentInvalid = 3,
	CurrentDegraded = 4,
	NoCandidate = 5,
	Committed = 6
}

/// <summary>
/// Snapshot of where the soldier is working from. Score is not stored; it is recomputed.
/// Occupied = this unit stands here, not squad reservation.
/// </summary>
public struct CurrentTacticalPosition
{
	public int CandidateId;
	public Vector3 Position;
	public CoverType CoverType;
	public int GeometryVersion;
	public bool Valid;
	public bool Occupied;

	public static CurrentTacticalPosition Invalid =>
		new CurrentTacticalPosition { Valid = false, CoverType = CoverType.None };

	public static CurrentTacticalPosition FromCandidate(CoverCandidate _candidate, bool _occupied)
	{
		if (_candidate == null || !CoverScoreMath.IsSelectable(_candidate))
			return Invalid;
		return new CurrentTacticalPosition
		{
			CandidateId = _candidate.CandidateId,
			Position = _candidate.Position,
			CoverType = _candidate.CoverType,
			GeometryVersion = _candidate.GeometryVersion,
			Valid = true,
			Occupied = _occupied
		};
	}
}

/// <summary>
/// #13.5 result. Selected ≠ Move. #14 later reads destination.
/// </summary>
public struct TacticalCoverDecision
{
	public TacticalCoverDecisionKind Decision;
	public TacticalCoverReason Reason;
	public CurrentTacticalPosition Current;
	public CoverCandidate Selected;
	public int CurrentCandidateId;
	public int SelectedCandidateId;
	public int BestCandidateId;
	public float CurrentScore;
	public float BestScore;
	public float SwitchingCost;
	public bool HasDestination;
	public Vector3 Destination;
	public bool FromCache;
	public IReadOnlyList<CoverPositionEvaluation> Evaluations;
}
