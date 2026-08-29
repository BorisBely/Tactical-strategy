using System.Globalization;
using UnityEngine;

/// <summary>
/// #14C.4 event logs. Not every tick. Not Move.
/// </summary>
public static class ThreatDirectionReorientationLog
{
	#region Public Properties
	public static string ChangedChannel => UnitActionLog.ThreatDirectionChanged;
	public static string FacingChannel => UnitActionLog.FacingUpdate;
	public static string FitChannel => UnitActionLog.CoverThreatFit;
	#endregion

	#region Public Methods
	public static string FormatChanged(
		ThreatDirectionCompass _from,
		ThreatDirectionCompass _to,
		float _confidence,
		float _deltaDegrees)
	{
		return "from=" + ThreatDirectionEstimator.CompassLabel(_from) +
		       " to=" + ThreatDirectionEstimator.CompassLabel(_to) +
		       " confidence=" + F2(_confidence) +
		       " delta=" + Mathf.RoundToInt(_deltaDegrees);
	}

	public static string FormatFacing(
		ThreatDirectionCompass _from,
		ThreatDirectionCompass _to,
		ThreatDirectionFacingReason _reason)
	{
		return "from=" + ThreatDirectionEstimator.CompassLabel(_from) +
		       " to=" + ThreatDirectionEstimator.CompassLabel(_to) +
		       " reason=" + _reason;
	}

	public static string FormatFit(
		int _coverId,
		ThreatDirectionCompass _direction,
		CoverThreatFit _fit)
	{
		return "cover=C" + _coverId +
		       " direction=" + ThreatDirectionEstimator.CompassLabel(_direction) +
		       " fit=" + _fit;
	}

	public static void EmitChanged(Component _actor, string _payload)
	{
		EmitOn(ChangedChannel, _actor, _payload);
	}

	public static void EmitFacing(Component _actor, string _payload)
	{
		EmitOn(FacingChannel, _actor, _payload);
	}

	public static void EmitFit(Component _actor, string _payload)
	{
		EmitOn(FitChannel, _actor, _payload);
	}
	#endregion

	#region Private Methods
	private static string F2(float _value)
	{
		return _value.ToString("0.##", CultureInfo.InvariantCulture);
	}

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
