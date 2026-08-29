/// <summary>
/// #11 command / interrupt bands. Comparison is deterministic: Emergency &gt; High &gt; Mission &gt; Tactical &gt; Routine.
/// </summary>
public enum UnitAIPriorityBand
{
	Routine = 0,
	Tactical = 1,
	Mission = 2,
	High = 3,
	Emergency = 4
}

/// <summary>
/// External orders, tactical reactions, and state completion must not compete as peer commands.
/// </summary>
public enum UnitAIPriorityKind
{
	None = 0,
	ExternalCommand = 1,
	TacticalReaction = 2,
	StateCompletion = 3
}

public enum UnitAIPriorityDecision
{
	None = 0,
	Accept = 1,
	Reject = 2,
	Interrupt = 3,
	ReplaceContext = 4,
	Resume = 5,
	HoldState = 6
}

public enum UnitAIPriorityReason
{
	None = 0,
	HigherPriority = 1,
	LowerPriority = 2,
	SameStateReplace = 3,
	OverlaySearch = 4,
	IllegalTransition = 5,
	EmergencyLocal = 6,
	Emergency = 7,
	ResumeReturnState = 8,
	PlayerCancel = 9,
	ReplaceMission = 10,
	StateCompletion = 11
}

/// <summary>
/// Snapshot of one resolver pass. State handlers do not choose priority.
/// </summary>
public readonly struct UnitAIPriorityEvaluation
{
	public readonly UnitAIPriorityKind Kind;
	public readonly UnitAIPriorityDecision Decision;
	public readonly UnitAIPriorityReason Reason;
	public readonly UnitAIPriorityBand CurrentBand;
	public readonly UnitAIPriorityBand IncomingBand;
	public readonly UnitAIState CurrentState;
	public readonly UnitAIState IncomingState;

	public UnitAIPriorityEvaluation(
		UnitAIPriorityKind _kind,
		UnitAIPriorityDecision _decision,
		UnitAIPriorityReason _reason,
		UnitAIPriorityBand _currentBand,
		UnitAIPriorityBand _incomingBand,
		UnitAIState _currentState,
		UnitAIState _incomingState)
	{
		Kind = _kind;
		Decision = _decision;
		Reason = _reason;
		CurrentBand = _currentBand;
		IncomingBand = _incomingBand;
		CurrentState = _currentState;
		IncomingState = _incomingState;
	}

	public bool IsReject => Decision == UnitAIPriorityDecision.Reject;
}

/// <summary>
/// #11 Command / Interruption layer. Does not change UnitAIState by itself.
/// ImmediateThreat never maps to Flee and never exits Search. Search overlay on Attack/Defense is allowed.
/// </summary>
public static class UnitAICommandPriority
{
	#region Public Methods
	public static UnitAIPriorityBand BandOf(UnitAIState _state)
	{
		switch (_state)
		{
			case UnitAIState.Flee:
				return UnitAIPriorityBand.Emergency;
			case UnitAIState.Retreat:
				return UnitAIPriorityBand.High;
			case UnitAIState.Attack:
			case UnitAIState.Defense:
				return UnitAIPriorityBand.Mission;
			case UnitAIState.Search:
				return UnitAIPriorityBand.Tactical;
			default:
				return UnitAIPriorityBand.Routine;
		}
	}

