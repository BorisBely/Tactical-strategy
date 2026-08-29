using UnityEngine;

/// <summary>
/// #12: AI.EngageTarget and Combat.SelectedTarget may differ.
/// AI = Hostile+VisibleNow max-Threat (want to fight). Combat = G5 knowledge + hysteresis.
/// Observed, never auto-merged.
/// </summary>
public static class TargetCombatMismatch
{
	#region Constants
	public const string Explanation =
		"AI.EngageTarget is Hostile+VisibleNow max-Threat (tactical engage intent). " +
		"Combat.SelectedTarget is G5 knowledge selection with hysteresis. Not merged.";
	#endregion

	#region Public Methods
	public static bool IsMismatch(Transform _aiEngageTarget, Transform _combatSelectedTarget)
	{
		return _aiEngageTarget != null &&
		       _combatSelectedTarget != null &&
		       !ReferenceEquals(_aiEngageTarget, _combatSelectedTarget);
	}

	public static string Describe(Transform _aiEngageTarget, Transform _combatSelectedTarget)
	{
		return IsMismatch(_aiEngageTarget, _combatSelectedTarget) ? Explanation : string.Empty;
	}
	#endregion
}
