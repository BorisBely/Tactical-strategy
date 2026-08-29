using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-unit ImmediateThreat cover overlay. Queries shared geometry only when the current position is insufficient.
/// Never writes Attack/Defense Destination. Never issues Move.
/// </summary>
public sealed class EmergencyCoverOverlay
{
	#region Private Fields
	private readonly EmergencyCoverSolver m_Solver = new EmergencyCoverSolver();
	private SharedCoverSpatialCache m_Cache;
	private CoverOccupancyBoard m_Occupancy;
	private IReadOnlyList<CoverCandidate> m_LastCandidates;
	#endregion

	#region Public Properties
	public EmergencyCoverDecision Last => m_Solver.Last;
	public SharedCoverSpatialCache Cache => m_Cache;
	public CoverOccupancyBoard Occupancy => m_Occupancy;
	public IReadOnlyList<CoverCandidate> LastCandidates => m_LastCandidates;
	public EmergencyCoverSolver Solver => m_Solver;
	#endregion

	#region Public Methods
	public void BindCache(SharedCoverSpatialCache _cache)
	{
		m_Cache = _cache;
	}

	public void BindOccupancy(CoverOccupancyBoard _occupancy)
	{
		m_Occupancy = _occupancy;
	}

	public static CoverCandidate FindOccupying(
		IReadOnlyList<CoverCandidate> _candidates,
		in CoverSituation _situation)
	{
		if (_candidates == null)
			return null;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (CoverScoreMath.IsAtCandidate(in _situation, candidate))
				return candidate;
		}

		return null;
	}

	public EmergencyCoverDecision Update(
		bool _immediateThreat,
		UnitAIState _state,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los = null,
		Component _logActor = null)
	{
		CoverSituation situation = _situation;
		if (m_Cache != null)
		{
			situation.GeometryVersion = m_Cache.GeometryVersion;
			situation.RegionId = m_Cache.RegionAt(situation.UnitPosition);
		}

		if (m_Occupancy != null)
		{
			m_Occupancy.NotifyGeometryVersion(situation.GeometryVersion, Time.time);
			m_Occupancy.Tick(Time.time);
			situation.OccupancyVersion = m_Occupancy.OccupancyVersion;
		}

		CoverCandidate occupying = FindOccupying(m_LastCandidates, in situation);
		if (m_Solver.ShouldQueryCandidates(
			    _immediateThreat, _state, in situation, occupying, m_LastCandidates, _los) &&
		    m_Cache != null)
		{
			m_LastCandidates = m_Cache.GetCandidates(situation.UnitPosition);
			occupying = FindOccupying(m_LastCandidates, in situation);
		}

		EmergencyCoverDecision decision = m_Solver.Decide(
			_immediateThreat, _state, in situation, m_LastCandidates, occupying, _los, m_Occupancy);
		if (m_Occupancy != null && decision.HasDestination && decision.Selected != null)
		{
			CoverReserveOutcome reserved = m_Occupancy.TryReserve(
				decision.Selected, situation.UnitId, Time.time, _logActor);
			if (!reserved.Success)
			{
				m_Solver.Invalidate();
				decision = m_Solver.Decide(
					_immediateThreat, _state, in situation, m_LastCandidates, occupying, _los, m_Occupancy);
			}
		}

		LogIfNeeded(_logActor, in situation, in decision);
		return decision;
	}
	#endregion

	#region Private Methods
	private void LogIfNeeded(
		Component _actor,
		in CoverSituation _situation,
		in EmergencyCoverDecision _decision)
	{
		if (!UnitActionLog.Enabled || !m_Solver.ShouldLog(in _decision))
			return;

		string payload =
			"result=" + _decision.Result +
			" reason=" + _decision.Reason +
			" candidate=C" + _decision.SelectedCandidateId +
			" dest=" + (_decision.HasDestination ? UnitActionLog.Vec(_decision.Destination) : "-") +
			" score=" + UnitActionLog.F1(_decision.SelectedScore) +
			" active=" + (_decision.Active ? "1" : "0") +
			" region=" + _situation.RegionId.LogLabel;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.EmergencyCover, payload);
		UnitActionLog.Timeline(
			UnitActionLog.EmergencyCover,
			(_actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty) + payload);
	}
	#endregion
}
