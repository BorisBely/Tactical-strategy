#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Editor/dev logging for route facing-arrow placement and activation.
/// Defaults: all logging off. Set <see cref="LoggingEnabled"/> = true and enable sub-flags when diagnosing.
/// </summary>
public static class FacingArrowDebug
{
	#region Public Fields
	/// <summary>Master switch for all facing-arrow debug logs.</summary>
	public static bool LoggingEnabled = false;

	/// <summary>One-shot activation events (single line per arrow).</summary>
	public static bool ActivationLoggingEnabled = false;

	/// <summary>Throttled PENDING lines while arrows wait for reach radius.</summary>
	public static bool PeriodicPendingLoggingEnabled = false;
	public static float PeriodicPendingLogIntervalSeconds = 3f;

	/// <summary>One-shot MISSED when the unit passes an arrow anchor without triggering it.</summary>
	public static bool MissedArrowLoggingEnabled = false;
	public static float MissedArrowRouteTSlack = 0.05f;

	/// <summary>Arrow priority phase transitions (Turning-&gt;BlueHold/GreenHold, etc.).</summary>
	public static bool PhaseLoggingEnabled = false;

	/// <summary>VISUAL_SYNC when facing-arrow LineRenderers are rebuilt.</summary>
	public static bool VisualSyncLoggingEnabled = false;

	/// <summary>VISUAL_REMAP / VISUAL_REBIND on route topology changes.</summary>
	public static bool VisualTopologyLoggingEnabled = false;

	/// <summary>VISUAL_DRIFT when fixed AnchorWorld and logical polyline anchor diverge.</summary>
	public static bool VisualDriftLoggingEnabled = false;
	public static float VisualDriftLogMinMeters = 0.5f;
	#endregion

	#region Public Methods
	public static void Log(RtsUnitMember _unit, string _message)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Debug.Log($"[ArrowDbg:{_unit.name}] {_message}", _unit);
	}

	public static void LogThrottled(RtsUnitMember _unit, ref float _nextLogTime, string _message)
	{
		if (!PeriodicPendingLoggingEnabled || !LoggingEnabled || _unit == null)
			return;
		if (Time.time < _nextLogTime)
			return;

		_nextLogTime = Time.time + PeriodicPendingLogIntervalSeconds;
		Log(_unit, _message);
	}
	#endregion
}
#endif
