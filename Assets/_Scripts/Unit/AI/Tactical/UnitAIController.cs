using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AI-1 FROZEN decision. Only this type changes <see cref="UnitAIState"/>. Orders enter via <see cref="TryApplyCommand"/>.
/// Perception may change <see cref="CurrentAction"/>; Search from LastKnown / Sound / Report is also applied here.
/// Search 2.0: area + cached candidates, does not write Memory. Does not call Fire(). Publishes <see cref="CombatIntent"/> only.
/// Stage 4 FROZEN: Search / Attack / Retreat / Flee walk via <see cref="IUnitMoveCommand"/> to a snapshotted destination.
/// #14: Attack/Defense/Retreat/Flee hop comes from <see cref="TacticalMovementOverlay"/>. Overlay does not Move.
/// #14B.3: ticks <see cref="ReadinessController"/> from perception / combat activity. Does not SetPose or Fire.
/// #14B.6: ticks ArmFatigue while bound (frozen on LifeGate). Physical AimTime / RecoilControl / yaw only.
/// #14C: ticks <see cref="ThreatDirectionController"/> from spawn pins and perception events. Does not Cover / Move / Aim.
/// #14C.1: cover preference overlay + event facing from threat sector. Does not rewrite CoverScore / 0.60 / Reservation.
/// Editor wiring: profiles on prefab; overlays stay owned here. Stage 2 CombatIntent FROZEN. Stage 3 Search decision FROZEN.
/// AI-1A: <see cref="UseOfForceLevel"/> is a separate field. <see cref="TrySetUseOfForcePolicy"/> does not change state.
/// Game orders (6.2): <see cref="GameCommandService"/> → <see cref="ITacticalCommandReceiver.IssueCommand"/>.
/// #11: <see cref="IssueCommand"/> goes through <see cref="UnitAICommandPriority"/> then the transition table.
/// Debug: <see cref="IUnitTacticalCommand"/> → <see cref="TryIssue"/>.
/// Editor wiring: <see cref="TacticalWorldProfile"/> + <see cref="InfantryTacticalProfile"/>. Overlays stay owned here.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(25)]
public sealed class UnitAIController : MonoBehaviour, ICombatIntentSource, IUnitTacticalCommand, ITacticalCommandReceiver
{
	#region Constants
	private const float c_CommandDefenseRadius = 10f;
	#endregion

	#region Private Fields
	[SerializeField] private bool m_DrawSearchHud;
	[SerializeField] private TacticalWorldProfile m_WorldProfile;
	[SerializeField] private InfantryTacticalProfile m_TacticalProfile;

	private readonly Dictionary<UnitAIState, IUnitAIStateHandler> m_Handlers =
		new Dictionary<UnitAIState, IUnitAIStateHandler>(6);
	private readonly List<string> m_Trace = new List<string>(32);
	private readonly AIPerceptionFrameScratch m_PerceptionScratch = new AIPerceptionFrameScratch();

	private IUnitAIStateHandler m_Handler;
	private UnitAIState m_State = UnitAIState.Idle;
	private UnitAIStateContext m_Context;
	private float m_StateTime;
	private bool m_Started;

	private IPerceivedContactRegistry m_Registry;
	private AIPerceptionSensor m_Sensor;
	private AIPerceptionFrame m_Perception = AIPerceptionFrame.Empty;
	private bool m_UseInjectedFrame;
	private UnitAIAction m_Action = UnitAIAction.None;
	private Transform m_EngageTarget;
	private bool m_HasHostileVisible;
	private UseOfForceLevel m_UseOfForceLevel = UseOfForceLevel.SelfDefense;
	private bool m_ImmediateThreat;
	private ForcePermission m_LastForcePermission;
	private bool m_SearchNavigationIssued;
	private bool m_SearchAreaReached;
	private bool m_TacticalNavigationIssued;
	private bool m_TacticalDestinationReached;
	private UnitAIState m_LastLoggedState = (UnitAIState)(-1);
	private UnitAIAction m_LastLoggedAction = (UnitAIAction)(-1);
	private EntityId m_LastLoggedEngageId;
	private UseOfForceLevel m_LastLoggedRoe = (UseOfForceLevel)(-1);
	private ImmediateThreatSource m_ThreatSource;
	private readonly UnitAISearchSession m_SearchSession = new UnitAISearchSession();
	private bool m_SearchSessionActive;
	private UnitAISearchCompletionReason m_LastSearchCompletionReason;
	private UnitAIPriorityEvaluation m_LastPriorityEvaluation;
	private bool m_LoggedThreatHold;
	private bool m_HasPendingCommand;
	private TacticalCommand m_PendingCommand;
	private readonly EmergencyCoverOverlay m_EmergencyCover = new EmergencyCoverOverlay();
	private readonly TacticalCoverOverlay m_TacticalCover = new TacticalCoverOverlay();
	private readonly CoverPeekOverlay m_PeekCover = new CoverPeekOverlay();
	private readonly TacticalMovementOverlay m_TacticalMovement = new TacticalMovementOverlay();
	private CoverOccupancyBoard m_CoverOccupancy;
	private ICoverLeanExecutor m_LeanExecutor;
	private ICoverLineOfSightProbe m_CoverLos;
	private ICoverOcclusionProbe m_CoverPeekOcclusion;
	private UnitHealth m_CoverHealth;
	private UnitConsciousness m_CoverConsciousness;
	private bool m_OccupancyReleasedOnIncapacitated;
	private CoverWeaponClass m_CoverWeapon = CoverWeaponClass.Rifle;
	private CoverRankClass m_CoverRank = CoverRankClass.Soldier;
	private readonly List<CoverCandidate> m_RouteCoverScratch = new List<CoverCandidate>(16);
	private float m_LastHostileVisibleAt = float.NegativeInfinity;
	private string m_PendingAiTransitionReason;
	private bool m_WorldBound;
	private int m_DebugHopLogs;
	private int m_DebugCoverLogs;
	private int m_DebugAcquireLogs;
	private float m_DebugHopLogAt;
	private float m_DebugAcquireLogAt = -1f;
	private readonly ReadinessController m_Readiness = new ReadinessController();
	private readonly ThreatDirectionController m_ThreatDirection = new ThreatDirectionController();
	private readonly ThreatDirectionFacingController m_ThreatFacing = new ThreatDirectionFacingController();
	private ThreatDirectionReorientation m_ThreatReorientation;
	private ThreatDirectionReposition m_ThreatReposition;
	private bool m_ReadinessBound;
	private bool m_ThreatDirectionBound;
	private bool m_ReadinessSawHostile;
	private bool m_LastArrivalWasAcquired;
	private UnitWeaponFireController m_FireController;
	#endregion

	#region Public Properties
	public UnitAIState CurrentState => m_State;
	public UnitAIStateContext CurrentContext => m_Context;
	public float StateTime => m_StateTime;
	public IReadOnlyList<string> Trace => m_Trace;
	public AIPerceptionFrame CurrentPerception => m_Perception;
	public UnitAIAction CurrentAction => m_Action;
	public Transform CurrentEngageTarget => m_EngageTarget;
	public bool HasHostileVisible => m_HasHostileVisible;
	public UseOfForceLevel CurrentUseOfForceLevel => m_UseOfForceLevel;
	public bool ImmediateThreat
	{
		get => m_ImmediateThreat;
		set
		{
			if (!value)
				m_ThreatSource?.ClearWindow();
			m_ImmediateThreat = value;
			m_TacticalMovement.NotifyImmediateThreat(value);
		}
	}
	public ForcePermission LastForcePermission => m_LastForcePermission;

	public CombatIntent CurrentCombatIntent =>
		CombatIntentMath.FromEngageAction(m_Action == UnitAIAction.Engage);

	public bool SearchNavigationIssued => m_SearchNavigationIssued;
	public bool SearchAreaReached => m_SearchAreaReached;
	public bool TacticalNavigationIssued => m_TacticalNavigationIssued;
	public bool TacticalDestinationReached => m_TacticalDestinationReached;
	public UnitAISearchSession SearchSession => m_SearchSessionActive ? m_SearchSession : null;
	public UnitAISearchCompletionReason LastSearchCompletionReason => m_LastSearchCompletionReason;
	public UnitAISearchArea CurrentSearchArea => m_SearchSession.Area;
	public UnitAIPriorityEvaluation LastPriorityEvaluation => m_LastPriorityEvaluation;
	public bool HasPendingCommand => m_HasPendingCommand;
	public TacticalCommand PendingCommand => m_PendingCommand;
	public bool EmergencyCoverActive => m_EmergencyCover.Last.Active;
	public Vector3 EmergencyCoverDestination => m_EmergencyCover.Last.Destination;
	public bool HasEmergencyCoverDestination => m_EmergencyCover.Last.HasDestination;
	public int EmergencyCoverSelectedCandidateId => m_EmergencyCover.Last.SelectedCandidateId;
	public EmergencyCoverDecision LastEmergencyCoverDecision => m_EmergencyCover.Last;
	public TacticalCoverDecision LastTacticalCoverDecision => m_TacticalCover.Last;
	public bool HasTacticalRepositionRequest =>
		m_TacticalCover.Last.Decision == TacticalCoverDecisionKind.Reposition &&
		m_TacticalCover.Last.HasDestination;
	public Vector3 TacticalCoverDestination => m_TacticalCover.Last.Destination;
	public CoverPeekDecision LastPeekDecision => m_PeekCover.Last;
	public CoverPeekOverlay PeekCover => m_PeekCover;
	public TacticalMovementOverlay TacticalMovement => m_TacticalMovement;
	public TacticalMovementDecision LastTacticalMovement => m_TacticalMovement.Last;
	public TacticalArrivalDecision LastTacticalArrival => m_TacticalMovement.LastArrival;
	public TacticalMovingLeanDecision LastMovingLean => m_TacticalMovement.LastMovingLean;
	public TacticalLodDecision LastLod => m_TacticalMovement.LastLod;
	public CoverOccupancyBoard CoverOccupancy => m_CoverOccupancy;
	public int CoverOccupancyUnitId => GetEntityId().GetHashCode();
	public TacticalWorldProfile WorldProfile => m_WorldProfile;
	public InfantryTacticalProfile TacticalProfile => m_TacticalProfile;
	public bool TacticalWorldBound => m_WorldBound;
	public ReadinessController Readiness => m_Readiness;
	public ThreatDirectionController ThreatDirection => m_ThreatDirection;
	public ThreatDirectionFacingController ThreatFacing => m_ThreatFacing;
	public ThreatDirectionReorientation ThreatReorientation
	{
		get
		{
			if (m_ThreatReorientation == null)
				m_ThreatReorientation = new ThreatDirectionReorientation(m_ThreatFacing);
			return m_ThreatReorientation;
		}
	}

