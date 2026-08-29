using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.1 evaluate then select. 14.2 may insert cover hops. 14.3 may bias wall corridors.
/// 14.4 fills an exposure profile. Viability first. Does not Move.
/// </summary>
public sealed class TacticalRouteEvaluator
{
	#region Nested
	private struct CacheKey : IEquatable<CacheKey>
	{
		public int OriginQx;
		public int OriginQz;
		public int DestQx;
		public int DestQz;
		public TacticalMovementMode Mode;
		public int Fingerprint;

		public bool Equals(CacheKey _other)
		{
			return OriginQx == _other.OriginQx &&
			       OriginQz == _other.OriginQz &&
			       DestQx == _other.DestQx &&
			       DestQz == _other.DestQz &&
			       Mode == _other.Mode &&
			       Fingerprint == _other.Fingerprint;
		}

		public override bool Equals(object _obj)
		{
			return _obj is CacheKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = OriginQx;
				hash = (hash * 397) ^ OriginQz;
				hash = (hash * 397) ^ DestQx;
				hash = (hash * 397) ^ DestQz;
				hash = (hash * 397) ^ (int)Mode;
				return hash ^ Fingerprint;
			}
		}
	}
	#endregion

	#region Private Fields
	private readonly TacticalCoverToCoverPlanner m_CoverPlanner = new TacticalCoverToCoverPlanner();
	private readonly List<Vector3> m_HintScratch = new List<Vector3>(8);
	private readonly List<TacticalRouteCandidate> m_Generated = new List<TacticalRouteCandidate>(8);
	private readonly List<TacticalRouteEvaluation> m_Evaluations = new List<TacticalRouteEvaluation>(8);
	private readonly List<TacticalWallAnchor> m_UrbanAnchors = new List<TacticalWallAnchor>(16);
	private TacticalUrbanGeometryContext m_UrbanContext;
	private CacheKey m_CachedKey;
	private TacticalRouteDecision m_Last;
	private bool m_HasCache;
	private int m_EvaluationCount;
	private int m_GenerationCount;
	private int m_CacheHitCount;
	private int m_ExposureFillCount;
	private ITacticalRoutePathProbe m_Probe;
	private int m_MaxRouteCandidates = TacticalRouteGenerator.DefaultMaxRouteCandidates;
	private float m_DiversityMeters = TacticalRouteGenerator.DefaultDiversityMeters;
	private float m_OffsetMeters = TacticalRouteGenerator.DefaultOffsetMeters;
	#endregion

	#region Public Properties
	public TacticalRouteDecision Last => m_Last;
	public TacticalCoverToCoverPlanner CoverPlanner => m_CoverPlanner;
	public TacticalUrbanGeometryContext LastUrbanContext => m_UrbanContext;
	public int EvaluationCount => m_EvaluationCount;
	public int GenerationCount => m_GenerationCount;
	public int CacheHitCount => m_CacheHitCount;
	public int ExposureFillCount => m_ExposureFillCount;
	public int MaxRouteCandidates
	{
		get => m_MaxRouteCandidates;
		set => m_MaxRouteCandidates = Mathf.Max(1, value);
	}

	public float DiversityMeters
	{
		get => m_DiversityMeters;
		set => m_DiversityMeters = Mathf.Max(0.25f, value);
	}
	#endregion

	#region Public Methods
	public void BindProbe(ITacticalRoutePathProbe _probe)
	{
		m_Probe = _probe;
		Invalidate();
	}

	public void Invalidate()
	{
		m_HasCache = false;
	}

	public static TacticalRouteSituation FromGoal(in TacticalMovementGoal _goal)
	{
		return new TacticalRouteSituation
		{
			Origin = _goal.Origin,
			Destination = _goal.Destination,
			HasDestination = _goal.HasDestination,
			Mode = _goal.Context.Mode,
			WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed,
			Now = _goal.Now
		};
	}

	public TacticalRouteDecision Evaluate(in TacticalMovementGoal _goal, Component _logActor = null)
	{
		return Evaluate(FromGoal(in _goal), null, _logActor);
	}

	public TacticalRouteDecision Evaluate(
		in TacticalRouteSituation _situation,
		IReadOnlyList<TacticalRouteCandidate> _authored,
		Component _logActor = null)
	{
		if (!_situation.HasDestination ||
		    !TacticalRouteViability.IsFinitePoint(_situation.Destination))
		{
			m_Last = default;
			m_HasCache = false;
			return m_Last;
		}

		int fingerprint = Fingerprint(_authored) ^ SituationFingerprint(in _situation);
		CacheKey key = BuildKey(in _situation, fingerprint);
		if (m_HasCache && key.Equals(m_CachedKey))
		{
			m_CacheHitCount++;
			TacticalRouteDecision cached = m_Last;
			cached.FromCache = true;
			m_Last = cached;
			return cached;
		}

		m_EvaluationCount++;
		m_Generated.Clear();
		m_UrbanAnchors.Clear();
		m_UrbanContext = default;
		bool usedCoverPlanner = false;
		if (_authored != null && _authored.Count > 0)
		{
			for (int i = 0; i < _authored.Count; i++)
			{
				if (_authored[i] != null)
					m_Generated.Add(_authored[i]);
			}

			TacticalRouteGenerator.CapAndDedup(m_Generated, m_MaxRouteCandidates, m_DiversityMeters);
		}
		else
		{
			m_GenerationCount++;
			usedCoverPlanner = m_CoverPlanner.TryGenerate(in _situation, m_Generated, m_Probe, _logActor);
			if (!usedCoverPlanner)
			{
				TacticalRouteGenerator.Generate(
					in _situation, m_Generated, m_MaxRouteCandidates, m_DiversityMeters, m_OffsetMeters);
			}
			else
			{
				int cap = Mathf.Max(m_MaxRouteCandidates, m_CoverPlanner.MaxRouteEvaluations);
				TacticalRouteGenerator.CapAndDedup(m_Generated, cap, m_DiversityMeters);
			}
		}

		TacticalUrbanWallMath.CollectAnchors(in _situation, m_UrbanAnchors);
		m_UrbanContext = TacticalUrbanWallMath.BuildContext(m_UrbanAnchors);
		if (!usedCoverPlanner &&
		    _authored == null &&
		    _situation.Mode == TacticalMovementMode.Tactical &&
		    m_UrbanContext.Present)
		{
			TacticalUrbanRouteGenerator.AppendCorridorCandidates(
				in _situation,
				m_UrbanAnchors,
				m_Generated,
				m_MaxRouteCandidates + 2,
				m_DiversityMeters);
		}

		TacticalRouteSituation scored = _situation;
		ApplyCoverHints(ref scored);

		m_Evaluations.Clear();
		TacticalRouteEvaluation best = default;
		bool hasBest = false;
		int viable = 0;
		for (int i = 0; i < m_Generated.Count; i++)
		{
			TacticalRouteCandidate candidate = m_Generated[i];
			TacticalRouteRejectReason reject = TacticalRouteViability.Classify(
				candidate, in _situation, m_Probe);
			var evaluation = new TacticalRouteEvaluation
			{
				Candidate = candidate,
				Viable = reject == TacticalRouteRejectReason.None,
				RejectReason = reject
			};
			candidate.Viable = evaluation.Viable;
			candidate.RejectReason = reject;
			if (evaluation.Viable)
			{
				TacticalRouteScoreMath.FillComputedMetrics(candidate, in scored);
				TacticalExposureTraversalMath.Fill(
					candidate, in scored, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
				m_ExposureFillCount++;
				ApplyUrbanSample(candidate);
				evaluation.Factors = TacticalRouteScoreMath.EvaluateFactors(
					candidate, scored.Mode, m_UrbanContext.Present);
				evaluation.Score = evaluation.Factors.Total;
				viable++;
				if (TacticalRouteScoreMath.IsBetter(in evaluation, in best, hasBest))
				{
					best = evaluation;
					hasBest = true;
				}
			}

			m_Evaluations.Add(evaluation);
			LogCandidate(_logActor, in evaluation, in m_UrbanContext);
		}

		var copy = new TacticalRouteEvaluation[m_Evaluations.Count];
		m_Evaluations.CopyTo(copy);
		TacticalRouteSelectReason reason = TacticalRouteSelectReason.None;
		if (hasBest)
		{
			reason = viable == 1
				? TacticalRouteSelectReason.OnlyViable
				: TacticalRouteSelectReason.HighestScore;
			if (best.Candidate != null && best.Candidate.Kind == TacticalRouteKind.Direct && viable == 1)
				reason = TacticalRouteSelectReason.DirectBaseline;
			else if (viable > 1)
			{
				for (int i = 0; i < m_Evaluations.Count; i++)
				{
					if (!m_Evaluations[i].Viable || m_Evaluations[i].Candidate == best.Candidate)
						continue;
					reason = TacticalRouteScoreMath.TieReason(in best, m_Evaluations[i]);
					break;
				}
			}
		}

		m_Last = new TacticalRouteDecision
		{
			HasSelection = hasBest,
			Selected = best,
			Reason = reason,
			CandidateCount = copy.Length,
			ViableCount = viable,
			FromCache = false,
			Evaluations = copy
		};
		m_CachedKey = key;
		m_HasCache = true;
		LogSelect(_logActor, in _situation, in m_Last);
		return m_Last;
	}
	#endregion

	#region Private Methods
	private static CacheKey BuildKey(in TacticalRouteSituation _situation, int _fingerprint)
	{
		return new CacheKey
		{
			OriginQx = TacticalRouteMath.Quantize(_situation.Origin.x),
			OriginQz = TacticalRouteMath.Quantize(_situation.Origin.z),
			DestQx = TacticalRouteMath.Quantize(_situation.Destination.x),
			DestQz = TacticalRouteMath.Quantize(_situation.Destination.z),
			Mode = _situation.Mode,
			Fingerprint = _fingerprint
		};
	}

	private static int Fingerprint(IReadOnlyList<TacticalRouteCandidate> _authored)
	{
		if (_authored == null)
			return 0;
		int hash = _authored.Count * 17;
		for (int i = 0; i < _authored.Count; i++)
		{
			TacticalRouteCandidate candidate = _authored[i];
			if (candidate == null)
				continue;
			hash = (hash * 397) ^ candidate.CandidateId;
			hash = (hash * 397) ^ (int)(candidate.DistanceMeters * 10f);
			if (candidate.Intermediates != null && candidate.Intermediates.Count > 0)
			{
				hash = (hash * 397) ^ TacticalRouteMath.Quantize(candidate.Intermediates[0].Position.x);
				hash = (hash * 397) ^ TacticalRouteMath.Quantize(candidate.Intermediates[0].Position.z);
			}
		}

		return hash;
	}

	private static int SituationFingerprint(in TacticalRouteSituation _situation)
	{
		int hash = _situation.HasKnownThreat ? 1 : 0;
		hash = (hash * 397) ^ (int)_situation.Mode;
		if (_situation.HasObjective)
		{
			hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.Objective.x);
			hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.Objective.z);
		}

		if (_situation.CoverHints == null || _situation.CoverHints.Count == 0)
		{
			if (_situation.CoverCandidates != null && _situation.CoverCandidates.Count > 0)
			{
				hash = (hash * 397) ^ _situation.CoverCandidates.Count;
				hash = (hash * 397) ^ _situation.CoverCandidates[0].CandidateId;
			}

			if (_situation.Occupancy != null)
				hash = (hash * 397) ^ _situation.Occupancy.OccupancyVersion;
			if (_situation.CoverCache != null)
				hash = (hash * 397) ^ _situation.CoverCache.GeometryVersion;
			hash = (hash * 397) ^ _situation.FinalCoverCandidateId;
			hash = HashWallAnchors(in _situation, hash);
			hash = HashHostiles(in _situation, hash);
			return HashVersions(in _situation, hash);
		}

		hash = (hash * 397) ^ _situation.CoverHints.Count;
		hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.CoverHints[0].x);
		hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.CoverHints[0].z);
		if (_situation.Occupancy != null)
			hash = (hash * 397) ^ _situation.Occupancy.OccupancyVersion;
		hash = HashWallAnchors(in _situation, hash);
		hash = HashHostiles(in _situation, hash);
		return HashVersions(in _situation, hash);
	}

	private static int HashVersions(in TacticalRouteSituation _situation, int _hash)
	{
		int hash = (_hash * 397) ^ _situation.GeometryVersion;
		return (hash * 397) ^ _situation.KnowledgeVersion;
	}

	private static int HashHostiles(in TacticalRouteSituation _situation, int _hash)
	{
		if (_situation.HostilePositions == null || _situation.HostilePositions.Count == 0)
			return _hash;
		int hash = (_hash * 397) ^ _situation.HostilePositions.Count;
		hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.HostilePositions[0].x);
		hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.HostilePositions[0].z);
		return hash;
	}

	private static int HashWallAnchors(in TacticalRouteSituation _situation, int _hash)
	{
		if (_situation.WallAnchors == null || _situation.WallAnchors.Count == 0)
			return _hash;
		int hash = (_hash * 397) ^ _situation.WallAnchors.Count;
		hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.WallAnchors[0].Position.x);
		hash = (hash * 397) ^ TacticalRouteMath.Quantize(_situation.WallAnchors[0].Position.z);
		return hash;
	}

	private void ApplyCoverHints(ref TacticalRouteSituation _situation)
	{
		if (_situation.CoverHints != null && _situation.CoverHints.Count > 0)
			return;
		m_HintScratch.Clear();
		IReadOnlyList<Vector3> plannerHints = m_CoverPlanner.LastCoverHints;
		if (plannerHints != null && plannerHints.Count > 0)
		{
			for (int i = 0; i < plannerHints.Count; i++)
				m_HintScratch.Add(plannerHints[i]);
		}
		else if (_situation.CoverCandidates != null)
		{
			for (int i = 0; i < _situation.CoverCandidates.Count; i++)
			{
				if (_situation.CoverCandidates[i] != null)
					m_HintScratch.Add(_situation.CoverCandidates[i].Position);
			}
		}

		if (m_HintScratch.Count > 0)
			_situation.CoverHints = m_HintScratch;
	}

	private void ApplyUrbanSample(TacticalRouteCandidate _candidate)
	{
		if (_candidate == null)
			return;
		if (_candidate.UseAuthoredMetrics)
		{
			if (_candidate.OpenExposure01 <= 0f && _candidate.WallProximity01 > 0f)
				_candidate.OpenExposure01 = 1f - _candidate.WallProximity01;
			return;
		}

		TacticalUrbanRouteSample sample = TacticalUrbanWallMath.SampleRoute(_candidate, m_UrbanAnchors);
		_candidate.WallProximity01 = sample.WallProximity01;
		_candidate.OpenExposure01 = sample.OpenExposure01;
	}

	private static void LogCandidate(
		Component _actor,
		in TacticalRouteEvaluation _evaluation,
		in TacticalUrbanGeometryContext _urban)
	{
		if (!UnitActionLog.Enabled)
			return;
		TacticalRouteCandidate candidate = _evaluation.Candidate;
		int id = candidate != null ? candidate.CandidateId : 0;
		string payload = _evaluation.Viable
			? "route=R" + id +
			  " distance=" + UnitActionLog.F1(candidate.DistanceMeters) +
			  " travel=" + UnitActionLog.F1(candidate.TravelTimeSeconds) +
			  " exposure=" + UnitActionLog.F2(candidate.Exposure01) +
			  " cover=" + UnitActionLog.F2(candidate.Cover01) +
			  " wallProximity=" + UnitActionLog.F2(candidate.WallProximity01) +
			  " openExposure=" + UnitActionLog.F2(candidate.OpenExposure01) +
			  " wallBias=" + UnitActionLog.F2(_evaluation.Factors.WallBias) +
			  " urban=" + (_urban.Present ? "1" : "0") +
			  " peak=" + UnitActionLog.F2(candidate.PeakExposure01) +
			  " timeAbove=" + UnitActionLog.F1(candidate.TimeAboveThresholdSeconds) +
			  " exposureCost=" + UnitActionLog.F1(candidate.ExposureCost) +
			  " mission=" + UnitActionLog.F2(candidate.MissionProgress01) +
			  " score=" + UnitActionLog.F1(_evaluation.Score)
			: "route=R" + id + " reject=" + _evaluation.RejectReason;
		if (_actor != null)
		{
			UnitActionLog.Write(_actor, UnitActionLog.RouteCandidate, payload);
			if (_evaluation.Viable)
			{
				UnitActionLog.Write(_actor, UnitActionLog.RouteScore, payload);
				LogExposureProfile(_actor, candidate);
			}
		}

		UnitActionLog.Timeline(UnitActionLog.RouteCandidate, payload);
		if (_evaluation.Viable)
		{
			UnitActionLog.Timeline(UnitActionLog.RouteScore, payload);
			if (candidate != null && candidate.ExposureSamples != null && candidate.ExposureSamples.Count > 0)
			{
				UnitActionLog.Timeline(
					UnitActionLog.ExposureProfile,
					"route=R" + id +
					" samples=" + candidate.ExposureSamples.Count +
					" average=" + UnitActionLog.F2(candidate.Exposure01) +
					" peak=" + UnitActionLog.F2(candidate.PeakExposure01) +
					" timeAbove=" + UnitActionLog.F2(candidate.TimeAboveThresholdSeconds));
			}
		}
	}

	private static void LogExposureProfile(Component _actor, TacticalRouteCandidate _candidate)
	{
		if (_candidate == null || _candidate.ExposureSamples == null || _candidate.ExposureSamples.Count == 0)
			return;
		UnitActionLog.Write(
			_actor,
			UnitActionLog.ExposureProfile,
			"route=R" + _candidate.CandidateId +
			" samples=" + _candidate.ExposureSamples.Count +
			" average=" + UnitActionLog.F2(_candidate.Exposure01) +
			" peak=" + UnitActionLog.F2(_candidate.PeakExposure01) +
			" timeAbove=" + UnitActionLog.F2(_candidate.TimeAboveThresholdSeconds));
	}

	private static void LogSelect(
		Component _actor,
		in TacticalRouteSituation _situation,
		in TacticalRouteDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		int id = _decision.HasSelection && _decision.Selected.Candidate != null
			? _decision.Selected.Candidate.CandidateId
			: 0;
		string query =
			"from=" + UnitActionLog.Vec(_situation.Origin) +
			" to=" + UnitActionLog.Vec(_situation.Destination) +
			" mode=" + _situation.Mode +
			" candidates=" + _decision.CandidateCount;
		string select =
			"route=R" + id +
			" reason=" + _decision.Reason +
			" score=" + UnitActionLog.F1(_decision.Selected.Score) +
			" viable=" + _decision.ViableCount;
		if (_actor != null)
		{
			UnitActionLog.Write(_actor, UnitActionLog.RouteQuery, query);
			UnitActionLog.Write(_actor, UnitActionLog.RouteSelect, select);
		}

		UnitActionLog.Timeline(UnitActionLog.RouteQuery, query);
		UnitActionLog.Timeline(UnitActionLog.RouteSelect, select);
	}
	#endregion
}
