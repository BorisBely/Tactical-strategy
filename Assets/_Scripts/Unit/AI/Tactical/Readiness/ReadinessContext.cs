/// <summary>
/// Live readiness facts. LastCombatActivityTime / HasActiveCombatActivity are the 14B.3 CombatActivity context.
/// ArmFatigue is a 14B.6 physical modifier (AimTime / RecoilControl / TurnTime). Not a ReadinessState.
/// </summary>
public struct ReadinessContext
{
	#region Public Fields
	public ReadinessState CurrentState;
	public ReadinessState PreviousState;
	public float StateEnterTime;
	public float LastCombatActivityTime;
	public bool HasActiveCombatActivity;
	public float CalmDownRemaining;
	public bool HasPendingTransition;
	public ReadinessState TransitionFrom;
	public ReadinessState TransitionTo;
	public float TransitionStartTime;
	public float TransitionDuration;
	public float TransitionProgress;
	public ReadinessRankKind Rank;
	public float ArmFatigue;
	public float ArmFatigueModifier;
	public ReadinessChangeReason LastChangeReason;
	public int ChangeCount;
	public ReadinessDecayPhase DecayPhase;
	#endregion
}

/// <summary>One tick output. Never requests Fire.</summary>
public struct ReadinessDecision
{
	#region Public Fields
	public ReadinessState State;
	public ReadinessChangeReason Reason;
	public bool Changed;
	public float TransitionProgress;
	public bool HasPendingTransition;
	public bool HasActiveCombatActivity;
	public float CalmDownRemaining;
	public ReadinessDecayPhase DecayPhase;
	#endregion

	#region Public Properties
	public bool RequestsFire => false;
	#endregion
}

/// <summary>
/// World facts for one readiness tick. Flags may combine; the controller applies raise priority.
/// </summary>
public struct ReadinessFrame
{
	#region Public Fields
	public bool HostileVisible;
	public bool HostileLost;
	public bool GunshotHeard;
	public bool CombatActivity;
	public bool CombatActivityExpired;
	public bool Firing;
	public string StimulusTarget;
	#endregion

	#region Public Methods
	public static ReadinessFrame FromStimulus(ReadinessStimulus _stimulus)
	{
		switch (_stimulus)
		{
			case ReadinessStimulus.HostileVisible:
				return new ReadinessFrame { HostileVisible = true };
			case ReadinessStimulus.GunshotHeard:
				return new ReadinessFrame { GunshotHeard = true };
			case ReadinessStimulus.CombatContactLost:
			case ReadinessStimulus.HostileLost:
				return new ReadinessFrame { HostileLost = true };
			case ReadinessStimulus.CombatActivityExpired:
				return new ReadinessFrame { CombatActivityExpired = true };
			case ReadinessStimulus.CombatActivity:
				return new ReadinessFrame { CombatActivity = true };
			default:
				return default;
		}
	}
	#endregion
}

/// <summary>Recorded raise / decay request. Duration 0 means instant apply.</summary>
public struct ReadinessTransitionRequest
{
	#region Public Fields
	public ReadinessState FromState;
	public ReadinessState ToState;
	public ReadinessChangeReason Reason;
	public float StartTime;
	public float Duration;
	public float Progress;
	public float ProfileDuration;
	public float RankModifier;
	#endregion
}