	public static UnitAIPriorityEvaluation EvaluateCommand(
		UnitAIState _current,
		UnitAIState _incoming,
		bool _isCancel)
	{
		UnitAIPriorityBand currentBand = BandOf(_current);
		UnitAIPriorityBand incomingBand = BandOf(_incoming);

		if (_current == _incoming)
		{
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.ExternalCommand,
				UnitAIPriorityDecision.ReplaceContext,
				UnitAIPriorityReason.SameStateReplace,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		if (_isCancel)
		{
			UnitAIPriorityDecision decision = _current == UnitAIState.Search
				? UnitAIPriorityDecision.Resume
				: UnitAIPriorityDecision.Interrupt;
			UnitAIPriorityReason reason = _current == UnitAIState.Search
				? UnitAIPriorityReason.ResumeReturnState
				: UnitAIPriorityReason.PlayerCancel;
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.ExternalCommand,
				decision,
				reason,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		if (_incoming == UnitAIState.Search &&
		    (_current == UnitAIState.Attack || _current == UnitAIState.Defense))
		{
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.ExternalCommand,
				UnitAIPriorityDecision.Accept,
				UnitAIPriorityReason.OverlaySearch,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		if (incomingBand < currentBand)
		{
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.ExternalCommand,
				UnitAIPriorityDecision.Reject,
				UnitAIPriorityReason.LowerPriority,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		if (incomingBand > currentBand)
		{
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.ExternalCommand,
				UnitAIPriorityDecision.Interrupt,
				UnitAIPriorityReason.HigherPriority,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		return new UnitAIPriorityEvaluation(
			UnitAIPriorityKind.ExternalCommand,
			UnitAIPriorityDecision.Accept,
			UnitAIPriorityReason.ReplaceMission,
			currentBand,
			incomingBand,
			_current,
			_incoming);
	}

	public static UnitAIPriorityEvaluation EvaluateImmediateThreat(UnitAIState _current)
	{
		UnitAIPriorityBand currentBand = BandOf(_current);
		return new UnitAIPriorityEvaluation(
			UnitAIPriorityKind.TacticalReaction,
			UnitAIPriorityDecision.HoldState,
			UnitAIPriorityReason.EmergencyLocal,
			currentBand,
			UnitAIPriorityBand.Emergency,
			_current,
			_current);
	}

	public static UnitAIPriorityEvaluation EvaluateInternal(UnitAIState _current, UnitAIState _incoming)
	{
		UnitAIPriorityBand currentBand = BandOf(_current);
		UnitAIPriorityBand incomingBand = BandOf(_incoming);
		if (_current == UnitAIState.Search && _incoming != UnitAIState.Search)
		{
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.StateCompletion,
				UnitAIPriorityDecision.Accept,
				UnitAIPriorityReason.StateCompletion,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		if (_incoming == UnitAIState.Search &&
		    (_current == UnitAIState.Attack || _current == UnitAIState.Defense))
		{
			return new UnitAIPriorityEvaluation(
				UnitAIPriorityKind.StateCompletion,
				UnitAIPriorityDecision.Accept,
				UnitAIPriorityReason.OverlaySearch,
				currentBand,
				incomingBand,
				_current,
				_incoming);
		}

		return new UnitAIPriorityEvaluation(
			UnitAIPriorityKind.StateCompletion,
			UnitAIPriorityDecision.Accept,
			UnitAIPriorityReason.None,
			currentBand,
			incomingBand,
			_current,
			_incoming);
	}

	public static UnitAIPriorityEvaluation Illegal(UnitAIState _current, UnitAIState _incoming)
	{
		return new UnitAIPriorityEvaluation(
			UnitAIPriorityKind.ExternalCommand,
			UnitAIPriorityDecision.Reject,
			UnitAIPriorityReason.IllegalTransition,
			BandOf(_current),
			BandOf(_incoming),
			_current,
			_incoming);
	}

	/// <summary>
	/// Deterministic combined outcome for one tick of command + ImmediateThreat.
	/// Threat never promotes to Flee and never exits Search by itself.
	/// <paramref name="_searchReturn"/> is kept for the frozen Predict signature.
	/// </summary>
	public static UnitAIState Predict(
		UnitAIState _current,
		bool _hasCommand,
		UnitAIState _incoming,
		bool _isCancel,
		bool _immediateThreat,
		UnitAIState _searchReturn)
	{
		_ = _searchReturn;
		_ = _immediateThreat;
		UnitAIState next = _current;
		if (_hasCommand)
		{
			UnitAIPriorityEvaluation command = EvaluateCommand(_current, _incoming, _isCancel);
			if (!command.IsReject)
				next = _incoming;
		}

		return next;
	}
	#endregion
}
