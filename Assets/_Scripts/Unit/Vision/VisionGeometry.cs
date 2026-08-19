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

	/// <summary>
	/// Conservative range/FOV using root plus optional bounds. False negatives are forbidden;
	/// extra false positives are acceptable (they still go through LOS later).
	/// </summary>
	public static bool IsWithinCoarseRangeAndFov(
		Vector3 _origin,
		Vector3 _forwardXZ,
		Vector3 _rootPosition,
		bool _hasBounds,
		Bounds _bounds,
		float _rangeSq,
		float _halfFovDegrees,
		out float _distanceSq,
		out bool _rangePass,
		out bool _fovPass)
	{
		_rangePass = false;
		_fovPass = false;
		_distanceSq = HorizontalDistanceSq(_origin, _rootPosition);

		TryCoarsePoint(_origin, _forwardXZ, _rootPosition, _rangeSq, _halfFovDegrees, ref _rangePass, ref _fovPass);
		if (_hasBounds)
		{
			TryCoarsePoint(_origin, _forwardXZ, _bounds.center, _rangeSq, _halfFovDegrees, ref _rangePass, ref _fovPass);
			TryCoarsePoint(
				_origin,
				_forwardXZ,
				_bounds.ClosestPoint(_origin),
				_rangeSq,
				_halfFovDegrees,
				ref _rangePass,
				ref _fovPass);
		}

		return _rangePass && _fovPass;
	}

	public static float HorizontalDistanceSq(Vector3 _origin, Vector3 _point)
	{
		Vector3 toPoint = _point - _origin;
		toPoint.y = 0f;
		return toPoint.sqrMagnitude;
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

	/// <summary>Horizontal angle in degrees between forward XZ and a world direction (Y ignored).</summary>
	public static float HorizontalAngleDegrees(Vector3 _forwardXZ, Vector3 _toPoint)
	{
		Vector3 forward = FlattenNormalized(_forwardXZ, Vector3.forward);
		Vector3 to = _toPoint;
		to.y = 0f;
		if (to.sqrMagnitude < 0.0001f)
			return 0f;
		return Vector3.Angle(forward, to.normalized);
	}

	private static void TryCoarsePoint(
		Vector3 _origin,
		Vector3 _forwardXZ,
		Vector3 _point,
		float _rangeSq,
		float _halfFovDegrees,
		ref bool _rangePass,
		ref bool _fovPass)
	{
		if (IsWithinRangeAndFov(_origin, _forwardXZ, _point, _rangeSq, _halfFovDegrees, out _))
		{
			_rangePass = true;
			_fovPass = true;
			return;
		}

		if (HorizontalDistanceSq(_origin, _point) <= _rangeSq)
			_rangePass = true;
	}
}
