using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #14C geometry: spawn-group centers and world bearing. No scene objects of its own.
/// </summary>
public static class ThreatDirectionEstimator
{
	#region Public Methods
	public static bool TryAverage(IReadOnlyList<Vector3> _points, out Vector3 _center)
	{
		_center = Vector3.zero;
		if (_points == null || _points.Count == 0)
			return false;

		Vector3 sum = Vector3.zero;
		int count = 0;
		for (int i = 0; i < _points.Count; i++)
		{
			sum += _points[i];
			count++;
		}

		if (count <= 0)
			return false;

		_center = sum / count;
		return true;
	}

	public static bool TryDirection(Vector3 _from, Vector3 _to, out Vector3 _direction)
	{
		Vector3 delta = _to - _from;
		delta.y = 0f;
		if (delta.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
		{
			_direction = Vector3.zero;
			return false;
		}

		_direction = delta.normalized;
		return true;
	}

	public static bool TryExpectedDirection(
		Vector3 _ownSpawnCenter,
		Vector3 _enemySpawnCenter,
		out Vector3 _direction)
	{
		return TryDirection(_ownSpawnCenter, _enemySpawnCenter, out _direction);
	}

	public static ThreatDirectionCompass CompassFrom(Vector3 _direction)
	{
		Vector3 flat = _direction;
		flat.y = 0f;
		if (flat.sqrMagnitude < ThreatDirectionMath.DirectionEpsilonSqr)
			return ThreatDirectionCompass.North;

		flat.Normalize();
		float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
		if (yaw < 0f)
			yaw += 360f;

		int octant = Mathf.RoundToInt(yaw / 45f) & 7;
		return (ThreatDirectionCompass)octant;
	}

	public static string CompassLabel(ThreatDirectionCompass _compass)
	{
		switch (_compass)
		{
			case ThreatDirectionCompass.NorthEast:
				return "NE";
			case ThreatDirectionCompass.East:
				return "E";
			case ThreatDirectionCompass.SouthEast:
				return "SE";
			case ThreatDirectionCompass.South:
				return "S";
			case ThreatDirectionCompass.SouthWest:
				return "SW";
			case ThreatDirectionCompass.West:
				return "W";
			case ThreatDirectionCompass.NorthWest:
				return "NW";
			default:
				return "N";
		}
	}

	public static Vector3 Opposite(Vector3 _direction)
	{
		return -_direction;
	}
	#endregion
}
