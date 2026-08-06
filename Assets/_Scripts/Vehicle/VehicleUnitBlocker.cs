using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Solid kinematic hull that blocks units from walking through the vehicle
/// without transferring push forces into the drive Rigidbody / WheelColliders.
/// Must NOT be parented under the drive Rigidbody — nested RBs break PhysX and can
/// teleport the chassis underground on wake.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleUnitBlocker : MonoBehaviour
{
	#region Constants
	private const string c_ObjectName = "UnitBlocker";
	private const string c_HolderName = "VehicleUnitBlockers";
	#endregion

	#region Static Cache
	private static Transform s_HolderCache;
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private BoxCollider m_BlockCollider;
	[SerializeField] private Rigidbody m_BlockBody;
	#endregion

	#region Sync State
	private Vector3 m_LastPosition;
	private Quaternion m_LastRotation;
	private Vector3 m_LastScale;
	private bool m_ScaleInitialized;
	#endregion

	#region Public Properties
	public VehicleController Vehicle => m_Vehicle;
	public BoxCollider BlockCollider => m_BlockCollider;
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		FollowVehicle();
	}

	private void FixedUpdate()
	{
		FollowVehicle();
	}

	private void OnDestroy()
	{
		if (m_Vehicle != null && m_Vehicle.transform != null)
		{
			Transform legacy = m_Vehicle.transform.Find(c_ObjectName);
			if (legacy != null && legacy.gameObject != gameObject)
				Destroy(legacy.gameObject);
		}
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// True when this collider belongs to a vehicle unit blocker that should not
	/// count as an obstacle during navigation planning.
	/// </summary>
	public static bool ShouldIgnoreForPlanning(Collider _collider, HashSet<Collider> _self)
	{
		if (_collider == null)
			return true;
		if (_self != null && _self.Contains(_collider))
			return true;

		var blocker = _collider.GetComponentInParent<VehicleUnitBlocker>();
		if (blocker == null)
			return false;

		// Orphan blockers from destroyed vehicles must never block planning.
		if (blocker.Vehicle == null)
			return true;

		if (_self != null &&
		    blocker.BlockCollider != null &&
		    _self.Contains(blocker.BlockCollider))
			return true;

		return false;
	}

	public static void DestroyFor(VehicleController _vehicle)
	{
		if (_vehicle == null)
			return;

		Transform holder = GetHolder();
		string goName = c_ObjectName + "_" + _vehicle.GetInstanceID();
		Transform existing = holder.Find(goName);
		if (existing != null)
			Object.Destroy(existing.gameObject);
	}

	public static VehicleUnitBlocker Ensure(VehicleController _vehicle, BoxCollider _selectionTemplate)
	{
		if (_vehicle == null)
			return null;

		// Remove legacy child-under-drive-RB blocker (nested Rigidbodies).
		Transform legacy = _vehicle.transform.Find(c_ObjectName);
		if (legacy != null)
			Object.Destroy(legacy.gameObject);

		Transform holder = GetHolder();
		string goName = c_ObjectName + "_" + _vehicle.GetInstanceID();
		Transform existing = holder.Find(goName);
		GameObject go = existing != null ? existing.gameObject : new GameObject(goName);
		if (existing == null)
			go.transform.SetParent(holder, false);

		int vehicleLayer = LayerMask.NameToLayer("Vehicle");
		if (vehicleLayer >= 0)
			go.layer = vehicleLayer;

		EnsureLayerCollisionMatrix();

		if (!go.TryGetComponent(out VehicleUnitBlocker blocker))
			blocker = go.AddComponent<VehicleUnitBlocker>();
		blocker.m_Vehicle = _vehicle;
		blocker.Configure(_selectionTemplate);
		blocker.FollowVehicle();
		blocker.IgnoreDriveColliders();
		return blocker;
	}

	public void Configure(BoxCollider _selectionTemplate)
	{
		if (!TryGetComponent(out m_BlockBody))
			m_BlockBody = gameObject.AddComponent<Rigidbody>();

		m_BlockBody.isKinematic = true;
		m_BlockBody.useGravity = false;
		m_BlockBody.interpolation = RigidbodyInterpolation.Interpolate;
		m_BlockBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
		m_BlockBody.constraints = RigidbodyConstraints.FreezeAll;

		if (!TryGetComponent(out m_BlockCollider))
			m_BlockCollider = gameObject.AddComponent<BoxCollider>();

		m_BlockCollider.isTrigger = false;
		m_BlockCollider.enabled = true;

		if (_selectionTemplate != null)
		{
			m_BlockCollider.center = _selectionTemplate.center;
			m_BlockCollider.size = _selectionTemplate.size;
		}
		else if (m_BlockCollider.size.sqrMagnitude < 0.01f)
		{
			m_BlockCollider.center = new Vector3(0f, 1.5f, 0.1f);
			m_BlockCollider.size = new Vector3(2.6f, 1.2f, 4.8f);
		}

		// Keep the solid blocker well above the wheel colliders.  If its bottom
		// dips below ~0.9 m it can intersect the wheels and shove the hull on wake.
		ClampBlockerBottom();

		m_BlockCollider.excludeLayers = 0;
		m_BlockBody.excludeLayers = 0;
	}

	/// <summary>While a unit boards/dismounts through the door volume, allow overlap.</summary>
	public void SetIgnoreUnit(Collider _unitCollider, bool _ignore)
	{
		if (m_BlockCollider == null || _unitCollider == null)
			return;
		Physics.IgnoreCollision(m_BlockCollider, _unitCollider, _ignore);
	}

	/// <summary>
	/// Re-scan the vehicle's colliders (including WheelColliders created after the blocker)
	/// and ignore collisions between them and the unit blocker.
	/// </summary>
	public void RefreshIgnoredDriveColliders()
	{
		IgnoreDriveColliders();
	}
	#endregion

	#region Private Methods
	private static Transform GetHolder()
	{
		if (s_HolderCache != null)
			return s_HolderCache;

		GameObject holderGo = GameObject.Find(c_HolderName);
		if (holderGo == null)
			holderGo = new GameObject(c_HolderName);
		s_HolderCache = holderGo.transform;
		return s_HolderCache;
	}

	private static void EnsureLayerCollisionMatrix()
	{
		int vehicleLayer = LayerMask.NameToLayer("Vehicle");
		int unitLayer = LayerMask.NameToLayer("Unit");
		if (vehicleLayer < 0)
			return;

		// The vehicle and its unit blocker live on the same layer.
		// They must not collide, otherwise the kinematic blocker launches the drive wheels.
		Physics.IgnoreLayerCollision(vehicleLayer, vehicleLayer, true);

		if (unitLayer >= 0)
			Physics.IgnoreLayerCollision(vehicleLayer, unitLayer, false);
	}

	private void ClampBlockerBottom()
	{
		if (m_BlockCollider == null)
			return;

		float bottom = m_BlockCollider.center.y - m_BlockCollider.size.y * 0.5f;
		if (bottom >= 0.9f)
			return;

		float lift = 0.9f - bottom;
		Vector3 center = m_BlockCollider.center;
		center.y += lift * 0.5f;
		m_BlockCollider.center = center;

		Vector3 size = m_BlockCollider.size;
		size.y = Mathf.Max(0.5f, size.y - lift);
		m_BlockCollider.size = size;
	}

	private void FollowVehicle()
	{
		if (m_Vehicle == null)
		{
			Destroy(gameObject);
			return;
		}
		Transform t = m_Vehicle.transform;

		Vector3 pos = t.position;
		Quaternion rot = t.rotation;
		bool moved = pos != m_LastPosition || rot != m_LastRotation;

		if (moved)
		{
			transform.SetPositionAndRotation(pos, rot);
			m_LastPosition = pos;
			m_LastRotation = rot;
		}

		Vector3 scale = t.lossyScale;
		if (!m_ScaleInitialized || scale != m_LastScale)
		{
			transform.localScale = scale;
			m_LastScale = scale;
			m_ScaleInitialized = true;
		}
	}

	private void IgnoreDriveColliders()
	{
		if (m_Vehicle == null || m_BlockCollider == null)
			return;

		Collider[] cols = m_Vehicle.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < cols.Length; i++)
		{
			Collider col = cols[i];
			if (col == null || col == m_BlockCollider)
				continue;

			// Unity 6: IgnoreCollision with WheelCollider desyncs ground hits (grounded=0/4).
			// Clear any previous ignore and do not re-ignore wheels.
			if (col is WheelCollider)
			{
				Physics.IgnoreCollision(m_BlockCollider, col, false);
				continue;
			}

			Physics.IgnoreCollision(m_BlockCollider, col, true);
		}
	}
	#endregion
}
