/// <summary>
/// Freshness of physical evidence for a perceived contact (orthogonal to DetectionState).
/// Detected + RecentlyLost / Detected + Lost are valid.
/// After grace, use <see cref="Lost"/> (contact may remain with LastSeen). NotObserved is reserved for “never had a contact”.
/// </summary>
public enum ObservationState
{
	/// <summary>No contact / never observed (not used after grace — see Lost).</summary>
	NotObserved = 0,
	Observed = 1,
	RecentlyLost = 2,
	/// <summary>Grace expired; contact may remain in registry with LastSeen evidence.</summary>
	Lost = 3
}
