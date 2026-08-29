using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14.9 when a unit may run an expensive tactical operation. Does not choose routes.
/// Budgets are prototype, not freeze.
/// </summary>
public sealed class TacticalUpdateScheduler
{
	#region Constants
	public const int DefaultMaxRouteEvaluationsPerTick = 20;
	public const int DefaultMaxExposureEvaluationsPerTick = 20;
	public const int DefaultMaxCoverQueriesPerTick = 20;
	public const int DefaultStaggerSlots = 5;
	#endregion

	#region Nested
	private sealed class RequestComparer : IComparer<TacticalSchedulerRequest>
	{
		public static readonly RequestComparer Instance = new RequestComparer();

		public int Compare(TacticalSchedulerRequest _left, TacticalSchedulerRequest _right)
		{
			int rank = TacticalLodMath.ComparePriority(_left.Criticality, _right.Criticality);
			if (rank != 0)
				return rank;
			return _left.UnitId.CompareTo(_right.UnitId);
		}
	}
	#endregion

	#region Private Fields
	private static readonly TacticalUpdateScheduler s_Shared = new TacticalUpdateScheduler();
	private readonly List<TacticalSchedulerRequest> m_Queue = new List<TacticalSchedulerRequest>(128);
	private readonly List<TacticalSchedulerAdmission> m_Admitted = new List<TacticalSchedulerAdmission>(32);
	private readonly Dictionary<int, TacticalLodTier> m_Tiers = new Dictionary<int, TacticalLodTier>(128);
	private int m_Tick = -1;
	private float m_Now;
	private int m_RouteRemaining;
	private int m_ExposureRemaining;
	private int m_CoverRemaining;
	private int m_FullCount;
	private int m_ReducedCount;
	private int m_BackgroundCount;
	private bool m_LoggedTick;
	#endregion

	#region Public Properties
	public static TacticalUpdateScheduler Shared => s_Shared;

	public int MaxRouteEvaluationsPerTick { get; set; } = DefaultMaxRouteEvaluationsPerTick;
	public int MaxExposureEvaluationsPerTick { get; set; } = DefaultMaxExposureEvaluationsPerTick;
	public int MaxCoverQueriesPerTick { get; set; } = DefaultMaxCoverQueriesPerTick;
	public int StaggerSlots { get; set; } = DefaultStaggerSlots;
	public int TickIndex => m_Tick;
	public float Now => m_Now;
	public int RouteBudgetRemaining => m_RouteRemaining;
	public int ExposureBudgetRemaining => m_ExposureRemaining;
	public int CoverBudgetRemaining => m_CoverRemaining;
	public int FullCount => m_FullCount;
	public int ReducedCount => m_ReducedCount;
	public int BackgroundCount => m_BackgroundCount;
	public int AdmittedCount => m_Admitted.Count;
	public IReadOnlyList<TacticalSchedulerAdmission> Admitted => m_Admitted;
	public int RouteBudgetUsed => Mathf.Max(0, MaxRouteEvaluationsPerTick - m_RouteRemaining);
	#endregion

	#region Public Methods
	public static void ResetShared()
	{
		s_Shared.Reset();
	}

	public void Reset()
	{
		m_Tick = -1;
		m_Now = 0f;
		m_RouteRemaining = MaxRouteEvaluationsPerTick;
		m_ExposureRemaining = MaxExposureEvaluationsPerTick;
		m_CoverRemaining = MaxCoverQueriesPerTick;
		m_Queue.Clear();
		m_Admitted.Clear();
		m_Tiers.Clear();
		m_FullCount = 0;
		m_ReducedCount = 0;
		m_BackgroundCount = 0;
		m_LoggedTick = false;
	}

	public void BeginTick(int _tick, float _now)
	{
		if (_tick == m_Tick)
			return;
		m_Tick = _tick;
		m_Now = _now;
		m_RouteRemaining = Mathf.Max(0, MaxRouteEvaluationsPerTick);
		m_ExposureRemaining = Mathf.Max(0, MaxExposureEvaluationsPerTick);
		m_CoverRemaining = Mathf.Max(0, MaxCoverQueriesPerTick);
		m_Queue.Clear();
		m_Admitted.Clear();
		m_Tiers.Clear();
		m_FullCount = 0;
		m_ReducedCount = 0;
		m_BackgroundCount = 0;
		m_LoggedTick = false;
	}

