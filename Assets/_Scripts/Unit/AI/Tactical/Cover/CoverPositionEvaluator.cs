using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.3 individual cover evaluation. Shared geometry in, per-unit scores out. Not Fire. Not Move.
/// </summary>
public sealed class CoverPositionEvaluator
{
	#region Nested
	private struct CacheKey : IEquatable<CacheKey>
	{
		public CoverRegionId Region;
		public int GeometryVersion;
		public int CandidateFingerprint;
		public int UnitQx;
		public int UnitQz;
		public int TargetQx;
		public int TargetQz;
		public int HostileQx;
		public int HostileQz;
		public bool HasTarget;
		public bool HasThreatDirection;
		public int ThreatOctant;
		public int ThreatQualityBand;
		public int ThreatUncertaintyBand;
		public CoverStance Stance;
		public CoverMissionIntent Mission;
		public CoverWeaponClass Weapon;
		public CoverRankClass Rank;
		public int OccupancyVersion;

		public bool Equals(CacheKey _other)
		{
			return Region.Equals(_other.Region) &&
			       GeometryVersion == _other.GeometryVersion &&
			       OccupancyVersion == _other.OccupancyVersion &&
			       CandidateFingerprint == _other.CandidateFingerprint &&
			       UnitQx == _other.UnitQx &&
			       UnitQz == _other.UnitQz &&
			       TargetQx == _other.TargetQx &&
			       TargetQz == _other.TargetQz &&
			       HostileQx == _other.HostileQx &&
			       HostileQz == _other.HostileQz &&
			       HasTarget == _other.HasTarget &&
			       Stance == _other.Stance &&
			       Mission == _other.Mission &&
			       Weapon == _other.Weapon &&
			       Rank == _other.Rank &&
			       HasThreatDirection == _other.HasThreatDirection &&
			       ThreatOctant == _other.ThreatOctant &&
			       ThreatQualityBand == _other.ThreatQualityBand &&
			       ThreatUncertaintyBand == _other.ThreatUncertaintyBand;
		}

		public override bool Equals(object _obj)
		{
			return _obj is CacheKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = Region.GetHashCode();
				hash = (hash * 397) ^ GeometryVersion;
				hash = (hash * 397) ^ CandidateFingerprint;
				hash = (hash * 397) ^ UnitQx;
				hash = (hash * 397) ^ UnitQz;
				hash = (hash * 397) ^ (HasTarget ? TargetQx : 0);
				hash = (hash * 397) ^ (int)Weapon;
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
	private readonly List<CoverPositionEvaluation> m_Scratch = new List<CoverPositionEvaluation>(16);
	private CacheKey m_CachedKey;
	private CoverEvaluationResult m_CachedResult;
	private bool m_HasCache;
	private int m_EvaluateCount;
	private int m_CacheHitCount;
	private int m_CacheMissCount;
	private int m_LastSelectedId = int.MinValue;
	#endregion

	#region Public Properties
	public int EvaluateCount => m_EvaluateCount;
	public int CacheHitCount => m_CacheHitCount;
	public int CacheMissCount => m_CacheMissCount;
	#endregion

	#region Public Methods
	public void Invalidate()
	{
		m_HasCache = false;
		m_CachedResult = null;
		m_LastSelectedId = int.MinValue;
	}

	public CoverEvaluationResult Evaluate(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los = null,
		CoverOccupancyBoard _occupancy = null)
	{
		CacheKey key = BuildKey(_candidates, in _situation);
		if (m_HasCache && key.Equals(m_CachedKey) && m_CachedResult != null)
		{
			m_CacheHitCount++;
			m_CachedResult.FromCache = true;
			return m_CachedResult;
		}

		m_EvaluateCount++;
		m_CacheMissCount++;
		CoverEvaluationResult result = BuildResult(_candidates, in _situation, _los, _occupancy);
		result.FromCache = false;
		m_CachedKey = key;
		m_CachedResult = result;
		m_HasCache = true;
		LogIfNeeded(in _situation, result);
		return result;
	}
	#endregion

	#region Private Methods
	private CoverEvaluationResult BuildResult(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los,
		CoverOccupancyBoard _occupancy)
	{
		m_Scratch.Clear();
		CoverPositionEvaluation best = default;
		bool hasBest = false;
		if (_candidates != null)
		{
			for (int i = 0; i < _candidates.Count; i++)
			{
				CoverCandidate candidate = _candidates[i];
				CoverPositionEvaluation evaluation = CoverScoreMath.EvaluateOne(candidate, in _situation, _los);
				evaluation = ThreatDirectionCoverMath.Stamp(evaluation, in _situation);
				m_Scratch.Add(evaluation);
				if (!evaluation.Valid)
					continue;
				if (_occupancy != null &&
				    !_occupancy.IsUsable(candidate, _situation.UnitId, Time.time))
					continue;
				if (!hasBest || ThreatDirectionCoverMath.IsBetterPreference(evaluation, best))
				{
					best = evaluation;
					hasBest = true;
				}
			}
		}

		CoverCandidate currentPlaceholder = CoverScoreMath.CreateCurrentPlaceholder(in _situation);
		CoverPositionEvaluation occupying = default;
		bool occupyingFound = TryFindOccupying(_candidates, in _situation, _los, out occupying);
		CoverPositionEvaluation current = occupyingFound
			? occupying
			: ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(currentPlaceholder, in _situation, _los),
				in _situation);

		bool recommended = false;
		if (hasBest)
		{
			// Stay/Reposition gate stays on frozen CoverScore, not PreferenceScore.
			recommended = CoverSwitchMath.ShouldReposition(
				current.Score,
				best.Score,
				CoverSwitchMath.DefaultSwitchingCost);
			if (CoverScoreMath.IsAtCandidate(in _situation, best.Candidate))
				recommended = false;
		}

		var copy = new CoverPositionEvaluation[m_Scratch.Count];
		m_Scratch.CopyTo(copy);
		return new CoverEvaluationResult
		{
			Evaluations = copy,
			Best = best,
			HasBest = hasBest,
			Current = current,
			RepositionRecommended = recommended
		};
	}

