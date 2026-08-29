using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// #14 movement overlay. Destination stays the goal. 14.1 evaluates. 14.2 may insert cover hops.
/// 14.3 may prefer wall corridors. 14.5 replans on events, not every frame.
/// 14.6 decides Continue / Replan / EmergencyCover / Hold under ImmediateThreat.
/// 14.7 validates navigation arrival vs tactical acquisition.
/// 14.8 decides when to lean while traversing. 14.9 schedules when that work runs.
/// Does not Move. Does not Fire. LOD does not change scores.
/// </summary>
public sealed class TacticalMovementOverlay
{
	#region Private Fields
	private readonly TacticalRoute m_Route = new TacticalRoute();
	private readonly TacticalRouteEvaluator m_Evaluator = new TacticalRouteEvaluator();
	private readonly List<CoverCandidate> m_CoverLookup = new List<CoverCandidate>(16);
	private readonly List<TacticalReplanEvent> m_Pending = new List<TacticalReplanEvent>(8);
	private TacticalMovementDecision m_Last;
	private CoverOccupancyBoard m_Occupancy;
	private int m_UnitId;
	private int m_FinalCoverId;
	private CoverRegionId m_FinalRegion;
	private bool m_NeedsReroute;
	private int m_ReservedCoverId;
	private CoverRegionId m_ReservedRegion;
	private CoverCandidate m_ReservedCandidate;
	private int m_LoggedApproachingCoverId;
	private bool m_Committed;
	private TacticalCommittedRoute m_Snap;
	private TacticalReplanCheck m_LastCheck;
	private TacticalReplanAction m_LastAction;
	private TacticalReplanReason m_LastReason;
	private TacticalRouteCommitStatus m_Status;
	private Vector3 m_LastGoalOrigin;
	private float m_LastReplanTime = -1f;
	private int m_EventsReceived;
	private int m_ReevaluationCount;
	private int m_ReplacementCount;
	private bool m_ThreatLatched;
	private TacticalUnderFireDecision m_LastUnderFire;
	private float m_LastUnderFireTime = -1f;
	private int m_UnderFireCount;
	private bool m_NeedsEmergencyCover;
	private TacticalArrivalDecision m_LastArrival;
	private float m_LastAcquireLogAt = -1f;
	private TacticalArrivalResult m_LastLoggedAcquireResult;
	private float m_LastHeartbeatLogAt = -1f;
	private CurrentTacticalPosition m_CurrentTacticalPosition;
	private TacticalMovingLeanDecision m_LastMovingLean;
	private bool m_MovingLeanActive;
	private bool m_HasMovingLeanEval;
	private int m_MovingLeanEvalCount;
	private CoverPeekDirection m_MovingLeanDirection;
	private CoverLeanLevel m_MovingLeanDepth;
	private TacticalUpdateScheduler m_Scheduler;
	private bool m_LodEnabled;
	private int m_LodUnitId;
	private TacticalLodDecision m_LastLod;
	private TacticalLodSituation m_LodHints;
	private bool m_HasLodHints;
	private TacticalLodCacheStamp m_RouteStamp;
	private TacticalLodCacheStamp m_ExposureStamp;
	private float m_LastLodEventTime = -1f;
	private float m_LastRouteEvalTime = -1f;
	private int m_LodDeniedCount;
	#endregion

	#region Public Properties
	public TacticalMovementDecision Last => m_Last;
	public TacticalRoute Route => m_Route;
	public TacticalRouteEvaluator Evaluator => m_Evaluator;
	public TacticalRouteDecision LastEvaluation => m_Evaluator.Last;
	public bool NeedsReroute => m_NeedsReroute;
	public int ReservedCoverCandidateId => m_ReservedCoverId;
	public CoverCandidate ReservedCoverCandidate => m_ReservedCandidate;
	public TacticalReplanCheck LastReplanCheck => m_LastCheck;
	public TacticalRouteCommitStatus CommitStatus => m_Status;
	public TacticalCommittedRoute Committed => m_Snap;
	public int EventsReceived => m_EventsReceived;
	public int ReevaluationCount => m_ReevaluationCount;
	public int ReplacementCount => m_ReplacementCount;
	public TacticalUnderFireDecision LastUnderFire => m_LastUnderFire;
	public int UnderFireEvaluationCount => m_UnderFireCount;
	public bool NeedsEmergencyCover => m_NeedsEmergencyCover;
	public TacticalArrivalDecision LastArrival => m_LastArrival;
	public CurrentTacticalPosition CurrentTacticalPosition => m_CurrentTacticalPosition;
	public bool CurrentHopRequiresCoverAcquire
	{
		get
		{
			if (!m_Route.HasDestination)
				return false;
			if (!m_Route.IsOnFinalHop)
				return m_Route.CurrentWaypoint.CoverCandidateId != 0;
			return m_FinalCoverId != 0;
		}
	}

	public TacticalMovingLeanDecision LastMovingLean => m_LastMovingLean;
	public bool MovingLeanActive => m_MovingLeanActive;
	public int MovingLeanEvaluationCount => m_MovingLeanEvalCount;
	public TacticalLodDecision LastLod => m_LastLod;
	public bool LodEnabled => m_LodEnabled;
	public int LodDeniedCount => m_LodDeniedCount;
	public TacticalLodCacheStamp LastRouteStamp => m_RouteStamp;
	public TacticalLodCacheStamp LastExposureStamp => m_ExposureStamp;
	public TacticalUpdateScheduler Scheduler => m_Scheduler;
	#endregion

	#region Public Methods
	public void Invalidate()
	{
		m_Evaluator.Invalidate();
		m_Committed = false;
		m_Snap = default;
		m_Status = TacticalRouteCommitStatus.None;
		m_Pending.Clear();
		m_NeedsReroute = false;
		m_LastAction = TacticalReplanAction.None;
		m_LastReason = TacticalReplanReason.None;
		m_LastCheck = default;
		m_EventsReceived = 0;
		m_ReevaluationCount = 0;
		m_ReplacementCount = 0;
		m_LastReplanTime = -1f;
		m_ThreatLatched = false;
		m_LastUnderFire = default;
		m_LastUnderFireTime = -1f;
		m_UnderFireCount = 0;
		m_NeedsEmergencyCover = false;
		m_LastArrival = default;
		m_LastAcquireLogAt = -1f;
		m_LastLoggedAcquireResult = default;
		m_LastHeartbeatLogAt = -1f;
		m_LoggedApproachingCoverId = 0;
		m_CurrentTacticalPosition = CurrentTacticalPosition.Invalid;
		m_LastMovingLean = default;
		m_MovingLeanActive = false;
		m_HasMovingLeanEval = false;
		m_MovingLeanEvalCount = 0;
		m_MovingLeanDirection = CoverPeekDirection.None;
		m_MovingLeanDepth = CoverLeanLevel.None;
		m_LastLod = default;
		m_HasLodHints = false;
		m_LodHints = default;
		m_RouteStamp = default;
		m_ExposureStamp = default;
		m_LastLodEventTime = -1f;
		m_LastRouteEvalTime = -1f;
		m_LodDeniedCount = 0;
	}

	public void BindScheduler(TacticalUpdateScheduler _scheduler, int _unitId)
	{
		m_Scheduler = _scheduler;
		m_LodUnitId = _unitId;
		m_LodEnabled = _scheduler != null;
	}

	public void SetLodHints(in TacticalLodSituation _hints)
	{
		m_LodHints = _hints;
		m_HasLodHints = true;
	}

	public TacticalLodDecision NotifyLod(in TacticalLodSituation _situation, Component _logActor = null)
	{
		m_LodHints = _situation;
		m_HasLodHints = true;
		TacticalLodDecision decision = TacticalLodMath.Select(in _situation);
		if (m_LastLod.Tier != TacticalLodTier.None &&
		    m_LastLod.Tier != decision.Tier)
			LogLod(_logActor, m_LastLod.Tier, in decision);
		m_LastLod = decision;
		if (m_Scheduler != null)
			m_Scheduler.ReportTier(ResolveLodUnitId(), decision.Tier);
		return decision;
	}

