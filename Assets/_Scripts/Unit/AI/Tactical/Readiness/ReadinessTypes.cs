/// <summary>
/// #14B independent readiness level. Not <see cref="WeaponPoseState"/>, not G6, not UnitAIState.
/// </summary>
public enum ReadinessState
{
	NotReady = 0,
	Patrol = 1,
	LowReady = 2,
	HighReady = 3,
	PreAim = 4,
	Aim = 5
}

/// <summary>
/// Perception / combat facts consumed by <see cref="ReadinessController"/>. They do not write state themselves.
/// </summary>
public enum ReadinessStimulus
{
	None = 0,
	HostileVisible = 1,
	GunshotHeard = 2,
	CombatContactLost = 3,
	CombatActivityExpired = 4,
	HostileLost = 5,
	CombatActivity = 6
}

/// <summary>Why CurrentState last changed. Event-based, not per tick.</summary>
public enum ReadinessChangeReason
{
	None = 0,
	Initial = 1,
	Gunshot = 2,
	HostileVisible = 3,
	CombatActivityExpired = 4,
	Calm = 5,
	TransitionComplete = 6,
	CalmDown = 7
}

/// <summary>#14B.5 decay phase. Hold then one ladder step. Not per-tick spam.</summary>
public enum ReadinessDecayPhase
{
	None = 0,
	Hold = 1,
	StepDown = 2
}

/// <summary>
/// Behavioural ranks for readiness policy. Matches DisplayName order
/// Recruit → Soldier → Corporal → Veteran → Elite, not Rank_*.asset file names.
/// </summary>
public enum ReadinessRankKind
{
	Recruit = 0,
	Soldier = 1,
	Corporal = 2,
	Veteran = 3,
	Elite = 4
}
