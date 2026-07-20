using UnityEngine;

/// <summary>
/// Баллистика ракет гранатомётов: решение угла возвышения и сэмпл параболы (g = const вниз).
/// </summary>
public static class RocketBallistics
{
	#region Constants
	private const float c_MinFlatDistanceMeters = 0.05f;
	private const float c_MinSpeed = 1f;
	private const int c_RaycastBufferSize = 16;
	#endregion

	#region Private Fields
	private static readonly RaycastHit[] s_RaycastHits = new RaycastHit[c_RaycastBufferSize];
	#endregion

	#region Public Methods
	/// <summary>
	/// Низкотраекторное решение: направление выстрела, чтобы при скорости/гравитации попасть в цель.
	/// </summary>
	public static bool TrySolveAimDirection(
		Vector3 _origin,
		Vector3 _target,
		float _muzzleSpeed,
		float _gravity,
		out Vector3 _aimDirection,
		out float _flightTimeSeconds)
	{
		_aimDirection = (_target - _origin).normalized;
		_flightTimeSeconds = 0f;

		float speed = Mathf.Max(c_MinSpeed, _muzzleSpeed);
		float g = Mathf.Abs(_gravity);
		if (g < 0.01f)
		{
			Vector3 flat = _target - _origin;
			_flightTimeSeconds = flat.magnitude / speed;
			return flat.sqrMagnitude > 0.0001f;
		}

		Vector3 toTarget = _target - _origin;
		Vector3 flatOffset = new Vector3(toTarget.x, 0f, toTarget.z);
		float flatDistance = flatOffset.magnitude;
		float height = toTarget.y;

		if (flatDistance < c_MinFlatDistanceMeters)
		{
			_aimDirection = height >= 0f ? Vector3.up : Vector3.down;
			_flightTimeSeconds = Mathf.Abs(height) / speed;
			return true;
		}

		float v2 = speed * speed;
		float v4 = v2 * v2;
		float flatSq = flatDistance * flatDistance;
		float discriminant = v4 - g * (g * flatSq + 2f * height * v2);

		Vector3 flatDir = flatOffset / flatDistance;

		if (discriminant < 0f)
		{
			// Цель вне досягаемости — максимальное возвышение ~45° в плоскости цели.
			const float sin45 = 0.70710678f;
			_aimDirection = (flatDir * sin45 + Vector3.up * sin45).normalized;
			_flightTimeSeconds = flatDistance / (speed * sin45);
			return false;
		}

		float root = Mathf.Sqrt(discriminant);
		// Низкий угол (прямой выстрел).
		float tanTheta = (v2 - root) / (g * flatDistance);
		float cosTheta = 1f / Mathf.Sqrt(1f + tanTheta * tanTheta);
		float sinTheta = tanTheta * cosTheta;
		_aimDirection = (flatDir * cosTheta + Vector3.up * sinTheta).normalized;
		_flightTimeSeconds = flatDistance / Mathf.Max(0.01f, speed * cosTheta);
		return true;
	}

	/// <summary>
	/// Сэмплирует параболу p = p0 + v0*t + 0.5*g*t² (g вниз). Останавливается по времени или Linecast.
	/// </summary>
	public static int SampleTrajectory(
		Vector3 _origin,
		Vector3 _initialVelocity,
		float _gravity,
		float _maxTimeSeconds,
		float _stepSeconds,
		Vector3[] _buffer,
		out bool _hitGeometry,
		out Vector3 _impactPoint,
		int _geometryMask = ~0,
		Collider[] _ignoreColliders = null)
	{
		_hitGeometry = false;
		_impactPoint = _origin;

		if (_buffer == null || _buffer.Length == 0)
			return 0;

		float step = Mathf.Max(0.01f, _stepSeconds);
		float maxTime = Mathf.Max(step, _maxTimeSeconds);
		float g = Mathf.Abs(_gravity);
		Vector3 gravityAccel = Vector3.down * g;

		_buffer[0] = _origin;
		int count = 1;
		Vector3 prev = _origin;

		for (float t = step; t <= maxTime + 0.0001f && count < _buffer.Length; t += step)
		{
			Vector3 next = _origin + _initialVelocity * t + 0.5f * gravityAccel * (t * t);

			if (TryLinecastIgnore(prev, next, _geometryMask, _ignoreColliders, out RaycastHit hit))
			{
				_buffer[count++] = hit.point;
				_hitGeometry = true;
				_impactPoint = hit.point;
				return count;
			}

			_buffer[count++] = next;
			prev = next;
		}

		_impactPoint = _buffer[count - 1];
		return count;
	}

	private static bool TryLinecastIgnore(
		Vector3 _from,
		Vector3 _to,
		int _geometryMask,
		Collider[] _ignoreColliders,
		out RaycastHit _hit)
	{
		Vector3 delta = _to - _from;
		float dist = delta.magnitude;
		if (dist < 0.0001f)
		{
			_hit = default;
			return false;
		}

		int hitCount = Physics.RaycastNonAlloc(
			_from,
			delta / dist,
			s_RaycastHits,
			dist,
			_geometryMask,
			QueryTriggerInteraction.Ignore);

		if (hitCount <= 0)
		{
			_hit = default;
			return false;
		}

		int bestIndex = -1;
		float bestDistance = float.MaxValue;
		for (int i = 0; i < hitCount; i++)
		{
			Collider col = s_RaycastHits[i].collider;
			if (col == null)
				continue;

			if (_ignoreColliders != null && ContainsCollider(_ignoreColliders, col))
				continue;

			if (s_RaycastHits[i].distance >= bestDistance)
				continue;

			bestDistance = s_RaycastHits[i].distance;
			bestIndex = i;
		}

		if (bestIndex < 0)
		{
			_hit = default;
			return false;
		}

		_hit = s_RaycastHits[bestIndex];
		return true;
	}

	private static bool ContainsCollider(Collider[] _colliders, Collider _candidate)
	{
		for (int i = 0; i < _colliders.Length; i++)
		{
			if (_colliders[i] == _candidate)
				return true;
		}

		return false;
	}

	public static Vector3 EvaluatePoint(Vector3 _origin, Vector3 _initialVelocity, float _gravity, float _timeSeconds)
	{
		float t = Mathf.Max(0f, _timeSeconds);
		float g = Mathf.Abs(_gravity);
		return _origin + _initialVelocity * t + 0.5f * Vector3.down * g * (t * t);
	}
	#endregion
}
