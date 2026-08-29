using System.Globalization;
using UnityEngine;

/// <summary>
/// #14C.3 event log. TACTICAL_POSITION. Not every tick. Not Move.
/// </summary>
public static class ThreatDirectionPositionLog
{
	#region Public Properties
	public static string Channel => UnitActionLog.TacticalPosition;
	#endregion

	#region Public Methods
	public static string Format(in CoverPositionEvaluation _evaluation)
	{
		int id = _evaluation.Candidate != null ? _evaluation.Candidate.CandidateId : 0;
		return "cover=C" + id +
		       " dirScore=" + Signed(_evaluation.DirectionScore) +
		       " facingScore=" + Signed(_evaluation.FacingScore) +
		       " weight=" + F2(_evaluation.ConfidenceWeight) +
		       " overlap=" + F2(_evaluation.SectorOverlap) +
		       " adj=" + Signed(_evaluation.PositionAdjustment);
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
	private static string Signed(float _value)
	{
		string body = F2(_value);
		if (_value > 0f && body.IndexOf('+') < 0)
			return "+" + body;
		return body;
	}

	private static string F2(float _value)
	{
		return _value.ToString("0.##", CultureInfo.InvariantCulture);
	}
	#endregion
}
