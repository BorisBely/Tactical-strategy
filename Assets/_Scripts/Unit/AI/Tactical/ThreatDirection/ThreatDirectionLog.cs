using System.Globalization;
using UnityEngine;

/// <summary>
/// #14C event log. THREAT_DIRECTION on source / state / compass. THREAT_DIRECTION_UPDATE on quality bands.
/// </summary>
public static class ThreatDirectionLog
{
	#region Public Properties
	public static string Channel => UnitActionLog.ThreatDirection;
	public static string UpdateChannel => UnitActionLog.ThreatDirectionUpdate;
	#endregion

	#region Public Methods
	public static string SourceLabel(ThreatDirectionSource _source)
	{
		switch (_source)
		{
			case ThreatDirectionSource.Visual:
				return "Visual";
			case ThreatDirectionSource.Sound:
				return "Sound";
			case ThreatDirectionSource.AllyReport:
				return "AllyReport";
			default:
				return "Initial";
		}
	}

	public static string Format(in ThreatDirectionKnowledge _knowledge)
	{
		return "source=" + SourceLabel(_knowledge.Source) +
		       " state=" + _knowledge.State +
		       " dir=" + ThreatDirectionEstimator.CompassLabel(_knowledge.Compass) +
		       " confidence=" + _knowledge.Confidence.ToString("0.##", CultureInfo.InvariantCulture) +
		       " uncertainty=" + _knowledge.UncertaintyDegrees.ToString("0.#", CultureInfo.InvariantCulture) +
		       " age=" + _knowledge.Age.ToString("0.##", CultureInfo.InvariantCulture);
	}

	public static void Emit(Component _actor, string _payload)
	{
		EmitOn(Channel, _actor, _payload);
	}

	public static void EmitUpdate(Component _actor, string _payload)
	{
		EmitOn(UpdateChannel, _actor, _payload);
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
