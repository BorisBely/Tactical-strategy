using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.2 cover-to-cover. Direct remains baseline. Does not Move. Combinations are bounded.
/// </summary>
public sealed class TacticalCoverToCoverPlanner
{
	#region Nested
	private struct RankedCover
	{
		public CoverCandidate Cover;
		public float Value;
	}
	#endregion

	#region Constants
	public const int DefaultMaxIntermediateCandidates = 6;
	public const int DefaultMaxTacticalHops = 3;
	public const int DefaultMaxRouteEvaluations = 6;
	public const float DirectAcceptableMeters = 8f;
	public const float DirectAcceptableExposure = 0.48f;
	#endregion

	#region Private Fields
	private readonly List<CoverCandidate> m_Collected = new List<CoverCandidate>(24);
	private readonly List<RankedCover> m_Useful = new List<RankedCover>(8);
	private readonly List<TacticalCoverFilterRejection> m_Rejections = new List<TacticalCoverFilterRejection>(16);
	private readonly List<Vector3> m_Hints = new List<Vector3>(8);
	private readonly List<TacticalRouteWaypoint> m_HopScratch = new List<TacticalRouteWaypoint>(4);
	private TacticalCoverPlanReason m_LastReason;
	private int m_LastEvaluationCount;
	private int m_MaxIntermediateCandidates = DefaultMaxIntermediateCandidates;
	private int m_MaxTacticalHops = DefaultMaxTacticalHops;
	private int m_MaxRouteEvaluations = DefaultMaxRouteEvaluations;
	#endregion

	#region Public Properties
	public TacticalCoverPlanReason LastReason => m_LastReason;
	public int LastEvaluationCount => m_LastEvaluationCount;
	public int LastUsefulCount => m_Useful.Count;
	public IReadOnlyList<TacticalCoverFilterRejection> LastRejections => m_Rejections;
	public IReadOnlyList<Vector3> LastCoverHints => m_Hints;

	public int MaxIntermediateCandidates
	{
		get => m_MaxIntermediateCandidates;
		set => m_MaxIntermediateCandidates = Mathf.Max(1, value);
	}

	public int MaxTacticalHops
	{
		get => m_MaxTacticalHops;
		set => m_MaxTacticalHops = Mathf.Max(1, value);
	}

	public int MaxRouteEvaluations
	{
		get => m_MaxRouteEvaluations;
		set => m_MaxRouteEvaluations = Mathf.Max(1, value);
	}

	public int MaxIntermediateHops => Mathf.Max(0, m_MaxTacticalHops - 1);
	#endregion

	#region Public Methods
	public static bool HasCoverSource(in TacticalRouteSituation _situation)
	{
		if (_situation.CoverCandidates != null && _situation.CoverCandidates.Count > 0)
			return true;
		return _situation.CoverCache != null;
	}

