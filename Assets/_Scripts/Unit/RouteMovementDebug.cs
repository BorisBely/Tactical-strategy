#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Editor/dev logging for RTS route queue, formation sync, and nav handoffs.
/// </summary>
public static class RouteMovementDebug
{
	#region Public Fields
	public static bool LoggingEnabled;
	public static bool PeriodicStateLoggingEnabled;
	public static float PeriodicStateLogIntervalSeconds = 2f;
	#endregion

	#region Public Methods
	public static void LogManager(string _message)
	{
		if (!LoggingEnabled)
			return;

		Debug.Log($"[RouteDbg:Manager] {_message}");
	}

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
