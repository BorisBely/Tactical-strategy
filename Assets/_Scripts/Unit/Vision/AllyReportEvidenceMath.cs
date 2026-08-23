using UnityEngine;

/// <summary>
/// Stage 17: range / throttle / event build. No Physics, not VisionRange, not radio nets.
/// </summary>
public static class AllyReportEvidenceMath
{
	#region Constants
	public const float DefaultRangeMeters = 80f;
	public const float MinIntervalSeconds = 1f;
	public const float MoveThresholdMeters = 8f;
	#endregion

	#region Public Methods
	public static bool IsInRange(float _distanceSq, float _rangeSq)
	{
		if (_rangeSq <= 0f)
			return false;
		return _distanceSq <= _rangeSq;
	}

	public static bool ShouldPublish(
		bool _hasPrevious,
		float _nowTime,
		float _lastTime,
		Vector3 _lastPosition,
		PerceivedIdentity _lastIdentity,
		Vector3 _position,
		PerceivedIdentity _identity)
	{
		if (!_hasPrevious)
			return true;
		if (_nowTime + 0.0001f < _lastTime + MinIntervalSeconds)
			return false;
		if (_identity != _lastIdentity)
			return true;
		return (_position - _lastPosition).sqrMagnitude >=
			MoveThresholdMeters * MoveThresholdMeters;
	}

	public static WorldAllyReportEvent Create(
		Transform _reporter,
		Transform _subject,
		Vector3 _position,
		PerceivedIdentity _reportedIdentity,
		float _confidence)
	{
		return new WorldAllyReportEvent
		{
			Reporter = _reporter,
			Subject = _subject,
			Position = _position,
			ReportedIdentity = _reportedIdentity,
			Confidence = Mathf.Clamp01(_confidence),
			RangeMeters = DefaultRangeMeters,
			Time = Time.time
		};
	}
	#endregion
}
