/// <summary>
/// Pure Use-of-Force evaluator. Relationship only — not Identity, not UnitTeam, not ThreatLevel.
/// Does not call TargetSelector, EngagementDecisionMath, or Weapon.
/// Level 5 is Relationship != Friendly.
/// </summary>
public static class UseOfForceEvaluator
{
	#region Public Methods
	public static ForcePermission Evaluate(in UseOfForceContext _context)
	{
		if (!_context.HasContact)
			return ForcePermission.Deny(ForcePermissionReason.NoContact);

		if (_context.Relationship == PerceivedRelationship.Friendly)
			return ForcePermission.Deny(ForcePermissionReason.FriendlyProtected);

		switch (_context.Level)
		{
			case UseOfForceLevel.SelfDefense:
				return EvaluateSelfDefense(_context.Relationship, _context.ImmediateThreat);
			case UseOfForceLevel.RestrictedDefense:
			case UseOfForceLevel.MissionCombat:
			case UseOfForceLevel.FullEngagement:
				return EvaluateHostileOnly(_context.Relationship);
			case UseOfForceLevel.NoFriendlyFire:
				return ForcePermission.Allow(ForcePermissionReason.NonFriendly);
			default:
				return ForcePermission.Deny(ForcePermissionReason.NoContact);
		}
	}
	#endregion

	#region Private Methods
	private static ForcePermission EvaluateSelfDefense(PerceivedRelationship _relationship, bool _immediateThreat)
	{
		if (_relationship == PerceivedRelationship.Hostile)
		{
			return _immediateThreat
				? ForcePermission.Allow(ForcePermissionReason.SelfDefenseImmediateThreat)
				: ForcePermission.Deny(ForcePermissionReason.SelfDefenseNoImmediateThreat);
		}

		return DenyNonHostile(_relationship);
	}

	private static ForcePermission EvaluateHostileOnly(PerceivedRelationship _relationship)
	{
		if (_relationship == PerceivedRelationship.Hostile)
			return ForcePermission.Allow(ForcePermissionReason.PolicyAllowsHostile);

		return DenyNonHostile(_relationship);
	}

	private static ForcePermission DenyNonHostile(PerceivedRelationship _relationship)
	{
		if (_relationship == PerceivedRelationship.Unknown)
			return ForcePermission.Deny(ForcePermissionReason.UnknownNotAllowed);
		if (_relationship == PerceivedRelationship.Neutral)
			return ForcePermission.Deny(ForcePermissionReason.NeutralNotAllowed);
		return ForcePermission.Deny(ForcePermissionReason.NotHostile);
	}
	#endregion
}