	public ThreatDirectionReposition ThreatReposition
	{
		get
		{
			if (m_ThreatReposition == null)
				m_ThreatReposition = new ThreatDirectionReposition();
			return m_ThreatReposition;
		}
	}

	public bool ThreatRepositionAllowed => ThreatReposition.AllowsCoverReevaluation;

	public bool SearchHasMoveIntent =>
		TryGetComponent(out IUnitMoveCommand move) && move.HasMoveIntent;

	public UnitNavigationReason CurrentNavigationReason =>
		TryGetComponent(out IUnitMoveCommand move) ? move.Reason : UnitNavigationReason.None;

	public bool DrawSearchHud
	{
		get => m_DrawSearchHud;
		set => m_DrawSearchHud = value;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureStarted();
	}

	private void Update()
	{
		Tick(Time.deltaTime);
	}

	private void OnGUI()
	{
		if (!m_DrawSearchHud || Application.isBatchMode)
			return;

		TryGetLiveHostileLastKnown(out Vector3 liveKnown, out float liveConf);
		string liveKnownText = liveKnown.sqrMagnitude > 0.0001f || liveConf > 0f
			? liveKnown.ToString("F1")
			: "-";
		GUI.Box(new Rect(12f, 280f, 440f, 168f), "Tactical Nav");
		GUI.Label(new Rect(24f, 304f, 420f, 136f),
			$"{m_State} / {m_Action} / {CurrentCombatIntent}\n" +
			$"SearchPos={m_Context.SearchPosition:F1}  liveLastKnown={liveKnownText}\n" +
			$"conf={liveConf:F2}  radius={m_Context.AreaRadius:F0}  cue={m_Context.SearchCue}  resume={m_Context.ResumeState}\n" +
			$"search issued={m_SearchNavigationIssued} reached={m_SearchAreaReached} idx={m_SearchSession.Index} left={m_SearchSession.Remaining}\n" +
			$"dest={m_Context.Destination:F1} hasDest={m_Context.HasDestination} " +
			$"issued={m_TacticalNavigationIssued} reached={m_TacticalDestinationReached}\n" +
			$"intent={SearchHasMoveIntent} reason={CurrentNavigationReason}");
	}
	#endregion

	#region Public Methods
	public void EnsureStarted()
	{
		if (m_Started)
			return;

		RegisterHandlers();
		TryAutoBindPerception();
		m_State = UnitAIState.Idle;
		m_Context = UnitAIStateContext.Empty;
		m_StateTime = 0f;
		m_Handler = m_Handlers[UnitAIState.Idle];
		m_Handler.Enter(this, in m_Context);
		m_Trace.Add("Enter:Idle");
		m_Started = true;
		TryGetComponent(out m_ThreatSource);
		EnsureCombatReadiness();
		RefreshPerception();
		ResolveAction();
		m_TacticalMovement.BindScheduler(TacticalUpdateScheduler.Shared, CoverOccupancyUnitId);
		TryBindTacticalWorld();
	}

	public void AssignTacticalProfiles(TacticalWorldProfile _world, InfantryTacticalProfile _tactical)
	{
		m_WorldProfile = _world;
		m_TacticalProfile = _tactical;
	}

	private void OnDestroy()
	{
		ReleaseCoverOccupancy(CoverReservationReason.Death);
		m_PeekCover.ForceReturn(m_LeanExecutor, CoverPeekReason.PositionChanged, this, Time.time);
		if (m_CoverHealth != null)
			m_CoverHealth.Changed -= OnCoverHealthChanged;
		if (m_CoverConsciousness != null)
			m_CoverConsciousness.ConsciousnessChanged -= OnCoverConsciousnessChanged;
	}

	public void Tick(float _dt)
	{
		using (InfantryProfilerMarkers.AiTick.Auto())
		{
			EnsureStarted();
			TacticalUpdateScheduler.Shared.BeginTick(Time.frameCount, Time.time);
			UnitLifeState life = UnitLifeStateMath.Resolve(this);
			if (!UnitLifeStateMath.AllowsTactical(life))
			{
				NotifyLifeState(life);
				TickReadiness();
				return;
			}

			TickCoverOccupancy();
			if (_dt < 0f)
				_dt = 0f;
			m_StateTime += _dt;
			if (m_ThreatSource != null)
				m_ThreatSource.Tick(_dt);
			RefreshPerception();
			ResolveAction();
			TryAutonomousTransitions();
			if (m_Handler != null)
				m_Handler.Tick(this, _dt);
		}
	}

	/// <summary>
	/// Stage 6.1 game entry. Does not bounce through Idle. Does not call Fire, Navigate, or write Vision/RoE.
	/// Same-type reissue replaces context and re-enters the current handler so navigation restarts.
	/// #11: priority resolver runs after the transition table; ImmediateThreat is not a command.
	/// </summary>
	public TacticalCommandResult IssueCommand(in TacticalCommand _command)
	{
		EnsureStarted();
		LogCommand("issue", in _command, TacticalCommandRejectReason.None);

		if (!TryValidateIssuedCommand(in _command, out TacticalCommandRejectReason reason))
		{
			LogCommand("rejected", in _command, reason);
			return TacticalCommandResult.Rejected(reason);
		}

		if (!UnitLifeStateMath.AllowsTactical(UnitLifeStateMath.Resolve(this)))
		{
			LogCommand("rejected", in _command, TacticalCommandRejectReason.UnitUnavailable);
			return TacticalCommandResult.Rejected(TacticalCommandRejectReason.UnitUnavailable);
		}

		if (!TryMapIssuedCommand(in _command, out UnitAIState next, out UnitAIStateContext context))
		{
			LogCommand("rejected", in _command, TacticalCommandRejectReason.InvalidCommandData);
			return TacticalCommandResult.Rejected(TacticalCommandRejectReason.InvalidCommandData);
		}

		bool isCancel = _command.Type == TacticalCommandType.Cancel;
		if (next != m_State && !UnitAITransitionTable.CanTransition(m_State, next))
		{
			m_LastPriorityEvaluation = UnitAICommandPriority.Illegal(m_State, next);
			LogCmdPriority(_command.Type.ToString(), m_LastPriorityEvaluation);
			LogCommand("rejected", in _command, TacticalCommandRejectReason.InvalidStateTransition);
			return TacticalCommandResult.Rejected(TacticalCommandRejectReason.InvalidStateTransition);
		}

		m_LastPriorityEvaluation = UnitAICommandPriority.EvaluateCommand(m_State, next, isCancel);
		LogCmdPriority(_command.Type.ToString(), m_LastPriorityEvaluation);

		if (m_LastPriorityEvaluation.IsReject)
		{
			LogCommand("rejected", in _command, TacticalCommandRejectReason.LowerPriority);
			return TacticalCommandResult.Rejected(TacticalCommandRejectReason.LowerPriority);
		}

		ClearPendingCommand();
		m_PendingAiTransitionReason = "CommandChanged";
		ReleaseCoverOccupancy(CoverReservationReason.CommandChanged);
		ReturnPeek(CoverPeekReason.CommandChanged);
		if (m_State == UnitAIState.Search && next != UnitAIState.Search)
		{
			UnitAISearchCompletionReason completion = isCancel
				? UnitAISearchCompletionReason.Cancelled
				: UnitAISearchCompletionReason.NewOrder;
			SetSearchCompletion(completion, false);
		}

		ChangeState(next, context);
		LogCommand("accepted", in _command, TacticalCommandRejectReason.None);
		return TacticalCommandResult.Ok();
	}

	public bool TryApplyCommand(UnitAICommand _command)
	{
		EnsureStarted();
		RefreshPerception();
		if (_command.State == m_State)
		{
			ResolveAction();
			return true;
		}

		if (!UnitAITransitionTable.CanTransition(m_State, _command.State))
		{
			if (UnitActionLog.Enabled)
				UnitActionLog.Write(this, UnitActionLog.Ai, "reject cmd=" + _command.State + " from=" + m_State);
			return false;
		}

		m_LastPriorityEvaluation = UnitAICommandPriority.EvaluateInternal(m_State, _command.State);
		LogCmdPriority(_command.State.ToString(), m_LastPriorityEvaluation);
		if (string.IsNullOrEmpty(m_PendingAiTransitionReason))
			m_PendingAiTransitionReason = "CommandChanged";
		ReleaseCoverOccupancy(CoverReservationReason.CommandChanged);
		ReturnPeek(CoverPeekReason.CommandChanged);
		ChangeState(_command.State, _command.Context);
		return true;
	}

	public bool TrySetContext(in UnitAIStateContext _context)
	{
		EnsureStarted();
		m_Context = _context;
		return true;
	}

	public void BindPerception(IPerceivedContactRegistry _registry)
	{
		m_Registry = _registry;
		m_UseInjectedFrame = false;
		EnsureStarted();
		RefreshPerception();
		ResolveAction();
	}

	public void SetPerceptionFrame(in AIPerceptionFrame _frame)
	{
		m_Perception = _frame.AllContacts != null ? _frame : AIPerceptionFrame.Empty;
		m_UseInjectedFrame = true;
		EnsureStarted();
		ResolveAction();
	}

	public void ClearPerceptionOverride()
	{
		m_UseInjectedFrame = false;
		EnsureStarted();
		RefreshPerception();
		ResolveAction();
	}

	public void ClearTrace()
	{
		m_Trace.Clear();
	}

