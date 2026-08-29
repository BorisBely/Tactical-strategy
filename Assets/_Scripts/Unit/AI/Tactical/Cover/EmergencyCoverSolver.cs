using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.4 pure overlay solver. ImmediateThreat + situation + candidates → destination.
/// Does not write <see cref="UnitAIStateContext.Destination"/>. Does not Move or Fire.
/// </summary>
public sealed class EmergencyCoverSolver
{
	#region Nested
	private struct CacheKey : IEquatable<CacheKey>
	{
		public bool ImmediateThreat;
		public UnitAIState State;
		public int UnitQx;
		public int UnitQz;
		public int GeometryVersion;
		public int CandidateFingerprint;
		public int OccupyingId;
		public CoverStance Stance;
		public CoverMissionIntent Mission;
		public CoverWeaponClass Weapon;
		public CoverRankClass Rank;
		public int OccupancyVersion;

		public bool Equals(CacheKey _other)
		{
			return ImmediateThreat == _other.ImmediateThreat &&
			       State == _other.State &&
			       UnitQx == _other.UnitQx &&
			       UnitQz == _other.UnitQz &&
			       GeometryVersion == _other.GeometryVersion &&
			       OccupancyVersion == _other.OccupancyVersion &&
			       CandidateFingerprint == _other.CandidateFingerprint &&
			       OccupyingId == _other.OccupyingId &&
			       Stance == _other.Stance &&
			       Mission == _other.Mission &&
			       Weapon == _other.Weapon &&
			       Rank == _other.Rank;
		}

