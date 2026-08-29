/// <summary>
/// Per-level hold + step-down durations. Prototype relations, not freeze.
/// Rising and falling are not symmetric.
/// </summary>
public struct ReadinessCalmDownProfile
{
	#region Public Fields
	public float AimHoldTime;
	public float PreAimHoldTime;
	public float LowReadyHoldTime;
	public float HighReadyHoldTime;
	public float AimToPreAimDuration;
	public float PreAimToReadyDuration;
	public float ReadyToCalmDuration;
	#endregion
}

/// <summary>
/// Rank-supplied readiness parameters. Rank does not invent extra states.
/// Durations are 14B prototypes (relations), not freeze.
/// </summary>
public struct ReadinessProfile
{
	#region Public Fields
	public ReadinessRankKind Rank;
	public ReadinessState CalmState;
	public ReadinessState HeardThreatState;
	public float TransitionSpeedModifier;
	public float AimTransitionModifier;
	public float CalmDownModifier;
	public float CalmDownDelay;
	public float RankReactionModifier;
	public float ReadyRaiseDuration;
	public float NotReadyAimDuration;
	public float PatrolAimDuration;
	public float LowReadyAimDuration;
	public float HighReadyAimDuration;
	public float PreAimAimDuration;
	public float AimHoldTime;
	public float PreAimHoldTime;
	public float LowReadyHoldTime;
	public float HighReadyHoldTime;
	public float AimToPreAimDuration;
	public float PreAimToReadyDuration;
	public float ReadyToCalmDuration;
	public ArmFatigueProfile ArmFatigue;
	#endregion

	#region Public Properties
	public float NotReadyToAimDuration
	{
		get => NotReadyAimDuration;
		set => NotReadyAimDuration = value;
	}

	public float PatrolToAimDuration
	{
		get => PatrolAimDuration;
		set => PatrolAimDuration = value;
	}

	public float LowReadyToAimDuration
	{
		get => LowReadyAimDuration;
		set => LowReadyAimDuration = value;
	}

	public float HighReadyToAimDuration
	{
		get => HighReadyAimDuration;
		set => HighReadyAimDuration = value;
	}

	public float PreAimToAimDuration
	{
		get => PreAimAimDuration;
		set => PreAimAimDuration = value;
	}

	public ReadinessState GunshotState
	{
		get => HeardThreatState;
		set => HeardThreatState = value;
	}

	public ReadinessState GunshotReadyState
	{
		get => HeardThreatState;
		set => HeardThreatState = value;
	}

	public float ToReadySpeed
	{
		get => TransitionSpeedModifier;
		set => TransitionSpeedModifier = value;
	}

	public float ToAimSpeed
	{
		get => AimTransitionModifier;
		set => AimTransitionModifier = value;
	}

	public float DecaySpeed
	{
		get => CalmDownModifier;
		set => CalmDownModifier = value;
	}

	public float CalmDownDelayModifier
	{
		get => CalmDownModifier;
		set => CalmDownModifier = value;
	}

	public float RankCalmDownModifier
	{
		get => CalmDownModifier;
		set => CalmDownModifier = value;
	}

	public ReadinessCalmDownProfile CalmDownProfile
	{
		get => new ReadinessCalmDownProfile
		{
			AimHoldTime = AimHoldTime,
			PreAimHoldTime = PreAimHoldTime,
			LowReadyHoldTime = LowReadyHoldTime,
			HighReadyHoldTime = HighReadyHoldTime,
			AimToPreAimDuration = AimToPreAimDuration,
			PreAimToReadyDuration = PreAimToReadyDuration,
			ReadyToCalmDuration = ReadyToCalmDuration
		};
		set
		{
			AimHoldTime = value.AimHoldTime;
			PreAimHoldTime = value.PreAimHoldTime;
			LowReadyHoldTime = value.LowReadyHoldTime;
			HighReadyHoldTime = value.HighReadyHoldTime;
			AimToPreAimDuration = value.AimToPreAimDuration;
			PreAimToReadyDuration = value.PreAimToReadyDuration;
			ReadyToCalmDuration = value.ReadyToCalmDuration;
		}
	}
	#endregion