	public void BindPathProbe(ITacticalRoutePathProbe _probe)
	{
		m_Evaluator.BindProbe(_probe);
	}

	public void BindOccupancy(CoverOccupancyBoard _board, int _unitId)
	{
		m_Occupancy = _board;
		m_UnitId = _unitId;
	}

	/// <summary>
	/// Keep Reserved alive while the hop is still valid. Does not occupy. Does not Move.
	/// </summary>
	public void HeartbeatReservation(float _now, Component _logActor = null)
	{
		if (m_Occupancy == null || m_UnitId == 0 || m_ReservedCoverId == 0)
			return;
		if (m_CurrentTacticalPosition.Occupied && m_CurrentTacticalPosition.CandidateId == m_ReservedCoverId)
			return;
		m_Occupancy.Heartbeat(m_ReservedRegion, m_ReservedCoverId, m_UnitId, _now);
		if (_logActor == null)
			return;
		Vector3 coverPos = m_ReservedCandidate != null
			? m_ReservedCandidate.Position
			: m_CurrentTacticalPosition.Position;
		float distance = TacticalArrivalMath.DistanceMeters(_logActor.transform.position, coverPos);
		float remaining = -1f;
		bool pathValid = false;
		if (CoverDiagnosticLog.TryReadAgent(_logActor, out NavMeshAgent agent) && agent.isOnNavMesh)
		{
			pathValid = agent.hasPath || agent.pathPending;
			if (pathValid && !float.IsPositiveInfinity(agent.remainingDistance))
				remaining = agent.remainingDistance;
		}

		CoverDiagnosticLog.HeartbeatKeep(
			_logActor,
			m_ReservedCoverId,
			m_UnitId,
			distance,
			remaining,
			pathValid,
			ref m_LastHeartbeatLogAt);
	}

	public void NotifyEvent(in TacticalReplanEvent _event)
	{
		if (_event.Kind == TacticalReplanEventKind.None)
			return;
		m_Pending.Add(_event);
		m_EventsReceived++;
	}

	public void NotifyImmediateThreat(bool _active)
	{
		if (_active && !m_ThreatLatched)
			NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		m_ThreatLatched = _active;
	}

	public TacticalMovementDecision Update(in TacticalMovementGoal _goal, Component _logActor = null)
	{
		return Update(TacticalRouteEvaluator.FromGoal(in _goal), null, _logActor);
	}

	public TacticalMovementDecision Update(
		in TacticalRouteSituation _situation,
		Component _logActor = null)
	{
		return Update(in _situation, null, _logActor);
	}

