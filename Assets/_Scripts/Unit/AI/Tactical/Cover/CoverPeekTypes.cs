/// <summary>
/// Peek side from cover geometry. Not a random lean.
/// </summary>
public enum CoverPeekDirection
{
	None = 0,
	Left = 1,
	Right = 2
}

/// <summary>
/// Tactical lean depth. Maps to existing UnitSpineLean levels 0..3.
/// Rank does not cap physical depth.
/// </summary>
public enum CoverLeanLevel
{
	None = 0,
	Small = 1,
	Medium = 2,
	Deep = 3
}

/// <summary>
/// Overlay outcome. Lean does not call Fire.
/// </summary>
public enum CoverPeekDecisionKind
{
	None = 0,
	Lean = 1,
	Return = 2
}

/// <summary>
/// Why peek was taken or skipped. Prototype labels, not a freeze.
/// </summary>
public enum CoverPeekReason
{
	NotApplicable = 0,
	NoOpportunity = 1,
	AlreadyVisible = 2,
	NoBenefit = 3,
	TargetAccess = 4,
	TargetLost = 5,
	CommandChanged = 6,
	PositionChanged = 7,
	FireFinished = 8
}
