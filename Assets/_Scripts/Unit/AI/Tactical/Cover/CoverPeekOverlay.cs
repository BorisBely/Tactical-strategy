using UnityEngine;

/// <summary>
/// Per-unit stationary peek. Event-driven. Requests existing spine lean. Never Fire. Never Move.
/// Moving-lean “when” is #14; this overlay only holds the executive pose while standing.
/// </summary>
public sealed class CoverPeekOverlay
{
	#region Nested
	private struct EvalKey
	{
		public int CandidateId;
		public int RegionX;
		public int RegionZ;
		public int GeometryVersion;
		public bool HasTarget;
		public int TargetQx;
		public int TargetQz;
		public CoverStance Stance;
		public CoverType CoverType;
		public bool Left;
		public bool Right;
		public bool VisibleWithoutLean;
		public bool PeekAllowed;
	}
	#endregion

	#region Private Fields
	private readonly CoverPeekSolver m_Solver = new CoverPeekSolver();
	private SharedCoverSpatialCache m_Cache;
	private CoverPeekDecision m_Last;
	private EvalKey m_Key;
	private bool m_HasKey;
	private CoverPeekDecisionKind m_AppliedKind;
	private CoverPeekDirection m_AppliedDirection;
	private CoverLeanLevel m_AppliedDepth;
	private float m_CommittedUntil;
	private int m_CacheHitCount;
	#endregion

	#region Public Properties
	public CoverPeekDecision Last => m_Last;
	public CoverPeekSolver Solver => m_Solver;
	public SharedCoverSpatialCache Cache => m_Cache;
	public int CacheHitCount => m_CacheHitCount;
	public int EvaluateCount => m_Solver.EvaluateCount;
	public float CommittedUntil => m_CommittedUntil;
	#endregion

	#region Public Methods
	public void BindCache(SharedCoverSpatialCache _cache)
	{
		m_Cache = _cache;
	}

	public void BindSettings(CoverPeekSettings _settings)
	{
		m_Solver.Settings = _settings;
	}

	public static bool AllowsPeek(UnitAIState _state)
	{
		return _state != UnitAIState.Retreat &&
		       _state != UnitAIState.Flee &&
		       _state != UnitAIState.Search;
	}

	public void Invalidate()
	{
		m_HasKey = false;
		m_Solver.Invalidate();
	}

	public CoverPeekDecision NotifyCommandChanged(
		ICoverLeanExecutor _executor,
		Component _logActor,
		float _now)
	{
		return ForceReturn(_executor, CoverPeekReason.CommandChanged, _logActor, _now);
	}

	public CoverPeekDecision NotifyFireFinished(
		ICoverLeanExecutor _executor,
		Component _logActor,
		float _now)
	{
		return ForceReturn(_executor, CoverPeekReason.FireFinished, _logActor, _now);
	}

	public CoverPeekDecision ForceReturn(
		ICoverLeanExecutor _executor,
		CoverPeekReason _reason,
		Component _logActor,
		float _now)
	{
		m_HasKey = false;
		m_CommittedUntil = 0f;
		if (m_AppliedKind != CoverPeekDecisionKind.Lean)
		{
			m_Last.Kind = CoverPeekDecisionKind.None;
			m_Last.FromCache = false;
			m_Last.Reason = _reason;
			return m_Last;
		}

		ApplyExecutor(_executor, CoverLeanLevel.None, CoverPeekDirection.None);
		m_Last.Kind = CoverPeekDecisionKind.Return;
		m_Last.Direction = CoverPeekDirection.None;
		m_Last.Depth = CoverLeanLevel.None;
		m_Last.Reason = _reason;
		m_Last.FromCache = false;
		m_AppliedKind = CoverPeekDecisionKind.None;
		LogLean(_logActor, in m_Last);
		return m_Last;
	}

	public CoverPeekDecision Update(
		UnitAIState _state,
		CoverCandidate _occupying,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los,
		ICoverOcclusionProbe _occlusion,
		ICoverLeanExecutor _executor,
		float _now,
		Component _logActor = null)
	{
		CoverPeekSides sides = CoverPeekGeometry.Sides(_occupying, _occlusion, m_Solver.Settings);
		return Update(_state, _occupying, in _situation, _los, sides, _executor, _now, _logActor);
	}

