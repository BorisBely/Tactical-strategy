using System.Globalization;
using UnityEngine;

/// <summary>
/// #14C.5 event log. Not every tick. Not Move.
/// </summary>
public static class ThreatDirectionRepositionLog
{
	#region Public Properties
	public static string Channel => UnitActionLog.ThreatReposition;
	#endregion

	#region Public Methods
	public static string Format(
		ThreatDirectionRepositionKind _kind,
		CoverThreatFit _fit,
		float _confidence,
		float _deltaDegrees,
		int _currentId,
		int _bestId)
	{
		return "kind=" + _kind +
		       " fit=" + _fit +
		       " confidence=" + F2(_confidence) +
		       " delta=" + Mathf.RoundToInt(_deltaDegrees) +
		       " current=C" + _currentId +
		       " best=C" + _bestId +
		       " margin=" + F2(ThreatDirectionRepositionMath.ThreatRepositionMargin);
	}

	public static void Emit(Component _actor, string _payload)
	{
		if (!UnitActionLog.Enabled || string.IsNullOrEmpty(_payload))
			return;

		UnitActionLog.Write(_actor, Channel, _payload);
		string prefix = _actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty;
		UnitActionLog.Timeline(Channel, prefix + _payload);
	}
	#endregion

	#region Private Methods
	private static string F2(float _value)
	{
		return _value.ToString("0.##", CultureInfo.InvariantCulture);
	}
	#endregion
}
