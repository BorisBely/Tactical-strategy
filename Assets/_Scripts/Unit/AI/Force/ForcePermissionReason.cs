/// <summary>
/// Why force was allowed or denied. For logs / AI debug, not G6 math.
/// </summary>
public enum ForcePermissionReason
{
	NoContact = 0,
	FriendlyProtected = 1,
	SelfDefenseNoImmediateThreat = 2,
	SelfDefenseImmediateThreat = 3,
	NotHostile = 4,
	UnknownNotAllowed = 5,
	NeutralNotAllowed = 6,
	PolicyAllowsHostile = 7,
	NonFriendly = 8
}
