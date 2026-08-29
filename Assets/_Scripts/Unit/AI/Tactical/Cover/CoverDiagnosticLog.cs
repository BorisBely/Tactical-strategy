using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Event-only cover / last-meter diagnostics. Does not tick. Does not retune score.
/// </summary>
public static class CoverDiagnosticLog
{
	#region Constants
	private const float c_HeartbeatKeepSeconds = 1f;
	#endregion

	#region Public Methods
	public static string CandidateRef(CoverCandidate _candidate)
	{
		if (_candidate == null)
			return "MISSING";
		return "0x" + RuntimeHelpers.GetHashCode(_candidate).ToString("X8");
	}

	public static void Decision(Component _actor, in TacticalCoverDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"current=C" + _decision.CurrentCandidateId +
			" best=C" + _decision.BestCandidateId +
			" decision=" + _decision.Decision +
			" reason=" + _decision.Reason +
			" score=" + UnitActionLog.F1(_decision.BestScore) +
			" currentScore=" + UnitActionLog.F1(_decision.CurrentScore) +
			" bestScore=" + UnitActionLog.F1(_decision.BestScore) +
			" switchingCost=" + UnitActionLog.F1(_decision.SwitchingCost);
		Write(_actor, UnitActionLog.CoverDecision, payload);
		Write(_actor, UnitActionLog.PositionDecision, payload);
	}

	public static void Invalid(Component _actor, int _coverId, string _reason)
	{
		if (!UnitActionLog.Enabled || string.IsNullOrEmpty(_reason))
			return;
		string payload =
			"cover=C" + _coverId +
			" reason=" + _reason;
		Write(_actor, UnitActionLog.CoverInvalid, payload);
	}

	public static void Ref(Component _actor, int _coverId, CoverCandidate _candidate, string _phase)
	{
		if (!UnitActionLog.Enabled || _coverId == 0 || string.IsNullOrEmpty(_phase))
			return;
		string payload =
			"coverId=C" + _coverId +
			" candidateRef=" + CandidateRef(_candidate) +
			" phase=" + _phase;
		if (_candidate != null)
		{
			payload +=
				" position=" + UnitActionLog.Vec(_candidate.Position) +
				" normal=" + UnitActionLog.Vec(_candidate.Normal) +
				" approachPosition=" + UnitActionLog.Vec(_candidate.Position);
		}

		Write(_actor, UnitActionLog.CoverRef, payload);
	}

	public static void HeartbeatKeep(
		Component _actor,
		int _coverId,
		int _reservedFor,
		float _distance,
		float _remaining,
		bool _pathValid,
		ref float _lastLogAt)
	{
		if (!UnitActionLog.Enabled || _coverId == 0)
			return;
		if (_lastLogAt >= 0f && Time.time - _lastLogAt < c_HeartbeatKeepSeconds)
			return;
		_lastLogAt = Time.time;
		string remaining = _remaining < 0f ? "n/a" : UnitActionLog.F2(_remaining);
		string payload =
			"cover=C" + _coverId +
			" reservedFor=" + UnitLabel(_actor, _reservedFor) +
			" distance=" + UnitActionLog.F2(_distance) +
			" remaining=" + remaining +
			" pathValid=" + (_pathValid ? "1" : "0") +
			" action=Keep";
		Write(_actor, UnitActionLog.CoverHeartbeat, payload);
	}

	public static void HeartbeatRelease(
		Component _actor,
		int _coverId,
		CoverReservationReason _reason)
	{
		if (!UnitActionLog.Enabled || _coverId == 0)
			return;
		string payload =
			"cover=C" + _coverId +
			" action=Release" +
			" reason=" + _reason;
		Write(_actor, UnitActionLog.CoverHeartbeat, payload);
	}

	public static void MoveCover(
		Component _actor,
		in TacticalArrivalSituation _situation,
		in TacticalArrivalDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;
		int coverId = _decision.CandidateId != 0 ? _decision.CandidateId : _situation.CandidateId;
		if (coverId == 0 && _situation.Candidate == null)
			return;

		Vector3 acquire = _decision.AcquirePosition;
		if (acquire.sqrMagnitude < 0.0001f && _situation.Candidate != null)
			acquire = _situation.Candidate.Position;
		Vector3 dest = _situation.HasMoveDestination ? _situation.MoveDestination : _decision.MoveDestination;
		if (!_situation.HasMoveDestination && dest.sqrMagnitude < 0.0001f)
			dest = acquire;
		string remaining = _situation.HasNavRemaining
			? UnitActionLog.F2(_situation.NavRemainingDistance)
			: "n/a";
		string velocity = _situation.HasVelocity
			? UnitActionLog.F2(_situation.Velocity.magnitude)
			: "n/a";
		string payload =
			"cover=C" + coverId +
			" goal=" + UnitActionLog.Vec(dest) +
			" acquire=" + UnitActionLog.Vec(acquire) +
			" unitPos=" + UnitActionLog.Vec(_situation.CurrentPosition) +
			" agentPos=" + (_situation.HasAgentPosition
				? UnitActionLog.Vec(_situation.AgentPosition)
				: "n/a") +
			" remaining=" + remaining +
			" velocity=" + velocity +
			" stoppingDistance=" + (_situation.HasStoppingDistance
				? UnitActionLog.F2(_situation.StoppingDistance)
				: "n/a") +
			" agentRadius=" + (_situation.HasAgentRadius
				? UnitActionLog.F2(_situation.AgentRadius)
				: "n/a") +
			" distance=" + UnitActionLog.F2(_decision.DistanceMeters) +
			" pathStatus=" + (string.IsNullOrEmpty(_situation.PathStatus) ? "n/a" : _situation.PathStatus);
		Write(_actor, UnitActionLog.MoveCover, payload);
	}

	public static bool TryReadAgent(Component _actor, out NavMeshAgent _agent)
	{
		_agent = null;
		return _actor != null && _actor.TryGetComponent(out _agent) && _agent != null && _agent.enabled;
	}
	#endregion

	#region Private Methods
	private static void Write(Component _actor, string _channel, string _payload)
	{
		if (_actor != null)
			_payload = "unit=" + UnitActionLog.Slot(_actor) + " " + _payload;
		if (_actor != null)
			UnitActionLog.Write(_actor, _channel, _payload);
		UnitActionLog.Timeline(
			_channel,
			(_actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty) + _payload);
	}

	private static string UnitLabel(Component _actor, int _unitId)
	{
		if (_actor != null)
			return UnitActionLog.Slot(_actor);
		return _unitId != 0 ? _unitId.ToString() : "none";
	}
	#endregion
}
