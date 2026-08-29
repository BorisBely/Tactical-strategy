using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One #14C.5 pass. Permission for existing #13 Stay / Reposition. Not Move.
/// </summary>
public struct ThreatDirectionRepositionResult
{
	public ThreatDirectionRepositionKind Kind;
	public CoverThreatFit ThreatFit;
	public bool Changed;
	public float AngleDeltaDegrees;
	public int CurrentCandidateId;
	public int BestCandidateId;
}

/// <summary>
/// #14C.5 Threat Direction → Reposition Decision. Event-driven.
/// Stay / FaceOnly / RepositionAllowed. Does not pick final cover, Reserve, Move, or scan.
/// </summary>
public sealed class ThreatDirectionReposition
{
	#region Private Fields
	private ThreatDirectionRepositionKind m_LastKind;
	private CoverThreatFit m_LastFit;
	private int m_LastCurrentId;
	private int m_LastBestId;
	private ThreatDirectionCompass m_LastCompass = ThreatDirectionCompass.North;
	private int m_DecideCount;
	private int m_LogCount;
	private string m_LastPayload = string.Empty;
	private Component m_LogActor;
	#endregion

	#region Public Properties
	public Component LogActor
	{
		get => m_LogActor;
		set => m_LogActor = value;
	}

	public ThreatDirectionRepositionKind LastKind => m_LastKind;

	public bool AllowsCoverReevaluation =>
		m_LastKind == ThreatDirectionRepositionKind.RepositionAllowed;

	public int DecideCount => m_DecideCount;

	public int LogCount => m_LogCount;

	public string LastPayload => m_LastPayload;

	public CoverThreatFit LastFit => m_LastFit;
	#endregion

	#region Public Methods
	public void Reset()
	{
		m_LastKind = ThreatDirectionRepositionKind.None;
		m_LastFit = CoverThreatFit.Unknown;
		m_LastCurrentId = 0;
		m_LastBestId = 0;
		m_LastCompass = ThreatDirectionCompass.North;
		m_DecideCount = 0;
		m_LogCount = 0;
		m_LastPayload = string.Empty;
	}

	public ThreatDirectionRepositionResult Evaluate(
		in ThreatDirectionKnowledge _knowledge,
		CoverCandidate _occupying,
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		float _angleDeltaDegrees)
	{
		m_DecideCount++;
		CoverPositionEvaluation current = default;
		CoverPositionEvaluation best = default;
		bool hasOccupying = _occupying != null;
		CoverThreatFit fit = CoverThreatFit.Unknown;
		int currentId = 0;
		int bestId = 0;
		if (hasOccupying)
		{
			current = Stamp(_occupying, in _situation);
			currentId = _occupying.CandidateId;
			fit = ThreatDirectionReorientationMath.ClassifyFit(_occupying.Normal, _knowledge.Direction);
			best = current;
			bestId = currentId;
			PickBest(_candidates, in _situation, ref best, ref bestId);
		}

		bool hold = m_LastKind == ThreatDirectionRepositionKind.RepositionAllowed;
		CoverThreatFit bestFit = best.Candidate != null
			? ThreatDirectionReorientationMath.ClassifyFit(best.Candidate.Normal, _knowledge.Direction)
			: CoverThreatFit.Unknown;
		ThreatDirectionRepositionKind kind = ThreatDirectionRepositionMath.Decide(
			_angleDeltaDegrees,
			_knowledge.Confidence,
			fit,
			hasOccupying,
			current.Score,
			current.TacticalPositionPreference,
			best.Score,
			best.TacticalPositionPreference,
			currentId,
			bestId,
			hold,
			current.PositionAdjustment,
			best.PositionAdjustment,
			bestFit);

		var result = new ThreatDirectionRepositionResult
		{
			Kind = kind,
			ThreatFit = fit,
			AngleDeltaDegrees = _angleDeltaDegrees,
			CurrentCandidateId = currentId,
			BestCandidateId = bestId
		};

		if (kind == m_LastKind &&
		    fit == m_LastFit &&
		    currentId == m_LastCurrentId &&
		    bestId == m_LastBestId &&
		    _knowledge.Compass == m_LastCompass)
			return result;

		result.Changed = kind != m_LastKind;
		m_LastKind = kind;
		m_LastFit = fit;
		m_LastCurrentId = currentId;
		m_LastBestId = bestId;
		m_LastCompass = _knowledge.Compass;
		m_LastPayload = ThreatDirectionRepositionLog.Format(
			kind,
			fit,
			_knowledge.Confidence,
			_angleDeltaDegrees,
			currentId,
			bestId);
		m_LogCount++;
		ThreatDirectionRepositionLog.Emit(m_LogActor, m_LastPayload);
		return result;
	}
	#endregion

	#region Private Methods
	private static CoverPositionEvaluation Stamp(CoverCandidate _candidate, in CoverSituation _situation)
	{
		return ThreatDirectionCoverMath.Stamp(
			CoverScoreMath.EvaluateOne(_candidate, in _situation, null),
			in _situation);
	}

	private static void PickBest(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		ref CoverPositionEvaluation _best,
		ref int _bestId)
	{
		if (_candidates == null)
			return;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate == null)
				continue;
			CoverPositionEvaluation evaluation = Stamp(candidate, in _situation);
			if (!evaluation.Valid)
				continue;
			if (!ThreatDirectionCoverMath.IsBetterPreference(evaluation, _best))
				continue;
			_best = evaluation;
			_bestId = candidate.CandidateId;
		}
	}
	#endregion
}
