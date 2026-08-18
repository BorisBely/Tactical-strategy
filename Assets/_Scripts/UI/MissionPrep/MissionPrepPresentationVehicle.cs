using UnityEngine;

/// <summary>
/// Маркер машины, заспавненной через Mission Prep.
/// RTS-выбор/приказы по ней блокируются только пока открыт экран prep.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepPresentationVehicle : MonoBehaviour
{
	public static bool IsPresentation(VehicleController _vehicle)
	{
		if (_vehicle == null || _vehicle.GetComponent<MissionPrepPresentationVehicle>() == null)
			return false;

		return MissionPrepSquadSpawner.IsMissionPrepInteractionLocked();
	}
}
