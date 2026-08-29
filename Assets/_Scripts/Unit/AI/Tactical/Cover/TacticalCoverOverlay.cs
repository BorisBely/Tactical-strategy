using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-unit tactical Stay / Reposition overlay. Event-driven. Never Move. Never writes Attack Destination.
/// Skipped while ImmediateThreat (13.4) or Retreat / Flee / Search.
/// While a Reserved slot is still valid, current is that slot even if the unit is still approaching.
/// </summary>
public sealed class TacticalCoverOverlay
{
	#region Private Fields
	private readonly TacticalCoverSolver m_Solver = new TacticalCoverSolver();
	private readonly List<CoverCandidate> m_EvalScratch = new List<CoverCandidate>(16);
	private SharedCoverSpatialCache m_Cache;
	private CoverOccupancyBoard m_Occupancy;
	private IReadOnlyList<CoverCandidate> m_LastCandidates;
	private CoverCandidate m_HeldCandidate;
	private string m_LastCoverDirectionPayload = string.Empty;
	private string m_LastTacticalPositionPayload = string.Empty;
	#endregion

	#region Public Properties
	public TacticalCoverDecision Last => m_Solver.Last;
	public TacticalCoverSolver Solver => m_Solver;
	public IReadOnlyList<CoverCandidate> LastCandidates => m_LastCandidates;
	public SharedCoverSpatialCache Cache => m_Cache;
	public CoverOccupancyBoard Occupancy => m_Occupancy;
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

	public TacticalCoverDecision Update(
		bool _immediateThreat,
		UnitAIState _state,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los = null,
		Component _logActor = null,
		float? _costOverride = null)
	{
		if (!TacticalCoverSolver.AllowsTactical(_state, _immediateThreat))
			return m_Solver.Last;

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

		CoverCandidate occupying = EmergencyCoverOverlay.FindOccupying(m_LastCandidates, in situation);
		CurrentTacticalPosition current = ResolveCurrent(in situation, occupying);
		if (!m_Solver.NeedsReevaluation(in situation, in current, m_LastCandidates, _los))
		{
			TacticalCoverDecision cached = m_Solver.Last;
			cached.FromCache = true;
			return cached;
		}

		if (m_Cache != null)
		{
			CopyCandidates(m_Cache.GetCandidates(situation.UnitPosition));
			occupying = EmergencyCoverOverlay.FindOccupying(m_LastCandidates, in situation);
			current = ResolveCurrent(in situation, occupying);
		}

		EnsureHeldInEvalList(in situation);
		TacticalCoverDecision decision = m_Solver.Decide(
			in situation, m_LastCandidates, in current, _los, _costOverride, m_Occupancy);
		if (m_Occupancy != null && decision.HasDestination && decision.Selected != null)
		{
			if (AllowsReserveSwap(in situation, in decision))
			{
				CoverReserveOutcome reserved = m_Occupancy.TryReserve(
					decision.Selected, situation.UnitId, Time.time, _logActor);
				if (!reserved.Success)
				{
					m_Solver.Invalidate();
					decision = m_Solver.Decide(
						in situation, m_LastCandidates, in current, _los, _costOverride, m_Occupancy);
				}
				else
				{
					m_HeldCandidate = decision.Selected;
				}
			}
		}

		LogIfNeeded(_logActor, in situation, in decision);
		return decision;
	}
	#endregion

	#region Private Methods
	private CurrentTacticalPosition ResolveCurrent(in CoverSituation _situation, CoverCandidate _occupying)
	{
		if (_occupying != null)
			return CurrentTacticalPosition.FromCandidate(_occupying, true);

		CoverCandidate held = ResolveHeldCandidate(in _situation);
		if (held == null)
			return CurrentTacticalPosition.Invalid;

		bool occupied = false;
		if (m_Occupancy != null &&
		    m_Occupancy.TryGetHeld(_situation.UnitId, Time.time, out CoverReservation reservation))
			occupied = reservation.State == CoverOccupancy.Occupied;
		return CurrentTacticalPosition.FromCandidate(held, occupied);
	}

	private CoverCandidate ResolveHeldCandidate(in CoverSituation _situation)
	{
		if (m_Occupancy == null || _situation.UnitId == 0)
			return m_HeldCandidate;

		if (!m_Occupancy.TryGetHeld(_situation.UnitId, Time.time, out CoverReservation held) ||
		    held.CandidateId == 0)
		{
			m_HeldCandidate = null;
			return null;
		}

		CoverCandidate fromList = FindById(m_LastCandidates, held.CandidateId);
		if (fromList != null)
		{
			m_HeldCandidate = fromList;
			return fromList;
		}

		if (m_HeldCandidate != null && m_HeldCandidate.CandidateId == held.CandidateId)
			return m_HeldCandidate;

		return null;
	}

	private bool AllowsReserveSwap(in CoverSituation _situation, in TacticalCoverDecision _decision)
	{
		if (m_Occupancy == null || _decision.Selected == null)
			return false;
		if (!TryGetHeldReservation(in _situation, out CoverReservation held))
			return true;
		if (held.CandidateId == 0 || held.CandidateId == _decision.Selected.CandidateId)
			return true;
		if (held.State != CoverOccupancy.Reserved)
			return true;
		return _decision.Reason == TacticalCoverReason.CurrentInvalid;
	}

	private bool TryGetHeldReservation(in CoverSituation _situation, out CoverReservation _held)
	{
		_held = default;
		return m_Occupancy != null &&
		       _situation.UnitId != 0 &&
		       m_Occupancy.TryGetHeld(_situation.UnitId, Time.time, out _held);
	}

