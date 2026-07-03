#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Editor/dev logging for RTS route queue, formation sync, and nav handoffs.
/// </summary>
public static class RouteMovementDebug
{
	#region Public Fields
	public static bool LoggingEnabled = false;
	public static bool PeriodicStateLoggingEnabled = false;
	public static float PeriodicStateLogIntervalSeconds = 2f;
	#endregion

	#region Public Methods
	public static void Log(RtsUnitMember _unit, string _message)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Debug.Log($"[RouteDbg:{_unit.name}] {_message}", _unit);
	}

	public static void LogThrottled(RtsUnitMember _unit, ref float _nextLogTime, string _message)
	{
		if (!PeriodicStateLoggingEnabled || !LoggingEnabled || _unit == null)
			return;
		if (Time.time < _nextLogTime)
			return;

		_nextLogTime = Time.time + PeriodicStateLogIntervalSeconds;
		Log(_unit, _message);
	}
	#endregion
}
#endif
