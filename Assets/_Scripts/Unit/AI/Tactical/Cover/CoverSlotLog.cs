using UnityEngine;

/// <summary>
/// COVER_STATE lines. Board owns Reserved / Occupied / Released.
/// Overlay owns Approaching / Acquired. Does not Move. Does not retune score.
/// </summary>
public static class CoverSlotLog
{
	#region Public Methods
	public static void Write(
		Component _actor,
		int _unitId,
		int _candidateId,
		CoverSlotPhase _phase,
		CoverReservationReason _reason = CoverReservationReason.None,
		float _distanceMeters = -1f,
		string _extra = null)
	{
		if (!UnitActionLog.Enabled || _candidateId == 0)
			return;

		string unit = _actor != null ? UnitActionLog.Slot(_actor) : _unitId.ToString();
		string payload =
			"unit=" + unit +
			" candidate=C" + _candidateId +
			" state=" + _phase;
		if (_reason != CoverReservationReason.None)
			payload += " reason=" + _reason;
		if (_distanceMeters >= 0f)
			payload += " dist=" + UnitActionLog.F2(_distanceMeters);
		if (!string.IsNullOrEmpty(_extra))
			payload += " " + _extra;

		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.CoverState, payload);
		UnitActionLog.Timeline(
			UnitActionLog.CoverState,
			(_actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty) + payload);
	}
	#endregion
}
