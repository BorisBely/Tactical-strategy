using UnityEngine;

/// <summary>
/// Legacy component. Drive↔unit shove is prevented by Vehicle↔Unit layer ignore and
/// collider.excludeLayers; <see cref="VehicleController"/> destroys this on setup.
/// Kept so old prefab references do not become missing scripts.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleDriveUnitPushIgnore : MonoBehaviour
{
	public void Configure(VehicleController _vehicle)
	{
	}
}