	public bool TryGenerate(
		in TacticalRouteSituation _situation,
		List<TacticalRouteCandidate> _destination,
		ITacticalRoutePathProbe _probe,
		Component _logActor = null)
	{
		m_LastReason = TacticalCoverPlanReason.None;
		m_LastEvaluationCount = 0;
		m_Rejections.Clear();
		m_Useful.Clear();
		m_Hints.Clear();
		if (_destination == null)
			return false;
		if (_situation.Mode != TacticalMovementMode.Tactical)
			return false;
		if (!HasCoverSource(in _situation))
			return false;

		Collect(in _situation);
		if (m_Collected.Count == 0)
			return false;

		_destination.Clear();
		var direct = new TacticalRouteCandidate();
		direct.SetDirect(1, _situation.Origin, _situation.Destination);
		TacticalRouteSituation open = _situation;
		open.CoverHints = null;
		TacticalRouteScoreMath.FillComputedMetrics(direct, in open);
		_destination.Add(direct);
		m_LastEvaluationCount = 1;
		LogPlan(_logActor, in _situation, direct.Exposure01, "direct");

		if (IsDirectAcceptable(direct))
		{
			m_LastReason = TacticalCoverPlanReason.DirectAcceptable;
			LogPlan(_logActor, in _situation, direct.Exposure01, "selected=Direct hops=1");
			return true;
		}

		FilterUseful(in _situation, _probe);
		if (m_Useful.Count == 0)
		{
			m_LastReason = TacticalCoverPlanReason.NoUsefulCover;
			return true;
		}

		for (int i = 0; i < m_Useful.Count; i++)
			m_Hints.Add(m_Useful[i].Cover.Position);

		int maxHops = MaxIntermediateHops;
		int cap = m_MaxRouteEvaluations;
		if (maxHops >= 1)
			AppendOneHopRoutes(in _situation, _destination, cap);
		if (maxHops >= 2 && _destination.Count < cap)
			AppendTwoHopRoutes(in _situation, _destination, cap);

		m_LastReason = _destination.Count > 1
			? TacticalCoverPlanReason.CoverChain
			: TacticalCoverPlanReason.DirectBaseline;
		m_LastEvaluationCount = _destination.Count;
		string selected = "candidates=" + _destination.Count + " useful=" + m_Useful.Count;
		LogPlan(_logActor, in _situation, direct.Exposure01, selected);
		return true;
	}
	#endregion

	#region Private Methods
	private void Collect(in TacticalRouteSituation _situation)
	{
		m_Collected.Clear();
		if (_situation.CoverCandidates != null)
		{
			for (int i = 0; i < _situation.CoverCandidates.Count; i++)
				AddUnique(_situation.CoverCandidates[i]);
		}

		if (_situation.CoverCache == null)
			return;
		AddRange(_situation.CoverCache.GetCandidates(_situation.Origin));
		AddRange(_situation.CoverCache.GetCandidates(_situation.Destination));
		AddRange(_situation.CoverCache.GetCandidates(
			Vector3.Lerp(_situation.Origin, _situation.Destination, 0.5f)));
	}

	private void AddRange(IReadOnlyList<CoverCandidate> _covers)
	{
		if (_covers == null)
			return;
		for (int i = 0; i < _covers.Count; i++)
			AddUnique(_covers[i]);
	}

	private void AddUnique(CoverCandidate _cover)
	{
		if (_cover == null)
			return;
		for (int i = 0; i < m_Collected.Count; i++)
		{
			if (m_Collected[i].CandidateId == _cover.CandidateId &&
			    m_Collected[i].RegionId.Equals(_cover.RegionId))
				return;
		}

		m_Collected.Add(_cover);
	}

	private void FilterUseful(in TacticalRouteSituation _situation, ITacticalRoutePathProbe _probe)
	{
		m_Useful.Clear();
		for (int i = 0; i < m_Collected.Count; i++)
		{
			CoverCandidate cover = m_Collected[i];
			TacticalCoverHopRejectReason reject = TacticalCoverToCoverFilter.Classify(
				cover, in _situation, _probe);
			if (reject != TacticalCoverHopRejectReason.None)
			{
				m_Rejections.Add(new TacticalCoverFilterRejection
				{
					CandidateId = cover.CandidateId,
					Position = cover.Position,
					Reason = reject
				});
				continue;
			}

			float value = TacticalCoverToCoverFilter.IntermediateValue(cover, in _situation);
			if (value < 0.15f)
			{
				m_Rejections.Add(new TacticalCoverFilterRejection
				{
					CandidateId = cover.CandidateId,
					Position = cover.Position,
					Reason = TacticalCoverHopRejectReason.NoExposureReduction
				});
				continue;
			}

			m_Useful.Add(new RankedCover { Cover = cover, Value = value });
		}

		m_Useful.Sort(CompareUseful);
		while (m_Useful.Count > m_MaxIntermediateCandidates)
		{
			RankedCover extra = m_Useful[m_Useful.Count - 1];
			m_Rejections.Add(new TacticalCoverFilterRejection
			{
				CandidateId = extra.Cover.CandidateId,
				Position = extra.Cover.Position,
				Reason = TacticalCoverHopRejectReason.Capped
			});
			m_Useful.RemoveAt(m_Useful.Count - 1);
		}
	}

