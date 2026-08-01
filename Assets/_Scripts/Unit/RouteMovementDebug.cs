#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Editor/dev logging for RTS route queue, formation sync, nav handoffs, and route orders.
/// For diagnostics: <see cref="LoggingEnabled"/> defaults to true.
/// Turn off when done investigating.
/// </summary>
public static class RouteMovementDebug
{
	#region Public Fields
	/// <summary>Master switch for route/order/wait/move logs.</summary>
	public static bool LoggingEnabled = false;

	/// <summary>Queued / executed / skipped route-order events.</summary>
	public static bool OrderLoggingEnabled = true;

	/// <summary>Wait-gate enter/exit/clear events (Alt wait points).</summary>
	public static bool WaitLoggingEnabled = true;

	/// <summary>Move start / stop / resume reasons.</summary>
	public static bool MoveLoggingEnabled = true;

	/// <summary>Throttled STATE / STUCK lines while on route.</summary>
	public static bool PeriodicStateLoggingEnabled = true;
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

	public static void LogOrder(RtsUnitMember _unit, string _message)
	{
		if (!LoggingEnabled || !OrderLoggingEnabled || _unit == null)
			return;

		Debug.Log($"[RouteOrder:{_unit.name}] {_message}", _unit);
	}

	public static void LogWait(RtsUnitMember _unit, string _message)
	{
		if (!LoggingEnabled || !WaitLoggingEnabled || _unit == null)
			return;

		Debug.Log($"[RouteWait:{_unit.name}] {_message}", _unit);
	}

	public static void LogMove(RtsUnitMember _unit, string _message)
	{
		if (!LoggingEnabled || !MoveLoggingEnabled || _unit == null)
			return;

		Debug.Log($"[RouteMove:{_unit.name}] {_message}", _unit);
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
