using UnityEngine;

/// <summary>
/// Cheap 3D segment vs AABB for EditMode slabs and debug. Not a score.
/// </summary>
public static class CoverOcclusionMath
{
	#region Public Methods
	public static bool SegmentHitsAabb(Vector3 _from, Vector3 _to, Bounds _bounds)
	{
		Vector3 dir = _to - _from;
		float length = dir.magnitude;
		if (length < 0.0001f)
			return _bounds.Contains(_from);

		dir /= length;
		Vector3 min = _bounds.min;
		Vector3 max = _bounds.max;
		float tMin = 0f;
		float tMax = length;
		if (!ClipAxis(_from.x, dir.x, min.x, max.x, ref tMin, ref tMax))
			return false;
		if (!ClipAxis(_from.y, dir.y, min.y, max.y, ref tMin, ref tMax))
			return false;
		if (!ClipAxis(_from.z, dir.z, min.z, max.z, ref tMin, ref tMax))
			return false;
		return tMax >= 0f && tMin <= length;
	}

	public static Vector3 PlanarNormal(Vector3 _normal)
	{
		Vector3 n = _normal;
		n.y = 0f;
		if (n.sqrMagnitude < 0.0001f)
			return Vector3.forward;
		return n.normalized;
	}
	#endregion

	#region Private Methods
	private static bool ClipAxis(
		float _origin,
		float _dir,
		float _min,
		float _max,
		ref float _tMin,
		ref float _tMax)
	{
		if (Mathf.Abs(_dir) < 1e-8f)
			return _origin >= _min && _origin <= _max;

		float inv = 1f / _dir;
		float t1 = (_min - _origin) * inv;
		float t2 = (_max - _origin) * inv;
		if (t1 > t2)
		{
			float swap = t1;
			t1 = t2;
			t2 = swap;
		}

		_tMin = Mathf.Max(_tMin, t1);
		_tMax = Mathf.Min(_tMax, t2);
		return _tMin <= _tMax;
	}
	#endregion
}
