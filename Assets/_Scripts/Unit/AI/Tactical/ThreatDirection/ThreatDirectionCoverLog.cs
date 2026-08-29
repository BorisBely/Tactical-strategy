using System.Globalization;
using UnityEngine;

/// <summary>
/// #14C.1 event logs. COVER_DIRECTION / FACING_DIRECTION. Not every tick.
/// </summary>
public static class ThreatDirectionCoverLog
{
	#region Public Properties
	public static string CoverChannel => UnitActionLog.CoverDirection;
	public static string FacingChannel => UnitActionLog.FacingDirection;
	#endregion

	#region Public Methods
	public static string FormatCover(
		in ThreatDirectionKnowledge _knowledge,
		int _coverId,
		float _adjustment)
	{
		return "source=" + ConsumerSource(in _knowledge) +
		       " dir=" + ThreatDirectionEstimator.CompassLabel(_knowledge.Compass) +
		       " cover=C" + _coverId +
		       " adjustment=" + Signed(_adjustment);
	}

	public static string FormatFacing(in ThreatDirectionKnowledge _knowledge)
	{
		return "dir=" + ThreatDirectionEstimator.CompassLabel(_knowledge.Compass) +
		       " source=" + ConsumerSource(in _knowledge);
	}

	public static void EmitCover(Component _actor, string _payload)
	{
		EmitOn(CoverChannel, _actor, _payload);
	}

	public static void EmitFacing(Component _actor, string _payload)
	{
		EmitOn(FacingChannel, _actor, _payload);
	}

	public static string ConsumerSource(in ThreatDirectionKnowledge _knowledge)
	{
		if (_knowledge.State == ThreatDirectionState.Expected)
			return "Expected";
		return ThreatDirectionLog.SourceLabel(_knowledge.Source);
	}
	#endregion

	#region Private Methods
	private static string Signed(float _value)
	{
		string body = _value.ToString("0.##", CultureInfo.InvariantCulture);
		if (_value > 0f && body.IndexOf('+') < 0)
			return "+" + body;
		return body;
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