	public bool TrySetUseOfForcePolicy(UseOfForceLevel _level)
	{
		EnsureStarted();
		UseOfForceLevel previous = m_UseOfForceLevel;
		m_UseOfForceLevel = _level;
		if (UnitActionLog.Enabled && previous != _level)
			UnitActionLog.Write(this, UnitActionLog.Ai, "roe=" + previous + "->" + _level);
		return true;
	}

	public ForcePermission EvaluateForce(in AIContactKnowledge _knowledge)
	{
		return EvaluateForce(_knowledge.Target != null, _knowledge.Relationship, _knowledge.Target);
	}

	public ImmediateThreatSource EnsureImmediateThreatSource()
	{
		EnsureStarted();
		if (m_ThreatSource == null)
			TryGetComponent(out m_ThreatSource);
		if (m_ThreatSource == null)
			m_ThreatSource = gameObject.AddComponent<ImmediateThreatSource>();
		return m_ThreatSource;
	}

	public void NotifyHostileAttack(Component _attacker, ImmediateThreatCause _cause)
	{
		EnsureImmediateThreatSource().NotifyHostileAttack(_attacker, _cause);
	}

	public void BindCoverCache(SharedCoverSpatialCache _cache)
	{
		m_EmergencyCover.BindCache(_cache);
		m_TacticalCover.BindCache(_cache);
		m_PeekCover.BindCache(_cache);
	}

	public void BindCoverLos(ICoverLineOfSightProbe _los)
	{
		m_CoverLos = _los;
	}

	public void BindCoverPeekOcclusion(ICoverOcclusionProbe _probe)
	{
		m_CoverPeekOcclusion = _probe;
	}

	public void BindCoverLeanExecutor(ICoverLeanExecutor _executor)
	{
		m_LeanExecutor = _executor;
	}

	public void BindCoverOccupancy(CoverOccupancyBoard _occupancy)
	{
		m_CoverOccupancy = _occupancy;
		m_EmergencyCover.BindOccupancy(_occupancy);
		m_TacticalCover.BindOccupancy(_occupancy);
		m_TacticalMovement.BindOccupancy(_occupancy, CoverOccupancyUnitId);
		if (m_CoverHealth == null)
			TryGetComponent(out m_CoverHealth);
		if (m_CoverHealth != null)
		{
			m_CoverHealth.Changed -= OnCoverHealthChanged;
			m_CoverHealth.Changed += OnCoverHealthChanged;
		}

		if (m_CoverConsciousness == null)
			TryGetComponent(out m_CoverConsciousness);
		if (m_CoverConsciousness != null)
		{
			m_CoverConsciousness.ConsciousnessChanged -= OnCoverConsciousnessChanged;
			m_CoverConsciousness.ConsciousnessChanged += OnCoverConsciousnessChanged;
		}

		UnitLifeState life = UnitLifeStateMath.Resolve(this);
		if (UnitLifeStateMath.RequiresCoverRelease(life))
		{
			m_OccupancyReleasedOnIncapacitated = false;
			NotifyLifeState(life);
		}
	}

	public void BindCoverProfile(CoverWeaponClass _weapon, CoverRankClass _rank)
	{
		m_CoverWeapon = _weapon;
		m_CoverRank = _rank;
	}

	/// <summary>
	/// Life is not an AI state. Unconscious / Dead release cover. Overlay does not Move.
	/// </summary>
	public void NotifyLifeState(UnitLifeState _state)
	{
		if (_state == UnitLifeState.Alive)
		{
			m_OccupancyReleasedOnIncapacitated = false;
			if (m_ReadinessBound)
				m_Readiness.SetAllowed(true);
			return;
		}

		if (m_ReadinessBound)
			m_Readiness.SetAllowed(false);

		if (m_OccupancyReleasedOnIncapacitated)
			return;

		m_OccupancyReleasedOnIncapacitated = true;
		CoverReservationReason reason = _state == UnitLifeState.Dead
			? CoverReservationReason.Death
			: CoverReservationReason.Unconscious;
		ReleaseCoverOccupancy(reason);
		m_TacticalMovement.ReleaseOccupancyHold();
		ReturnPeek(CoverPeekReason.PositionChanged);
	}

	/// <summary>
	/// #14.1: Destination stays the goal. Current hop comes from selected route. Overlay does not Move.
	/// Production without <see cref="InfantryTacticalProfile.UseCover"/> still queries Normal (Direct typically wins).
	/// With UseCover, mode comes from the profile; a #13 RepositionRequest becomes the walk goal without rewriting Attack context.
	/// 14.5: ImmediateThreat is an edge event, not a per-frame replan.
	/// 14.6: under-fire reaction may Continue / Replan / EmergencyCover; overlay still does not Move.
	/// 14.7: navigation Reached is not tactical acquire.
	/// 14.9: scheduler decides when overlay may re-evaluate; hop still comes from last route.
	/// </summary>
	public Vector3 ResolvePointMovementHop(bool _hasDestination, Vector3 _destination)
	{
		if (!_hasDestination)
			return _destination;
		TryBindTacticalWorld();
		if (m_CoverOccupancy != null)
			m_TacticalMovement.BindOccupancy(m_CoverOccupancy, CoverOccupancyUnitId);
		m_TacticalMovement.NotifyImmediateThreat(m_ImmediateThreat);
		TacticalLodSituation lodHints = BuildLodHints();
		m_TacticalMovement.SetLodHints(in lodHints);
		Vector3 goal = ResolveCoverAwareGoal(_destination);
		TacticalMovementMode mode = ResolveMovementMode();
		TacticalRouteSituation situation = BuildRouteSituation(goal, mode);
		TacticalMovementDecision decision = m_TacticalMovement.Update(in situation, this);
		Vector3 hop = decision.HasRoute ? decision.CurrentHop : goal;
		LogHopDebug(goal, mode, hop, in decision);
		TryCaptureAcquireLive(hop);
		return hop;
	}

	internal void SetImmediateThreatFlag(bool _value)
	{
		m_ImmediateThreat = _value;
		m_TacticalMovement.NotifyImmediateThreat(_value);
	}

	public ForcePermission EvaluateForce(bool _hasContact, PerceivedRelationship _relationship, Transform _target = null)
	{
		EnsureStarted();
		var context = new UseOfForceContext
		{
			Level = m_UseOfForceLevel,
			HasContact = _hasContact,
			Relationship = _relationship,
			ImmediateThreat = m_ImmediateThreat,
			Target = _target,
			State = m_State
		};
		m_LastForcePermission = UseOfForceEvaluator.Evaluate(in context);
		return m_LastForcePermission;
	}

	public bool TryResumeSearchBecauseNavigationFailed()
	{
		EnsureStarted();
		if (m_State != UnitAIState.Search)
			return false;
		return TryApplyCommand(BuildResumeOrFoundCommand(false));
	}

	public void NotifySearchNavigationIssued()
	{
		m_SearchNavigationIssued = true;
	}

	public void NotifySearchAreaReached()
	{
		m_SearchAreaReached = true;
	}

	public void ClearSearchArrival()
	{
		m_SearchAreaReached = false;
	}

	public void ClearSearchNavigationDebug()
	{
		m_SearchNavigationIssued = false;
		m_SearchAreaReached = false;
	}

	public void BeginSearchSession()
	{
		float now = Time.time;
		UnitAISearchArea area;
		if (!UnitAISearchDecision.TryBuildSearchArea(in m_Perception, now, out area))
		{
			float radius = m_Context.AreaRadius > 0.01f
				? m_Context.AreaRadius
				: UnitAISearchDecision.DefaultAreaRadius;
			UnitAISearchCue cue = m_Context.SearchCue != UnitAISearchCue.None
				? m_Context.SearchCue
				: UnitAISearchCue.VisualMemory;
			area = new UnitAISearchArea(m_Context.SearchPosition, radius, cue, 1f, now);
		}

		m_SearchSession.Reset(area);
		m_SearchSessionActive = true;
		m_LastSearchCompletionReason = UnitAISearchCompletionReason.None;
		ISearchReachability reach = UnitAISearchAlwaysReachable.Instance;
		if (Application.isPlaying &&
		    NavMesh.SamplePosition(transform.position, out _, 1.5f, NavMesh.AllAreas))
			reach = UnitAISearchNavMeshReachability.Instance;
		UnitAISearchPlanner.Build(
			in area,
			transform.position,
			now,
			reach,
			m_SearchSession.CandidateBuffer);
		m_SearchSession.BindBuiltPlan();
		ApplyCurrentSearchCandidateToContext();
		LogSearch(
			"source=" + area.Source +
			" area=" + UnitActionLog.Vec(area.Center) +
			" r=" + UnitActionLog.F1(area.Radius) +
			" candidates=" + m_SearchSession.Candidates.Count);
		LogSearchCandidate("Active");
	}

	public bool TryAdvanceSearchCandidate()
	{
		if (!m_SearchSessionActive || !m_SearchSession.TryAdvance())
			return false;
		ApplyCurrentSearchCandidateToContext();
		LogSearchCandidate("Active");
		return true;
	}

	public void NotifySearchCandidateInspected()
	{
		LogSearchCandidate("Checked");
	}

	public bool TryCompleteSearchBecauseExhausted()
	{
		m_PendingAiTransitionReason = "Exhausted";
		return CompleteSearch(UnitAISearchCompletionReason.Exhausted, false);
	}

	public void FinishSearchSessionIfOpen()
	{
		if (!m_SearchSessionActive)
			return;
		if (m_SearchSession.Completion == UnitAISearchCompletionReason.None)
		{
			m_SearchSession.SetCompletion(UnitAISearchCompletionReason.Cancelled);
			m_LastSearchCompletionReason = UnitAISearchCompletionReason.Cancelled;
			LogSearch("result=Cancelled");
		}

		m_SearchSessionActive = false;
	}

	public void NotifyTacticalNavigationIssued()
	{
		m_TacticalNavigationIssued = true;
	}

	public void NotifyTacticalDestinationReached()
	{
		m_TacticalDestinationReached = true;
	}

