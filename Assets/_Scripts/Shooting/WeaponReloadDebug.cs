#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Verbose reload trace logs. Off by default — enable when diagnosing reload animation/state issues.
/// </summary>
public static class WeaponReloadDebug
{
	public static bool LoggingEnabled;

	public static void Log(string _message)
	{
		if (!LoggingEnabled)
			return;

		Debug.Log($"[Reload] {_message}");
	}
}
#endif