	public CoverPeekDecision Update(
		UnitAIState _state,
		CoverCandidate _occupying,
		in CoverSituation _situation,
		ICoverLineOfSightProbe _los,
		CoverPeekSides _sides,
		ICoverLeanExecutor _executor,
		float _now,
		Component _logActor = null)
	{
		CoverCandidate occupying = _occupying;
		CoverSituation situation = _situation;
		bool allowed = AllowsPeek(_state);
		if (!allowed)
		{
			if (m_AppliedKind == CoverPeekDecisionKind.Lean)
				return ForceReturn(_executor, CoverPeekReason.CommandChanged, _logActor, _now);
			m_Last.Kind = CoverPeekDecisionKind.None;
			m_Last.Reason = CoverPeekReason.NotApplicable;
			m_Last.FromCache = false;
			return m_Last;
		}

		if (occupying == null)
		{
			if (m_AppliedKind == CoverPeekDecisionKind.Lean)
				return ForceReturn(_executor, CoverPeekReason.PositionChanged, _logActor, _now);
			m_Last = default;
			m_Last.Reason = CoverPeekReason.NotApplicable;
			m_HasKey = false;
			return m_Last;
		}

		bool visibleWithoutLean = false;
		if (situation.HasTarget &&
		    CoverPeekGeometry.CanPeek(occupying.CoverType) &&
		    _sides.Any &&
		    _los != null)
		{
			Vector3 eye = CoverPeekGeometry.EyeWithoutLean(occupying, situation.Stance, m_Solver.Settings);
			visibleWithoutLean = _los.HasClearLook(eye, situation.TargetPosition);
		}

		EvalKey key = BuildKey(occupying, in situation, _sides, visibleWithoutLean, allowed);
		if (m_HasKey && KeysEqual(in m_Key, in key))
		{
			m_CacheHitCount++;
			m_Last.FromCache = true;
			return m_Last;
		}

		if (!situation.HasTarget && m_AppliedKind == CoverPeekDecisionKind.Lean)
			return ForceReturn(_executor, CoverPeekReason.TargetLost, _logActor, _now);

		CoverPeekDecision decision = m_Solver.Evaluate(occupying, in situation, _sides, _los);
		decision.FromCache = false;
		m_Last = FinalizeAndApply(_executor, decision, _now, _logActor);
		m_Key = key;
		m_HasKey = true;
		return m_Last;
	}
	#endregion

	#region Private Methods
	private CoverPeekDecision FinalizeAndApply(
		ICoverLeanExecutor _executor,
		CoverPeekDecision _decision,
		float _now,
		Component _logActor)
	{
		if (_decision.RequestsLean)
		{
			bool changed = m_AppliedKind != CoverPeekDecisionKind.Lean ||
			               m_AppliedDirection != _decision.Direction ||
			               m_AppliedDepth != _decision.Depth;
			if (changed)
				ApplyExecutor(_executor, _decision.Depth, _decision.Direction);
			m_AppliedKind = CoverPeekDecisionKind.Lean;
			m_AppliedDirection = _decision.Direction;
			m_AppliedDepth = _decision.Depth;
			m_CommittedUntil = _now + Mathf.Max(0.05f, m_Solver.Settings.CommitSeconds);
			if (changed)
			{
				LogPeek(_logActor, in _decision);
				LogLean(_logActor, in _decision);
			}

			return _decision;
		}

		if (m_AppliedKind == CoverPeekDecisionKind.Lean)
		{
			ApplyExecutor(_executor, CoverLeanLevel.None, CoverPeekDirection.None);
			m_AppliedKind = CoverPeekDecisionKind.None;
			m_AppliedDirection = CoverPeekDirection.None;
			m_AppliedDepth = CoverLeanLevel.None;
			m_CommittedUntil = 0f;
			CoverPeekDecision returned = _decision;
			returned.Kind = CoverPeekDecisionKind.Return;
			if (returned.Reason != CoverPeekReason.AlreadyVisible &&
			    returned.Reason != CoverPeekReason.CommandChanged &&
			    returned.Reason != CoverPeekReason.PositionChanged &&
			    returned.Reason != CoverPeekReason.FireFinished)
			{
				returned.Reason = CoverPeekReason.TargetLost;
			}

			LogLean(_logActor, in returned);
			return returned;
		}

		m_AppliedKind = CoverPeekDecisionKind.None;
		m_AppliedDirection = CoverPeekDirection.None;
		m_AppliedDepth = CoverLeanLevel.None;
		m_CommittedUntil = 0f;
		LogPeek(_logActor, in _decision);
		return _decision;
	}

