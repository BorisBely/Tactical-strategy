using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Двери на hinge-пустышках. Дверь остаётся открытой, пока юнит у exit-точки.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleDoorController : MonoBehaviour
{
	#region Nested
	[Serializable]
	public struct DoorBinding
	{
		public VehicleDoorId DoorId;
		public Transform Hinge;
		public Transform ApproachPoint;
		public Transform ExitPoint;
		public float OpenAngle;
		public bool InvertOpen;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private DoorBinding[] m_Doors = Array.Empty<DoorBinding>();
	[SerializeField, Min(0.05f)] private float m_OpenSeconds = 0.35f;
	[SerializeField, Min(0.5f)] private float m_ClearExitDistance = 1.6f;
	[SerializeField] private AudioClip m_OpenClip;
	[SerializeField] private AudioClip m_CloseClip;
	[SerializeField, Range(0f, 1f)] private float m_DoorVolume = 0.9f;
	#endregion

	#region Private Fields
	private readonly HashSet<VehicleDoorId> m_HoldOpen = new HashSet<VehicleDoorId>();
	private readonly Dictionary<VehicleDoorId, float> m_OpenT = new Dictionary<VehicleDoorId, float>(4);
	private readonly Dictionary<VehicleDoorId, RtsUnitMember> m_ExitWatch =
		new Dictionary<VehicleDoorId, RtsUnitMember>(4);
	private Coroutine m_WatchCoroutine;
	private bool m_WatchRunning;
	#endregion

	#region Public Methods
	public void SetDoors(DoorBinding[] _doors)
	{
		m_Doors = _doors ?? Array.Empty<DoorBinding>();
		m_OpenT.Clear();
		for (int i = 0; i < m_Doors.Length; i++)
			m_OpenT[m_Doors[i].DoorId] = 0f;
	}

	public bool TryGetDoor(VehicleDoorId _doorId, out DoorBinding _binding)
	{
		for (int i = 0; i < m_Doors.Length; i++)
		{
			if (m_Doors[i].DoorId != _doorId)
				continue;
			_binding = m_Doors[i];
			return _binding.Hinge != null;
		}

		_binding = default;
		return false;
	}

	public IEnumerator OpenDoorRoutine(VehicleDoorId _doorId)
	{
		m_HoldOpen.Add(_doorId);
		if (m_OpenT.TryGetValue(_doorId, out float open01) && open01 >= 0.98f)
			yield break;
		yield return AnimateDoor(_doorId, 1f);
	}

	public IEnumerator CloseDoorIfFreeRoutine(VehicleDoorId _doorId)
	{
		if (m_HoldOpen.Contains(_doorId) || m_ExitWatch.ContainsKey(_doorId))
			yield break;
		yield return AnimateDoor(_doorId, 0f);
	}

	/// <summary>Keep door open across consecutive queue jobs without re-animating.</summary>
	public void KeepHold(VehicleDoorId _doorId)
	{
		m_HoldOpen.Add(_doorId);
	}

	public void ReleaseHold(VehicleDoorId _doorId)
	{
		m_HoldOpen.Remove(_doorId);
	}

	public bool IsHeldOpen(VehicleDoorId _doorId) => m_HoldOpen.Contains(_doorId);

	/// <summary>Force-close every door (drive start / board cancel). Clears holds and exit watches.</summary>
	public IEnumerator CloseAllDoorsForcedRoutine()
	{
		m_HoldOpen.Clear();
		m_ExitWatch.Clear();
		for (int i = 0; i < m_Doors.Length; i++)
			yield return AnimateDoor(m_Doors[i].DoorId, 0f);
	}

	public void CloseAllDoorsForcedImmediate()
	{
		m_HoldOpen.Clear();
		m_ExitWatch.Clear();
		for (int i = 0; i < m_Doors.Length; i++)
		{
			VehicleDoorId id = m_Doors[i].DoorId;
			m_OpenT[id] = 0f;
			if (m_Doors[i].Hinge != null)
				ApplyHinge(m_Doors[i], 0f);
		}
	}

	public void WatchExit(VehicleDoorId _doorId, RtsUnitMember _unit)
	{
		if (_unit == null)
			return;

		m_HoldOpen.Add(_doorId);
		m_ExitWatch[_doorId] = _unit;
		EnsureWatchRunning();
	}

	public static VehicleDoorId DoorForSeat(VehicleSeatId _seatId, VehicleBoardSide _sideFilter)
	{
		GetRowDoors(_seatId, out VehicleDoorId leftDoor, out VehicleDoorId rightDoor);
		if (_sideFilter == VehicleBoardSide.Right)
			return rightDoor;
		return leftDoor;
	}

	/// <summary>Передний ряд (водитель/командир/стрелок) или задний (салон/носилки).</summary>
	public static bool IsFrontRowSeat(VehicleSeatId _seatId)
	{
		return _seatId == VehicleSeatId.Driver ||
		       _seatId == VehicleSeatId.Commander ||
		       _seatId == VehicleSeatId.Gunner;
	}

	public static void GetRowDoors(
		VehicleSeatId _seatId,
		out VehicleDoorId _leftDoor,
		out VehicleDoorId _rightDoor)
	{
		if (IsFrontRowSeat(_seatId))
		{
			_leftDoor = VehicleDoorId.FrontLeft;
			_rightDoor = VehicleDoorId.FrontRight;
			return;
		}

		_leftDoor = VehicleDoorId.RearLeft;
		_rightDoor = VehicleDoorId.RearRight;
	}

	public static bool IsDoorOnSide(VehicleDoorId _doorId, VehicleBoardSide _side)
	{
		if (_side == VehicleBoardSide.Any)
			return true;
		bool left = _doorId == VehicleDoorId.FrontLeft || _doorId == VehicleDoorId.RearLeft;
		return _side == VehicleBoardSide.Left ? left : !left;
	}
	#endregion

	#region Private Methods
	private IEnumerator AnimateDoor(VehicleDoorId _doorId, float _target)
	{
		if (!TryGetDoor(_doorId, out DoorBinding binding) || binding.Hinge == null)
			yield break;

		if (!m_OpenT.TryGetValue(_doorId, out float current))
			current = 0f;

		float start = current;
		if (Mathf.Abs(start - _target) > 0.05f)
			PlayDoorSound(_target > start ? m_OpenClip : m_CloseClip, binding.Hinge);

		float elapsed = 0f;
		while (elapsed < m_OpenSeconds)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / m_OpenSeconds);
			current = Mathf.Lerp(start, _target, t);
			m_OpenT[_doorId] = current;
			ApplyHinge(binding, current);
			yield return null;
		}

		m_OpenT[_doorId] = _target;
		ApplyHinge(binding, _target);
	}

	private void PlayDoorSound(AudioClip _clip, Transform _hinge)
	{
		if (_clip == null || _hinge == null)
			return;

		AudioSource.PlayClipAtPoint(_clip, _hinge.position, m_DoorVolume);
	}

	private static void ApplyHinge(DoorBinding _binding, float _open01)
	{
		float angle = _binding.OpenAngle * _open01;
		if (_binding.InvertOpen)
			angle = -angle;
		_binding.Hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
	}

	private void EnsureWatchRunning()
	{
		if (m_WatchRunning)
			return;
		m_WatchCoroutine = StartCoroutine(WatchExitsRoutine());
	}

	private IEnumerator WatchExitsRoutine()
	{
		m_WatchRunning = true;
		var toClose = new List<VehicleDoorId>(2);
		while (m_ExitWatch.Count > 0)
		{
			toClose.Clear();
			foreach (KeyValuePair<VehicleDoorId, RtsUnitMember> pair in m_ExitWatch)
			{
				if (!TryGetDoor(pair.Key, out DoorBinding door) || door.ExitPoint == null || pair.Value == null)
				{
					toClose.Add(pair.Key);
					continue;
				}

				float sqr = (pair.Value.transform.position - door.ExitPoint.position).sqrMagnitude;
				if (sqr >= m_ClearExitDistance * m_ClearExitDistance)
					toClose.Add(pair.Key);
			}

			for (int i = 0; i < toClose.Count; i++)
			{
				VehicleDoorId doorId = toClose[i];
				m_ExitWatch.Remove(doorId);
				m_HoldOpen.Remove(doorId);
				yield return CloseDoorIfFreeRoutine(doorId);
			}

			yield return null;
		}

		m_WatchRunning = false;
		m_WatchCoroutine = null;
	}
	#endregion
}
