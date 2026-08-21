/// <summary>
/// Hold vetoes Aim/Fire to Ignore. Track stays. Engage does not call Fire.
/// Does not change <see cref="EngagementDecisionMath"/>. Stage 2 FROZEN.
/// </summary>
public static class CombatIntentMath
{
	#region Public Methods
	public static CombatIntent FromEngageAction(bool _isEngage)
	{
		return _isEngage ? CombatIntent.Engage : CombatIntent.Hold;
	}

	public static EngagementDecision ApplyHoldVeto(EngagementDecision _g6, CombatIntent _intent)
	{
		if (_intent != CombatIntent.Hold)
			return _g6;

		if (_g6 == EngagementDecision.Fire || _g6 == EngagementDecision.Aim)
			return EngagementDecision.Ignore;

		return _g6;
	}
	#endregion
}
