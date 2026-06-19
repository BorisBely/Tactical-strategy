using UnityEngine;

/// <summary>
/// Длительности фаз самолечения с учётом длины клипов и speed состояний в UnitAnimController.
/// </summary>
public static class SelfHealPresentationTiming
{
	#region Clip And State Speed
	public const float HealStartClipSeconds = 0.55f;
	public const float HealLoopClipSeconds = 1.1166667f;
	public const float HealEndClipSeconds = 0.36666667f;

	public const float HealStartStateSpeed = 0.6f;
	public const float HealLoopStateSpeed = 0.4f;
	public const float HealEndStateSpeed = 0.6f;
	#endregion

	#region Grace
	public const float TransitionGraceSeconds = 0.25f;
	public const float HealLoopsGraceSeconds = 0.5f;
	#endregion

	#region Durations
	public static float HealStartDuration => HealStartClipSeconds / HealStartStateSpeed;
	public static float HealLoopCycleDuration => HealLoopClipSeconds / HealLoopStateSpeed;
	public static float HealEndDuration => HealEndClipSeconds / HealEndStateSpeed;
	#endregion

	#region Public Methods
	public static int ResolveHealCycles(int _sortPriority)
	{
		if (_sortPriority <= 10)
			return 6;
		if (_sortPriority <= 20)
			return 5;
		if (_sortPriority <= 30)
			return 4;

		return 3;
	}

	public static float GetHealStartTimeoutSeconds()
	{
		return HealStartDuration + TransitionGraceSeconds;
	}

	public static float GetHealLoopsTimeoutSeconds(int _requiredCycles)
	{
		return HealLoopCycleDuration * Mathf.Max(1, _requiredCycles) + HealLoopsGraceSeconds;
	}

	public static float GetHealEndTimeoutSeconds()
	{
		return HealEndDuration + TransitionGraceSeconds;
	}

	public static float GetTotalPresentationDurationSeconds(int _requiredCycles)
	{
		int cycles = Mathf.Max(1, _requiredCycles);
		return HealStartDuration + HealLoopCycleDuration * cycles + HealEndDuration;
	}
	#endregion
}
