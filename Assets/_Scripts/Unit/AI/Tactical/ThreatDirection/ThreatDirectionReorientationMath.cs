using UnityEngine;

/// <summary>
/// #14C.4 cover vs current threat. Physically valid cover can still fit poorly.
/// </summary>
public enum CoverThreatFit
{
	Unknown = 0,
	Good = 1,
	Poor = 2
}

/// <summary>
/// #14C.4 significant-change and ThreatFit. Prototype thresholds, not freeze.
/// Does not Move / Release / scan.
/// </summary>
public static class ThreatDirectionReorientationMath
{
	#region Constants
	public const float TacticalChangeDegrees = 50f;
	public const float TacticalConfidence = 0.4f;
	public const float FacingConfidenceFloor = 0.4f;
	#endregion

	#region Public Methods
	public static float AngleDegrees(Vector3 _from, Vector3 _to)
	{
		Vector3 a = _from;
		Vector3 b = _to;
		a.y = 0f;
		b.y = 0f;
		if (a.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr ||
		    b.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return 0f;
		return Vector3.Angle(a.normalized, b.normalized);
	}

	public static bool IsSignificantChange(Vector3 _from, Vector3 _to, float _confidence)
	{
		if (_confidence < TacticalConfidence)
			return false;
		return AngleDegrees(_from, _to) >= TacticalChangeDegrees;
	}

	public static bool AllowsFacingUpdate(float _confidence, bool _alreadyHasFacing)
	{
		if (!_alreadyHasFacing)
			return true;
		return _confidence >= FacingConfidenceFloor;
	}

	public static CoverThreatFit ClassifyFit(Vector3 _coverNormal, Vector3 _threatDirection)
	{
		float alignment = ThreatDirectionCoverMath.Alignment(_coverNormal, _threatDirection);
		return alignment >= ThreatDirectionCoverMath.GoodDot
			? CoverThreatFit.Good
			: CoverThreatFit.Poor;
	}

	public static float TurnDuration(float _fatigue, in ArmFatigueProfile _profile)
	{
		return ArmFatigueMath.FinalTurnToTargetTime(_fatigue, in _profile);
	}

	public static Vector3 DirectionFromYaw(float _yawDegrees)
	{
		float yaw = _yawDegrees * Mathf.Deg2Rad;
		return new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
	}
	#endregion
}
