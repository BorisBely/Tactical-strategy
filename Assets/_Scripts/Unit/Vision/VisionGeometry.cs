using UnityEngine;

/// <summary>
/// Range / FOV / direction checks for vision detection.
/// Does not raycast or choose combat targets.
/// </summary>
public static class VisionGeometry
{
	public static bool IsWithinRangeAndFov(
		Vector3 _origin,
		Vector3 _forwardXZ,
		Vector3 _point,
		float _rangeSq,
		float _halfFovDegrees,
		out float _distanceSq)
	{
		Vector3 toPoint = _point - _origin;
		toPoint.y = 0f;
		_distanceSq = toPoint.sqrMagnitude;
		if (_distanceSq > _rangeSq || _distanceSq < 0.0001f)
			return false;

		float ang = Vector3.Angle(_forwardXZ, toPoint.normalized);
		return ang <= _halfFovDegrees;
	}

	public static float ResolveHalfFovDegrees(
		float _fieldOfViewDegrees,
		bool _widenForWeaponNotReady,
		float _minHalfFovWhenNotReady,
		bool _hasTrackingTarget,
		float _trackingHalfFovExtraDegrees)
	{
		float halfFov = _fieldOfViewDegrees * 0.5f;
		if (_widenForWeaponNotReady)
			halfFov = Mathf.Max(halfFov, _minHalfFovWhenNotReady);
		if (_hasTrackingTarget)
			halfFov += _trackingHalfFovExtraDegrees;
		return halfFov;
	}

	public static Vector3 FlattenNormalized(Vector3 _direction, Vector3 _fallback)
	{
		Vector3 f = _direction;
		f.y = 0f;
		if (f.sqrMagnitude < 0.0001f)
			return _fallback.sqrMagnitude > 0.0001f ? _fallback.normalized : Vector3.forward;
		return f.normalized;
	}
}
