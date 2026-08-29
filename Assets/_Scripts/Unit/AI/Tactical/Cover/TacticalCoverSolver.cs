using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.5 Stay / Reposition. Uses 13.3 PositionScore + SwitchingCost. Not Fire. Not Move.
/// Does not run under ImmediateThreat (13.4 owns that).
/// #14C.1: Best ranking may use TacticalPositionPreference. Stay/Reposition still uses CoverScore. Occupied stays Committed unless #14C.5 ThreatRepositionAllowed.
/// </summary>
public sealed class TacticalCoverSolver
{
	#region Nested
	private struct EventKey : IEquatable<EventKey>
	{
		public int GeometryVersion;
		public int CandidateFingerprint;
		public int UnitQx;
		public int UnitQz;
		public int TargetQx;
		public int TargetQz;
		public int HostileQx;
		public int HostileQz;
		public bool HasTarget;
		public CoverMissionIntent Mission;
		public CoverWeaponClass Weapon;
		public CoverRankClass Rank;
		public int CurrentId;
		public bool CurrentValid;
		public int CurrentVersion;
		public bool CurrentLosClear;
		public bool HasThreatDirection;
		public int ThreatOctant;
		public int ThreatQualityBand;
		public int ThreatUncertaintyBand;
		public bool ThreatRepositionAllowed;

		public bool Equals(EventKey _other)
		{
			return GeometryVersion == _other.GeometryVersion &&
			       CandidateFingerprint == _other.CandidateFingerprint &&
			       UnitQx == _other.UnitQx &&
			       UnitQz == _other.UnitQz &&
			       TargetQx == _other.TargetQx &&
			       TargetQz == _other.TargetQz &&
			       HostileQx == _other.HostileQx &&
			       HostileQz == _other.HostileQz &&
			       HasTarget == _other.HasTarget &&
			       Mission == _other.Mission &&
			       Weapon == _other.Weapon &&
			       Rank == _other.Rank &&
			       CurrentId == _other.CurrentId &&
			       CurrentValid == _other.CurrentValid &&
			       CurrentVersion == _other.CurrentVersion &&
			       CurrentLosClear == _other.CurrentLosClear &&
			       HasThreatDirection == _other.HasThreatDirection &&
			       ThreatOctant == _other.ThreatOctant &&
			       ThreatQualityBand == _other.ThreatQualityBand &&
			       ThreatUncertaintyBand == _other.ThreatUncertaintyBand &&
			       ThreatRepositionAllowed == _other.ThreatRepositionAllowed;
		}