	#region Public Methods
	public static ReadinessProfile ForRank(ReadinessRankKind _rank)
	{
		ReadinessProfile profile = DefaultDurations();
		profile.Rank = _rank;
		switch (_rank)
		{
			case ReadinessRankKind.Recruit:
				profile.CalmState = ReadinessState.NotReady;
				profile.HeardThreatState = ReadinessState.LowReady;
				profile.TransitionSpeedModifier = 0.7f;
				profile.AimTransitionModifier = 0.7f;
				profile.CalmDownModifier = 0.85f;
				break;
			case ReadinessRankKind.Soldier:
				profile.CalmState = ReadinessState.Patrol;
				profile.HeardThreatState = ReadinessState.LowReady;
				profile.TransitionSpeedModifier = 1f;
				profile.AimTransitionModifier = 1f;
				profile.CalmDownModifier = 1f;
				break;
			case ReadinessRankKind.Corporal:
				profile.CalmState = ReadinessState.Patrol;
				profile.HeardThreatState = ReadinessState.HighReady;
				profile.TransitionSpeedModifier = 1.15f;
				profile.AimTransitionModifier = 1.15f;
				profile.CalmDownModifier = 1.1f;
				break;
			case ReadinessRankKind.Veteran:
				profile.CalmState = ReadinessState.Patrol;
				profile.HeardThreatState = ReadinessState.HighReady;
				profile.TransitionSpeedModifier = 1.3f;
				profile.AimTransitionModifier = 1.3f;
				profile.CalmDownModifier = 1.15f;
				break;
			default:
				profile.CalmState = ReadinessState.Patrol;
				profile.HeardThreatState = ReadinessState.HighReady;
				profile.TransitionSpeedModifier = 1.45f;
				profile.AimTransitionModifier = 1.45f;
				profile.CalmDownModifier = 1.2f;
				break;
		}

		profile.RankReactionModifier = profile.AimTransitionModifier;
		return profile;
	}

	/// <summary>EditMode contract profile: instant raise, 1 s hold, instant step-down.</summary>
	public static ReadinessProfile Instant(ReadinessRankKind _rank)
	{
		ReadinessProfile profile = ForRank(_rank);
		profile.NotReadyAimDuration = 0f;
		profile.PatrolAimDuration = 0f;
		profile.LowReadyAimDuration = 0f;
		profile.HighReadyAimDuration = 0f;
		profile.PreAimAimDuration = 0f;
		profile.ReadyRaiseDuration = 0f;
		profile.CalmDownDelay = 1f;
		profile.AimHoldTime = 1f;
		profile.PreAimHoldTime = 1f;
		profile.LowReadyHoldTime = 1f;
		profile.HighReadyHoldTime = 1f;
		profile.AimToPreAimDuration = 0f;
		profile.PreAimToReadyDuration = 0f;
		profile.ReadyToCalmDuration = 0f;
		profile.ArmFatigue = ArmFatigueProfile.Disabled();
		return profile;
	}
	#endregion

	#region Private Methods
	private static ReadinessProfile DefaultDurations()
	{
		return new ReadinessProfile
		{
			CalmDownDelay = 6f,
			ReadyRaiseDuration = 0.22f,
			NotReadyAimDuration = 0.8f,
			PatrolAimDuration = 0.55f,
			LowReadyAimDuration = 0.3f,
			HighReadyAimDuration = 0.18f,
			PreAimAimDuration = 0.08f,
			AimHoldTime = 6f,
			PreAimHoldTime = 4f,
			LowReadyHoldTime = 10f,
			HighReadyHoldTime = 10f,
			AimToPreAimDuration = 0.4f,
			PreAimToReadyDuration = 0.5f,
			ReadyToCalmDuration = 0.7f,
			ArmFatigue = ArmFatigueProfile.PlayPrototype()
		};
	}
	#endregion
}
