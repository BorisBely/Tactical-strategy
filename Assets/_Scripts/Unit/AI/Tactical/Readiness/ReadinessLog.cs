using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// #14B event log. READINESS keeps 14B.1 payloads. 14B.3 adds EVENT / TRANSITION / DECAY.
/// Event-based, not every tick.
/// </summary>
public static class ReadinessLog
{
	#region Public Properties
	public static string Channel => UnitActionLog.Readiness;
	public static string EventChannel => UnitActionLog.ReadinessEvent;
	public static string TransitionChannel => UnitActionLog.ReadinessTransition;
	public static string DecayChannel => UnitActionLog.ReadinessDecay;
	#endregion

	#region Public Methods
	public static string ReasonLabel(ReadinessChangeReason _reason)
	{
		switch (_reason)
		{
			case ReadinessChangeReason.Gunshot:
				return "GunshotHeard";
			case ReadinessChangeReason.Calm:
			case ReadinessChangeReason.CalmDown:
				return "CalmDown";
			default:
				return _reason.ToString();
		}
	}

	public static string StimulusLabel(ReadinessStimulus _stimulus)
	{
		switch (_stimulus)
		{
			case ReadinessStimulus.None:
				return "None";
			default:
				return _stimulus.ToString();
		}
	}

	public static string FormatState(ReadinessState _state, ReadinessChangeReason _reason)
	{
		return "state=" + _state + " reason=" + ReasonLabel(_reason);
	}

	public static string FormatTransition(
		ReadinessState _from,
		ReadinessState _to,
		ReadinessChangeReason _reason,
		float _duration)
	{
		return FormatTransition(
			_from,
			_to,
			_reason,
			_duration,
			ReadinessRankKind.Soldier,
			_duration,
			1f);
	}

	public static string FormatTransition(
		ReadinessState _from,
		ReadinessState _to,
		ReadinessChangeReason _reason,
		float _duration,
		ReadinessRankKind _rank,
		float _profileDuration,
		float _rankModifier)
	{
		return "transition=" + _from + "->" + _to +
		       " reason=" + ReasonLabel(_reason) +
		       " duration=" + _duration.ToString("0.###", CultureInfo.InvariantCulture) +
		       " rank=" + _rank +
		       " profileDuration=" + _profileDuration.ToString("0.###", CultureInfo.InvariantCulture) +
		       " rankModifier=" + _rankModifier.ToString("0.###", CultureInfo.InvariantCulture);
	}

	public static string FormatChannelTransition(
		ReadinessState _from,
		ReadinessState _to,
		ReadinessChangeReason _reason)
	{
		return FormatChannelTransition(
			_from,
			_to,
			_reason,
			ReadinessRankKind.Soldier,
			0f,
			0f,
			1f);
	}

	public static string FormatChannelTransition(
		ReadinessState _from,
		ReadinessState _to,
		ReadinessChangeReason _reason,
		ReadinessRankKind _rank,
		float _duration,
		float _profileDuration,
		float _rankModifier)
	{
		return _from + "->" + _to +
		       " from=" + _from +
		       " to=" + _to +
		       " rank=" + _rank +
		       " duration=" + _duration.ToString("0.###", CultureInfo.InvariantCulture) +
		       " reason=" + ReasonLabel(_reason) +
		       " profileDuration=" + _profileDuration.ToString("0.###", CultureInfo.InvariantCulture) +
		       " rankModifier=" + _rankModifier.ToString("0.###", CultureInfo.InvariantCulture);
	}

	public static string FormatDecayHold(ReadinessState _state, float _remaining)
	{
		return "hold state=" + _state +
		       " remaining=" + _remaining.ToString("0.#", CultureInfo.InvariantCulture);
	}

	public static string FormatEvent(ReadinessStimulus _type, string _target)
	{
		string line = "type=" + StimulusLabel(_type);
		if (!string.IsNullOrEmpty(_target))
			line += " target=" + _target;
		return line;
	}

	public static bool IsDecayReason(ReadinessChangeReason _reason)
	{
		return _reason == ReadinessChangeReason.CombatActivityExpired ||
		       _reason == ReadinessChangeReason.Calm ||
		       _reason == ReadinessChangeReason.CalmDown;
	}

	public static void Emit(Component _actor, string _payload)
	{
		EmitOn(Channel, _actor, _payload);
	}

	public static void EmitEvent(Component _actor, string _payload)
	{
		EmitOn(EventChannel, _actor, _payload);
	}

	public static void EmitTransition(Component _actor, string _payload)
	{
		EmitOn(TransitionChannel, _actor, _payload);
	}

	public static void EmitDecay(Component _actor, string _payload)
	{
		EmitOn(DecayChannel, _actor, _payload);
	}

	public static bool ContainsTransition(
		IReadOnlyList<string> _lines,
		ReadinessState _from,
		ReadinessState _to)
	{
		if (_lines == null)
			return false;

		string needle = "transition=" + _from + "->" + _to;
		for (int i = 0; i < _lines.Count; i++)
		{
			if (_lines[i] != null && _lines[i].IndexOf(needle, System.StringComparison.Ordinal) >= 0)
				return true;
		}

		return false;
	}

	public static bool ContainsEvent(IReadOnlyList<string> _lines, ReadinessStimulus _type)
	{
		if (_lines == null)
			return false;

		string needle = "type=" + StimulusLabel(_type);
		for (int i = 0; i < _lines.Count; i++)
		{
			if (_lines[i] != null && _lines[i].IndexOf(needle, System.StringComparison.Ordinal) >= 0)
				return true;
		}

		return false;
	}

	public static bool ContainsHold(IReadOnlyList<string> _lines, ReadinessState _state)
	{
		if (_lines == null)
			return false;

		string needle = "hold state=" + _state;
		for (int i = 0; i < _lines.Count; i++)
		{
			if (_lines[i] != null && _lines[i].IndexOf(needle, System.StringComparison.Ordinal) >= 0)
				return true;
		}

		return false;
	}

	public static bool ContainsDecay(
		IReadOnlyList<string> _lines,
		ReadinessState _from,
		ReadinessState _to)
	{
		if (_lines == null)
			return false;

		string needle = _from + "->" + _to;
		for (int i = 0; i < _lines.Count; i++)
		{
			string line = _lines[i];
			if (string.IsNullOrEmpty(line))
				continue;
			if (line.IndexOf("type=", System.StringComparison.Ordinal) >= 0)
				continue;
			if (line.IndexOf(needle, System.StringComparison.Ordinal) >= 0 &&
			    (line.IndexOf("CombatActivityExpired", System.StringComparison.Ordinal) >= 0 ||
			     line.IndexOf("CalmDown", System.StringComparison.Ordinal) >= 0))
				return true;
		}

		return false;
	}
	#endregion

	#region Private Methods
	private static void EmitOn(string _channel, Component _actor, string _payload)
	{
		if (!UnitActionLog.Enabled || string.IsNullOrEmpty(_payload))
			return;

		UnitActionLog.Write(_actor, _channel, _payload);
		string prefix = _actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty;
		UnitActionLog.Timeline(_channel, prefix + _payload);
	}
	#endregion
}
