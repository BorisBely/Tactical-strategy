using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI-1 FROZEN. Only this type changes <see cref="UnitAIState"/>. Orders enter via <see cref="TryApplyCommand"/>.
/// Perception may change <see cref="CurrentAction"/>; Search from LastKnown is also applied here.
/// Search does not write Memory. Does not call Fire(). Publishes <see cref="CombatIntent"/> only.
/// Stage 4 FROZEN: Search / Attack / Retreat / Flee walk via <see cref="IUnitMoveCommand"/> to a snapshotted destination.
/// Not baked onto Unit.prefab. Stage 2 CombatIntent FROZEN. Stage 3 Search decision FROZEN.
/// AI-1A: <see cref="UseOfForceLevel"/> is a separate field. <see cref="TrySetUseOfForcePolicy"/> does not change state.
/// Game orders (6.2): <see cref="GameCommandService"/> → <see cref="ITacticalCommandReceiver.IssueCommand"/>.
/// Debug: <see cref="IUnitTacticalCommand"/> → <see cref="TryIssue"/>.
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
		}
	}
	public ForcePermission LastForcePermission => m_LastForcePermission;

	public CombatIntent CurrentCombatIntent =>
		CombatIntentMath.FromEngageAction(m_Action == UnitAIAction.Engage);

	public bool SearchNavigationIssued => m_SearchNavigationIssued;
	public bool SearchAreaReached => m_SearchAreaReached;
	public bool TacticalNavigationIssued => m_TacticalNavigationIssued;
	public bool TacticalDestinationReached => m_TacticalDestinationReached;

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
			$"conf={liveConf:F2}  radius={m_Context.AreaRadius:F0}  resume={m_Context.ResumeState}\n" +
			$"search issued={m_SearchNavigationIssued} reached={m_SearchAreaReached}\n" +
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
	}

	public void Tick(float _dt)
	{
		using (InfantryProfilerMarkers.AiTick.Auto())
		{
			EnsureStarted();
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

		if (!TryMapIssuedCommand(in _command, out UnitAIState next, out UnitAIStateContext context))
		{
			LogCommand("rejected", in _command, TacticalCommandRejectReason.InvalidCommandData);
			return TacticalCommandResult.Rejected(TacticalCommandRejectReason.InvalidCommandData);
		}

		if (next == m_State)
		{
			ChangeState(next, context);
			LogCommand("accepted", in _command, TacticalCommandRejectReason.None);
			return TacticalCommandResult.Ok();
		}

		if (!UnitAITransitionTable.CanTransition(m_State, next))
		{
			LogCommand("rejected", in _command, TacticalCommandRejectReason.InvalidStateTransition);
			return TacticalCommandResult.Rejected(TacticalCommandRejectReason.InvalidStateTransition);
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

	internal void SetImmediateThreatFlag(bool _value)
	{
		m_ImmediateThreat = _value;
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

	public void ClearSearchNavigationDebug()
	{
		m_SearchNavigationIssued = false;
		m_SearchAreaReached = false;
	}

	public void NotifyTacticalNavigationIssued()
	{
		m_TacticalNavigationIssued = true;
	}

	public void NotifyTacticalDestinationReached()
	{
		m_TacticalDestinationReached = true;
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
		if (!UnitAISearchDecision.TryGetSearchContact(in m_Perception, out AIContactKnowledge contact))
			return false;
		return TryIssue(UnitAICommand.Search(BuildIssuedSearchContext(contact.LastKnownPosition)));
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
				_state = UnitAIState.Idle;
				_context = UnitAIStateContext.Empty;
				return true;
			default:
				return false;
		}
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

	private void TryAutonomousTransitions()
	{
		if (UnitAISearchDecision.ShouldStartSearch(m_State, in m_Perception))
		{
			if (TryBuildSearchCommand(out UnitAICommand search))
				TryApplyCommand(search);
			return;
		}

		if (UnitAISearchDecision.ShouldFinishSearchBecauseFound(m_State, in m_Perception))
		{
			TryApplyCommand(BuildResumeOrFoundCommand(true));
			return;
		}

		if (UnitAISearchDecision.ShouldFinishSearchBecauseMemoryGone(m_State, in m_Perception))
			TryApplyCommand(BuildResumeOrFoundCommand(false));
	}

	private bool TryBuildSearchCommand(out UnitAICommand _command)
	{
		_command = default;
		Vector3 searchPosition;
		UnitAISearchCue cue;
		if (UnitAISearchDecision.TryGetSearchContact(in m_Perception, out AIContactKnowledge contact))
		{
			searchPosition = contact.LastKnownPosition;
			cue = UnitAISearchCue.VisualMemory;
		}
		else if (UnitAISearchDecision.TryGetSearchSound(in m_Perception, out AISoundContact sound))
		{
			searchPosition = sound.Position;
			cue = UnitAISearchCue.Sound;
		}
		else
		{
			return false;
		}

		Vector3 origin = m_State == UnitAIState.Defense ? m_Context.AnchorPosition : m_Context.Destination;
		UnitAIStateContext ctx = UnitAIStateContext.ForSearch(
			origin,
			searchPosition,
			UnitAISearchDecision.DefaultAreaRadius,
			m_State,
			cue);
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
		EnsureStarted();
		UnitAIState resume = UnitAIState.Attack;
		if (m_State == UnitAIState.Defense)
			resume = UnitAIState.Defense;
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
			resume);
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