	public void EnsureTick(float _now)
	{
		if (m_Tick >= 0)
			return;
		BeginTick(0, _now);
	}

	public void ReportTier(int _unitId, TacticalLodTier _tier)
	{
		int id = _unitId != 0 ? _unitId : -1;
		if (m_Tiers.TryGetValue(id, out TacticalLodTier previous))
		{
			if (previous == _tier)
				return;
			AddTierCount(previous, -1);
		}

		m_Tiers[id] = _tier;
		AddTierCount(_tier, 1);
	}

	public void Enqueue(int _unitId, TacticalLodOperation _operation, TacticalCriticality _criticality)
	{
		m_Queue.Add(new TacticalSchedulerRequest
		{
			UnitId = _unitId,
			Operation = _operation,
			Criticality = _criticality
		});
	}

	public int Dispatch()
	{
		if (m_Queue.Count > 1)
			m_Queue.Sort(RequestComparer.Instance);
		int granted = 0;
		for (int i = 0; i < m_Queue.Count; i++)
		{
			TacticalSchedulerRequest request = m_Queue[i];
			if (TryAdmit(request.UnitId, request.Operation, request.Criticality))
				granted++;
		}

		m_Queue.Clear();
		return granted;
	}

	public bool TryAdmit(int _unitId, TacticalLodOperation _operation, TacticalCriticality _criticality)
	{
		EnsureTick(m_Now);
		bool emergency = _criticality == TacticalCriticality.Emergency;
		if (!emergency && !PassesStagger(_unitId, _criticality))
			return false;
		if (!emergency && !HasBudget(_operation))
			return false;
		Consume(_operation, emergency);
		m_Admitted.Add(new TacticalSchedulerAdmission
		{
			UnitId = _unitId,
			Operation = _operation,
			Criticality = _criticality,
			Tick = m_Tick
		});
		return true;
	}

	public string FormatLog()
	{
		return "budget=" + MaxRouteEvaluationsPerTick +
		       " used=" + RouteBudgetUsed +
		       " full=" + m_FullCount +
		       " reduced=" + m_ReducedCount +
		       " background=" + m_BackgroundCount;
	}

	public void LogIfEnabled(Component _actor)
	{
		if (!UnitActionLog.Enabled || m_LoggedTick)
			return;
		m_LoggedTick = true;
		string payload = FormatLog();
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.TacticalScheduler, payload);
		UnitActionLog.Timeline(UnitActionLog.TacticalScheduler, payload);
	}
	#endregion

	#region Private Methods
	private bool PassesStagger(int _unitId, TacticalCriticality _criticality)
	{
		if (_criticality >= TacticalCriticality.High)
			return true;
		int slots = Mathf.Max(1, StaggerSlots);
		if (slots <= 1)
			return true;
		int unit = _unitId < 0 ? 0 : _unitId;
		int tick = m_Tick < 0 ? 0 : m_Tick;
		return unit % slots == tick % slots;
	}

	private bool HasBudget(TacticalLodOperation _operation)
	{
		switch (_operation)
		{
			case TacticalLodOperation.Exposure:
				return m_ExposureRemaining > 0;
			case TacticalLodOperation.CoverEvaluation:
				return m_CoverRemaining > 0;
			default:
				return m_RouteRemaining > 0;
		}
	}

	private void Consume(TacticalLodOperation _operation, bool _emergency)
	{
		switch (_operation)
		{
			case TacticalLodOperation.Exposure:
				if (m_ExposureRemaining > 0)
					m_ExposureRemaining--;
				else if (!_emergency)
					m_ExposureRemaining = 0;
				break;
			case TacticalLodOperation.CoverEvaluation:
				if (m_CoverRemaining > 0)
					m_CoverRemaining--;
				break;
			default:
				if (m_RouteRemaining > 0)
					m_RouteRemaining--;
				break;
		}
	}

	private void AddTierCount(TacticalLodTier _tier, int _delta)
	{
		switch (_tier)
		{
			case TacticalLodTier.Full:
				m_FullCount += _delta;
				break;
			case TacticalLodTier.Reduced:
				m_ReducedCount += _delta;
				break;
			case TacticalLodTier.Background:
				m_BackgroundCount += _delta;
				break;
		}
	}
	#endregion
}