	private static void ApplyExecutor(
		ICoverLeanExecutor _executor,
		CoverLeanLevel _level,
		CoverPeekDirection _direction)
	{
		_executor?.SetLean(_level, _direction);
	}

	private static EvalKey BuildKey(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		CoverPeekSides _sides,
		bool _visibleWithoutLean,
		bool _allowed)
	{
		return new EvalKey
		{
			CandidateId = _candidate.CandidateId,
			RegionX = _candidate.RegionId.X,
			RegionZ = _candidate.RegionId.Z,
			GeometryVersion = _situation.GeometryVersion != 0
				? _situation.GeometryVersion
				: _candidate.GeometryVersion,
			HasTarget = _situation.HasTarget,
			TargetQx = Quantize(_situation.TargetPosition.x),
			TargetQz = Quantize(_situation.TargetPosition.z),
			Stance = _situation.Stance,
			CoverType = _candidate.CoverType,
			Left = _sides.Left,
			Right = _sides.Right,
			VisibleWithoutLean = _visibleWithoutLean,
			PeekAllowed = _allowed
		};
	}

	private static bool KeysEqual(in EvalKey _a, in EvalKey _b)
	{
		return _a.CandidateId == _b.CandidateId &&
		       _a.RegionX == _b.RegionX &&
		       _a.RegionZ == _b.RegionZ &&
		       _a.GeometryVersion == _b.GeometryVersion &&
		       _a.HasTarget == _b.HasTarget &&
		       _a.TargetQx == _b.TargetQx &&
		       _a.TargetQz == _b.TargetQz &&
		       _a.Stance == _b.Stance &&
		       _a.CoverType == _b.CoverType &&
		       _a.Left == _b.Left &&
		       _a.Right == _b.Right &&
		       _a.VisibleWithoutLean == _b.VisibleWithoutLean &&
		       _a.PeekAllowed == _b.PeekAllowed;
	}

	private static int Quantize(float _value)
	{
		return Mathf.RoundToInt(_value * 2f);
	}

	private static void LogPeek(Component _actor, in CoverPeekDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;

		string payload =
			"candidate=C" + _decision.CandidateId +
			" direction=" + _decision.Direction +
			" available=" + (_decision.PeekAvailable ? "1" : "0") +
			" visibilityGain=" + UnitActionLog.F2(_decision.VisibilityGain) +
			" risk=" + UnitActionLog.F2(_decision.Risk) +
			" decision=" + (_decision.Kind == CoverPeekDecisionKind.Lean ? "Lean" : "NoLean");
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.Peek, payload);
		UnitActionLog.Timeline(
			UnitActionLog.Peek,
			(_actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty) + payload);
	}

	private static void LogLean(Component _actor, in CoverPeekDecision _decision)
	{
		if (!UnitActionLog.Enabled)
			return;

		string payload;
		if (_decision.Kind == CoverPeekDecisionKind.Return)
		{
			payload = "result=Return reason=" + _decision.Reason;
		}
		else
		{
			payload =
				"direction=" + _decision.Direction +
				" depth=" + _decision.Depth +
				" reason=" + _decision.Reason;
		}

		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.Lean, payload);
		UnitActionLog.Timeline(
			UnitActionLog.Lean,
			(_actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty) + payload);
	}
	#endregion
}