	/// <summary>
	/// 14.7: NavMesh reached the hop. Tactical acquire / reject is separate.
	/// Search does not call this. Overlay does not Move and does not change mission.
	/// </summary>
	public TacticalArrivalDecision NotifyTacticalArrival()
	{
		Vector3 moveDest = m_TacticalMovement.Route != null && m_TacticalMovement.Route.HasDestination
			? m_TacticalMovement.Route.CurrentHop
			: transform.position;
		var sit = new TacticalArrivalSituation
		{
			NavigationReached = true,
			CurrentPosition = transform.position,
			MoveDestination = moveDest,
			HasMoveDestination = true,
			Now = Time.time,
			UnitId = CoverOccupancyUnitId,
			Occupancy = m_CoverOccupancy,
			GeometryVersion = m_CoverOccupancy != null ? m_CoverOccupancy.GeometryVersion : 0,
			MissionState = m_State,
			AcquireToleranceMeters = TacticalArrivalMath.DefaultAcquireToleranceMeters
		};
		if (TryGetComponent(out NavMeshAgent agent) && agent.enabled)
		{
			sit.HasAgentPosition = true;
			sit.AgentPosition = agent.nextPosition;
			sit.HasVelocity = true;
			sit.Velocity = agent.velocity;
			sit.HasStoppingDistance = true;
			sit.StoppingDistance = agent.stoppingDistance;
			sit.HasAgentRadius = true;
			sit.AgentRadius = agent.radius;
			if (!agent.isOnNavMesh)
				sit.PathStatus = "none";
			else if (agent.pathPending)
				sit.PathStatus = "pending";
			else if (!agent.hasPath)
				sit.PathStatus = "none";
			else
				sit.PathStatus = agent.pathStatus.ToString();
			if (agent.isOnNavMesh && !float.IsPositiveInfinity(agent.remainingDistance))
			{
				sit.HasNavRemaining = true;
				sit.NavRemainingDistance = agent.remainingDistance;
			}
		}

		TacticalArrivalDecision decision = m_TacticalMovement.NotifyTacticalArrival(in sit, this);
		LogAcquireDebug(in sit, in decision);
		TryCaptureAcquireDebug(in sit, in decision);
		bool coverApproach =
			TacticalArrivalMath.IsTransientAcquireMiss(decision.Reason) &&
			m_TacticalMovement.CurrentHopRequiresCoverAcquire;
		bool acquired = decision.Result == TacticalArrivalResult.Acquired;
		if (acquired)
			m_TacticalDestinationReached = true;
		else if (decision.Result != TacticalArrivalResult.Traversed && !coverApproach)
			m_TacticalDestinationReached = true;
		if (acquired && !m_LastArrivalWasAcquired)
			NotifyThreatFacing(ThreatDirectionFacingReason.CoverAcquired);
		m_LastArrivalWasAcquired = acquired;
		return decision;
	}

	public void ClearTacticalNavigationDebug()
	{
		m_TacticalNavigationIssued = false;
		m_TacticalDestinationReached = false;
	}

	public bool TryCompleteFleeBecauseReached()
	{
		EnsureStarted();
		if (m_State != UnitAIState.Flee)
			return false;
		return TryApplyCommand(UnitAICommand.Idle());
	}

	public bool TryGetLiveHostileLastKnown(out Vector3 _position, out float _confidence)
	{
		_position = default;
		_confidence = 0f;
		IReadOnlyList<AIContactKnowledge> all = m_Perception.AllContacts;
		if (all == null || all.Count == 0)
			return false;

		for (int i = 0; i < all.Count; i++)
		{
			AIContactKnowledge contact = all[i];
			if (!contact.Hostile)
				continue;
			_position = contact.LastKnownPosition;
			_confidence = contact.LastSeenConfidence;
			return true;
		}

		return false;
	}

	public bool SetIdle()
	{
		return TryIssue(UnitAICommand.Idle());
	}

