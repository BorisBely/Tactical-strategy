/// <summary>
/// Default combat engagement rules (G6). Selector = who; this = what to do.
/// Does not shoot. LastKnown is not represented here — missing LOS aim → Track.
/// Threat never gates Fire. Unknown may Fire. Observe / Suppress / Report are never returned.
/// </summary>
public static class EngagementDecisionMath
{
	public static EngagementDecision Evaluate(in EngagementDecisionContext _context)
	{
		if (!_context.HasSelectedTarget)
			return EngagementDecision.None;

		if (!_context.HasContact)
			return EngagementDecision.Ignore;

		bool hasKnowledge = _context.HasKnowledge || _context.LastSeenConfidence > 0f;
		if (!hasKnowledge)
			return EngagementDecision.Ignore;

		if (_context.Identity == PerceivedIdentity.Friendly ||
		    _context.Relationship == PerceivedRelationship.Friendly)
			return EngagementDecision.Ignore;

		if (_context.Identity == PerceivedIdentity.Neutral)
			return EngagementDecision.Ignore;

		if (!_context.IsWorldEngageable)
			return EngagementDecision.Ignore;

		if (!_context.HasLosConfirmedAim)
			return EngagementDecision.Track;

		if (!_context.AimReadyToFire || !_context.WeaponCanFireEventually)
			return EngagementDecision.Aim;

		return EngagementDecision.Fire;
	}
}
