using UnityEngine;

/// <summary>
/// Inputs for <see cref="UseOfForceEvaluator"/>. Does not copy <see cref="PerceivedContact"/>.
/// <see cref="State"/> and <see cref="Target"/> are for logs only — not used in the decision.
/// </summary>
public struct UseOfForceContext
{
	public UseOfForceLevel Level;
	public PerceivedRelationship Relationship;
	public bool ImmediateThreat;
	public bool HasContact;
	public Transform Target;
	public UnitAIState State;
}