	public bool SetDefense(Vector3 _point)
	{
		Vector3 facing = PlanarDirection(_point, transform.position, transform.forward);
		return TryIssue(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(_point, _point, c_CommandDefenseRadius, facing)));
	}

	public bool SetAttack(Vector3 _point, Transform _target = null)
	{
		Vector3 destination = _target != null ? _target.position : _point;
		Vector3 direction = PlanarDirection(destination, transform.position, transform.forward);
		return TryIssue(UnitAICommand.Attack(
			UnitAIStateContext.ForAttack(destination, direction, _target)));
	}

	public bool SetSearch()
	{
		EnsureStarted();
		RefreshPerception();
		if (!UnitAISearchDecision.TryBuildSearchArea(in m_Perception, Time.time, out UnitAISearchArea area))
			return false;
		return TryIssue(UnitAICommand.Search(BuildIssuedSearchContext(area.Center, area.Source)));
	}

	public bool SetSearch(Vector3 _point)
	{
		return TryIssue(UnitAICommand.Search(BuildIssuedSearchContext(_point)));
	}

	public bool SetRetreat(Vector3 _point)
	{
		return TryIssue(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(_point)));
	}

	public bool SetFlee(Vector3 _point)
	{
		Vector3 direction = PlanarDirection(_point, transform.position, transform.forward);
		return TryIssue(UnitAICommand.Flee(UnitAIStateContext.ForFlee(direction, _point)));
	}
	#endregion

	#region Private Methods
	private void TryBindTacticalWorld()
	{
		if (m_WorldBound)
			return;
		if (IsIsolatedTacticalHarness())
			return;
		if (m_WorldProfile == null && (m_TacticalProfile == null || !m_TacticalProfile.UseCover))
			return;
		TacticalWorld world = TacticalWorld.Find(m_WorldProfile);
		if (world == null)
		{
			// #region agent log
			AgentDebugNdjson.Write(
				"B",
				"UnitAIController.TryBindTacticalWorld",
				"no world",
				"{\"hasWorldProfile\":" + (m_WorldProfile != null ? "true" : "false") + "}");
			// #endregion
			return;
		}

		world.EnsureRuntime();
		BindCoverCache(world.Cache);
		if (m_TacticalProfile == null || m_TacticalProfile.AllowCoverReservation)
			BindCoverOccupancy(world.Occupancy);
		EnsureCoverPeekBindings();
		m_WorldBound = true;
		// #region agent log
		AgentDebugNdjson.Write(
			"B",
			"UnitAIController.TryBindTacticalWorld",
			"bound",
			"{\"baked\":" + world.BakedCount +
			",\"useCover\":" + (m_TacticalProfile != null && m_TacticalProfile.UseCover ? "true" : "false") +
			",\"mode\":\"" + ResolveMovementMode() + "\"}");
		// #endregion
	}

	private static bool IsIsolatedTacticalHarness()
	{
		return DetectionHarnessPlayMode.IsCalibrationPlay ||
		       DetectionHarnessPlayMode.IsGRegressionPlay ||
		       DetectionHarnessPlayMode.RunTacticalMovement ||
		       DetectionHarnessPlayMode.RunCoverGeneration ||
		       DetectionHarnessPlayMode.RunCoverClassification ||
		       DetectionHarnessPlayMode.RunCoverEvaluation ||
		       DetectionHarnessPlayMode.RunCoverEmergency ||
		       DetectionHarnessPlayMode.RunCoverTactical ||
		       DetectionHarnessPlayMode.RunCoverOccupancy ||
		       DetectionHarnessPlayMode.RunCoverPeek ||
		       DetectionHarnessPlayMode.RunCoverIntegration;
	}

	private bool UsesCover()
	{
		return m_TacticalProfile != null && m_TacticalProfile.UseCover && m_WorldBound;
	}

	private TacticalMovementMode ResolveMovementMode()
	{
		if (!UsesCover())
			return TacticalMovementMode.Normal;
		return m_TacticalProfile.MovementMode;
	}

	private Vector3 ResolveCoverAwareGoal(Vector3 _destination)
	{
		if (!UsesCover())
			return _destination;
		if (m_ImmediateThreat && HasEmergencyCoverDestination)
			return EmergencyCoverDestination;
		CoverCandidate reserved = m_TacticalMovement.ReservedCoverCandidate;
		if (reserved != null)
			return reserved.Position;
		if (m_TacticalMovement.CurrentTacticalPosition.Valid)
			return m_TacticalMovement.CurrentTacticalPosition.Position;
		if (HasTacticalRepositionRequest)
			return TacticalCoverDestination;
		return _destination;
	}

	private TacticalRouteSituation BuildRouteSituation(Vector3 _goal, TacticalMovementMode _mode)
	{
		SharedCoverSpatialCache cache = m_TacticalCover.Cache;
		FillRouteCovers(cache);
		int reservedId = m_TacticalMovement.ReservedCoverCandidateId;
		int finalId = reservedId;
		if (finalId == 0 && UsesCover())
		{
			if (m_ImmediateThreat && HasEmergencyCoverDestination)
				finalId = EmergencyCoverSelectedCandidateId;
			else if (HasTacticalRepositionRequest)
				finalId = m_TacticalCover.Last.SelectedCandidateId;
		}

		return new TacticalRouteSituation
		{
			Origin = transform.position,
			Destination = _goal,
			HasDestination = true,
			Mode = _mode,
			HasKnownThreat = m_HasHostileVisible || m_ImmediateThreat,
			WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed,
			CoverCache = cache,
			CoverCandidates = m_RouteCoverScratch,
			Occupancy = m_CoverOccupancy,
			OccupancyUnitId = CoverOccupancyUnitId,
			Now = Time.time,
			FinalCoverCandidateId = finalId
		};
	}

	private void FillRouteCovers(SharedCoverSpatialCache _cache)
	{
		m_RouteCoverScratch.Clear();
		if (_cache != null)
		{
			IReadOnlyList<CoverCandidate> nearby = _cache.GetCandidates(transform.position);
			if (nearby != null)
			{
				for (int i = 0; i < nearby.Count; i++)
				{
					if (nearby[i] != null)
						m_RouteCoverScratch.Add(nearby[i]);
				}
			}
		}

		EnsureRouteCover(m_TacticalCover.Last.Selected);
		EnsureRouteCover(m_TacticalMovement.ReservedCoverCandidate);
	}

	private void EnsureRouteCover(CoverCandidate _cover)
	{
		if (_cover == null)
			return;
		for (int i = 0; i < m_RouteCoverScratch.Count; i++)
		{
			if (m_RouteCoverScratch[i] != null &&
			    m_RouteCoverScratch[i].CandidateId == _cover.CandidateId)
				return;
		}

		m_RouteCoverScratch.Add(_cover);
	}

	private void LogHopDebug(
		Vector3 _goal,
		TacticalMovementMode _mode,
		Vector3 _hop,
		in TacticalMovementDecision _decision)
	{
		if (m_DebugHopLogs >= 8 && Time.time - m_DebugHopLogAt < 2f)
			return;
		m_DebugHopLogs++;
		m_DebugHopLogAt = Time.time;
		string hypo = UsesCover() ? "C" : "C";
		if (HasTacticalRepositionRequest)
			hypo = "D";
		// #region agent log
		AgentDebugNdjson.Write(
			hypo,
			"UnitAIController.ResolvePointMovementHop",
			"hop",
			"{\"mode\":\"" + _mode +
			"\",\"useCover\":" + (UsesCover() ? "true" : "false") +
			",\"reposition\":" + (HasTacticalRepositionRequest ? "true" : "false") +
			",\"emergency\":" + (HasEmergencyCoverDestination ? "true" : "false") +
			",\"kind\":\"" + _decision.Kind +
			"\",\"goalX\":" + _goal.x.ToString("0.0") +
			",\"goalZ\":" + _goal.z.ToString("0.0") +
			",\"hopX\":" + _hop.x.ToString("0.0") +
			",\"hopZ\":" + _hop.z.ToString("0.0") +
			",\"coverReason\":\"" + m_TacticalCover.Last.Reason + "\"}");
		// #endregion
	}

	private void LogAcquireDebug(in TacticalArrivalSituation _situation, in TacticalArrivalDecision _decision)
	{
		bool transient = TacticalArrivalMath.IsTransientAcquireMiss(_decision.Reason);
		if (transient && m_DebugAcquireLogAt >= 0f && Time.time - m_DebugAcquireLogAt < 0.45f)
			return;
		m_DebugAcquireLogs++;
		m_DebugAcquireLogAt = Time.time;
		Vector3 acquire = _decision.AcquirePosition;
		Vector3 dest = _decision.MoveDestination.sqrMagnitude > 0.0001f
			? _decision.MoveDestination
			: (_situation.HasMoveDestination ? _situation.MoveDestination : acquire);
		string rem = _situation.HasNavRemaining
			? _situation.NavRemainingDistance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
			: "n/a";
		string agent = _situation.HasAgentPosition
			? "{\"x\":" + _situation.AgentPosition.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			  ",\"z\":" + _situation.AgentPosition.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "}"
			: "null";
		// #region agent log
		AgentDebugNdjson.Write(
			"E",
			"UnitAIController.NotifyTacticalArrival",
			"acquire",
			"{\"result\":\"" + _decision.Result +
			"\",\"reason\":\"" + _decision.Reason +
			"\",\"candidateId\":" + _decision.CandidateId +
			",\"unitX\":" + _situation.CurrentPosition.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"unitZ\":" + _situation.CurrentPosition.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"destX\":" + dest.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"destZ\":" + dest.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"acquireX\":" + acquire.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"acquireZ\":" + acquire.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"dist\":" + _decision.DistanceMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"tol\":" + TacticalArrivalMath.ResolveTolerance(_situation.AcquireToleranceMeters)
				.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"remaining\":\"" + rem +
			"\",\"navRadius\":" + TacticalArrivalMath.ArrivalRadiusForHop(
				m_TacticalMovement.CurrentHopRequiresCoverAcquire)
				.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
			",\"agent\":" + agent + "}");
		// #endregion
	}

	private void TryCaptureAcquireLive(Vector3 _hop)
	{
		if (!TryGetComponent(out TacticalMovementDebugDraw draw))
			return;
		TacticalMovementDecision last = m_TacticalMovement.Last;
		draw.Capture(in last, transform.position);
		float remaining = -1f;
		if (TryGetComponent(out NavMeshAgent agent) &&
		    agent.enabled &&
		    agent.isOnNavMesh &&
		    !float.IsPositiveInfinity(agent.remainingDistance))
			remaining = agent.remainingDistance;
		int coverId = m_TacticalMovement.LastArrival.CandidateId != 0
			? m_TacticalMovement.LastArrival.CandidateId
			: m_TacticalMovement.ReservedCoverCandidateId;
		Vector3 acquire = _hop;
		if (m_TacticalMovement.LastArrival.Position.Valid)
			acquire = m_TacticalMovement.LastArrival.Position.Position;
		draw.CaptureAcquireLive(
			_hop,
			acquire,
			TacticalArrivalMath.DefaultAcquireToleranceMeters,
			remaining,
			coverId,
			m_TacticalMovement.ReservedCoverCandidateId,
			m_TacticalMovement.CurrentTacticalPosition.Occupied);
	}

	private void TryCaptureAcquireDebug(
		in TacticalArrivalSituation _situation,
		in TacticalArrivalDecision _decision)
	{
		if (!TryGetComponent(out TacticalMovementDebugDraw draw))
			return;
		draw.CaptureArrival(in _decision);
		Vector3 acquire = _decision.AcquirePosition;
		Vector3 dest = _decision.MoveDestination.sqrMagnitude > 0.0001f
			? _decision.MoveDestination
			: (_situation.HasMoveDestination ? _situation.MoveDestination : acquire);
		draw.CaptureAcquireLive(
			dest,
			acquire,
			TacticalArrivalMath.ResolveTolerance(_situation.AcquireToleranceMeters),
			_situation.HasNavRemaining ? _situation.NavRemainingDistance : -1f,
			_decision.CandidateId,
			m_TacticalMovement.ReservedCoverCandidateId,
			_decision.Result == TacticalArrivalResult.Acquired ||
			m_TacticalMovement.CurrentTacticalPosition.Occupied);
	}

	private TacticalLodSituation BuildLodHints()
	{
		bool hasRoute = m_TacticalMovement.Last.HasRoute && !m_TacticalDestinationReached;
		float playerDistance;
		bool hasPlayer = TryPlayerDistance(out playerDistance);
		return new TacticalLodSituation
		{
			Now = Time.time,
			PreviousTier = m_TacticalMovement.LastLod.Tier,
			Idle = m_State == UnitAIState.Idle || !hasRoute,
			HasActiveTacticalMovement = hasRoute,
			UnderFire = m_TacticalMovement.LastUnderFire.Action != TacticalUnderFireAction.None,
			InCombat = m_HasHostileVisible || m_ImmediateThreat || m_State == UnitAIState.Attack,
			SeesHostile = m_HasHostileVisible,
			HasImmediateThreat = m_ImmediateThreat,
			IncomingFire = m_ImmediateThreat,
			HasPlayerDistance = hasPlayer,
			DistanceToPlayerMeters = playerDistance,
			CurrentlyLeaning = m_TacticalMovement.MovingLeanActive,
			Arriving = m_TacticalMovement.LastArrival.IsAcquired
		};
	}

	private bool TryPlayerDistance(out float _meters)
	{
		_meters = 0f;
		Camera camera = Camera.main;
		if (camera == null)
			return false;
		_meters = Mathf.Sqrt(
			CoverSpatialMath.PlanarDistanceSqr(transform.position, camera.transform.position));
		return true;
	}

	private bool TryValidateIssuedCommand(in TacticalCommand _command, out TacticalCommandRejectReason _reason)
	{
		_reason = TacticalCommandRejectReason.None;
		if (!IsKnownCommandType(_command.Type))
		{
			_reason = TacticalCommandRejectReason.InvalidCommandData;
			return false;
		}

		if (_command.Type == TacticalCommandType.Cancel)
			return true;

		if (!_command.HasPosition)
		{
			_reason = TacticalCommandRejectReason.MissingDestination;
			return false;
		}

		if (!IsFinitePosition(_command.Position))
		{
			_reason = TacticalCommandRejectReason.InvalidCommandData;
			return false;
		}

		return true;
	}

	private bool TryMapIssuedCommand(
		in TacticalCommand _command,
		out UnitAIState _state,
		out UnitAIStateContext _context)
	{
		_state = UnitAIState.Idle;
		_context = UnitAIStateContext.Empty;
		Vector3 facing = transform.forward;
		switch (_command.Type)
		{
			case TacticalCommandType.Defense:
				_state = UnitAIState.Defense;
				_context = UnitAIStateContext.ForDefense(
					_command.Position,
					_command.Position,
					c_CommandDefenseRadius,
					PlanarDirection(_command.Position, transform.position, facing));
				return true;
			case TacticalCommandType.Attack:
				_state = UnitAIState.Attack;
				_context = UnitAIStateContext.ForAttack(
					_command.Position,
					PlanarDirection(_command.Position, transform.position, facing),
					_command.Target);
				return true;
			case TacticalCommandType.Search:
				_state = UnitAIState.Search;
				_context = BuildIssuedSearchContext(_command.Position);
				return true;
			case TacticalCommandType.Retreat:
				_state = UnitAIState.Retreat;
				_context = UnitAIStateContext.ForRetreat(_command.Position);
				return true;
			case TacticalCommandType.Flee:
				_state = UnitAIState.Flee;
				_context = UnitAIStateContext.ForFlee(
					PlanarDirection(_command.Position, transform.position, facing),
					_command.Position);
				return true;
			case TacticalCommandType.Cancel:
				if (m_State == UnitAIState.Search)
				{
					UnitAICommand resume = BuildResumeOrFoundCommand(false);
					_state = resume.State;
					_context = resume.Context;
					return true;
				}

				_state = UnitAIState.Idle;
				_context = UnitAIStateContext.Empty;
				return true;
			default:
				return false;
		}
	}

	private void LogCmdPriority(string _incoming, in UnitAIPriorityEvaluation _eval)
	{
		if (!UnitActionLog.Enabled)
			return;

		string payload =
			"incoming=" + _incoming +
			" current=" + _eval.CurrentState +
			" result=" + _eval.Decision +
			" reason=" + _eval.Reason +
			" kind=" + _eval.Kind;
		UnitActionLog.Write(this, UnitActionLog.CmdPriority, payload);
		UnitActionLog.Timeline(
			UnitActionLog.CmdPriority,
			"actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private void ClearPendingCommand()
	{
		m_HasPendingCommand = false;
		m_PendingCommand = default;
	}

	private void LogCommand(string _verb, in TacticalCommand _command, TacticalCommandRejectReason _reason)
	{
		if (!UnitActionLog.Enabled)
			return;

		string pos = _command.HasPosition ? UnitActionLog.Vec(_command.Position) : "none";
		string tgt = _command.Target != null ? UnitActionLog.Slot(_command.Target) : "none";
		string payload =
			_verb +
			" type=" + _command.Type +
			" pos=" + pos +
			" tgt=" + tgt +
			" source=" + _command.Source +
			" from=" + m_State;
		if (_reason != TacticalCommandRejectReason.None)
			payload += " reason=" + _reason;
		UnitActionLog.Write(this, UnitActionLog.Cmd, payload);
		UnitActionLog.Timeline(UnitActionLog.Cmd, "actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private static bool IsKnownCommandType(TacticalCommandType _type)
	{
		return _type == TacticalCommandType.Defense ||
		       _type == TacticalCommandType.Attack ||
		       _type == TacticalCommandType.Search ||
		       _type == TacticalCommandType.Retreat ||
		       _type == TacticalCommandType.Flee ||
		       _type == TacticalCommandType.Cancel;
	}

	private static bool IsFinitePosition(Vector3 _position)
	{
		return !(float.IsNaN(_position.x) || float.IsNaN(_position.y) || float.IsNaN(_position.z) ||
		         float.IsInfinity(_position.x) || float.IsInfinity(_position.y) || float.IsInfinity(_position.z));
	}

	private void RegisterHandlers()
	{
		m_Handlers.Clear();
		AddHandler(new UnitAIIdleHandler());
		AddHandler(new UnitAIDefenseHandler());
		AddHandler(new UnitAIAttackHandler());
		AddHandler(new UnitAISearchHandler());
		AddHandler(new UnitAIRetreatHandler());
		AddHandler(new UnitAIFleeHandler());
	}

	private void AddHandler(IUnitAIStateHandler _handler)
	{
		m_Handlers[_handler.State] = _handler;
	}

	private void TryAutoBindPerception()
	{
		if (m_Registry == null && TryGetComponent(out DetectionProcessor processor))
			m_Registry = processor;
		if (m_Sensor == null)
			TryGetComponent(out m_Sensor);
	}

	private void EnsureCombatReadiness()
	{
		if (GetComponent<CombatReadinessController>() == null)
			gameObject.AddComponent<CombatReadinessController>();
	}

	private void EnsureReadiness()
	{
		if (m_ReadinessBound)
			return;

		m_Readiness.LogActor = this;
		m_Readiness.Reset(ReadinessProfile.ForRank(ResolveReadinessRank()), Time.time);
		m_ReadinessBound = true;
	}

	private ReadinessRankKind ResolveReadinessRank()
	{
		if (TryGetComponent(out UnitCombatStats stats) && stats.RankPreset != null)
		{
			int index = UnitCombatRankCycle.GetRankAssetNameIndex(stats.RankPreset);
			return ReadinessMath.RankFromAssetIndex(index);
		}

		return ReadinessRankKind.Soldier;
	}

	private void TickReadiness()
	{
		UnitLifeState life = UnitLifeStateMath.Resolve(this);
		if (!UnitLifeStateMath.AllowsTactical(life))
		{
			if (m_ReadinessBound)
			{
				m_Readiness.SetAllowed(false);
				TickReadinessFrozen();
			}

			return;
		}

		EnsureReadiness();
		if (!m_Readiness.Allowed)
		{
			TickReadinessFrozen();
			return;
		}

		bool previousVisible = m_ReadinessSawHostile;
		ReadinessFrame frame = ReadinessStimulusMath.FromPerception(
			in m_Perception,
			previousVisible,
			m_ImmediateThreat);
		frame.Firing = IsFiringForReadiness();
		m_ReadinessSawHostile = frame.HostileVisible;
		int readinessChanges = m_Readiness.Context.ChangeCount;
		m_Readiness.Tick(Time.time, in frame);
		TickThreatDirection();
		if (m_Readiness.Context.ChangeCount != readinessChanges)
			NotifyThreatFacing(ThreatDirectionFacingReason.ReadinessChanged);
	}

	private void TickReadinessFrozen()
	{
		ReadinessFrame frozen = default;
		frozen.Firing = false;
		m_Readiness.Tick(Time.time, in frozen);
		TickThreatDirectionFrozen();
	}

	private bool IsFiringForReadiness()
	{
		if (m_FireController == null)
			TryGetComponent(out m_FireController);
		return m_FireController != null && m_FireController.IsFiringCommandActive;
	}

	private void EnsureThreatDirection()
	{
		if (m_ThreatDirectionBound)
			return;

		m_ThreatDirection.LogActor = this;
		m_ThreatFacing.LogActor = this;
		ThreatReorientation.LogActor = this;
		ThreatReposition.LogActor = this;
		m_ThreatDirectionBound = true;
		UnitTeamId team = UnitTeamId.Neutral;
		if (TryGetComponent(out UnitTeam unitTeam) && unitTeam != null)
			team = unitTeam.Team;
		if (ThreatDirectionSpawnQuery.TryGetCenters(team, out Vector3 ownCenter, out Vector3 enemyCenter))
			m_ThreatDirection.ApplyBattleStart(ownCenter, enemyCenter, Time.time);
	}

	private void TickThreatDirection()
	{
		EnsureThreatDirection();
		int logCount = m_ThreatDirection.LogCount;
		m_ThreatDirection.Tick(Time.time, transform.position, in m_Perception);
		if (m_ThreatDirection.LogCount != logCount ||
		    (!m_ThreatFacing.HasDesiredFacing && m_ThreatDirection.HasThreatDirection))
			NotifyThreatFacing(ThreatDirectionFacingReason.ThreatDirectionChanged);
	}

	private void NotifyThreatFacing(ThreatDirectionFacingReason _reason)
	{
		if (!m_ThreatDirection.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge))
			return;
		ThreatDirectionReorientationResult result = ThreatReorientation.Observe(
			in knowledge,
			ResolveOccupyingCover(),
			transform.eulerAngles.y,
			_reason);
		RefreshThreatReposition(in knowledge, result.AngleDeltaDegrees);
		if (!result.FacingUpdated)
			return;
		if (m_HasHostileVisible)
			return;
		if (TryGetComponent(out UnitWeaponReadyHandsLayer readyHands) &&
		    readyHands.WantsCombatTargetFacing())
			return;
		if (TryGetComponent(out UnitClickToMove clickToMove))
			clickToMove.OverrideFacingAngle = m_ThreatFacing.DesiredYaw;
		if (TryGetComponent(out UnitNavLocomotionDriver locomotion))
			locomotion.OverrideFacingAngle = m_ThreatFacing.DesiredYaw;
	}

	private CoverCandidate ResolveOccupyingCover()
	{
		CurrentTacticalPosition position = m_TacticalMovement.CurrentTacticalPosition;
		if (!position.Valid || !position.Occupied)
			return null;

		IReadOnlyList<CoverCandidate> candidates = m_TacticalCover.LastCandidates;
		if (candidates == null)
			return null;

		for (int i = 0; i < candidates.Count; i++)
		{
			CoverCandidate candidate = candidates[i];
			if (candidate != null && candidate.CandidateId == position.CandidateId)
				return candidate;
		}

		return null;
	}

	private void RefreshThreatReposition(in ThreatDirectionKnowledge _knowledge, float _angleDeltaDegrees)
	{
		CoverSituation situation = BuildCoverSituation();
		ThreatReposition.Evaluate(
			in _knowledge,
			ResolveOccupyingCover(),
			m_TacticalCover.LastCandidates,
			in situation,
			_angleDeltaDegrees);
	}

	private void TickThreatDirectionFrozen()
	{
		if (!m_ThreatDirectionBound)
			return;

		m_ThreatDirection.Tick(Time.time);
	}

	private void ChangeState(UnitAIState _next, UnitAIStateContext _context)
	{
		UnitAIState previous = m_State;
		if (m_Handler != null)
		{
			m_Handler.Exit(this);
			m_Trace.Add("Exit:" + previous);
		}

		m_State = _next;
		m_Context = _context;
		m_StateTime = 0f;
		m_Handler = m_Handlers[_next];
		m_Handler.Enter(this, in m_Context);
		m_Trace.Add("Enter:" + _next);
		ResolveAction();
		LogAiTransition(previous, _next);
		LogAiIfChanged("state", true);
	}

	private void RefreshPerception()
	{
		if (m_UseInjectedFrame)
			return;

		if (m_Registry != null)
		{
			m_Perception = AIPerceptionFrameBuilder.Build(m_Registry, m_PerceptionScratch);
			return;
		}

		if (m_Sensor != null)
		{
			m_Sensor.Rebuild();
			m_Perception = m_Sensor.CurrentFrame;
			return;
		}

		m_Perception = AIPerceptionFrame.Empty;
	}

	private void ResolveAction()
	{
		m_HasHostileVisible = UnitAIActionResolver.HasHostileVisible(in m_Perception);
		if (m_HasHostileVisible)
			m_LastHostileVisibleAt = Time.time;
		m_Action = UnitAIActionResolver.Resolve(m_State, in m_Perception);
		if (m_Action == UnitAIAction.Engage &&
		    UnitAIActionResolver.TryGetEngageContact(in m_Perception, out AIContactKnowledge knowledge))
		{
			m_EngageTarget = knowledge.Target;
		}
		else
		{
			m_EngageTarget = null;
		}

		TickReadiness();
		LogAiIfChanged("action", false);
	}

	private void LogAiIfChanged(string _cause, bool _force)
	{
		if (!UnitActionLog.Enabled)
			return;

		EntityId engageId = m_EngageTarget != null ? m_EngageTarget.GetEntityId() : default;
		if (!_force &&
		    m_State == m_LastLoggedState &&
		    m_Action == m_LastLoggedAction &&
		    engageId == m_LastLoggedEngageId &&
		    m_UseOfForceLevel == m_LastLoggedRoe)
			return;

		m_LastLoggedState = m_State;
		m_LastLoggedAction = m_Action;
		m_LastLoggedEngageId = engageId;
		m_LastLoggedRoe = m_UseOfForceLevel;

		string engage = m_EngageTarget != null ? UnitActionLog.Slot(m_EngageTarget) : "none";
		string dest = m_Context.HasDestination ? UnitActionLog.Vec(m_Context.Destination) : "none";
		string search = m_State == UnitAIState.Search ? UnitActionLog.Vec(m_Context.SearchPosition) : "n/a";
		string payload =
			"cause=" + _cause +
			" state=" + m_State +
			" action=" + m_Action +
			" intent=" + CurrentCombatIntent +
			" roe=" + m_UseOfForceLevel +
			" engage=" + engage +
			" dest=" + dest +
			" search=" + search +
			" hostileVis=" + (m_HasHostileVisible ? "1" : "0");
		UnitActionLog.Write(this, UnitActionLog.Ai, payload);
		if (_force || _cause == "state")
			UnitActionLog.Timeline(UnitActionLog.Ai, "actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private void LogAiTransition(UnitAIState _from, UnitAIState _to)
	{
		string reason = m_PendingAiTransitionReason;
		m_PendingAiTransitionReason = null;
		if (!UnitActionLog.Enabled || _from == _to)
			return;
		bool searchPair = _from == UnitAIState.Search || _to == UnitAIState.Search;
		if (!searchPair)
			return;
		if (string.IsNullOrEmpty(reason))
			reason = "CommandChanged";
		string target = "none";
		if (m_EngageTarget != null)
			target = UnitActionLog.Slot(m_EngageTarget);
		else if (UnitAIActionResolver.TryGetEngageContact(in m_Perception, out AIContactKnowledge knowledge) &&
		         knowledge.Target != null)
			target = UnitActionLog.Slot(knowledge.Target);
		string payload =
			"unit=" + UnitActionLog.Slot(this) +
			" from=" + _from +
			" to=" + _to +
			" reason=" + reason +
			" target=" + target +
			" immediateThreat=" + (m_ImmediateThreat ? "1" : "0");
		UnitActionLog.Write(this, UnitActionLog.AiTransition, payload);
		UnitActionLog.Timeline(
			UnitActionLog.AiTransition,
			"actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private void TryAutonomousTransitions()
	{
		if (m_ImmediateThreat)
			ApplyImmediateThreatPriority();
		else
			m_LoggedThreatHold = false;

		if (UnitAISearchDecision.ShouldStartSearch(
			    m_State, in m_Perception, Time.time, m_LastHostileVisibleAt))
		{
			if (TryBuildSearchCommand(out UnitAICommand search))
			{
				m_PendingAiTransitionReason = "LostCurrentTarget";
				TryApplyCommand(search);
			}

			return;
		}

		if (UnitAISearchDecision.ShouldFinishSearchBecauseFound(m_State, in m_Perception))
		{
			m_PendingAiTransitionReason = "HostileVisible";
			CompleteSearch(UnitAISearchCompletionReason.Found, true);
			UpdateEmergencyCover();
			UpdateTacticalCover();
			UpdatePeekCover();
			return;
		}

		if (UnitAISearchDecision.ShouldFinishSearchBecauseMemoryGone(m_State, in m_Perception))
		{
			m_PendingAiTransitionReason = "Expired";
			CompleteSearch(UnitAISearchCompletionReason.Expired, false);
		}

		UpdateEmergencyCover();
		UpdateTacticalCover();
		UpdatePeekCover();
	}

	private void UpdateEmergencyCover()
	{
		CoverSituation situation = BuildCoverSituation();
		EmergencyCoverDecision decision = m_EmergencyCover.Update(
			m_ImmediateThreat,
			m_State,
			in situation,
			null,
			this);
		TryCaptureEmergencyDebug(in decision);
	}

	private void UpdateTacticalCover()
	{
		CoverSituation situation = BuildCoverSituation();
		if (m_ThreatDirectionBound &&
		    m_ThreatDirection.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge))
		{
			ThreatReposition.Evaluate(
				in knowledge,
				ResolveOccupyingCover(),
				m_TacticalCover.LastCandidates,
				in situation,
				ThreatReorientation.LastAngleDeltaDegrees);
			situation.ThreatRepositionAllowed = ThreatReposition.AllowsCoverReevaluation;
		}

		TacticalCoverDecision decision = m_TacticalCover.Update(
			m_ImmediateThreat,
			m_State,
			in situation,
			null,
			this);
		if (m_DebugCoverLogs < 8)
		{
			m_DebugCoverLogs++;
			int candidateCount = m_TacticalCover.LastCandidates != null ? m_TacticalCover.LastCandidates.Count : 0;
			// #region agent log
			AgentDebugNdjson.Write(
				"D",
				"UnitAIController.UpdateTacticalCover",
				"cover tick",
				"{\"state\":\"" + m_State +
				"\",\"decision\":\"" + decision.Decision +
				"\",\"reason\":\"" + decision.Reason +
				"\",\"hasDest\":" + (decision.HasDestination ? "true" : "false") +
				",\"selectedId\":" + decision.SelectedCandidateId +
				",\"candidates\":" + candidateCount +
				",\"hasTarget\":" + (situation.HasTarget ? "true" : "false") +
				",\"bound\":" + (m_WorldBound ? "true" : "false") + "}");
			// #endregion
		}
		if (!m_ImmediateThreat)
			TryCaptureTacticalDebug(in decision);
		TryCaptureOccupancyDebug();
	}

	private void UpdatePeekCover()
	{
		UpdateMovingLean();
		bool moving = m_TacticalMovement.Last.HasRoute && !m_TacticalDestinationReached;
		if (m_TacticalMovement.MovingLeanActive || moving)
			return;
		EnsureCoverPeekBindings();
		CoverSituation situation = BuildCoverSituation();
		IReadOnlyList<CoverCandidate> candidates =
			m_TacticalCover.LastCandidates ?? m_EmergencyCover.LastCandidates;
		CoverCandidate occupying = EmergencyCoverOverlay.FindOccupying(candidates, in situation);
		CoverPeekDecision decision = m_PeekCover.Update(
			m_State,
			occupying,
			in situation,
			m_CoverLos,
			m_CoverPeekOcclusion,
			m_LeanExecutor,
			Time.time,
			this);
		TryCapturePeekDebug(in decision);
	}

	private void UpdateMovingLean()
	{
		EnsureCoverPeekBindings();
		var sit = new TacticalMovingLeanSituation
		{
			Present = true,
			Moving = m_TacticalMovement.Last.HasRoute && !m_TacticalDestinationReached,
			ImmediateThreat = m_ImmediateThreat,
			Arrived = m_TacticalMovement.LastArrival.IsAcquired,
			Replan = m_TacticalMovement.Last.ReplanAction == TacticalReplanAction.Replace
		};
		m_TacticalMovement.NotifyMovingLean(in sit, m_LeanExecutor, this);
	}

	private void EnsureCoverPeekBindings()
	{
		if (m_LeanExecutor == null && TryGetComponent(out UnitSpineLean spine))
			m_LeanExecutor = new UnitSpineLeanExecutor(spine);
		if (m_CoverLos == null)
			m_CoverLos = new PhysicsCoverLosProbe();
		if (m_CoverPeekOcclusion == null)
			m_CoverPeekOcclusion = new PhysicsCoverOcclusionProbe();
	}

	private void ReturnPeek(CoverPeekReason _reason)
	{
		EnsureCoverPeekBindings();
		m_PeekCover.ForceReturn(m_LeanExecutor, _reason, this, Time.time);
	}

	private void TickCoverOccupancy()
	{
		if (m_CoverOccupancy == null)
			return;
		m_TacticalMovement.HeartbeatReservation(Time.time, this);
		if (m_EmergencyCover.Cache != null)
			m_CoverOccupancy.NotifyGeometryVersion(m_EmergencyCover.Cache.GeometryVersion, Time.time);
		m_CoverOccupancy.Tick(Time.time);
		UnitLifeState life = UnitLifeStateMath.Resolve(this);
		if (UnitLifeStateMath.RequiresCoverRelease(life))
		{
			NotifyLifeState(life);
		}
	}

	private void ReleaseCoverOccupancy(CoverReservationReason _reason)
	{
		if (m_CoverOccupancy != null)
			m_CoverOccupancy.ReleaseUnit(CoverOccupancyUnitId, Time.time, _reason, this);
		m_TacticalMovement.ReleaseOccupancyHold();
	}

	private void OnCoverHealthChanged()
	{
		NotifyLifeState(UnitLifeStateMath.Resolve(this));
	}

	private void OnCoverConsciousnessChanged(bool _isConscious)
	{
		NotifyLifeState(UnitLifeStateMath.Resolve(this));
	}

	private void TryCaptureOccupancyDebug()
	{
		if (m_CoverOccupancy == null || !TryGetComponent(out CoverCandidateDebugDraw debug))
			return;
		IReadOnlyList<CoverCandidate> candidates = m_TacticalCover.LastCandidates ?? m_EmergencyCover.LastCandidates;
		debug.CaptureOccupancy(candidates, m_CoverOccupancy, Time.time);
	}

	private CoverSituation BuildCoverSituation()
	{
		Vector3 unit = transform.position;
		Vector3 target = unit;
		bool hasTarget = false;
		Vector3 hostile = transform.forward;
		if (m_ThreatSource != null && m_ThreatSource.LastAttacker != null)
		{
			target = m_ThreatSource.LastAttacker.position;
			hasTarget = true;
			hostile = target - unit;
		}
		else if (m_EngageTarget != null)
		{
			target = m_EngageTarget.position;
			hasTarget = true;
			hostile = target - unit;
		}

		hostile.y = 0f;
		if (hostile.sqrMagnitude < 0.0001f)
			hostile = transform.forward;

		CoverMissionIntent mission = CoverMissionIntent.Hold;
		if (m_State == UnitAIState.Attack)
			mission = CoverMissionIntent.Attack;
		else if (m_State == UnitAIState.Defense)
			mission = CoverMissionIntent.Defense;

		var situation = new CoverSituation
		{
			UnitPosition = unit,
			Stance = CoverStance.Standing,
			Mission = mission,
			Weapon = m_CoverWeapon,
			Rank = m_CoverRank,
			TargetPosition = target,
			HasTarget = hasTarget,
			SectorForward = transform.forward,
			HostileDirection = hostile,
			UnitId = CoverOccupancyUnitId,
			OccupancyVersion = m_CoverOccupancy != null ? m_CoverOccupancy.OccupancyVersion : 0
		};
		if (m_ThreatDirectionBound &&
		    m_ThreatDirection.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge))
			ThreatDirectionCoverMath.Bind(ref situation, in knowledge);
		return situation;
	}

	private void TryCaptureEmergencyDebug(in EmergencyCoverDecision _decision)
	{
		if (!TryGetComponent(out CoverCandidateDebugDraw debug))
			return;
		Bounds bounds = new Bounds(transform.position, Vector3.one * CoverSpatialMath.DefaultRegionSizeMeters);
		if (m_EmergencyCover.Cache != null)
		{
			CoverRegionId region = m_EmergencyCover.Cache.RegionAt(transform.position);
			bounds = CoverSpatialMath.RegionBounds(region, m_EmergencyCover.Cache.RegionSizeMeters);
		}

		debug.CaptureEmergency(
			bounds,
			m_EmergencyCover.LastCandidates,
			_decision.Evaluations,
			_decision.Result,
			_decision.SelectedCandidateId,
			_decision.FromCache,
			_decision.Active);
	}

	private void TryCaptureTacticalDebug(in TacticalCoverDecision _decision)
	{
		if (!TryGetComponent(out CoverCandidateDebugDraw debug))
			return;
		Bounds bounds = new Bounds(transform.position, Vector3.one * CoverSpatialMath.DefaultRegionSizeMeters);
		if (m_TacticalCover.Cache != null)
		{
			CoverRegionId region = m_TacticalCover.Cache.RegionAt(transform.position);
			bounds = CoverSpatialMath.RegionBounds(region, m_TacticalCover.Cache.RegionSizeMeters);
		}

		debug.CaptureTactical(
			bounds,
			m_TacticalCover.LastCandidates,
			_decision.Evaluations,
			_decision.CurrentCandidateId,
			_decision.BestCandidateId,
			_decision.CurrentScore,
			_decision.BestScore,
			_decision.SwitchingCost,
			_decision.Decision,
			_decision.FromCache);
	}

	private void TryCapturePeekDebug(in CoverPeekDecision _decision)
	{
		if (!TryGetComponent(out CoverCandidateDebugDraw debug))
			return;
		debug.CapturePeek(in _decision);
	}

	private void ApplyImmediateThreatPriority()
	{
		m_LastPriorityEvaluation = UnitAICommandPriority.EvaluateImmediateThreat(m_State);
		bool logHold = m_LastPriorityEvaluation.Decision == UnitAIPriorityDecision.HoldState && !m_LoggedThreatHold;
		bool logInterrupt = m_LastPriorityEvaluation.Decision == UnitAIPriorityDecision.Interrupt;
		if (logHold || logInterrupt)
		{
			LogCmdPriority("ImmediateThreat", m_LastPriorityEvaluation);
			if (logHold)
				m_LoggedThreatHold = true;
		}
	}

	private bool CompleteSearch(UnitAISearchCompletionReason _reason, bool _found)
	{
		if (m_State != UnitAIState.Search)
			return false;
		SetSearchCompletion(_reason, _found);
		return TryApplyCommand(BuildResumeOrFoundCommand(_found));
	}

	private void SetSearchCompletion(UnitAISearchCompletionReason _reason, bool _found)
	{
		if (m_SearchSessionActive)
			m_SearchSession.SetCompletion(_reason);
		m_LastSearchCompletionReason = _reason;
		string target = string.Empty;
		if (_found && m_EngageTarget != null)
			target = " target=" + UnitActionLog.Slot(m_EngageTarget);
		LogSearch("result=" + _reason + target);
	}

	private void ApplyCurrentSearchCandidateToContext()
	{
		UnitAIStateContext ctx = m_Context;
		ctx.SearchPosition = m_SearchSession.CurrentPosition;
		ctx.AreaCenter = m_SearchSession.Area.Center;
		ctx.AreaRadius = m_SearchSession.Area.Radius;
		ctx.SearchCue = m_SearchSession.Area.Source;
		m_Context = ctx;
	}

	private void LogSearch(string _payload)
	{
		if (!UnitActionLog.Enabled)
			return;
		UnitActionLog.Write(this, UnitActionLog.Search, _payload);
		UnitActionLog.Timeline(
			UnitActionLog.Search,
			"actor=" + UnitActionLog.Slot(this) + " " + _payload);
	}

	private void LogSearchCandidate(string _state)
	{
		LogSearch(
			"candidate=" + m_SearchSession.Index +
			" pos=" + UnitActionLog.Vec(m_SearchSession.CurrentPosition) +
			" score=" + UnitActionLog.F1(m_SearchSession.CurrentScore) +
			" remaining=" + m_SearchSession.Remaining +
			" state=" + _state);
	}

	private bool TryBuildSearchCommand(out UnitAICommand _command)
	{
		_command = default;
		if (!UnitAISearchDecision.TryBuildSearchArea(in m_Perception, Time.time, out UnitAISearchArea area))
			return false;

		Vector3 origin = m_State == UnitAIState.Defense ? m_Context.AnchorPosition : m_Context.Destination;
		UnitAIStateContext ctx = UnitAIStateContext.ForSearch(
			origin,
			area.Center,
			UnitAISearchDecision.DefaultAreaRadius,
			m_State,
			area.Source);
		ctx.Facing = m_Context.Facing;
		ctx.AnchorPosition = m_State == UnitAIState.Defense ? m_Context.AnchorPosition : origin;
		ctx.Destination = m_Context.Destination;
		ctx.HasDestination = m_Context.HasDestination;
		ctx.TargetEntity = m_Context.TargetEntity;
		ctx.AttackDirection = m_Context.AttackDirection;
		_command = UnitAICommand.Search(ctx);
		return true;
	}

	private UnitAICommand BuildResumeOrFoundCommand(bool _found)
	{
		UnitAIState resume = m_Context.ResumeState;
		if (resume == UnitAIState.Defense)
		{
			Vector3 anchor = m_Context.SearchOrigin;
			return UnitAICommand.Defense(
				UnitAIStateContext.ForDefense(anchor, anchor, 10f, m_Context.Facing));
		}

		if (resume == UnitAIState.Attack || _found)
		{
			Vector3 destination = m_Context.Destination;
			Transform target = m_Context.TargetEntity;
			Vector3 direction = m_Context.AttackDirection;
			if (_found &&
			    UnitAIActionResolver.TryGetEngageContact(in m_Perception, out AIContactKnowledge engage))
			{
				if (target == null)
					target = engage.Target;
				if (destination.sqrMagnitude < 0.0001f)
					destination = engage.LastKnownPosition;
			}

			if (direction.sqrMagnitude < 0.0001f)
				direction = Vector3.forward;
			return UnitAICommand.Attack(
				UnitAIStateContext.ForAttack(destination, direction, target));
		}

		return UnitAICommand.Idle();
	}

	private bool TryIssue(UnitAICommand _command)
	{
		EnsureStarted();
		if (_command.State == UnitAIState.Idle)
			return m_State == UnitAIState.Idle || TryApplyCommand(UnitAICommand.Idle());

		if (m_State != UnitAIState.Idle)
		{
			if (!UnitAITransitionTable.CanTransition(m_State, UnitAIState.Idle))
				return false;
			if (!TryApplyCommand(UnitAICommand.Idle()))
				return false;
		}

		if (TryApplyCommand(_command))
			return true;

		if (_command.State == UnitAIState.Retreat)
		{
			Vector3 hold = transform.position;
			Vector3 facing = transform.forward;
			if (!TryApplyCommand(UnitAICommand.Defense(
				    UnitAIStateContext.ForDefense(hold, hold, c_CommandDefenseRadius, facing))))
				return false;
			return TryApplyCommand(_command);
		}

		return false;
	}

	private UnitAIStateContext BuildIssuedSearchContext(Vector3 _searchPosition)
	{
		return BuildIssuedSearchContext(_searchPosition, UnitAISearchCue.VisualMemory);
	}

	private UnitAIStateContext BuildIssuedSearchContext(Vector3 _searchPosition, UnitAISearchCue _cue)
	{
		EnsureStarted();
		UnitAIState resume = UnitAIState.Idle;
		if (m_State == UnitAIState.Attack || m_State == UnitAIState.Defense)
			resume = m_State;
		else if (m_State == UnitAIState.Search && m_Context.ResumeState != UnitAIState.Idle)
			resume = m_Context.ResumeState;

		Vector3 origin = transform.position;
		if (m_State == UnitAIState.Defense)
			origin = m_Context.AnchorPosition;
		else if (m_State == UnitAIState.Search && m_Context.ResumeState == UnitAIState.Defense)
			origin = m_Context.SearchOrigin;
		else if (m_Context.HasDestination)
			origin = m_Context.Destination;

		UnitAIStateContext ctx = UnitAIStateContext.ForSearch(
			origin,
			_searchPosition,
			UnitAISearchDecision.DefaultAreaRadius,
			resume,
			_cue);
		ctx.Facing = m_Context.Facing;
		ctx.AnchorPosition = resume == UnitAIState.Defense ? origin : m_Context.AnchorPosition;
		ctx.Destination = m_Context.Destination;
		ctx.HasDestination = m_Context.HasDestination;
		ctx.TargetEntity = m_Context.TargetEntity;
		ctx.AttackDirection = m_Context.AttackDirection;
		return ctx;
	}

	private static Vector3 PlanarDirection(Vector3 _to, Vector3 _from, Vector3 _fallback)
	{
		Vector3 dir = _to - _from;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.0001f)
		{
			dir = _fallback;
			dir.y = 0f;
		}

		if (dir.sqrMagnitude < 0.0001f)
			return Vector3.forward;

		return dir.normalized;
	}
	#endregion
}
