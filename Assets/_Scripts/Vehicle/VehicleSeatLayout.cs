using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Слоты: 2 litter только для раненых; живые — Driver→Commander→RL→RR→RC→Gunner.
/// Раненые: Litter→RL→RR→RC→Commander (не Driver/Gunner).
/// Двери посадки: Driver→FL, Commander→FR; остальные — задняя (Side Left→BL, Right→BR, Any→preferred/ближайшая).
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleSeatLayout : MonoBehaviour
{
	#region Nested
	[Serializable]
	public struct SeatBinding
	{
		public VehicleSeatId SeatId;
		public Transform Anchor;
		public VehicleDoorId PreferredDoor;
		public bool IsLitter;
	}
	#endregion

	#region Static Orders
	private static readonly VehicleSeatId[] s_LivingBoardOrder =
	{
		VehicleSeatId.Driver,
		VehicleSeatId.Commander,
		VehicleSeatId.RearLeft,
		VehicleSeatId.RearRight,
		VehicleSeatId.RearCenter,
		VehicleSeatId.Gunner
	};

	/// <summary>Обратный порядок посадки для назначения на турель (без водителя).</summary>
	private static readonly VehicleSeatId[] s_GunnerPromoteOrder =
	{
		VehicleSeatId.RearCenter,
		VehicleSeatId.RearRight,
		VehicleSeatId.RearLeft,
		VehicleSeatId.Commander
	};

	private static readonly VehicleSeatId[] s_UnconsciousOrder =
	{
		VehicleSeatId.Litter1,
		VehicleSeatId.Litter2,
		VehicleSeatId.RearLeft,
		VehicleSeatId.RearRight,
		VehicleSeatId.RearCenter,
		VehicleSeatId.Commander
	};

	/// <summary>Куда слезать с турели (обратный порядок живых мест без Gunner).</summary>
	private static readonly VehicleSeatId[] s_GunnerDemoteOrder =
	{
		VehicleSeatId.RearCenter,
		VehicleSeatId.RearRight,
		VehicleSeatId.RearLeft,
		VehicleSeatId.Commander,
		VehicleSeatId.Driver
	};
	#endregion

	#region Serialized Fields
	[SerializeField] private SeatBinding[] m_Seats = Array.Empty<SeatBinding>();
	#endregion

	#region Private Fields
	private readonly Dictionary<VehicleSeatId, RtsUnitMember> m_Occupants =
		new Dictionary<VehicleSeatId, RtsUnitMember>(8);
	private readonly Dictionary<RtsUnitMember, VehicleSeatId> m_UnitToSeat =
		new Dictionary<RtsUnitMember, VehicleSeatId>(8);
	#endregion

	#region Events
	public event Action OccupancyChanged;
	#endregion

	#region Public Properties
	public int OccupantCount => m_Occupants.Count;
	public bool HasDriver => IsOccupied(VehicleSeatId.Driver);
	public bool HasGunner => IsOccupied(VehicleSeatId.Gunner);
	#endregion

	#region Public Methods
	public void SetSeats(SeatBinding[] _seats)
	{
		m_Seats = _seats ?? Array.Empty<SeatBinding>();
	}

	public bool TryGetSeat(VehicleSeatId _seatId, out SeatBinding _binding)
	{
		for (int i = 0; i < m_Seats.Length; i++)
		{
			if (m_Seats[i].SeatId != _seatId)
				continue;
			_binding = m_Seats[i];
			return _binding.Anchor != null;
		}

		_binding = default;
		return false;
	}

	/// <summary>
	/// Дверь посадки/высадки для слота.
	/// FL — только водитель; FR — только командир.
	/// Side Left/Right — все прочие места через заднюю дверь этой стороны.
	/// Side Any — задние места через PreferredDoor; RC/Gunner — ближайшая задняя.
	/// </summary>
	public VehicleDoorId ResolveBoardDoor(
		VehicleSeatId _seatId,
		VehicleBoardSide _side,
		System.Func<VehicleDoorId, float> _approachDistance)
	{
		if (_seatId == VehicleSeatId.Driver)
			return VehicleDoorId.FrontLeft;

		if (_seatId == VehicleSeatId.Commander)
			return VehicleDoorId.FrontRight;

		if (_side == VehicleBoardSide.Left)
			return VehicleDoorId.RearLeft;

		if (_side == VehicleBoardSide.Right)
			return VehicleDoorId.RearRight;

		if (_seatId == VehicleSeatId.RearCenter && _approachDistance != null)
		{
			float leftDist = _approachDistance(VehicleDoorId.RearLeft);
			float rightDist = _approachDistance(VehicleDoorId.RearRight);
			return leftDist <= rightDist ? VehicleDoorId.RearLeft : VehicleDoorId.RearRight;
		}

		if (_seatId == VehicleSeatId.Gunner && _approachDistance != null)
		{
			float leftDist = _approachDistance(VehicleDoorId.RearLeft);
			float rightDist = _approachDistance(VehicleDoorId.RearRight);
			return leftDist <= rightDist ? VehicleDoorId.RearLeft : VehicleDoorId.RearRight;
		}

		if (TryGetSeat(_seatId, out SeatBinding seat))
			return seat.PreferredDoor;

		VehicleDoorController.GetRowDoors(_seatId, out VehicleDoorId leftDoor, out _);
		return leftDoor;
	}

	public VehicleDoorId ResolveDisembarkDoor(System.Func<VehicleDoorId, float> _approachDistance, VehicleSeatId _seatId)
	{
		return ResolveBoardDoor(_seatId, VehicleBoardSide.Any, _approachDistance);
	}

	/// <summary>Fallback без дистанции (нет layout или нет approach callback).</summary>
	public static VehicleDoorId ResolveBoardDoorStatic(VehicleSeatId _seatId, VehicleBoardSide _side)
	{
		if (_seatId == VehicleSeatId.Driver)
			return VehicleDoorId.FrontLeft;

		if (_seatId == VehicleSeatId.Commander)
			return VehicleDoorId.FrontRight;

		if (_side == VehicleBoardSide.Left)
			return VehicleDoorId.RearLeft;

		if (_side == VehicleBoardSide.Right)
			return VehicleDoorId.RearRight;

		switch (_seatId)
		{
			case VehicleSeatId.RearRight:
			case VehicleSeatId.Litter2:
				return VehicleDoorId.RearRight;
			default:
				return VehicleDoorId.RearLeft;
		}
	}

	public bool IsOccupied(VehicleSeatId _seatId) => m_Occupants.ContainsKey(_seatId);

	public bool TryGetOccupant(VehicleSeatId _seatId, out RtsUnitMember _unit) =>
		m_Occupants.TryGetValue(_seatId, out _unit);

	public bool TryGetSeatOf(RtsUnitMember _unit, out VehicleSeatId _seatId) =>
		m_UnitToSeat.TryGetValue(_unit, out _seatId);

	public void CollectOccupants(List<RtsUnitMember> _buffer)
	{
		if (_buffer == null)
			return;
		_buffer.Clear();
		foreach (KeyValuePair<VehicleSeatId, RtsUnitMember> pair in m_Occupants)
		{
			if (pair.Value != null)
				_buffer.Add(pair.Value);
		}
	}

	public void CollectOccupantsOrdered(List<(VehicleSeatId Seat, RtsUnitMember Unit)> _buffer)
	{
		if (_buffer == null)
			return;
		_buffer.Clear();
		AppendIfPresent(_buffer, VehicleSeatId.Driver);
		AppendIfPresent(_buffer, VehicleSeatId.Gunner);
		AppendIfPresent(_buffer, VehicleSeatId.Commander);
		AppendIfPresent(_buffer, VehicleSeatId.RearLeft);
		AppendIfPresent(_buffer, VehicleSeatId.RearRight);
		AppendIfPresent(_buffer, VehicleSeatId.RearCenter);
		AppendIfPresent(_buffer, VehicleSeatId.Litter1);
		AppendIfPresent(_buffer, VehicleSeatId.Litter2);
	}

	public bool TryAssignSeatForBoarder(
		RtsUnitMember _unit,
		bool _isUnconscious,
		out VehicleSeatId _seatId,
		List<(RtsUnitMember Unit, VehicleSeatId From, VehicleSeatId To)> _displaces = null)
	{
		_seatId = VehicleSeatId.Driver;
		if (_unit == null || m_UnitToSeat.ContainsKey(_unit))
			return false;

		return _isUnconscious
			? TryAssignUnconscious(out _seatId)
			: TryAssignLiving(out _seatId, _displaces);
	}

	public bool TryAssignLitter(out VehicleSeatId _seatId)
	{
		if (!IsOccupied(VehicleSeatId.Litter1))
		{
			_seatId = VehicleSeatId.Litter1;
			return true;
		}

		if (!IsOccupied(VehicleSeatId.Litter2))
		{
			_seatId = VehicleSeatId.Litter2;
			return true;
		}

		_seatId = VehicleSeatId.Litter1;
		return false;
	}

	public bool TryPeekSeatForBoarder(
		bool _isUnconscious,
		out VehicleSeatId _seatId,
		IReadOnlyCollection<VehicleSeatId> _alsoReserved = null)
	{
		if (_isUnconscious)
			return TryPeekUnconsciousSeat(out _seatId, _alsoReserved);

		for (int i = 0; i < s_LivingBoardOrder.Length; i++)
		{
			VehicleSeatId seat = s_LivingBoardOrder[i];
			if (IsSeatBlockedForPeek(seat, _alsoReserved))
				continue;

			if (!IsOccupied(seat))
			{
				_seatId = seat;
				return true;
			}

			if (!m_Occupants.TryGetValue(seat, out RtsUnitMember occupant) || occupant == null)
				continue;
			if (!IsUnitUnconscious(occupant))
				continue;
			if (!HasAnyFreeLitter())
				continue;

			_seatId = seat;
			return true;
		}

		_seatId = VehicleSeatId.Driver;
		return false;
	}

	public bool TryAssignPreferredSeatForBoarder(
		RtsUnitMember _unit,
		VehicleSeatId _preferredSeat,
		bool _isUnconscious,
		out VehicleSeatId _seatId,
		List<(RtsUnitMember Unit, VehicleSeatId From, VehicleSeatId To)> _displaces = null)
	{
		_seatId = _preferredSeat;
		if (_unit == null || m_UnitToSeat.ContainsKey(_unit))
			return false;

		if (_isUnconscious)
			return TryAssignUnconscious(out _seatId);

		if (!IsOccupied(_preferredSeat))
			return true;

		if (!m_Occupants.TryGetValue(_preferredSeat, out RtsUnitMember occupant) || occupant == null)
			return false;
		if (!IsUnitUnconscious(occupant))
			return false;
		if (!TryAssignLitter(out VehicleSeatId litter))
			return false;

		m_Occupants.Remove(_preferredSeat);
		m_Occupants[litter] = occupant;
		m_UnitToSeat[occupant] = litter;
		_displaces?.Add((occupant, _preferredSeat, litter));
		OccupancyChanged?.Invoke();
		return true;
	}

	public bool HasAnyFreeSeatForLiving() => TryPeekSeatForBoarder(false, out _);

	public bool HasFreeGunnerSeat => !IsOccupied(VehicleSeatId.Gunner);

	public bool HasAnyFreeSeatForWounded() => TryAssignUnconscious(out _);

	public bool HasAnyFreeLitter() => TryAssignLitter(out _);

	public bool TryFindGunnerPromoteCandidate(out RtsUnitMember _unit)
	{
		_unit = null;
		if (IsOccupied(VehicleSeatId.Gunner))
			return false;

		for (int i = 0; i < s_GunnerPromoteOrder.Length; i++)
		{
			if (!m_Occupants.TryGetValue(s_GunnerPromoteOrder[i], out RtsUnitMember occupant) ||
			    occupant == null)
				continue;
			if (IsUnitUnconscious(occupant))
				continue;
			_unit = occupant;
			return true;
		}

		return false;
	}

	public bool TryFindGunnerDemoteSeat(out VehicleSeatId _seatId)
	{
		for (int i = 0; i < s_GunnerDemoteOrder.Length; i++)
		{
			VehicleSeatId seat = s_GunnerDemoteOrder[i];
			if (IsOccupied(seat))
				continue;
			_seatId = seat;
			return true;
		}

		_seatId = VehicleSeatId.RearCenter;
		return false;
	}

	public bool CanPromoteToGunner() => TryFindGunnerPromoteCandidate(out _);

	public bool CanDemoteGunner() =>
		HasGunner && TryFindGunnerDemoteSeat(out _);

	public void Occupy(VehicleSeatId _seatId, RtsUnitMember _unit)
	{
		if (_unit == null)
			return;

		if (m_UnitToSeat.TryGetValue(_unit, out VehicleSeatId oldSeat))
			m_Occupants.Remove(oldSeat);

		if (m_Occupants.TryGetValue(_seatId, out RtsUnitMember previous) && previous != null && previous != _unit)
			m_UnitToSeat.Remove(previous);

		m_Occupants[_seatId] = _unit;
		m_UnitToSeat[_unit] = _seatId;
		OccupancyChanged?.Invoke();
	}

	public void Vacate(RtsUnitMember _unit)
	{
		if (_unit == null || !m_UnitToSeat.TryGetValue(_unit, out VehicleSeatId seatId))
			return;

		m_UnitToSeat.Remove(_unit);
		if (m_Occupants.TryGetValue(seatId, out RtsUnitMember current) && current == _unit)
			m_Occupants.Remove(seatId);
		OccupancyChanged?.Invoke();
	}

	public void VacateSeat(VehicleSeatId _seatId)
	{
		if (!m_Occupants.TryGetValue(_seatId, out RtsUnitMember unit))
			return;
		Vacate(unit);
	}

	public static bool IsLitterSeat(VehicleSeatId _seatId) =>
		_seatId == VehicleSeatId.Litter1 || _seatId == VehicleSeatId.Litter2;

	public static bool UsesDrivingPose(VehicleSeatId _seatId) =>
		_seatId != VehicleSeatId.Gunner && !IsLitterSeat(_seatId);
	#endregion

	#region Private Methods
	private void AppendIfPresent(List<(VehicleSeatId, RtsUnitMember)> _buffer, VehicleSeatId _seatId)
	{
		if (m_Occupants.TryGetValue(_seatId, out RtsUnitMember unit) && unit != null)
			_buffer.Add((_seatId, unit));
	}

	private bool TryAssignUnconscious(out VehicleSeatId _seatId)
	{
		return TryPeekUnconsciousSeat(out _seatId);
	}

	private bool TryPeekUnconsciousSeat(
		out VehicleSeatId _seatId,
		IReadOnlyCollection<VehicleSeatId> _alsoReserved = null)
	{
		for (int i = 0; i < s_UnconsciousOrder.Length; i++)
		{
			VehicleSeatId seat = s_UnconsciousOrder[i];
			if (IsSeatBlockedForPeek(seat, _alsoReserved))
				continue;
			if (IsOccupied(seat))
				continue;
			_seatId = seat;
			return true;
		}

		_seatId = VehicleSeatId.Litter1;
		return false;
	}

	private bool IsSeatBlockedForPeek(
		VehicleSeatId _seatId,
		IReadOnlyCollection<VehicleSeatId> _alsoReserved)
	{
		if (_alsoReserved == null)
			return false;

		foreach (VehicleSeatId reserved in _alsoReserved)
		{
			if (reserved == _seatId)
				return true;
		}

		return false;
	}

	private bool TryAssignLiving(
		out VehicleSeatId _seatId,
		List<(RtsUnitMember Unit, VehicleSeatId From, VehicleSeatId To)> _displaces)
	{
		for (int i = 0; i < s_LivingBoardOrder.Length; i++)
		{
			VehicleSeatId seat = s_LivingBoardOrder[i];
			if (!IsOccupied(seat))
			{
				_seatId = seat;
				return true;
			}

			if (!m_Occupants.TryGetValue(seat, out RtsUnitMember occupant) || occupant == null)
				continue;
			if (!IsUnitUnconscious(occupant))
				continue;
			if (!TryAssignLitter(out VehicleSeatId litter))
				continue;

			m_Occupants.Remove(seat);
			m_Occupants[litter] = occupant;
			m_UnitToSeat[occupant] = litter;
			_displaces?.Add((occupant, seat, litter));
			OccupancyChanged?.Invoke();

			_seatId = seat;
			return true;
		}

		_seatId = VehicleSeatId.Driver;
		return false;
	}

	private static bool IsUnitUnconscious(RtsUnitMember _unit)
	{
		if (_unit == null)
			return false;
		UnitConsciousness consciousness = _unit.GetComponentInChildren<UnitConsciousness>(true);
		return consciousness != null && !consciousness.IsConscious;
	}
	#endregion
}