	public TacticalMovementDecision Update(
		in TacticalRouteSituation _situation,
		IReadOnlyList<TacticalRouteCandidate> _authored,
		Component _logActor = null)
	{
		if (!_situation.HasDestination)
		{
			m_Route.Clear();
			m_Last = default;
			m_Committed = false;
			m_Status = TacticalRouteCommitStatus.None;
			return m_Last;
		}

		RememberCovers(in _situation);
		m_LastGoalOrigin = _situation.Origin;
		if (m_Committed)
		{
			m_Snap.Progress01 = TacticalReplanMath.Progress01(
				m_Snap.Origin, m_Snap.Destination, _situation.Origin);
			if (DestinationChanged(_situation.Destination) || m_Snap.Mode != _situation.Mode)
				NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.MissionChanged, 1f));
		}

		float now = ResolveNow(_situation.Now);
		HeartbeatReservation(now);
		bool hadWindow = m_Pending.Count > 0;
		TacticalReplanCheck check = default;
		TacticalReplanEvent coalesced = default;
		if (hadWindow)
		{
			coalesced = TacticalReplanMath.Coalesce(m_Pending, out int coalescedCount);
			m_Pending.Clear();
			LogRouteEvent(_logActor, in coalesced, coalescedCount);
			check = TacticalReplanMath.EvaluateGate(
				in m_Snap,
				in coalesced,
				coalescedCount,
				now,
				m_LastReplanTime,
				TacticalReplanMath.DefaultCooldownSeconds);
			if (m_Committed)
				ConsiderUnderFire(ref check, in coalesced, in _situation, now, _logActor);
			LogReplanCheck(_logActor, in check);
		}
		else if (m_Committed)
		{
			check.Reason = TacticalReplanReason.NoEvent;
			m_LastCheck = check;
			Stamp(TacticalReplanAction.None, TacticalReplanReason.NoEvent);
			RefreshLodFromRoute(in _situation, false, in check, now, _logActor);
			RefreshDecision(true);
			return m_Last;
		}

		m_LastCheck = check;
		if (m_Committed && !check.ShouldReevaluate)
		{
			Stamp(TacticalReplanAction.None, check.Reason);
			RefreshLodFromRoute(in _situation, hadWindow, in check, now, _logActor);
			RefreshDecision(true);
			return m_Last;
		}

		RefreshLodFromRoute(in _situation, hadWindow, in check, now, _logActor);
		if (hadWindow && check.ShouldReevaluate)
			m_LastLodEventTime = now;
		if (!TryAdmitRouteEvaluation(in check, now))
		{
			if (m_Committed)
			{
				Stamp(TacticalReplanAction.None, check.Reason);
				m_Status = TacticalRouteCommitStatus.Committed;
				RefreshDecision(true);
				return m_Last;
			}

			m_Last = default;
			return m_Last;
		}

		if (m_Committed)
		{
			m_Evaluator.Invalidate();
			m_Status = TacticalRouteCommitStatus.Replanning;
		}

		TacticalRouteDecision evaluation = m_Evaluator.Evaluate(in _situation, _authored, _logActor);
		m_LastRouteEvalTime = now;
		RememberCacheStamp(in evaluation, in _situation);
		if (m_Committed)
		{
			m_ReevaluationCount++;
			m_LastReplanTime = now;
			if (IsSameRoute(in evaluation))
			{
				check.Reason = TacticalReplanReason.SameRoute;
				m_LastCheck = check;
				Stamp(TacticalReplanAction.Keep, TacticalReplanReason.SameRoute);
				m_Status = TacticalRouteCommitStatus.Committed;
				ReserveCurrent(now, _logActor);
				RefreshDecision(true);
				LogReplan(_logActor, false, 0, m_Snap.CandidateId);
				return m_Last;
			}

			float newScore = evaluation.HasSelection ? evaluation.Selected.Score : float.MinValue;
			float cost = check.Mandatory
				? 0f
				: (check.ReplanningCost > 0f
					? check.ReplanningCost
					: TacticalReplanMath.ComputeReplanningCost(in m_Snap, check.EmergencyBypass));
			check.NewAdvantage = newScore - m_Snap.Score;
			if (!check.Mandatory &&
			    evaluation.HasSelection &&
			    !TacticalReplanMath.AdvantageBeatsCost(m_Snap.Score, newScore, cost))
			{
				check.Reason = TacticalReplanReason.AdvantageTooSmall;
				m_LastCheck = check;
				Stamp(TacticalReplanAction.Keep, TacticalReplanReason.AdvantageTooSmall);
				m_Status = TacticalRouteCommitStatus.Committed;
				RefreshDecision(true);
				return m_Last;
			}

			if (!evaluation.HasSelection)
			{
				Stamp(TacticalReplanAction.Keep, check.Reason);
				m_Status = TacticalRouteCommitStatus.Committed;
				RefreshDecision(true);
				return m_Last;
			}

			int oldId = m_Snap.CandidateId;
			int released = ReleaseCommittedReservations(now, _logActor);
			m_ReplacementCount++;
			check.ShouldReplace = true;
			m_LastCheck = check;
			Stamp(TacticalReplanAction.Replace, check.Reason);
			m_Status = TacticalRouteCommitStatus.Committed;
			Apply(in evaluation, _situation.Mode, false, _logActor, now);
			CommitSnap(in evaluation, in _situation);
			LogReplan(_logActor, true, released, oldId);
			return m_Last;
		}

		Apply(in evaluation, _situation.Mode, evaluation.FromCache, _logActor, now);
		CommitSnap(in evaluation, in _situation);
		Stamp(TacticalReplanAction.None, TacticalReplanReason.None);
		m_Status = TacticalRouteCommitStatus.Committed;
		if (!m_Last.FromCache)
			LogDecision(_logActor, in m_Last);
		return m_Last;
	}

	/// <summary>
	/// Test / later planners may adopt a waypoint route. Destination stays the goal.
	/// </summary>
	public TacticalMovementDecision Adopt(
		Vector3 _origin,
		Vector3 _destination,
		IReadOnlyList<TacticalRouteWaypoint> _intermediates,
		TacticalMovementMode _mode,
		Component _logActor = null)
	{
		m_Route.SetWaypoints(_origin, _destination, _mode, _intermediates);
		m_Evaluator.Invalidate();
		m_Pending.Clear();
		m_NeedsReroute = false;
		m_LastGoalOrigin = _origin;
		m_Committed = true;
		m_Snap = new TacticalCommittedRoute
		{
			Present = true,
			Origin = _origin,
			Destination = _destination,
			Kind = m_Route.Kind,
			Mode = _mode,
			IntermediateCount = m_Route.IntermediateCount
		};
		m_Status = TacticalRouteCommitStatus.Committed;
		Stamp(TacticalReplanAction.None, TacticalReplanReason.None);
		m_Last = Decorate(m_Route.ToDecision(false), 0, 0f, TacticalRouteSelectReason.None, 0, 0);
		LogDecision(_logActor, in m_Last);
		return m_Last;
	}

	public bool NotifyHopCompleted(float _now, Component _logActor = null)
	{
		if (!m_Route.HasDestination)
			return false;
		TacticalRouteWaypoint arrived = m_Route.CurrentWaypoint;
		if (arrived.CoverCandidateId != 0 &&
		    m_Occupancy != null &&
		    m_UnitId != 0 &&
		    !m_Occupancy.IsUsable(arrived.CoverRegion, arrived.CoverCandidateId, m_UnitId, _now) &&
		    m_ReservedCoverId != arrived.CoverCandidateId)
		{
			m_NeedsReroute = true;
			m_Last.NeedsReroute = true;
			NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.CoverInvalid, 1f));
			LogHop(_logActor, arrived.CoverCandidateId, "reroute=1");
			return false;
		}

		if (!m_Route.IsOnFinalHop)
		{
			ReleaseIfIntermediate(arrived, _now, _logActor);
			m_Route.TryAdvanceHop();
			ReserveCurrent(_now, _logActor);
			RefreshDecision(true);
			LogHop(_logActor, m_Route.CurrentWaypoint.CoverCandidateId, "next=1");
			return true;
		}

		ConfirmFinal(_now, _logActor);
		RefreshDecision(true);
		if (UnitActionLog.Enabled)
		{
			string payload = "dest=" + UnitActionLog.Vec(m_Route.Destination) + " arrived=1";
			if (_logActor != null)
				UnitActionLog.Write(_logActor, UnitActionLog.RouteArrival, payload);
			UnitActionLog.Timeline(UnitActionLog.RouteArrival, payload);
		}

		return true;
	}

	/// <summary>
	/// 14.7: NavMesh Reached is an input. Tactical acquire / reject is the output.
	/// Does not replan. Does not Move. NotifyHopCompleted remains the 14.2 hop FSM.
	/// </summary>
	public TacticalArrivalDecision NotifyTacticalArrival(
		in TacticalArrivalSituation _situation,
		Component _logActor = null)
	{
		TacticalArrivalSituation sit = BindFromRoute(in _situation);
		TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
		LogArrival(_logActor, in sit, in decision);
		ApplyArrival(in sit, ref decision, _logActor);
		m_LastArrival = decision;
		RefreshDecision(true);
		LogAcquire(_logActor, in sit, in decision);
		return decision;
	}

	/// <summary>
	/// 14.8: whether to lean while traversing. Pose goes through
	/// <see cref="CoverMovementLeanContract"/> / existing executor. Does not Move.
	/// </summary>
	public TacticalMovingLeanDecision NotifyMovingLean(
		in TacticalMovingLeanSituation _situation,
		ICoverLeanExecutor _executor = null,
		Component _logActor = null)
	{
		TacticalMovingLeanSituation sit = BindMovingLean(in _situation);
		MaybeWakeFromLean(in sit, _logActor);
		if (!AllowsMovingLean(in sit))
		{
			m_LastMovingLean.FromCache = true;
			RefreshDecision(true);
			return m_LastMovingLean;
		}

		if (!ShouldEvaluateMovingLean(in sit))
		{
			m_LastMovingLean.FromCache = true;
			RefreshDecision(true);
			return m_LastMovingLean;
		}

		m_HasMovingLeanEval = true;
		m_MovingLeanEvalCount++;
		TacticalMovingLeanDecision decision = TacticalMovingLeanMath.Decide(in sit);
		ApplyMovingLean(in decision, _executor, _logActor);
		m_LastMovingLean = decision;
		RefreshDecision(true);
		return decision;
	}

	public CoverReserveOutcome ReleaseFinal(float _now, Component _logActor = null)
	{
		if (m_Occupancy == null || m_FinalCoverId == 0 || m_UnitId == 0)
			return default;
		CoverReserveOutcome outcome = m_Occupancy.Release(
			m_FinalRegion, m_FinalCoverId, m_UnitId, _now, CoverReservationReason.Released, _logActor);
		if (m_ReservedCoverId == m_FinalCoverId)
			ClearReserved();
		RefreshDecision(true);
		return outcome;
	}

	public void ReleaseOccupancyHold()
	{
		ClearReserved();
		m_CurrentTacticalPosition = CurrentTacticalPosition.Invalid;
		RefreshDecision(true);
	}
	#endregion

	#region Private Methods
	private void RememberCovers(in TacticalRouteSituation _situation)
	{
		m_CoverLookup.Clear();
		if (_situation.CoverCandidates != null)
		{
			for (int i = 0; i < _situation.CoverCandidates.Count; i++)
			{
				if (_situation.CoverCandidates[i] != null)
					m_CoverLookup.Add(_situation.CoverCandidates[i]);
			}
		}

		if (m_ReservedCandidate != null)
			EnsureInLookup(m_ReservedCandidate);

		if (_situation.Occupancy != null)
			m_Occupancy = _situation.Occupancy;
		if (_situation.OccupancyUnitId != 0)
			m_UnitId = _situation.OccupancyUnitId;
		if (_situation.FinalCoverCandidateId != 0)
			m_FinalCoverId = _situation.FinalCoverCandidateId;
		else if (m_ReservedCoverId != 0)
			m_FinalCoverId = m_ReservedCoverId;
		if (m_FinalCoverId != 0)
		{
			CoverCandidate finalCover = FindCover(m_FinalCoverId, default);
			if (finalCover != null)
				m_FinalRegion = finalCover.RegionId;
		}
	}

	private void Apply(
		in TacticalRouteDecision _evaluation,
		TacticalMovementMode _mode,
		bool _fromCache,
		Component _logActor,
		float _now)
	{
		if (_fromCache && m_Route.HasDestination)
		{
			m_Last = Decorate(
				m_Route.ToDecision(true),
				_evaluation.HasSelection && _evaluation.Selected.Candidate != null
					? _evaluation.Selected.Candidate.CandidateId
					: m_Last.SelectedCandidateId,
				_evaluation.Selected.Score,
				_evaluation.Reason,
				_evaluation.CandidateCount,
				_evaluation.ViableCount);
			return;
		}

		if (!_evaluation.HasSelection || _evaluation.Selected.Candidate == null)
		{
			m_Route.Clear();
			m_Last = default;
			m_Last.FromCache = _fromCache;
			m_Committed = false;
			return;
		}

		TacticalRouteCandidate selected = _evaluation.Selected.Candidate;
		Vector3 origin = m_LastGoalOrigin;
		if (selected.Kind == TacticalRouteKind.Direct || selected.Intermediates.Count == 0)
			m_Route.SetDirect(origin, selected.Destination, _mode);
		else
			m_Route.SetWaypoints(origin, selected.Destination, _mode, selected.Intermediates);
		m_NeedsReroute = false;
		ReserveCurrent(_now, _logActor);
		m_Last = Decorate(
			m_Route.ToDecision(_fromCache),
			selected.CandidateId,
			_evaluation.Selected.Score,
			_evaluation.Reason,
			_evaluation.CandidateCount,
			_evaluation.ViableCount);
	}

	private void CommitSnap(in TacticalRouteDecision _evaluation, in TacticalRouteSituation _situation)
	{
		m_Committed = _evaluation.HasSelection && _evaluation.Selected.Candidate != null;
		if (!m_Committed)
		{
			m_Snap = default;
			return;
		}

		TacticalRouteCandidate selected = _evaluation.Selected.Candidate;
		m_Snap = new TacticalCommittedRoute
		{
			Present = true,
			CandidateId = selected.CandidateId,
			Score = _evaluation.Selected.Score,
			Exposure01 = selected.Exposure01,
			GeometryVersion = _situation.GeometryVersion,
			Origin = m_LastGoalOrigin,
			Destination = selected.Destination,
			Kind = selected.Kind,
			Mode = _situation.Mode,
			IntermediateCount = selected.Intermediates.Count,
			Progress01 = TacticalReplanMath.Progress01(
				m_LastGoalOrigin, selected.Destination, m_LastGoalOrigin)
		};
	}

	private bool DestinationChanged(Vector3 _destination)
	{
		return CoverSpatialMath.PlanarDistanceSqr(m_Snap.Destination, _destination) > 0.04f;
	}

	private bool IsSameRoute(in TacticalRouteDecision _evaluation)
	{
		if (!_evaluation.HasSelection || _evaluation.Selected.Candidate == null || !m_Snap.Present)
			return false;
		TacticalRouteCandidate candidate = _evaluation.Selected.Candidate;
		if (DestinationChanged(candidate.Destination))
			return false;
		if (m_Snap.CandidateId != 0 && candidate.CandidateId != 0)
			return candidate.CandidateId == m_Snap.CandidateId;
		if (candidate.Kind != m_Snap.Kind || candidate.Intermediates.Count != m_Snap.IntermediateCount)
			return false;
		if (candidate.Intermediates.Count != m_Route.IntermediateCount)
			return false;
		for (int i = 0; i < candidate.Intermediates.Count; i++)
		{
			TacticalRouteWaypoint next = candidate.Intermediates[i];
			TacticalRouteWaypoint current = m_Route.Intermediates[i];
			if (next.CoverCandidateId != current.CoverCandidateId)
				return false;
			if (CoverSpatialMath.PlanarDistanceSqr(next.Position, current.Position) > 0.04f)
				return false;
		}

		return true;
	}

	private int ReleaseCommittedReservations(float _now, Component _logActor)
	{
		if (m_Occupancy == null || m_UnitId == 0)
			return 0;
		int before = m_Occupancy.CountHeld();
		m_Occupancy.ReleaseUnit(m_UnitId, _now, CoverReservationReason.Released, _logActor);
		ClearReserved();
		int after = m_Occupancy.CountHeld();
		return Mathf.Max(0, before - after);
	}

	private static float ResolveNow(float _situationNow)
	{
		if (_situationNow > 0f)
			return _situationNow;
		return Time.time;
	}

	private void Stamp(TacticalReplanAction _action, TacticalReplanReason _reason)
	{
		m_LastAction = _action;
		m_LastReason = _reason;
	}

	private void ReserveCurrent(float _now, Component _logActor)
	{
		if (m_Occupancy == null || m_UnitId == 0)
			return;
		TacticalRouteWaypoint hop = m_Route.CurrentWaypoint;
		int coverId = hop.CoverCandidateId;
		CoverRegionId region = hop.CoverRegion;
		if (coverId == 0 && m_Route.IsOnFinalHop)
		{
			coverId = m_FinalCoverId;
			region = m_FinalRegion;
		}

		if (coverId == 0)
			return;
		CoverReserveOutcome outcome = m_Occupancy.TryReserve(region, coverId, m_UnitId, _now, _logActor);
		if (outcome.Success)
		{
			m_ReservedCoverId = coverId;
			m_ReservedRegion = region;
			PinReservedCandidate(FindCover(coverId, region));
			if (outcome.Reason == CoverReservationReason.Reserved)
				CoverDiagnosticLog.Ref(_logActor, coverId, m_ReservedCandidate, "Reserve");
			LogCoverHop(_logActor, coverId, "reserved=1");
			if (m_LoggedApproachingCoverId != coverId)
			{
				m_LoggedApproachingCoverId = coverId;
				CoverSlotLog.Write(
					_logActor,
					m_UnitId,
					coverId,
					CoverSlotPhase.Approaching,
					CoverReservationReason.Reserved);
			}
		}
		else if (m_Route.CurrentHopIndex == 0 && !m_Route.IsDirect)
		{
			m_NeedsReroute = true;
			NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.CoverInvalid, 1f));
		}
	}

	private void ReleaseIfIntermediate(in TacticalRouteWaypoint _arrived, float _now, Component _logActor)
	{
		if (_arrived.Kind == TacticalWaypointKind.Destination || _arrived.CoverCandidateId == 0)
			return;
		if (_arrived.CoverCandidateId == m_FinalCoverId)
			return;
		if (m_Occupancy == null || m_UnitId == 0)
			return;
		m_Occupancy.Release(
			_arrived.CoverRegion,
			_arrived.CoverCandidateId,
			m_UnitId,
			_now,
			CoverReservationReason.Released,
			_logActor);
		if (m_ReservedCoverId == _arrived.CoverCandidateId)
			ClearReserved();
		LogCoverHop(_logActor, _arrived.CoverCandidateId, "reservation=released");
	}

	private void ConfirmFinal(float _now, Component _logActor)
	{
		if (m_Occupancy == null || m_UnitId == 0 || m_FinalCoverId == 0)
			return;
		if (m_ReservedCoverId != m_FinalCoverId)
			m_Occupancy.TryReserve(m_FinalRegion, m_FinalCoverId, m_UnitId, _now, _logActor);
		m_Occupancy.ConfirmOccupied(m_FinalRegion, m_FinalCoverId, m_UnitId, _now, _logActor);
		m_ReservedCoverId = m_FinalCoverId;
		m_ReservedRegion = m_FinalRegion;
		PinReservedCandidate(FindCover(m_FinalCoverId, m_FinalRegion));
	}

	private TacticalArrivalSituation BindFromRoute(in TacticalArrivalSituation _situation)
	{
		TacticalArrivalSituation sit = _situation;
		if (m_Occupancy != null && sit.Occupancy == null)
			sit.Occupancy = m_Occupancy;
		if (m_UnitId != 0 && sit.UnitId == 0)
			sit.UnitId = m_UnitId;
		if (sit.GeometryVersion == 0 && sit.Occupancy != null)
			sit.GeometryVersion = sit.Occupancy.GeometryVersion;
		if (!m_Route.HasDestination)
			return sit;

		sit.IntermediateHop = !m_Route.IsOnFinalHop;
		TacticalRouteWaypoint hop = m_Route.CurrentWaypoint;
		if (sit.CandidateId == 0)
		{
			sit.CandidateId = hop.CoverCandidateId != 0
				? hop.CoverCandidateId
				: (m_Route.IsOnFinalHop ? m_FinalCoverId : 0);
		}

		if (sit.Candidate == null && sit.CandidateId == 0 && m_Route.IsOnFinalHop)
		{
			if (m_ReservedCandidate != null)
				sit.CandidateId = m_ReservedCandidate.CandidateId;
			else if (m_ReservedCoverId != 0)
				sit.CandidateId = m_ReservedCoverId;
			else if (m_FinalCoverId != 0)
				sit.CandidateId = m_FinalCoverId;
		}

		if (sit.Candidate == null && sit.CandidateId != 0)
		{
			if (m_ReservedCandidate != null && m_ReservedCandidate.CandidateId == sit.CandidateId)
				sit.Candidate = m_ReservedCandidate;
			else
			{
				CoverRegionId region = hop.CoverCandidateId != 0 ? hop.CoverRegion : m_FinalRegion;
				sit.Candidate = FindCover(sit.CandidateId, region);
			}

			if (sit.Candidate != null)
				sit.CandidateRegion = sit.Candidate.RegionId;
		}

		if (sit.Candidate != null && sit.RequiredCoverType == CoverType.None)
			sit.RequiredCoverType = sit.Candidate.CoverType;
		if (!sit.HasMoveDestination && m_Route.HasDestination)
		{
			sit.MoveDestination = m_Route.CurrentHop;
			sit.HasMoveDestination = true;
		}

		if (!sit.HasTargetPosition)
		{
			sit.TargetPosition = sit.Candidate != null ? sit.Candidate.Position : m_Route.CurrentHop;
			sit.HasTargetPosition = true;
		}

		return sit;
	}

	private void ApplyArrival(
		in TacticalArrivalSituation _situation,
		ref TacticalArrivalDecision _decision,
		Component _logActor)
	{
		float now = _situation.Now > 0f ? _situation.Now : Time.time;
		if (_decision.Result == TacticalArrivalResult.Traversed)
		{
			TacticalRouteWaypoint arrived = m_Route.CurrentWaypoint;
			ReleaseIfIntermediate(arrived, now, _logActor);
			LogPositionRelease(_logActor, arrived.CoverCandidateId);
			m_Route.TryAdvanceHop();
			ReserveCurrent(now, _logActor);
			return;
		}

		if (_decision.Result != TacticalArrivalResult.Acquired)
		{
			if (_decision.Reason == TacticalArrivalFailureReason.CandidateMissing ||
			    (_decision.CandidateId != 0 && _situation.Candidate == null))
			{
				int missingId = _decision.CandidateId != 0 ? _decision.CandidateId : _situation.CandidateId;
				CoverDiagnosticLog.Ref(_logActor, missingId, null, "Acquire");
				CoverDiagnosticLog.Ref(_logActor, missingId, null, "ConfirmOccupied");
			}

			if (!TacticalArrivalMath.IsTransientAcquireMiss(_decision.Reason))
				m_NeedsReroute = true;
			CueArrivalFailure(in _decision);
			return;
		}

		CoverDiagnosticLog.Ref(_logActor, _decision.CandidateId, _situation.Candidate, "Acquire");
		if (_decision.CandidateId == 0 || _situation.Candidate == null)
		{
			CoverDiagnosticLog.Ref(
				_logActor,
				_decision.CandidateId != 0 ? _decision.CandidateId : _situation.CandidateId,
				null,
				"ConfirmOccupied");
			return;
		}

		ReleasePreviousIfNeeded(_situation.Candidate, now, _logActor);
		CoverSlotLog.Write(
			_logActor,
			m_UnitId,
			_decision.CandidateId,
			CoverSlotPhase.Acquired,
			CoverReservationReason.None,
			_decision.DistanceMeters);
		if (m_Occupancy != null && m_UnitId != 0)
		{
			CoverReserveOutcome reserved = m_Occupancy.TryReserve(
				_situation.Candidate, m_UnitId, now, _logActor);
			if (!reserved.Success && reserved.OwnerUnitId != m_UnitId)
			{
				_decision.Result = TacticalArrivalResult.Occupied;
				_decision.Reason = TacticalArrivalFailureReason.Occupied;
				_decision.Position = CurrentTacticalPosition.Invalid;
				m_NeedsReroute = true;
				CueArrivalFailure(in _decision);
				return;
			}

			CoverReserveOutcome occupied = m_Occupancy.ConfirmOccupied(
				_situation.Candidate, m_UnitId, now, _logActor);
			if (!occupied.Success)
			{
				_decision.Result = TacticalArrivalResult.Reevaluate;
				_decision.Reason = TacticalArrivalFailureReason.ReservationLost;
				_decision.Position = CurrentTacticalPosition.Invalid;
				m_NeedsReroute = true;
				CueArrivalFailure(in _decision);
				return;
			}
		}

		m_ReservedCoverId = _situation.Candidate.CandidateId;
		m_ReservedRegion = _situation.Candidate.RegionId;
		m_FinalCoverId = _situation.Candidate.CandidateId;
		m_FinalRegion = _situation.Candidate.RegionId;
		PinReservedCandidate(_situation.Candidate);
		m_CurrentTacticalPosition = CurrentTacticalPosition.FromCandidate(_situation.Candidate, true);
		_decision.Position = m_CurrentTacticalPosition;
	}

	private void ReleasePreviousIfNeeded(CoverCandidate _acquired, float _now, Component _logActor)
	{
		if (!m_CurrentTacticalPosition.Valid ||
		    !m_CurrentTacticalPosition.Occupied ||
		    _acquired == null ||
		    m_CurrentTacticalPosition.CandidateId == _acquired.CandidateId)
			return;
		int previousId = m_CurrentTacticalPosition.CandidateId;
		CoverCandidate previous = FindCover(previousId, default);
		if (m_Occupancy != null && m_UnitId != 0 && previous != null)
			m_Occupancy.Release(previous, m_UnitId, _now, CoverReservationReason.Released, _logActor);
		LogPositionRelease(_logActor, previousId);
	}

	private void CueArrivalFailure(in TacticalArrivalDecision _decision)
	{
		TacticalReplanEventKind kind = TacticalReplanEventKind.CoverInvalid;
		if (_decision.Reason == TacticalArrivalFailureReason.GeometryChanged)
			kind = TacticalReplanEventKind.GeometryChanged;
		else if (_decision.Reason == TacticalArrivalFailureReason.RouteStale)
			kind = TacticalReplanEventKind.DestinationInvalid;
		else if (_decision.Reason == TacticalArrivalFailureReason.OutOfTolerance ||
		         _decision.Reason == TacticalArrivalFailureReason.NavigationStopped)
			return;
		NotifyEvent(TacticalReplanEvent.Of(kind, 1f));
	}

	private void ClearReserved()
	{
		m_ReservedCoverId = 0;
		m_ReservedRegion = default;
		m_ReservedCandidate = null;
		m_LoggedApproachingCoverId = 0;
	}

	private void PinReservedCandidate(CoverCandidate _candidate)
	{
		if (_candidate == null)
			return;
		m_ReservedCandidate = _candidate;
		EnsureInLookup(_candidate);
	}

	private void EnsureInLookup(CoverCandidate _candidate)
	{
		if (_candidate == null)
			return;
		for (int i = 0; i < m_CoverLookup.Count; i++)
		{
			if (m_CoverLookup[i] != null && m_CoverLookup[i].CandidateId == _candidate.CandidateId)
				return;
		}

		m_CoverLookup.Add(_candidate);
	}

	private CoverCandidate FindCover(int _id, CoverRegionId _region)
	{
		for (int i = 0; i < m_CoverLookup.Count; i++)
		{
			CoverCandidate cover = m_CoverLookup[i];
			if (cover.CandidateId != _id)
				continue;
			if (_region.Equals(default) || cover.RegionId.Equals(_region))
				return cover;
		}

		return null;
	}

	private void RefreshDecision(bool _fromCache)
	{
		m_Last = Decorate(
			m_Route.ToDecision(_fromCache),
			m_Last.SelectedCandidateId,
			m_Last.SelectedScore,
			m_Last.SelectReason,
			m_Last.CandidateCount,
			m_Last.ViableCount);
	}

	private TacticalMovementDecision Decorate(
		TacticalMovementDecision _decision,
		int _id,
		float _score,
		TacticalRouteSelectReason _reason,
		int _candidates,
		int _viable)
	{
		_decision.SelectedCandidateId = _id;
		_decision.SelectedScore = _score;
		_decision.SelectReason = _reason;
		_decision.CandidateCount = _candidates;
		_decision.ViableCount = _viable;
		_decision.CurrentHopIndex = m_Route.CurrentHopIndex;
		_decision.NeedsReroute = m_NeedsReroute;
		_decision.ReservedCoverCandidateId = m_ReservedCoverId;
		_decision.CommitStatus = m_Status;
		_decision.ReplanAction = m_LastAction;
		_decision.ReplanReason = m_LastReason;
		_decision.LastEventKind = m_LastCheck.EventKind;
		_decision.UnderFireAction = m_LastUnderFire.Action;
		_decision.UnderFireReason = m_LastUnderFire.Reason;
		_decision.NeedsEmergencyCover = m_NeedsEmergencyCover;
		_decision.ArrivalResult = m_LastArrival.Result;
		_decision.ArrivalReason = m_LastArrival.Reason;
		_decision.ArrivalDistanceMeters = m_LastArrival.DistanceMeters;
		_decision.CurrentTacticalPosition = m_CurrentTacticalPosition;
		_decision.MovingLeanAction = m_LastMovingLean.Action;
		_decision.MovingLeanDirection = m_LastMovingLean.Direction;
		_decision.MovingLeanDepth = m_LastMovingLean.Depth;
		_decision.MovingLeanReason = m_LastMovingLean.Reason;
		_decision.LodTier = m_LastLod.Tier;
		_decision.LodReason = m_LastLod.Reason;
		return _decision;
	}

	private void ConsiderUnderFire(
		ref TacticalReplanCheck _check,
		in TacticalReplanEvent _coalesced,
		in TacticalRouteSituation _situation,
		float _now,
		Component _logActor)
	{
		if (_coalesced.Kind == TacticalReplanEventKind.MissionChanged ||
		    _situation.UnderFire.MissionOverride)
		{
			if (_situation.UnderFire.Present)
			{
				TacticalUnderFireDecision command = TacticalUnderFireMath.Decide(in _situation.UnderFire);
				StoreUnderFire(in command, _now, true, _logActor);
			}

			return;
		}

		if (TacticalReplanMath.IsMandatory(in _coalesced))
			return;
		if (_coalesced.Kind != TacticalReplanEventKind.ImmediateThreat)
			return;

		bool explicitSit = _situation.UnderFire.Present;
		TacticalUnderFireSituation underFire = explicitSit
			? _situation.UnderFire
			: TacticalUnderFireMath.FromCommitted(in _situation, in m_Snap, m_Route);
		if (!underFire.ImmediateThreat)
			return;

		bool cooling = m_LastUnderFireTime >= 0f &&
		               (_now - m_LastUnderFireTime) < TacticalReplanMath.DefaultCooldownSeconds &&
		               m_LastUnderFire.Action != TacticalUnderFireAction.None;
		TacticalUnderFireDecision decided;
		if (cooling)
			decided = m_LastUnderFire;
		else
		{
			decided = TacticalUnderFireMath.Decide(in underFire);
			StoreUnderFire(in decided, _now, true, _logActor);
		}

		if (decided.Action == TacticalUnderFireAction.Replan)
		{
			_check.ShouldReevaluate = true;
			_check.EmergencyBypass = true;
			return;
		}

		if (TacticalUnderFireMath.ShouldSuppressReplan(in decided, explicitSit))
			_check.ShouldReevaluate = false;
	}

	private void StoreUnderFire(
		in TacticalUnderFireDecision _decision,
		float _now,
		bool _countEvaluation,
		Component _logActor)
	{
		m_LastUnderFire = _decision;
		m_LastUnderFireTime = _now;
		m_NeedsEmergencyCover = _decision.Action == TacticalUnderFireAction.EmergencyCover ||
		                        _decision.NeedsEmergencyCover;
		if (_countEvaluation)
		{
			m_UnderFireCount++;
			LogUnderFire(_logActor, in _decision);
		}
	}

	private static void LogUnderFire(Component _actor, in TacticalUnderFireDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"distanceToCover=" + UnitActionLog.F1(_decision.CoverAheadMeters) +
			" currentExposure=" + UnitActionLog.F2(_decision.CurrentExposure01) +
			" decision=" + FormatUnderFireAction(_decision.Action) +
			" reason=" + _decision.Reason;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.UnderFire, payload);
		UnitActionLog.Timeline(UnitActionLog.UnderFire, payload);
	}

	private static string FormatUnderFireAction(TacticalUnderFireAction _action)
	{
		switch (_action)
		{
			case TacticalUnderFireAction.Continue:
				return "CONTINUE";
			case TacticalUnderFireAction.Replan:
				return "REPLAN";
			case TacticalUnderFireAction.EmergencyCover:
				return "EMERGENCY_COVER";
			case TacticalUnderFireAction.Hold:
				return "HOLD";
			default:
				return "NONE";
		}
	}

	private static void LogDecision(Component _actor, in TacticalMovementDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		string decision =
			"route=" + _decision.Kind +
			" dest=" + UnitActionLog.Vec(_decision.Destination) +
			" hop=" + UnitActionLog.Vec(_decision.CurrentHop) +
			" intermediates=" + _decision.IntermediateCount +
			" selected=R" + _decision.SelectedCandidateId +
			" score=" + UnitActionLog.F1(_decision.SelectedScore);
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.RouteDecision, decision);
		UnitActionLog.Timeline(UnitActionLog.RouteDecision, decision);
	}

	private static void LogRouteEvent(Component _actor, in TacticalReplanEvent _event, int _count)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"type=" + _event.Kind +
			" count=" + _count +
			" delta=" + UnitActionLog.F2(_event.Delta) +
			" onRoute=" + (_event.OnRoute ? "1" : "0");
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.RouteEvent, payload);
		UnitActionLog.Timeline(UnitActionLog.RouteEvent, payload);
	}

	private static void LogReplanCheck(Component _actor, in TacticalReplanCheck _check)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"reason=" + _check.EventKind +
			" decision=" + (_check.ShouldReevaluate ? "Yes" : "No") +
			" why=" + _check.Reason +
			" delta=" + UnitActionLog.F2(_check.Delta) +
			" coalesced=" + _check.CoalescedCount;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.ReplanCheck, payload);
		UnitActionLog.Timeline(UnitActionLog.ReplanCheck, payload);
	}

	private void LogReplan(Component _actor, bool _replaced, int _released, int _oldId)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"old=R" + _oldId +
			" new=R" + m_Last.SelectedCandidateId +
			" replaced=" + (_replaced ? "1" : "0") +
			" released=" + _released +
			" reserved=" + (m_ReservedCoverId != 0 ? "1" : "0") +
			" reason=" + m_LastReason;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.RouteReplan, payload);
		UnitActionLog.Timeline(UnitActionLog.RouteReplan, payload);
	}

	private static void LogHop(Component _actor, int _coverId, string _detail)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload = "candidate=C" + _coverId + " " + _detail;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.RouteHop, payload);
		UnitActionLog.Timeline(UnitActionLog.RouteHop, payload);
	}

	private static void LogCoverHop(Component _actor, int _coverId, string _detail)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload = "candidate=C" + _coverId + " " + _detail;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.CoverHop, payload);
		UnitActionLog.Timeline(UnitActionLog.CoverHop, payload);
	}

	private static void LogArrival(
		Component _actor,
		in TacticalArrivalSituation _situation,
		in TacticalArrivalDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		int candidateId = _decision.CandidateId != 0
			? _decision.CandidateId
			: _situation.CandidateId;
		string payload =
			"candidate=C" + candidateId +
			" distance=" + UnitActionLog.F2(_decision.DistanceMeters);
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.Arrival, payload);
		UnitActionLog.Timeline(UnitActionLog.Arrival, payload);
	}

	private void LogAcquire(
		Component _actor,
		in TacticalArrivalSituation _situation,
		in TacticalArrivalDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		if (TacticalArrivalMath.IsTransientAcquireMiss(_decision.Reason) &&
		    m_LastLoggedAcquireResult == _decision.Result &&
		    m_LastAcquireLogAt >= 0f &&
		    Time.time - m_LastAcquireLogAt < 0.45f)
			return;
		m_LastAcquireLogAt = Time.time;
		m_LastLoggedAcquireResult = _decision.Result;
		bool rejected = _decision.Result != TacticalArrivalResult.Acquired &&
		                _decision.Result != TacticalArrivalResult.Traversed;
		Vector3 acquire = _decision.AcquirePosition;
		if (acquire.sqrMagnitude < 0.0001f && _situation.Candidate != null)
			acquire = _situation.Candidate.Position;
		Vector3 dest = _situation.HasMoveDestination ? _situation.MoveDestination : _decision.MoveDestination;
		if (!_situation.HasMoveDestination && dest.sqrMagnitude < 0.0001f)
			dest = acquire;
		string remaining = _situation.HasNavRemaining
			? FormatRemaining(_situation.NavRemainingDistance)
			: "n/a";
		string velocity = _situation.HasVelocity
			? UnitActionLog.F2(_situation.Velocity.magnitude)
			: "n/a";
		bool hasCover = _decision.CandidateId != 0 && _situation.Candidate != null;
		string coverLabel = hasCover ? "C" + _decision.CandidateId : "0";
		string pathStatus = string.IsNullOrEmpty(_situation.PathStatus) ? "n/a" : _situation.PathStatus;
		string payload =
			"candidate=C" + _decision.CandidateId +
			" cover=" + coverLabel +
			" result=" + (rejected ? "Rejected" : _decision.Result) +
			(_decision.Reason != TacticalArrivalFailureReason.None ? " reason=" + _decision.Reason : string.Empty) +
			" distance=" + UnitActionLog.F2(_decision.DistanceMeters) +
			" dist=" + UnitActionLog.F2(_decision.DistanceMeters) +
			" tolerance=" + UnitActionLog.F2(TacticalArrivalMath.ResolveTolerance(_situation.AcquireToleranceMeters)) +
			" tol=" + UnitActionLog.F2(TacticalArrivalMath.ResolveTolerance(_situation.AcquireToleranceMeters)) +
			" remaining=" + remaining +
			" velocity=" + velocity +
			" pathStatus=" + pathStatus +
			" stoppingDistance=" + (_situation.HasStoppingDistance
				? UnitActionLog.F2(_situation.StoppingDistance)
				: "n/a") +
			" radius=" + (_situation.HasAgentRadius
				? UnitActionLog.F2(_situation.AgentRadius)
				: "n/a") +
			" unitPos=" + FormatAcquireVec(_situation.CurrentPosition) +
			" dest=" + FormatAcquireVec(dest) +
			" acquire=" + FormatAcquireVec(acquire) +
			(_situation.HasAgentPosition ? " agentPos=" + FormatAcquireVec(_situation.AgentPosition) : string.Empty);
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.PositionAcquire, payload);
		UnitActionLog.Timeline(UnitActionLog.PositionAcquire, payload);
		if (hasCover || _decision.CandidateId != 0)
			CoverDiagnosticLog.MoveCover(_actor, in _situation, in _decision);
	}

	private static string FormatAcquireVec(Vector3 _value)
	{
		return string.Format(
			System.Globalization.CultureInfo.InvariantCulture,
			"({0:0.00}, {1:0.00}, {2:0.00})",
			_value.x,
			_value.y,
			_value.z);
	}

	private static string FormatRemaining(float _remaining)
	{
		if (_remaining < 0f || float.IsPositiveInfinity(_remaining) || float.IsNaN(_remaining))
			return "n/a";
		return UnitActionLog.F2(_remaining);
	}

	private static void LogPositionRelease(Component _actor, int _candidateId)
	{
		if (!UnitActionLog.Enabled || _candidateId == 0)
			return;
		string payload = "candidate=C" + _candidateId;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.PositionRelease, payload);
		UnitActionLog.Timeline(UnitActionLog.PositionRelease, payload);
	}

	private TacticalMovingLeanSituation BindMovingLean(in TacticalMovingLeanSituation _situation)
	{
		TacticalMovingLeanSituation sit = _situation;
		if (!sit.ImmediateThreat && m_ThreatLatched)
			sit.ImmediateThreat = true;
		if (!sit.Replan && m_LastAction == TacticalReplanAction.Replace)
			sit.Replan = true;
		if (!sit.Arrived && m_LastArrival.IsAcquired)
			sit.Arrived = true;
		if (!sit.CurrentlyLeaning)
			sit.CurrentlyLeaning = m_MovingLeanActive;
		if (sit.CurrentlyLeaning && sit.CurrentDirection == CoverPeekDirection.None)
			sit.CurrentDirection = m_MovingLeanDirection;
		if (sit.CurrentlyLeaning && sit.CurrentDepth == CoverLeanLevel.None)
			sit.CurrentDepth = m_MovingLeanDepth;
		return sit;
	}

	private bool ShouldEvaluateMovingLean(in TacticalMovingLeanSituation _situation)
	{
		if (m_MovingLeanActive)
			return true;
		if (!m_HasMovingLeanEval)
			return _situation.Present || _situation.Moving || _situation.Approach;
		return _situation.Approach ||
		       _situation.RouteChanged ||
		       _situation.TargetChanged ||
		       _situation.CornerPassed;
	}

	private void ApplyMovingLean(
		in TacticalMovingLeanDecision _decision,
		ICoverLeanExecutor _executor,
		Component _logActor)
	{
		if (_decision.Action == TacticalMovingLeanAction.Lean)
		{
			bool changed = !m_MovingLeanActive ||
			               m_MovingLeanDirection != _decision.Direction ||
			               m_MovingLeanDepth != _decision.Depth;
			CoverMovementLeanRequest request = _decision.Request;
			if (changed)
			{
				CoverMovementLeanContract.Apply(_executor, in request);
				LogMovingLean(_logActor, in _decision);
			}

			m_MovingLeanActive = true;
			m_MovingLeanDirection = _decision.Direction;
			m_MovingLeanDepth = _decision.Depth;
			return;
		}

		if (_decision.Action != TacticalMovingLeanAction.Exit || !m_MovingLeanActive)
			return;

		CoverMovementLeanRequest idle = CoverMovementLeanRequest.Idle;
		CoverMovementLeanContract.Apply(_executor, in idle);
		LogMovingLeanExit(_logActor, _decision.Reason);
		m_MovingLeanActive = false;
		m_MovingLeanDirection = CoverPeekDirection.None;
		m_MovingLeanDepth = CoverLeanLevel.None;
	}

	private static void LogMovingLean(Component _actor, in TacticalMovingLeanDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"direction=" + _decision.Direction +
			" depth=" + _decision.Depth +
			" reason=" + _decision.Reason;
		if (_actor != null)
		{
			UnitActionLog.Write(_actor, UnitActionLog.MovingLean, payload);
			UnitActionLog.Write(_actor, UnitActionLog.Lean, "mode=Moving " + payload);
		}

		UnitActionLog.Timeline(UnitActionLog.MovingLean, payload);
	}

	private static void LogMovingLeanExit(Component _actor, TacticalMovingLeanReason _reason)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload = "reason=" + _reason;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.MovingLeanExit, payload);
		UnitActionLog.Timeline(UnitActionLog.MovingLeanExit, payload);
	}

	private void RefreshLodFromRoute(
		in TacticalRouteSituation _situation,
		bool _hadEvent,
		in TacticalReplanCheck _check,
		float _now,
		Component _logActor)
	{
		TacticalLodSituation sit = m_HasLodHints ? m_LodHints : default;
		if (m_HasLodHints)
		{
			sit.PreviousTier = m_LastLod.Tier;
			sit.Now = _now;
		}
		else
		{
			sit.Now = _now;
			sit.PreviousTier = m_LastLod.Tier;
			sit.HasActiveTacticalMovement = m_Committed && m_Route.HasDestination && !m_LastArrival.IsAcquired;
			sit.Idle = !sit.HasActiveTacticalMovement;
			sit.UnderFire = m_LastUnderFire.Action != TacticalUnderFireAction.None;
			sit.InCombat = _situation.HasKnownThreat || m_ThreatLatched;
			sit.SeesHostile = _situation.HasKnownThreat;
			sit.HasImmediateThreat = m_ThreatLatched;
			sit.IncomingFire = _check.EventKind == TacticalReplanEventKind.ImmediateThreat;
			sit.HasPendingSignificantEvent = _hadEvent && _check.ShouldReevaluate;
			sit.InComplexGeometry = _situation.WallAnchors != null && _situation.WallAnchors.Count > 0;
			sit.Arriving = m_LastArrival.Result != TacticalArrivalResult.None;
			sit.CurrentlyLeaning = m_MovingLeanActive;
			sit.GeometryVersion = _situation.GeometryVersion;
			sit.KnowledgeVersion = _situation.KnowledgeVersion;
		}

		if (m_LastLodEventTime >= 0f)
			sit.SecondsSinceSignificantEvent = Mathf.Max(0f, _now - m_LastLodEventTime);
		else if (sit.SecondsSinceSignificantEvent <= 0f && sit.HasPendingSignificantEvent)
			sit.SecondsSinceSignificantEvent = 0f;
		else if (sit.SecondsSinceSignificantEvent <= 0f)
			sit.SecondsSinceSignificantEvent = 999f;
		NotifyLod(in sit, _logActor);
		if (m_Scheduler != null)
			m_Scheduler.LogIfEnabled(_logActor);
	}

	private bool TryAdmitRouteEvaluation(in TacticalReplanCheck _check, float _now)
	{
		if (!m_LodEnabled || m_Scheduler == null)
			return true;
		if (!m_Committed)
			return true;
		m_Scheduler.EnsureTick(_now);
		var gate = new TacticalLodGate
		{
			HasEvent = _check.ShouldReevaluate,
			FirstEvaluation = !m_Committed,
			TickDue = TacticalLodMath.TickDue(_now, m_LastRouteEvalTime, m_LastLod.Tier),
			Mandatory = _check.Mandatory
		};
		bool emergency = _check.EmergencyBypass ||
		                 m_LastLod.Criticality == TacticalCriticality.Emergency ||
		                 m_ThreatLatched;
		if (!m_Committed || emergency || _check.ShouldReevaluate || _check.Mandatory)
		{
			TacticalCriticality granted = emergency
				? TacticalCriticality.Emergency
				: TacticalCriticality.High;
			m_Scheduler.TryAdmit(ResolveLodUnitId(), TacticalLodOperation.RouteEvaluation, granted);
			return true;
		}

		if (!TacticalLodMath.Allows(m_LastLod.Tier, TacticalLodOperation.RouteEvaluation, in gate))
		{
			m_LodDeniedCount++;
			return false;
		}

		TacticalCriticality criticality = m_LastLod.Criticality != TacticalCriticality.None
			? m_LastLod.Criticality
			: TacticalCriticality.Medium;
		if (m_Scheduler.TryAdmit(ResolveLodUnitId(), TacticalLodOperation.RouteEvaluation, criticality))
			return true;
		m_LodDeniedCount++;
		return false;
	}

	private void RememberCacheStamp(
		in TacticalRouteDecision _evaluation,
		in TacticalRouteSituation _situation)
	{
		int candidateId = _evaluation.HasSelection && _evaluation.Selected.Candidate != null
			? _evaluation.Selected.Candidate.CandidateId
			: 0;
		float score = _evaluation.HasSelection ? _evaluation.Selected.Score : 0f;
		int routeVersion = m_RouteStamp.Present ? m_RouteStamp.RouteVersion : 0;
		if (!_evaluation.FromCache)
			routeVersion++;
		m_RouteStamp = TacticalLodMath.Stamp(
			routeVersion,
			_situation.GeometryVersion,
			_situation.KnowledgeVersion,
			score,
			candidateId);
		if (_evaluation.FromCache)
			m_ExposureStamp = m_RouteStamp;
		else
			m_ExposureStamp = m_RouteStamp;
	}

	private void MaybeWakeFromLean(in TacticalMovingLeanSituation _situation, Component _logActor)
	{
		if (!m_LodEnabled)
			return;
		bool corner = _situation.Approach ||
		              _situation.CornerPassed ||
		              (_situation.HasCorner &&
		               _situation.DistanceToCornerMeters > 0f &&
		               _situation.DistanceToCornerMeters <= TacticalMovingLeanMath.DefaultApproachMeters);
		if (!_situation.ImmediateThreat && !_situation.Replan && !_situation.Arrived && !corner)
			return;
		if (m_LastLod.Tier == TacticalLodTier.Full)
			return;
		var sit = new TacticalLodSituation
		{
			Now = Time.time,
			PreviousTier = m_LastLod.Tier,
			HasImmediateThreat = _situation.ImmediateThreat,
			ApproachingCorner = corner && !_situation.ImmediateThreat,
			HasActiveTacticalMovement = _situation.Moving,
			CurrentlyLeaning = _situation.CurrentlyLeaning
		};
		NotifyLod(in sit, _logActor);
	}

	private bool AllowsMovingLean(in TacticalMovingLeanSituation _situation)
	{
		if (!m_LodEnabled || m_LastLod.Tier == TacticalLodTier.None)
			return true;
		bool corner = _situation.Approach ||
		              _situation.CornerPassed ||
		              (_situation.HasCorner &&
		               _situation.DistanceToCornerMeters > 0f &&
		               _situation.DistanceToCornerMeters <= TacticalMovingLeanMath.DefaultApproachMeters);
		var gate = new TacticalLodGate
		{
			HasEvent = _situation.ImmediateThreat || _situation.Replan || _situation.Arrived,
			ApproachingCorner = corner,
			CurrentlyLeaning = m_MovingLeanActive || _situation.CurrentlyLeaning
		};
		return TacticalLodMath.Allows(m_LastLod.Tier, TacticalLodOperation.MovingLean, in gate);
	}

	private int ResolveLodUnitId()
	{
		if (m_LodUnitId != 0)
			return m_LodUnitId;
		return m_UnitId;
	}

	private static void LogLod(
		Component _actor,
		TacticalLodTier _from,
		in TacticalLodDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"from=" + _from +
			" to=" + _decision.Tier +
			" reason=" + _decision.Reason;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.TacticalLod, payload);
		UnitActionLog.Timeline(UnitActionLog.TacticalLod, payload);
	}
	#endregion
}
