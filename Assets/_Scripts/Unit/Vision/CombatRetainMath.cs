using UnityEngine;

/// <summary>
/// Reload / misfire retain uses the current VisionSource envelope.
/// Not a second SELECT, not memory, not LastKnown AimPoint.
/// </summary>
public static class CombatRetainMath
{
	#region Public Methods
	public static float ResolveRetainRangeMeters(float _resolvedMaxRangeMeters)
	{
		return Mathf.Max(0.5f, _resolvedMaxRangeMeters);
	}

	public static bool CanRetainAtDistance(float _distanceMeters, float _resolvedMaxRangeMeters)
	{
		if (_distanceMeters < 0.01f)
			return false;
		return UnitVisionProfile.IsWithinResolvedRange(
			_distanceMeters,
			ResolveRetainRangeMeters(_resolvedMaxRangeMeters));
	}
	#endregion
}
