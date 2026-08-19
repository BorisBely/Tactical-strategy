using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI-1 FROZEN. Only this type changes <see cref="UnitAIState"/>. Orders enter via <see cref="TryApplyCommand"/>.
/// Perception may change <see cref="CurrentAction"/>; Search from LastKnown is also applied here.
/// Search does not write Memory. Navigation / Combat execution is later. Not baked onto Unit.prefab.
/// AI-1A: <see cref="UseOfForceLevel"/> is a separate field. <see cref="TrySetUseOfForcePolicy"/> does not change state.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(25)]
public sealed class UnitAIController : MonoBehaviour
{
	#region Private Fields
	private readonly Dictionary<UnitAIState, IUnitAIStateHandler> m_Handlers =
		new Dictionary<UnitAIState, IUnitAIStateHandler>(6);
	private readonly List<string> m_Trace = new List<string>(32);

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
		set => m_ImmediateThreat = value;
	}
	public ForcePermission LastForcePermission => m_LastForcePermission;
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
		RefreshPerception();
		ResolveAction();
	}

	public void Tick(float _dt)
	{
		EnsureStarted();
		if (_dt < 0f)
			_dt = 0f;
		m_StateTime += _dt;
		RefreshPerception();
		ResolveAction();
		TryAutonomousTransitions();
		if (m_Handler != null)
			m_Handler.Tick(this, _dt);
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
			return false;

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
		m_UseOfForceLevel = _level;
		return true;
	}

	public ForcePermission EvaluateForce(in AIContactKnowledge _knowledge)
	{
		return EvaluateForce(_knowledge.Target != null, _knowledge.Relationship, _knowledge.Target);
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
	#endregion

	#region Private Methods
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
	}

	private void RefreshPerception()
	{
		if (m_UseInjectedFrame)
			return;

		if (m_Registry != null)
		{
			m_Perception = AIPerceptionFrameBuilder.Build(m_Registry);
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
		if (!UnitAISearchDecision.TryGetSearchContact(in m_Perception, out AIContactKnowledge contact))
			return false;

		Vector3 origin = m_State == UnitAIState.Defense ? m_Context.AnchorPosition : m_Context.Destination;
		UnitAIStateContext ctx = UnitAIStateContext.ForSearch(
			origin,
			contact.LastKnownPosition,
			UnitAISearchDecision.DefaultAreaRadius,
			m_State);
		ctx.Facing = m_Context.Facing;
		ctx.AnchorPosition = m_State == UnitAIState.Defense ? m_Context.AnchorPosition : origin;
		ctx.Destination = m_Context.Destination;
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
	#endregion
}