	private static bool TryFindOccupying(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los,
		out CoverPositionEvaluation _evaluation)
	{
		_evaluation = default;
		if (_candidates == null)
			return false;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (!CoverScoreMath.IsAtCandidate(in _situation, candidate))
				continue;
			_evaluation = ThreatDirectionCoverMath.Stamp(
				CoverScoreMath.EvaluateOne(candidate, in _situation, _los),
				in _situation);
			return true;
		}

		return false;
	}

	private static CacheKey BuildKey(IReadOnlyList<CoverCandidate> _candidates, in CoverSituation _situation)
	{
		int fingerprint = 0;
		int version = _situation.GeometryVersion;
		CoverRegionId region = _situation.RegionId;
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
				region = candidate.RegionId;
			}

			fingerprint ^= _candidates.Count * 17;
		}

		return new CacheKey
		{
			Region = region,
			GeometryVersion = version,
			OccupancyVersion = _situation.OccupancyVersion,
			CandidateFingerprint = fingerprint,
			UnitQx = Quantize(_situation.UnitPosition.x),
			UnitQz = Quantize(_situation.UnitPosition.z),
			HasTarget = _situation.HasTarget,
			TargetQx = Quantize(_situation.TargetPosition.x),
			TargetQz = Quantize(_situation.TargetPosition.z),
			HostileQx = Quantize(_situation.HostileDirection.x),
			HostileQz = Quantize(_situation.HostileDirection.z),
			Stance = _situation.Stance,
			Mission = _situation.Mission,
			Weapon = _situation.Weapon,
			Rank = _situation.Rank,
			HasThreatDirection = _situation.HasThreatDirection,
			ThreatOctant = ThreatOctant(in _situation),
			ThreatQualityBand = ThreatDirectionMath.ConsumerQualityBand(in _situation),
			ThreatUncertaintyBand = ThreatDirectionPositionMath.ConsumerUncertaintyBand(in _situation)
		};
	}

	private static int ThreatOctant(in CoverSituation _situation)
	{
		if (!_situation.HasThreatDirection)
			return -1;
		return (int)ThreatDirectionEstimator.CompassFrom(_situation.ThreatDirection);
	}

	private static int Quantize(float _meters)
	{
		return Mathf.RoundToInt(_meters / c_QuantizeMeters);
	}

	private void LogIfNeeded(in CoverSituation _situation, CoverEvaluationResult _result)
	{
		if (!UnitActionLog.Enabled || _result == null || !_result.HasBest || _result.Best.Candidate == null)
			return;

		int selectedId = _result.Best.Candidate.CandidateId;
		if (selectedId == m_LastSelectedId)
			return;
		m_LastSelectedId = selectedId;

		CoverScoreFactors f = _result.Best.Factors;
		string payload =
			"region=" + _situation.RegionId.LogLabel +
			" candidate=C" + selectedId +
			" score=" + UnitActionLog.F1(_result.Best.Score) +
			" protection=" + UnitActionLog.F1(f.Protection) +
			" visibility=" + UnitActionLog.F1(f.Visibility) +
			" fireLane=" + UnitActionLog.F1(f.FireLane) +
			" travel=-" + UnitActionLog.F1(f.TravelCost) +
			" danger=-" + UnitActionLog.F1(f.Danger) +
			" selected=1" +
			" reposition=" + (_result.RepositionRecommended ? "1" : "0");
		UnitActionLog.Timeline(UnitActionLog.PositionScore, payload);
		if (_situation.HasThreatDirection)
		{
			CoverPositionEvaluation best = _result.Best;
			ThreatDirectionPositionLog.Emit(null, ThreatDirectionPositionLog.Format(in best));
		}
	}
	#endregion
}
