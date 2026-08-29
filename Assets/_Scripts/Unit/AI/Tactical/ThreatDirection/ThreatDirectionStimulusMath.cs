using UnityEngine;

/// <summary>
/// Maps frozen perception channels onto #14C positions. Does not write Vision / LastKnown.
/// </summary>
public static class ThreatDirectionStimulusMath
{
	#region Public Methods
	public static bool TryGetVisual(in AIPerceptionFrame _frame, out Vector3 _lastKnown)
	{
		_lastKnown = Vector3.zero;
		if (!ReadinessStimulusMath.TryGetHostileVisible(in _frame, out AIContactKnowledge contact))
			return false;
		_lastKnown = contact.LastKnownPosition;
		return true;
	}

	public static bool TryGetSound(in AIPerceptionFrame _frame, out Vector3 _position, out float _time)
	{
		_position = Vector3.zero;
		_time = 0f;
		if (!UnitAISearchDecision.TryGetSearchSound(in _frame, out AISoundContact sound))
			return false;
		_position = sound.Position;
		_time = sound.Time;
		return true;
	}

	public static bool TryGetReport(in AIPerceptionFrame _frame, out Vector3 _position, out float _time)
	{
		_position = Vector3.zero;
		_time = 0f;
		if (!UnitAISearchDecision.TryGetSearchReport(in _frame, out AIReportContact report))
			return false;
		_position = report.Position;
		_time = report.Time;
		return true;
	}
	#endregion
}
