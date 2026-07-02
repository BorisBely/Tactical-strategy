using UnityEngine;

/// <summary>
/// Единый источник состояния паузы симуляции: меню (Esc) и тактическая (Space).
/// </summary>
public static class GamePauseState
{
	#region Public Properties
	public static bool IsMenuPaused { get; private set; }
	public static bool IsTacticalPaused { get; private set; }
	public static bool IsSimulationPaused => IsMenuPaused || IsTacticalPaused;
	#endregion

	#region Internal Methods
	internal static void SetMenuPaused(bool _paused)
	{
		IsMenuPaused = _paused;
		ApplyTimeScale();
	}

	internal static void SetTacticalPaused(bool _paused)
	{
		IsTacticalPaused = _paused;
		ApplyTimeScale();
	}

	internal static void ResetAll()
	{
		IsMenuPaused = false;
		IsTacticalPaused = false;
		ApplyTimeScale();
	}
	#endregion

	#region Public Methods
	public static void ApplyTimeScale()
	{
		Time.timeScale = IsSimulationPaused ? 0f : 1f;
	}
	#endregion
}
