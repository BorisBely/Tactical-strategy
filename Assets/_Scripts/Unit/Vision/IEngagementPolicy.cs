/// <summary>
/// Role-specific mapping from knowledge snapshot to <see cref="EngagementDecision"/>.
/// G6 ships <see cref="DefaultCombatEngagementPolicy"/> only.
/// </summary>
public interface IEngagementPolicy
{
	EngagementDecision Evaluate(in EngagementDecisionContext _context);
}