		public override bool Equals(object _obj)
		{
			return _obj is CacheKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = ImmediateThreat ? 1 : 0;
				hash = (hash * 397) ^ (int)State;
				hash = (hash * 397) ^ UnitQx;
				hash = (hash * 397) ^ UnitQz;
				hash = (hash * 397) ^ GeometryVersion;
				hash = (hash * 397) ^ CandidateFingerprint;
				hash = (hash * 397) ^ OccupyingId;
				return hash;
			}
		}
	}
	#endregion

	#region Constants
	private const float c_QuantizeMeters = 0.75f;
	private const float c_TravelEpsilon = 0.05f;
	private const float c_ScoreEpsilon = 0.0001f;
	#endregion

	#region Private Fields
	private readonly List<CoverEmergencyEvaluation> m_Scratch = new List<CoverEmergencyEvaluation>(16);
	private CacheKey m_CachedKey;
	private EmergencyCoverDecision m_Last;
	private bool m_HasCache;
	private int m_LastLoggedResult = int.MinValue;
	private int m_LastLoggedId = int.MinValue;
	private int m_LastLoggedActive = int.MinValue;
	#endregion

	#region Public Properties
	public EmergencyCoverDecision Last => m_Last;
	#endregion

	#region Public Methods
	public void Invalidate()
	{
		m_HasCache = false;
	}

	/// <summary>
	/// ImmediateThreat overlay. Search stays Search; Retreat/Flee do not take emergency cover.
	/// </summary>
	public static bool AllowsOverlay(UnitAIState _state)
	{
		return _state != UnitAIState.Retreat &&
		       _state != UnitAIState.Flee;
	}

	public bool CanReuse(
		bool _immediateThreat,
		UnitAIState _state,
		in CoverSituation _situation,
		CoverCandidate _occupying,
		IReadOnlyList<CoverCandidate> _candidates)
	{
		if (!m_HasCache || !_immediateThreat || !AllowsOverlay(_state))
			return false;
		return BuildKey(_immediateThreat, _state, in _situation, _occupying, _candidates).Equals(m_CachedKey);
	}

	public bool ShouldQueryCandidates(
		bool _immediateThreat,
		UnitAIState _state,
		in CoverSituation _situation,
		CoverCandidate _occupying,
		IReadOnlyList<CoverCandidate> _lastCandidates,
		ICoverLineOfSightProbe _los)
	{
		if (!_immediateThreat || !AllowsOverlay(_state))
			return false;
		if (CoverEmergencyScoreMath.IsCurrentSufficient(_occupying, in _situation, _los))
			return false;
		if (CanReuse(_immediateThreat, _state, in _situation, _occupying, _lastCandidates))
			return false;
		return true;
	}

	public EmergencyCoverDecision Decide(
		bool _immediateThreat,
		UnitAIState _state,
		in CoverSituation _situation,
		IReadOnlyList<CoverCandidate> _candidates,
		CoverCandidate _occupying,
		ICoverLineOfSightProbe _los = null,
		CoverOccupancyBoard _occupancy = null)
	{
		if (!_immediateThreat || !AllowsOverlay(_state))
			return DeactivateKeepingDestination();

		CacheKey key = BuildKey(true, _state, in _situation, _occupying, _candidates);
		if (m_HasCache && key.Equals(m_CachedKey))
		{
			EmergencyCoverDecision cached = m_Last;
			cached.FromCache = true;
			m_Last = cached;
			return cached;
		}

		EmergencyCoverDecision decision;
		if (CoverEmergencyScoreMath.IsCurrentSufficient(_occupying, in _situation, _los))
			decision = Stay(_occupying, in _situation, _los);
		else
			decision = Pick(_candidates, in _situation, _los, _occupancy);

		decision.FromCache = false;
		m_CachedKey = key;
		m_Last = decision;
		m_HasCache = true;
		return decision;
	}

	public bool ShouldLog(in EmergencyCoverDecision _decision)
	{
		int result = (int)_decision.Result;
		int id = _decision.SelectedCandidateId;
		int active = _decision.Active ? 1 : 0;
		if (result == m_LastLoggedResult && id == m_LastLoggedId && active == m_LastLoggedActive)
			return false;
		m_LastLoggedResult = result;
		m_LastLoggedId = id;
		m_LastLoggedActive = active;
		return true;
	}
	#endregion

	#region Private Methods
	private EmergencyCoverDecision DeactivateKeepingDestination()
	{
		EmergencyCoverDecision last = m_Last;
		last.Active = false;
		last.FromCache = false;
		m_Last = last;
		m_HasCache = false;
		return last;
	}

	private EmergencyCoverDecision Stay(
		CoverCandidate _occupying,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los)
	{
		CoverEmergencyEvaluation evaluation = CoverEmergencyScoreMath.Evaluate(_occupying, in _situation, _los);
		return new EmergencyCoverDecision
		{
			Active = true,
			Result = EmergencyCoverResult.Stay,
			Reason = EmergencyCoverReason.CurrentCoverSufficient,
			Destination = _occupying != null ? _occupying.Position : _situation.UnitPosition,
			HasDestination = false,
			SelectedCandidateId = _occupying != null ? _occupying.CandidateId : 0,
			Selected = _occupying,
			SelectedScore = evaluation.Score,
			Evaluations = new[] { evaluation }
		};
	}

	private EmergencyCoverDecision Pick(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los,
		CoverOccupancyBoard _occupancy)
	{
		m_Scratch.Clear();
		CoverEmergencyEvaluation bestAcceptable = default;
		CoverEmergencyEvaluation bestFallback = default;
		bool hasAcceptable = false;
		bool hasFallback = false;
		if (_candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				CoverCandidate candidate = _candidates[i];
				CoverEmergencyEvaluation evaluation = CoverEmergencyScoreMath.Evaluate(
					candidate, in _situation, _los);
				m_Scratch.Add(evaluation);
				if (_occupancy != null &&
				    !_occupancy.IsUsable(candidate, _situation.UnitId, Time.time))
					continue;
				if (evaluation.Acceptable && IsCloserAcceptable(evaluation, bestAcceptable, hasAcceptable))
				{
					bestAcceptable = evaluation;
					hasAcceptable = true;
				}

				if (evaluation.Valid && IsBetterFallback(evaluation, bestFallback, hasFallback))
				{
					bestFallback = evaluation;
					hasFallback = true;
				}
			}
		}

		var copy = new CoverEmergencyEvaluation[m_Scratch.Count];
		m_Scratch.CopyTo(copy);
		if (hasAcceptable)
			return Selected(bestAcceptable, copy, EmergencyCoverResult.Selected, EmergencyCoverReason.ImmediateThreat);
		if (hasFallback)
			return Selected(
				bestFallback,
				copy,
				EmergencyCoverResult.Fallback,
				EmergencyCoverReason.NoAcceptableCandidate);
		return new EmergencyCoverDecision
		{
			Active = true,
			Result = EmergencyCoverResult.None,
			Reason = EmergencyCoverReason.NoCandidates,
			HasDestination = false,
			SelectedCandidateId = 0,
			Evaluations = copy
		};
	}

	private static EmergencyCoverDecision Selected(
		in CoverEmergencyEvaluation _evaluation,
		CoverEmergencyEvaluation[] _copy,
		EmergencyCoverResult _result,
		EmergencyCoverReason _reason)
	{
		CoverCandidate candidate = _evaluation.Candidate;
		return new EmergencyCoverDecision
		{
			Active = true,
			Result = _result,
			Reason = _reason,
			Destination = candidate != null ? candidate.Position : Vector3.zero,
			HasDestination = candidate != null,
			SelectedCandidateId = candidate != null ? candidate.CandidateId : 0,
			Selected = candidate,
			SelectedScore = _evaluation.Score,
			Evaluations = _copy
		};
	}

	private static bool IsCloserAcceptable(
		in CoverEmergencyEvaluation _candidate,
		in CoverEmergencyEvaluation _best,
		bool _hasBest)
	{
		if (!_hasBest)
			return true;
		if (_candidate.TravelMeters + c_TravelEpsilon < _best.TravelMeters)
			return true;
		if (_best.TravelMeters + c_TravelEpsilon < _candidate.TravelMeters)
			return false;
		int idA = _candidate.Candidate != null ? _candidate.Candidate.CandidateId : 0;
		int idB = _best.Candidate != null ? _best.Candidate.CandidateId : 0;
		return idA < idB;
	}

	private static bool IsBetterFallback(
		in CoverEmergencyEvaluation _candidate,
		in CoverEmergencyEvaluation _best,
		bool _hasBest)
	{
		if (!_hasBest)
			return true;
		if (_candidate.Score > _best.Score + c_ScoreEpsilon)
			return true;
		if (_candidate.Score < _best.Score - c_ScoreEpsilon)
			return false;
		int idA = _candidate.Candidate != null ? _candidate.Candidate.CandidateId : 0;
		int idB = _best.Candidate != null ? _best.Candidate.CandidateId : 0;
		return idA < idB;
	}

	private static CacheKey BuildKey(
		bool _immediateThreat,
		UnitAIState _state,
		in CoverSituation _situation,
		CoverCandidate _occupying,
		IReadOnlyList<CoverCandidate> _candidates)
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

		return new CacheKey
		{
			ImmediateThreat = _immediateThreat,
			State = _state,
			UnitQx = Quantize(_situation.UnitPosition.x),
			UnitQz = Quantize(_situation.UnitPosition.z),
			GeometryVersion = version,
			OccupancyVersion = _situation.OccupancyVersion,
			CandidateFingerprint = fingerprint,
			OccupyingId = _occupying != null ? _occupying.CandidateId : 0,
			Stance = _situation.Stance,
			Mission = _situation.Mission,
			Weapon = _situation.Weapon,
			Rank = _situation.Rank
		};
	}

	private static int Quantize(float _meters)
	{
		return Mathf.RoundToInt(_meters / c_QuantizeMeters);
	}
	#endregion
}