		public override bool Equals(object _obj)
		{
			return _obj is EventKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = GeometryVersion;
				hash = (hash * 397) ^ CandidateFingerprint;
				hash = (hash * 397) ^ UnitQx;
				hash = (hash * 397) ^ CurrentId;
				hash = (hash * 397) ^ (int)Mission;
				return hash;
			}
		}
	}
	#endregion

	#region Constants
	private const float c_QuantizeMeters = 0.75f;
	#endregion

	#region Private Fields
	private readonly List<CoverPositionEvaluation> m_Evaluations = new List<CoverPositionEvaluation>(16);
	private EventKey m_LastKey;
	private TacticalCoverDecision m_Last;
	private bool m_HasLast;
	private int m_DecideCount;
	private int m_LastLoggedKind = int.MinValue;
	private int m_LastLoggedSelected = int.MinValue;
	#endregion

	#region Public Properties
	public int DecideCount => m_DecideCount;
	public TacticalCoverDecision Last => m_Last;
	#endregion

	#region Public Methods
	public void Invalidate()
	{
		m_HasLast = false;
	}

	public static bool AllowsTactical(UnitAIState _state, bool _immediateThreat)
	{
		if (_immediateThreat)
			return false;
		return _state != UnitAIState.Retreat &&
		       _state != UnitAIState.Flee &&
		       _state != UnitAIState.Search;
	}

	public bool NeedsReevaluation(
		in CoverSituation _situation,
		in CurrentTacticalPosition _current,
		IReadOnlyList<CoverCandidate> _candidates,
		ICoverLineOfSightProbe _los = null)
	{
		if (!m_HasLast)
			return true;
		return !BuildKey(in _situation, in _current, _candidates, _los).Equals(m_LastKey);
	}

	public TacticalCoverDecision DecideFromScores(
		float _currentScore,
		float _bestScore,
		float _switchingCost,
		bool _currentValid,
		int _currentId,
		int _bestId)
	{
		m_DecideCount++;
		TacticalCoverDecisionKind kind;
		TacticalCoverReason reason;
		if (!_currentValid)
		{
			kind = TacticalCoverDecisionKind.Reposition;
			reason = TacticalCoverReason.CurrentInvalid;
		}
		else if (CoverSwitchMath.ShouldReposition(_currentScore, _bestScore, _switchingCost))
		{
			kind = TacticalCoverDecisionKind.Reposition;
			reason = TacticalCoverReason.BetterTacticalPosition;
		}
		else
		{
			kind = TacticalCoverDecisionKind.Stay;
			reason = TacticalCoverReason.ImprovementTooSmall;
		}

		m_Last = new TacticalCoverDecision
		{
			Decision = kind,
			Reason = reason,
			CurrentScore = _currentScore,
			BestScore = _bestScore,
			SwitchingCost = _switchingCost,
			CurrentCandidateId = _currentId,
			SelectedCandidateId = kind == TacticalCoverDecisionKind.Reposition ? _bestId : _currentId,
			BestCandidateId = _bestId,
			HasDestination = kind == TacticalCoverDecisionKind.Reposition,
			FromCache = false
		};
		m_HasLast = true;
		return m_Last;
	}

	public TacticalCoverDecision Decide(
		in CoverSituation _situation,
		IReadOnlyList<CoverCandidate> _candidates,
		in CurrentTacticalPosition _current,
		ICoverLineOfSightProbe _los = null,
		float? _costOverride = null,
		CoverOccupancyBoard _occupancy = null)
	{
		EventKey key = BuildKey(in _situation, in _current, _candidates, _los);
		if (m_HasLast && key.Equals(m_LastKey))
		{
			TacticalCoverDecision cached = m_Last;
			cached.FromCache = true;
			m_Last = cached;
			return cached;
		}

		m_DecideCount++;
		CoverPositionEvaluation currentEval = EvaluateCurrent(_candidates, in _situation, in _current, _los);
		CoverPositionEvaluation best = default;
		bool hasBest = TryPickBest(_candidates, in _situation, _los, _occupancy, out best);
		bool currentValid = _current.Valid && currentEval.Valid &&
		                    (_current.GeometryVersion == 0 ||
		                     currentEval.Candidate == null ||
		                     currentEval.Candidate.GeometryVersion == _current.GeometryVersion ||
		                     currentEval.Candidate.GeometryVersion == _situation.GeometryVersion);

		TacticalCoverDecision decision;
		if (!hasBest)
		{
			decision = Build(
				TacticalCoverDecisionKind.Stay,
				TacticalCoverReason.NoCandidate,
				in _current,
				currentEval,
				default,
				0f,
				null);
		}
		else if (!currentValid)
		{
			decision = Build(
				TacticalCoverDecisionKind.Reposition,
				TacticalCoverReason.CurrentInvalid,
				in _current,
				currentEval,
				best,
				0f,
				best.Candidate);
		}
		else if (!_current.Occupied)
		{
			decision = Build(
				TacticalCoverDecisionKind.Stay,
				TacticalCoverReason.Committed,
				in _current,
				currentEval,
				best,
				0f,
				currentEval.Candidate);
		}
		else if (!key.CurrentLosClear)
		{
			decision = Build(
				TacticalCoverDecisionKind.Reposition,
				TacticalCoverReason.CurrentInvalid,
				in _current,
				currentEval,
				best,
				0f,
				best.Candidate);
		}
		else
		{
			CoverCandidate from = currentEval.Candidate;
			float cost = _costOverride ??
			             CoverSwitchMath.ComputeSwitchingCost(from, best.Candidate, in _situation);
			if (!TryThreatDirectionReposition(
				    in _situation, in _current, in currentEval, in best, cost, out decision))
			{
				decision = Build(
					TacticalCoverDecisionKind.Stay,
					TacticalCoverReason.Committed,
					in _current,
					currentEval,
					best,
					cost,
					from);
			}
		}

		decision.FromCache = false;
		decision.Evaluations = CopyEvaluations();
		m_LastKey = key;
		m_Last = decision;
		m_HasLast = true;
		return decision;
	}

	public bool ShouldLog(in TacticalCoverDecision _decision)
	{
		int kind = (int)_decision.Decision;
		int selected = _decision.SelectedCandidateId;
		if (kind == m_LastLoggedKind && selected == m_LastLoggedSelected)
			return false;
		m_LastLoggedKind = kind;
		m_LastLoggedSelected = selected;
		return true;
	}
	#endregion

	#region Private Methods
	private static TacticalCoverDecision Build(
		TacticalCoverDecisionKind _kind,
		TacticalCoverReason _reason,
		in CurrentTacticalPosition _current,
		in CoverPositionEvaluation _currentEval,
		in CoverPositionEvaluation _best,
		float _cost,
		CoverCandidate _selected)
	{
		bool reposition = _kind == TacticalCoverDecisionKind.Reposition && _selected != null;
		int bestId = _best.Candidate != null ? _best.Candidate.CandidateId : 0;
		return new TacticalCoverDecision
		{
			Decision = _kind,
			Reason = _reason,
			Current = _current,
			Selected = _selected,
			CurrentCandidateId = _current.CandidateId,
			SelectedCandidateId = _selected != null ? _selected.CandidateId : 0,
			BestCandidateId = bestId,
			CurrentScore = _currentEval.Score,
			BestScore = _best.Candidate != null ? _best.Score : _currentEval.Score,
			SwitchingCost = _cost,
			HasDestination = reposition,
			Destination = reposition ? _selected.Position : Vector3.zero
		};
	}

	private static CoverPositionEvaluation EvaluateCurrent(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		in CurrentTacticalPosition _current,
		ICoverLineOfSightProbe _los)
	{
		if (_current.Valid && _candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				CoverCandidate candidate = _candidates[i];
				if (candidate == null || candidate.CandidateId != _current.CandidateId)
					continue;
				return ThreatDirectionCoverMath.Stamp(
					CoverScoreMath.EvaluateOne(candidate, in _situation, _los),
					in _situation);
			}
		}

		CoverCandidate occupying = EmergencyCoverOverlay.FindOccupying(_candidates, in _situation);
		if (occupying != null)
			return ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(occupying, in _situation, _los),
				in _situation);
		return ThreatDirectionCoverMath.Stamp(
			CoverScoreMath.EvaluateOne(CoverScoreMath.CreateCurrentPlaceholder(in _situation), in _situation, _los),
			in _situation);
	}

	private bool TryPickBest(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los,
		CoverOccupancyBoard _occupancy,
		out CoverPositionEvaluation _best)
	{
		_best = default;
		m_Evaluations.Clear();
		bool hasBest = false;
		if (_candidates == null)
			return false;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			CoverPositionEvaluation evaluation = ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(candidate, in _situation, _los),
				in _situation);
			m_Evaluations.Add(evaluation);
			if (!evaluation.Valid)
				continue;
			if (_occupancy != null &&
			    !_occupancy.IsUsable(candidate, _situation.UnitId, Time.time))
				continue;
			if (!hasBest || ThreatDirectionCoverMath.IsBetterPreference(evaluation, _best))
			{
				_best = evaluation;
				hasBest = true;
			}
		}

		return hasBest;
	}

	private static bool TryThreatDirectionReposition(
		in CoverSituation _situation,
		in CurrentTacticalPosition _current,
		in CoverPositionEvaluation _currentEval,
		in CoverPositionEvaluation _best,
		float _cost,
		out TacticalCoverDecision _decision)
	{
		_decision = default;
		if (!_situation.ThreatRepositionAllowed)
			return false;
		if (_best.Candidate == null || _currentEval.Candidate == null)
			return false;
		if (_best.Candidate.CandidateId == _currentEval.Candidate.CandidateId)
			return false;

		CoverThreatFit currentFit = ThreatDirectionReorientationMath.ClassifyFit(
			_currentEval.Candidate.Normal,
			_situation.ThreatDirection);
		CoverThreatFit bestFit = ThreatDirectionReorientationMath.ClassifyFit(
			_best.Candidate.Normal,
			_situation.ThreatDirection);
		bool orientationUpgrade = currentFit == CoverThreatFit.Poor && bestFit == CoverThreatFit.Good;
		if (!orientationUpgrade &&
		    !ThreatDirectionRepositionMath.HasNoticeableAdvantage(
			    _currentEval.Score,
			    _currentEval.TacticalPositionPreference,
			    _currentEval.PositionAdjustment,
			    _best.Score,
			    _best.TacticalPositionPreference,
			    _best.PositionAdjustment))
			return false;

		_decision = Build(
			TacticalCoverDecisionKind.Reposition,
			TacticalCoverReason.BetterTacticalPosition,
			in _current,
			in _currentEval,
			in _best,
			_cost,
			_best.Candidate);
		return true;
	}

	private List<CoverPositionEvaluation> CopyEvaluations()
	{
		var copy = new List<CoverPositionEvaluation>(m_Evaluations.Count);
		for (int i = 0; i < m_Evaluations.Count; i++)
			copy.Add(m_Evaluations[i]);
		return copy;
	}

	private static EventKey BuildKey(
		in CoverSituation _situation,
		in CurrentTacticalPosition _current,
		IReadOnlyList<CoverCandidate> _candidates,
		ICoverLineOfSightProbe _los)
	{
		int fingerprint = 0;
		int version = _situation.GeometryVersion;
		if (_candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				CoverCandidate candidate = _candidates[i];
				if (candidate == null)
					continue;
				fingerprint = (fingerprint * 397) ^ candidate.CandidateId;
				if (version == 0)
					version = candidate.GeometryVersion;
			}

			fingerprint ^= _candidates.Count * 17;
		}

		return new EventKey
		{
			GeometryVersion = version,
			CandidateFingerprint = fingerprint,
			UnitQx = Quantize(_situation.UnitPosition.x),
			UnitQz = Quantize(_situation.UnitPosition.z),
			HasTarget = _situation.HasTarget,
			TargetQx = Quantize(_situation.TargetPosition.x),
			TargetQz = Quantize(_situation.TargetPosition.z),
			HostileQx = Quantize(_situation.HostileDirection.x),
			HostileQz = Quantize(_situation.HostileDirection.z),
			Mission = _situation.Mission,
			Weapon = _situation.Weapon,
			Rank = _situation.Rank,
			CurrentId = _current.CandidateId,
			CurrentValid = _current.Valid,
			CurrentVersion = _current.GeometryVersion,
			CurrentLosClear = ProbeCurrentLos(in _situation, in _current, _candidates, _los),
			HasThreatDirection = _situation.HasThreatDirection,
			ThreatOctant = _situation.HasThreatDirection
				? (int)ThreatDirectionEstimator.CompassFrom(_situation.ThreatDirection)
				: -1,
			ThreatQualityBand = ThreatDirectionMath.ConsumerQualityBand(in _situation),
			ThreatUncertaintyBand = ThreatDirectionPositionMath.ConsumerUncertaintyBand(in _situation),
			ThreatRepositionAllowed = _situation.ThreatRepositionAllowed
		};
	}

	private static bool ProbeCurrentLos(
		in CoverSituation _situation,
		in CurrentTacticalPosition _current,
		IReadOnlyList<CoverCandidate> _candidates,
		ICoverLineOfSightProbe _los)
	{
		if (_los == null || !_situation.HasTarget)
			return true;

		CoverCandidate at = null;
		if (_current.Valid && _candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				CoverCandidate candidate = _candidates[i];
				if (candidate == null || candidate.CandidateId != _current.CandidateId)
					continue;
				at = candidate;
				break;
			}
		}

		if (at == null)
			at = EmergencyCoverOverlay.FindOccupying(_candidates, in _situation);
		if (at == null)
			return true;

		Vector3 from = at.Position + Vector3.up * CoverScoreMath.EyeHeightMeters;
		Vector3 to = _situation.TargetPosition;
		if (to.y < 0.2f)
			to.y = CoverScoreMath.EyeHeightMeters;
		return _los.HasClearLook(from, to);
	}

	private static int Quantize(float _meters)
	{
		return Mathf.RoundToInt(_meters / c_QuantizeMeters);
	}
	#endregion
}
