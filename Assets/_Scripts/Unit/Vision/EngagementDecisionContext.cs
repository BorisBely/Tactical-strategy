/// <summary>
/// Pure snapshot for <see cref="EngagementDecisionMath"/>. No MonoBehaviour, no world UnitTeam.
/// LastKnown is not an input — use <see cref="HasLosConfirmedAim"/> from TargetSelector.
/// </summary>
public struct EngagementDecisionContext
{
	public bool HasSelectedTarget;
	public bool HasContact;
	public PerceivedIdentity Identity;
	public PerceivedRelationship Relationship;
	public ThreatLevel Threat;
	public ObservationState ObservationState;
	public float LastSeenConfidence;
	/// <summary>
	/// Source-blind knowledge. True when LastSeen, sound, or shared confidence is still live.
	/// Tests that only set LastSeenConfidence remain valid: Evaluate also treats LastSeenConfidence above zero as knowledge.
	/// </summary>
	public bool HasKnowledge;
	public bool IsWorldEngageable;
	public bool HasLosConfirmedAim;
	public bool WeaponCanFireEventually;
	public bool AimReadyToFire;
}
