/// <summary>
/// Infantry default: names today's fire intent (visible+LOS+aim+weapon → Fire; memory → Track).
/// Does not implement Scout / MG / Commander / Civilian roles.
/// </summary>
public sealed class DefaultCombatEngagementPolicy : IEngagementPolicy
{
	public EngagementDecision Evaluate(in EngagementDecisionContext _context)
	{
		return EngagementDecisionMath.Evaluate(_context);
	}
}
