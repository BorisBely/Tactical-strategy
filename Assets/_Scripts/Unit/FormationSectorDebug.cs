#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor/dev logging for formation live-sector state, facing overrides, and locomotion gates.
/// </summary>
public static class FormationSectorDebug
{
	#region Public Fields
	public static bool LoggingEnabled = false;
	public static bool PeriodicStateLoggingEnabled = false;
	public static float PeriodicStateLogIntervalSeconds = 1.5f;
	public static bool LogLocomotionOverrideGate = true;
	public static float LocomotionGateLogIntervalSeconds = 1.5f;
	#endregion

	#region Private Fields
	private static readonly Dictionary<int, float> s_NextLocomotionGateLogTime = new Dictionary<int, float>();
	#endregion

	#region Public Methods
	public static void Log(RtsUnitMember _unit, string _message)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Debug.Log($"[FormSector:{_unit.name}] {_message}", _unit);
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

	public static void LogApplyOverride(RtsUnitMember _unit, float _yaw, string _reason)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Log(_unit, $"APPLY_OVERRIDE yaw={_yaw:F1} ready={_unit.WantsReady} reason={_reason}");
	}

	public static void LogClearOverride(RtsUnitMember _unit, string _reason)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Log(_unit, $"CLEAR_OVERRIDE ready={_unit.WantsReady} reason={_reason}");
	}

	public static void LogLocomotionGate(
		RtsUnitMember _unit,
		MonoBehaviour _locomotion,
		float _overrideYaw,
		bool _apply,
		bool _moving,
		bool _hasIntent)
	{
		if (!LoggingEnabled || !LogLocomotionOverrideGate || _unit == null)
			return;

		int id = _unit.GetInstanceID();
		if (s_NextLocomotionGateLogTime.TryGetValue(id, out float nextTime) && Time.time < nextTime)
			return;

		s_NextLocomotionGateLogTime[id] = Time.time + LocomotionGateLogIntervalSeconds;
		Log(
			_unit,
			$"LOCOMOTION_GATE via={_locomotion.GetType().Name} apply={_apply} ready={_unit.WantsReady} " +
			$"moving={_moving} intent={_hasIntent} overrideYaw={_overrideYaw:F1}");
	}

	public static void LogPendingSlot(RtsUnitMember _unit, float _yaw, string _action)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Log(_unit, $"PENDING_SLOT {_action} yaw={_yaw:F1}");
	}

	public static void LogSlotArrivalReject(RtsUnitMember _unit, string _reason, string _probe = null)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		string probe = string.IsNullOrEmpty(_probe) ? string.Empty : $" probe={_probe}";
		Log(_unit, $"SLOT_ARRIVAL_REJECT reason={_reason}{probe}");
	}

	public static void LogArrivalProbe(RtsUnitMember _unit, string _context, string _probe)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Log(_unit, $"ARRIVAL_PROBE ctx={_context} {_probe}");
	}

	public static void LogMarchLock(RtsUnitMember _unit, bool _active, string _reason)
	{
		if (!LoggingEnabled || _unit == null)
			return;

		Log(_unit, $"MARCH_LOCK active={_active} reason={_reason}");
	}
	#endregion
}
#endif
