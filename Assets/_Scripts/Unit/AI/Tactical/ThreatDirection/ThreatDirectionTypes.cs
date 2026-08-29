/// <summary>
/// #14C knowledge of likely threat bearing. Not a precise enemy position.
/// </summary>
public enum ThreatDirectionState
{
	None = 0,
	Expected = 1,
	Known = 2,
	Stale = 3
}

/// <summary>
/// Provenance of the current bearing. Visual LastKnown outranks Sound, then AllyReport, then spawn estimate.
/// </summary>
public enum ThreatDirectionSource
{
	InitialEstimate = 0,
	AllyReport = 1,
	Sound = 2,
	Visual = 3
}

/// <summary>
/// World XZ octant. +Z is North, +X is East.
/// </summary>
public enum ThreatDirectionCompass
{
	North = 0,
	NorthEast = 1,
	East = 2,
	SouthEast = 3,
	South = 4,
	SouthWest = 5,
	West = 6,
	NorthWest = 7
}

/// <summary>Event that may rewrite knowledge. Not a per-tick poll.</summary>
public enum ThreatDirectionStimulus
{
	None = 0,
	BattleStart = 1,
	HostileVisible = 2,
	HostileLost = 3,
	GunshotHeard = 4,
	AllyReport = 5,
	KnowledgeExpiry = 6
}
