using UnityEngine;

/// <summary>
/// Safety net: any collider under an <see cref="RtsUnitMember"/> must not push the drive body,
/// regardless of physics layer (teams share Unit layer; decorations may sit on Default).
/// The kinematic <see cref="VehicleUnitBlocker"/> still blocks walking through.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleDriveUnitPushIgnore : MonoBehaviour
{
	#region Private Fields
	private VehicleController m_Vehicle;
	private Collider[] m_DriveColliders = System.Array.Empty<Collider>();
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle)
	{
		m_Vehicle = _vehicle;
		RebuildDriveColliderCache();
	}
	#endregion

	#region Unity Lifecycle
	private void OnCollisionEnter(Collision _collision)
	{
		TryIgnoreUnitPush(_collision);
	}

	private void OnCollisionStay(Collision _collision)
	{
		TryIgnoreUnitPush(_collision);
	}
	#endregion

	#region Private Methods
	private void RebuildDriveColliderCache()
	{
		Collider[] all = GetComponentsInChildren<Collider>(true);
		int count = 0;
		for (int i = 0; i < all.Length; i++)
		{
			if (IsDriveCollider(all[i]))
				count++;
		}

		m_DriveColliders = new Collider[count];
		int write = 0;
		for (int i = 0; i < all.Length; i++)
		{
			if (!IsDriveCollider(all[i]))
				continue;
			m_DriveColliders[write++] = all[i];
		}
	}

	private static bool IsDriveCollider(Collider _col)
	{
		if (_col == null || _col.isTrigger)
			return false;
		if (_col is WheelCollider)
			return false;
		if (_col.GetComponentInParent<VehicleUnitBlocker>() != null)
			return false;
		return true;
	}

	private void TryIgnoreUnitPush(Collision _collision)
	{
		if (_collision == null || _collision.collider == null)
			return;

		RtsUnitMember unit = _collision.collider.GetComponentInParent<RtsUnitMember>();
		if (unit == null)
			return;

		if (m_DriveColliders == null || m_DriveColliders.Length == 0)
			RebuildDriveColliderCache();

		Collider[] unitCols = unit.GetComponentsInChildren<Collider>(true);
		for (int u = 0; u < unitCols.Length; u++)
		{
			Collider unitCol = unitCols[u];
			if (unitCol == null || unitCol.isTrigger)
				continue;

			for (int d = 0; d < m_DriveColliders.Length; d++)
			{
				Collider driveCol = m_DriveColliders[d];
				if (driveCol == null)
					continue;
				Physics.IgnoreCollision(driveCol, unitCol, true);
			}
		}
	}
	#endregion
}