	private static int CompareUseful(RankedCover _a, RankedCover _b)
	{
		int byValue = _b.Value.CompareTo(_a.Value);
		if (byValue != 0)
			return byValue;
		return _a.Cover.CandidateId.CompareTo(_b.Cover.CandidateId);
	}

	private void AppendOneHopRoutes(
		in TacticalRouteSituation _situation,
		List<TacticalRouteCandidate> _destination,
		int _cap)
	{
		for (int i = 0; i < m_Useful.Count && _destination.Count < _cap; i++)
		{
			CoverCandidate cover = m_Useful[i].Cover;
			m_HopScratch.Clear();
			m_HopScratch.Add(TacticalRouteWaypoint.CoverHop(
				cover.Position, cover.CandidateId, cover.RegionId));
			var route = new TacticalRouteCandidate();
			route.SetCoverHops(100 + cover.CandidateId, _situation.Origin, _situation.Destination, m_HopScratch);
			_destination.Add(route);
		}
	}

	private void AppendTwoHopRoutes(
		in TacticalRouteSituation _situation,
		List<TacticalRouteCandidate> _destination,
		int _cap)
	{
		int added = 0;
		for (int i = 0; i < m_Useful.Count && _destination.Count < _cap && added < 2; i++)
		{
			for (int j = i + 1; j < m_Useful.Count && _destination.Count < _cap && added < 2; j++)
			{
				CoverCandidate a = m_Useful[i].Cover;
				CoverCandidate b = m_Useful[j].Cover;
				if (!IsProgressOrder(a, b, in _situation))
				{
					CoverCandidate swap = a;
					a = b;
					b = swap;
					if (!IsProgressOrder(a, b, in _situation))
						continue;
				}

				m_HopScratch.Clear();
				m_HopScratch.Add(TacticalRouteWaypoint.CoverHop(a.Position, a.CandidateId, a.RegionId));
				m_HopScratch.Add(TacticalRouteWaypoint.CoverHop(b.Position, b.CandidateId, b.RegionId));
				var route = new TacticalRouteCandidate();
				route.SetCoverHops(
					1000 + a.CandidateId * 20 + b.CandidateId,
					_situation.Origin,
					_situation.Destination,
					m_HopScratch);
				_destination.Add(route);
				added++;
			}
		}
	}

	private static bool IsProgressOrder(
		CoverCandidate _first,
		CoverCandidate _second,
		in TacticalRouteSituation _situation)
	{
		Vector3 span = _situation.Destination - _situation.Origin;
		span.y = 0f;
		if (span.sqrMagnitude < 0.01f)
			return _first.CandidateId < _second.CandidateId;
		span.Normalize();
		float a = Vector3.Dot(_first.Position - _situation.Origin, span);
		float b = Vector3.Dot(_second.Position - _situation.Origin, span);
		return a + 0.5f < b;
	}

	private static bool IsDirectAcceptable(TacticalRouteCandidate _direct)
	{
		if (_direct == null)
			return true;
		if (_direct.DistanceMeters <= DirectAcceptableMeters &&
		    _direct.Exposure01 <= DirectAcceptableExposure)
			return true;
		return _direct.Exposure01 <= 0.32f;
	}

	private static void LogPlan(
		Component _actor,
		in TacticalRouteSituation _situation,
		float _directExposure,
		string _detail)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"start=" + UnitActionLog.Vec(_situation.Origin) +
			" target=" + UnitActionLog.Vec(_situation.Destination) +
			" directExposure=" + UnitActionLog.F2(_directExposure) +
			" " + _detail;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.RoutePlan, payload);
		UnitActionLog.Timeline(UnitActionLog.RoutePlan, payload);
	}
	#endregion
}
