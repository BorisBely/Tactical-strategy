using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Назначения юнитов на места машин в Mission Prep (только UI/состояние, без физического board).
/// </summary>
public sealed class MissionPrepVehicleAssignmentStore
{
	#region Events
	public event Action Changed;
	#endregion

	#region Private Fields
	private readonly Dictionary<EntityId, Dictionary<VehicleSeatId, GameObject>> m_ByVehicle =
		new Dictionary<EntityId, Dictionary<VehicleSeatId, GameObject>>(8);
	#endregion

	#region Public Methods
	public void ClearAll()
	{
		m_ByVehicle.Clear();
		Changed?.Invoke();
	}

	public void Assign(VehicleController _vehicle, VehicleSeatId _seatId, GameObject _unitRoot)
	{
		if (_vehicle == null || _unitRoot == null)
			return;

		ClearUnitFromAll(_unitRoot, _raise: false);

		EntityId key = _vehicle.GetEntityId();
		if (!m_ByVehicle.TryGetValue(key, out Dictionary<VehicleSeatId, GameObject> seats))
		{
			seats = new Dictionary<VehicleSeatId, GameObject>(8);
			m_ByVehicle[key] = seats;
		}

		seats[_seatId] = _unitRoot;
		Changed?.Invoke();
	}

	public void ClearSeat(VehicleController _vehicle, VehicleSeatId _seatId)
	{
		if (_vehicle == null)
			return;

		EntityId key = _vehicle.GetEntityId();
		if (!m_ByVehicle.TryGetValue(key, out Dictionary<VehicleSeatId, GameObject> seats))
			return;

		if (!seats.Remove(_seatId))
			return;

		if (seats.Count == 0)
			m_ByVehicle.Remove(key);

		Changed?.Invoke();
	}

	public void ClearUnitFromAll(GameObject _unitRoot, bool _raise = true)
	{
		if (_unitRoot == null)
			return;

		bool changed = false;
		var emptyVehicles = new List<EntityId>(4);
		foreach (KeyValuePair<EntityId, Dictionary<VehicleSeatId, GameObject>> pair in m_ByVehicle)
		{
			List<VehicleSeatId> toRemove = null;
			foreach (KeyValuePair<VehicleSeatId, GameObject> seat in pair.Value)
			{
				if (seat.Value != _unitRoot)
					continue;
				toRemove ??= new List<VehicleSeatId>(2);
				toRemove.Add(seat.Key);
			}

			if (toRemove == null)
				continue;

			for (int i = 0; i < toRemove.Count; i++)
				pair.Value.Remove(toRemove[i]);

			changed = true;
			if (pair.Value.Count == 0)
				emptyVehicles.Add(pair.Key);
		}

		for (int i = 0; i < emptyVehicles.Count; i++)
			m_ByVehicle.Remove(emptyVehicles[i]);

		if (changed && _raise)
			Changed?.Invoke();
	}

	public bool TryGetAssignedUnit(
		VehicleController _vehicle,
		VehicleSeatId _seatId,
		out GameObject _unitRoot)
	{
		_unitRoot = null;
		if (_vehicle == null)
			return false;

		if (!m_ByVehicle.TryGetValue(_vehicle.GetEntityId(), out Dictionary<VehicleSeatId, GameObject> seats))
			return false;

		return seats.TryGetValue(_seatId, out _unitRoot) && _unitRoot != null;
	}

	public bool TryGetUnitAssignment(
		GameObject _unitRoot,
		out VehicleController _vehicle,
		out VehicleSeatId _seatId)
	{
		_vehicle = null;
		_seatId = default;
		if (_unitRoot == null)
			return false;

		foreach (KeyValuePair<EntityId, Dictionary<VehicleSeatId, GameObject>> pair in m_ByVehicle)
		{
			foreach (KeyValuePair<VehicleSeatId, GameObject> seat in pair.Value)
			{
				if (seat.Value != _unitRoot)
					continue;

				_seatId = seat.Key;
				_vehicle = FindVehicleByEntityId(pair.Key);
				return _vehicle != null;
			}
		}

		return false;
	}
	#endregion

	#region Private Methods
	private static VehicleController FindVehicleByEntityId(EntityId _entityId)
	{
		IReadOnlyList<VehicleController> instances = VehicleController.Instances;
		for (int i = 0; i < instances.Count; i++)
		{
			VehicleController vehicle = instances[i];
			if (vehicle != null && vehicle.GetEntityId() == _entityId)
				return vehicle;
		}

		return null;
	}
	#endregion
}