	private void CopyCandidates(IReadOnlyList<CoverCandidate> _source)
	{
		m_EvalScratch.Clear();
		if (_source != null)
		{
			for (int i = 0; i < _source.Count; i++)
			{
				if (_source[i] != null)
					m_EvalScratch.Add(_source[i]);
			}
		}

		m_LastCandidates = m_EvalScratch;
	}

	private void EnsureHeldInEvalList(in CoverSituation _situation)
	{
		CoverCandidate held = ResolveHeldCandidate(in _situation);
		if (held == null)
			return;
		if (m_EvalScratch.Count == 0 && m_LastCandidates != null && !ReferenceEquals(m_LastCandidates, m_EvalScratch))
			CopyCandidates(m_LastCandidates);
		for (int i = 0; i < m_EvalScratch.Count; i++)
		{
			if (m_EvalScratch[i] != null && m_EvalScratch[i].CandidateId == held.CandidateId)
			{
				m_LastCandidates = m_EvalScratch;
				return;
			}
		}

		m_EvalScratch.Add(held);
		m_LastCandidates = m_EvalScratch;
	}

	private static CoverCandidate FindById(IReadOnlyList<CoverCandidate> _candidates, int _id)
	{
		if (_candidates == null || _id == 0)
			return null;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate != null && candidate.CandidateId == _id)
				return candidate;
		}

		return null;
	}

	private void LogIfNeeded(
		Component _actor,
		in CoverSituation _situation,
		in TacticalCoverDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;

		if (m_Solver.ShouldLog(in _decision))
		{
			CoverDiagnosticLog.Decision(_actor, in _decision);
			if (_decision.Reason == TacticalCoverReason.CurrentInvalid ||
			    _decision.Reason == TacticalCoverReason.CurrentDegraded)
			{
				CoverDiagnosticLog.Invalid(
					_actor,
					_decision.CurrentCandidateId != 0
						? _decision.CurrentCandidateId
						: _decision.SelectedCandidateId,
					ClassifyInvalid(in _situation, in _decision));
			}
		}

		TryLogCoverDirection(_actor, in _situation, in _decision);
		TryLogTacticalPosition(_actor, in _situation, in _decision);
	}

	private void TryLogCoverDirection(
		Component _actor,
		in CoverSituation _situation,
		in TacticalCoverDecision _decision)
	{
		if (!_situation.HasThreatDirection || _decision.SelectedCandidateId == 0)
			return;

		CoverPositionEvaluation selected = default;
		if (_decision.Evaluations != null)
		{
			for (int i = 0; i < _decision.Evaluations.Count; i++)
			{
				CoverPositionEvaluation evaluation = _decision.Evaluations[i];
				if (evaluation.Candidate == null ||
				    evaluation.Candidate.CandidateId != _decision.SelectedCandidateId)
					continue;
				selected = evaluation;
				break;
			}
		}

		var knowledge = new ThreatDirectionKnowledge(
			_situation.ThreatDirection,
			ThreatDirectionEstimator.CompassFrom(_situation.ThreatDirection),
			0f,
			0f,
			0f,
			_situation.ThreatSource,
			_situation.ThreatState);
		string payload = ThreatDirectionCoverLog.FormatCover(
			in knowledge,
			_decision.SelectedCandidateId,
			selected.ThreatDirectionAdjustment);
		if (payload == m_LastCoverDirectionPayload)
			return;

		m_LastCoverDirectionPayload = payload;
		ThreatDirectionCoverLog.EmitCover(_actor, payload);
	}

	private void TryLogTacticalPosition(
		Component _actor,
		in CoverSituation _situation,
		in TacticalCoverDecision _decision)
	{
		if (!_situation.HasThreatDirection || _decision.SelectedCandidateId == 0)
			return;

		CoverPositionEvaluation selected = default;
		if (_decision.Evaluations != null)
		{
			for (int i = 0; i < _decision.Evaluations.Count; i++)
			{
				CoverPositionEvaluation evaluation = _decision.Evaluations[i];
				if (evaluation.Candidate == null ||
				    evaluation.Candidate.CandidateId != _decision.SelectedCandidateId)
					continue;
				selected = evaluation;
				break;
			}
		}

		string payload = ThreatDirectionPositionLog.Format(in selected);
		if (payload == m_LastTacticalPositionPayload)
			return;

		m_LastTacticalPositionPayload = payload;
		ThreatDirectionPositionLog.Emit(_actor, payload);
	}

	private string ClassifyInvalid(in CoverSituation _situation, in TacticalCoverDecision _decision)
	{
		if (_decision.Reason == TacticalCoverReason.CurrentDegraded)
			return "ExposureChanged";

		CoverCandidate current = FindById(m_LastCandidates, _decision.CurrentCandidateId) ?? m_HeldCandidate;
		bool hasHold = TryGetHeldReservation(in _situation, out CoverReservation held) &&
		               held.CandidateId != 0;
		if (!hasHold)
		{
			if (current != null || m_HeldCandidate != null || _decision.CurrentCandidateId != 0)
				return "ReservationLost";
			return "CandidateMissing";
		}

		if (current == null)
			return "CandidateMissing";
		if (!current.NavMeshValid)
			return "PathInvalid";
		bool occupied = held.State == CoverOccupancy.Occupied || _decision.Current.Occupied;
		if (occupied && !CoverScoreMath.IsAtCandidate(in _situation, current))
			return "TooFar";
		return "CandidateMissing";
	}
	#endregion
}
