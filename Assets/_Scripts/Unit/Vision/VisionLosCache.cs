using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short-TTL cache of last LOS/exposure for one observer. Never a permanent see-through-walls result.
/// </summary>
public sealed class VisionLosCache
{
	private readonly Dictionary<Transform, Entry> m_Entries = new Dictionary<Transform, Entry>(32);

	public struct Entry
	{
		public bool HasLos;
		public Vector3 AimPoint;
		public float Exposure01;
		public float Time;
		public Vector3 ObserverOrigin;
		public Vector3 ObserverForwardXZ;
		public Vector3 TargetPosition;
	}

	public bool TryGetValid(
		Transform _target,
		float _now,
		Vector3 _origin,
		Vector3 _forwardXZ,
		Vector3 _targetPosition,
		float _ttlSeconds,
		float _moveEpsilonMeters,
		float _forwardAngleEpsilonDegrees,
		out Entry _entry)
	{
		_entry = default;
		if (_target == null || !m_Entries.TryGetValue(_target, out Entry stored))
			return false;

		if (!VisionLodMath.CacheIsValid(
			    _now,
			    stored.Time,
			    _ttlSeconds,
			    stored.ObserverOrigin,
			    _origin,
			    stored.TargetPosition,
			    _targetPosition,
			    stored.ObserverForwardXZ,
			    _forwardXZ,
			    _moveEpsilonMeters,
			    _forwardAngleEpsilonDegrees))
		{
			m_Entries.Remove(_target);
			return false;
		}

		_entry = stored;
		return true;
	}

	public void Store(
		Transform _target,
		bool _hasLos,
		Vector3 _aimPoint,
		float _exposure01,
		float _now,
		Vector3 _origin,
		Vector3 _forwardXZ,
		Vector3 _targetPosition)
	{
		if (_target == null)
			return;

		m_Entries[_target] = new Entry
		{
			HasLos = _hasLos,
			AimPoint = _aimPoint,
			Exposure01 = _exposure01,
			Time = _now,
			ObserverOrigin = _origin,
			ObserverForwardXZ = _forwardXZ,
			TargetPosition = _targetPosition
		};
	}

	public void Remove(Transform _target)
	{
		if (_target == null)
			return;
		m_Entries.Remove(_target);
	}

	public void Clear()
	{
		m_Entries.Clear();
	}
}
