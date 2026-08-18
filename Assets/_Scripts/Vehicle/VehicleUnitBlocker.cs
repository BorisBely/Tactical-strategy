using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VehicleNavigation;

/// <summary>
/// External kinematic hull that always blocks units (Vehicle↔Unit stays ignored on the drive RB).
/// Must NOT be parented under the drive Rigidbody — nested RBs break PhysX.
/// Speed only gates NavMeshObstacle carving; solid collision stays on at all speeds.
/// Fast-motion OverlapBox still reports crush/hit events.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleUnitBlocker : MonoBehaviour
{
	#region Constants
	private const string c_ObjectName = "UnitBlocker";
	private const string c_HolderName = "VehicleUnitBlockers";
	private const string c_BlockerLayerName = "VehicleBlocker";
	private const float c_HitThrottleSec = 0.1f;
	private const int c_OverlapBufferSize = 24;
	#endregion

	#region Static Cache
	private static Transform s_HolderCache;
	private static bool s_LayerMatrixReady;
	private static readonly Collider[] s_OverlapBuffer = new Collider[c_OverlapBufferSize];
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private BoxCollider m_BlockCollider;
	[SerializeField] private Rigidbody m_BlockBody;
	[SerializeField] private NavMeshObstacle m_NavObstacle;
	[SerializeField] private float m_CarveEnterSpeedKmh = 7f;
	[SerializeField] private float m_CarveExitSpeedKmh = 11f;
	#endregion

	#region Runtime State
	private Vector3 m_LastPosition;
	private Quaternion m_LastRotation = Quaternion.identity;
	private Vector3 m_LastScale = Vector3.one;
	private bool m_ScaleInitialized;
	private bool m_CarveActive = true;
	private Vector3 m_LocalBoxCenter = new Vector3(0f, 1.5f, 0.1f);
	private Vector3 m_LocalBoxSize = new Vector3(2.6f, 1.2f, 4.8f);
	private float m_LastPlanarSpeedMs;
	private readonly Dictionary<EntityId, float> m_LastHitTimeByUnit = new Dictionary<EntityId, float>(8);
	#endregion

	#region Public Properties
	public VehicleController Vehicle => m_Vehicle;
	public BoxCollider BlockCollider => m_BlockCollider;
	/// <summary>Blocker is always solid while alive; kept for planning cache API.</summary>
	public bool IsSolidActive => isActiveAndEnabled && m_BlockCollider != null && m_BlockCollider.enabled;
	#endregion

	#region Unity Lifecycle
	private void FixedUpdate()
	{
		if (m_Vehicle == null)
			return;
		FollowVehicle(false);
	}

	private void OnCollisionEnter(Collision _collision)
	{
		TryEmitSolidHit(_collision);
	}

	private void OnCollisionStay(Collision _collision)
	{
		TryEmitSolidHit(_collision);
	}

	private void OnDestroy()
	{
		if (m_Vehicle != null)
			PlanningObstacleSnapshot.ClearColliderCache(m_Vehicle.transform);

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
	/// count as an obstacle during navigation planning for the given self set.
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

		if (blocker.Vehicle == null)
			return true;

		if (!blocker.IsSolidActive)
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

		PlanningObstacleSnapshot.ClearColliderCache(_vehicle.transform);

		Transform holder = GetHolder();
		string goName = c_ObjectName + "_" + _vehicle.GetEntityId();
		Transform existing = holder.Find(goName);
		if (existing != null)
			Object.Destroy(existing.gameObject);
	}

	public static void CleanupOrphans()
	{
		Transform holder = GetHolder();
		for (int i = holder.childCount - 1; i >= 0; i--)
		{
			Transform child = holder.GetChild(i);
			if (child == null)
				continue;
			if (!child.TryGetComponent(out VehicleUnitBlocker blocker) || blocker.Vehicle == null)
				Object.Destroy(child.gameObject);
		}
	}

	public static VehicleUnitBlocker Ensure(VehicleController _vehicle, BoxCollider _selectionTemplate)
	{
		if (_vehicle == null)
			return null;

		CleanupOrphans();

		Transform legacy = _vehicle.transform.Find(c_ObjectName);
		if (legacy != null)
			Object.Destroy(legacy.gameObject);

		Transform holder = GetHolder();
		string goName = c_ObjectName + "_" + _vehicle.GetEntityId();
		Transform existing = holder.Find(goName);

		// Destroy() is deferred — never reclaim a dying orphan that still shares this name.
		if (existing != null)
		{
			bool usable = existing.TryGetComponent(out VehicleUnitBlocker existingBlocker) &&
			              existingBlocker.Vehicle == _vehicle;
			if (!usable)
			{
				existing.name = goName + "_orphan";
				Object.Destroy(existing.gameObject);
				existing = null;
			}
		}

		GameObject go;
		if (existing != null)
		{
			go = existing.gameObject;
		}
		else
		{
			go = new GameObject(goName);
			go.transform.SetParent(holder, false);
			go.hideFlags = HideFlags.DontSave;
		}

		EnsureLayerCollisionMatrix();

		int blockerLayer = LayerMask.NameToLayer(c_BlockerLayerName);
		if (blockerLayer >= 0)
			go.layer = blockerLayer;

		if (!go.TryGetComponent(out VehicleUnitBlocker blocker))
			blocker = go.AddComponent<VehicleUnitBlocker>();
		blocker.m_Vehicle = _vehicle;
		blocker.Configure(_selectionTemplate);
		if (!go.activeSelf)
			go.SetActive(true);
		blocker.FollowVehicle(true);
		blocker.IgnoreDriveColliders();
		blocker.ApplyCarveState(true, true);
		PlanningObstacleSnapshot.ClearColliderCache(_vehicle.transform);
		return blocker;
	}

	public void Configure(BoxCollider _selectionTemplate)
	{
		if (!TryGetComponent(out m_BlockBody))
			m_BlockBody = gameObject.AddComponent<Rigidbody>();

		m_BlockBody.isKinematic = true;
		m_BlockBody.useGravity = false;
		m_BlockBody.interpolation = RigidbodyInterpolation.None;
		m_BlockBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
		// FreezeAll blocks kinematic MovePosition in Unity 6 (rotation may still apply) —
		// kinematic body is already immune to forces; do not freeze pose.
		m_BlockBody.constraints = RigidbodyConstraints.None;

		if (!TryGetComponent(out m_BlockCollider))
			m_BlockCollider = gameObject.AddComponent<BoxCollider>();

		m_BlockCollider.isTrigger = false;
		m_BlockCollider.enabled = true;

		if (_selectionTemplate != null)
		{
			m_LocalBoxCenter = _selectionTemplate.center;
			m_LocalBoxSize = _selectionTemplate.size;
			m_BlockCollider.center = m_LocalBoxCenter;
			m_BlockCollider.size = m_LocalBoxSize;
		}
		else if (m_BlockCollider.size.sqrMagnitude < 0.01f)
		{
			m_BlockCollider.center = m_LocalBoxCenter;
			m_BlockCollider.size = m_LocalBoxSize;
		}
		else
		{
			m_LocalBoxCenter = m_BlockCollider.center;
			m_LocalBoxSize = m_BlockCollider.size;
		}

		ClampBlockerBottom();
		m_LocalBoxCenter = m_BlockCollider.center;
		m_LocalBoxSize = m_BlockCollider.size;

		m_BlockCollider.excludeLayers = 0;
		m_BlockBody.excludeLayers = 0;

		EnsureNavObstacle();
	}

	/// <summary>
	/// Per-unit boarding ignore against this blocker box only (not SoftPass for everyone).
	/// </summary>
	public void SetIgnoreUnit(Collider _unitCollider, bool _ignore)
	{
		TryIgnoreCollision(m_BlockCollider, _unitCollider, _ignore);
	}

	public void SetIgnoreUnit(RtsUnitMember _unit, bool _ignore)
	{
		if (_unit == null || m_BlockCollider == null)
			return;

		Collider[] cols = _unit.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < cols.Length; i++)
		{
			Collider col = cols[i];
			if (col == null || !col.enabled)
				continue;
			TryIgnoreCollision(m_BlockCollider, col, _ignore);
		}
	}

	public void RefreshIgnoredDriveColliders()
	{
		if (isActiveAndEnabled)
			IgnoreDriveColliders();
	}

	/// <summary>
	/// Called every FixedUpdate from <see cref="VehicleController"/>.
	/// Solid collision always on; speed only toggles NavMesh carving + hit probes.
	/// </summary>
	public void TickFromVehicle(float _speedKmh)
	{
		if (m_Vehicle == null)
		{
			Destroy(gameObject);
			return;
		}

		if (!gameObject.activeSelf)
			gameObject.SetActive(true);
		if (m_BlockCollider != null && !m_BlockCollider.enabled)
			m_BlockCollider.enabled = true;

		FollowVehicle(false);

		m_LastPlanarSpeedMs = ResolvePlanarVehicleSpeedMs(_speedKmh);

		float absSpeed = Mathf.Abs(_speedKmh);
		float enter = Mathf.Min(m_CarveEnterSpeedKmh, m_CarveExitSpeedKmh);
		float exit = Mathf.Max(m_CarveEnterSpeedKmh, m_CarveExitSpeedKmh);

		if (m_CarveActive)
		{
			if (absSpeed > exit)
				ApplyCarveState(false, false);
		}
		else if (absSpeed < enter)
		{
			ApplyCarveState(true, false);
		}

		// Impact probes while moving — do not gate on carve threshold (carve starts ~7 km/h;
		// light trauma begins ~3.6 km/h and solid OnCollision often never fires on kinematic hull).
		if (m_LastPlanarSpeedMs >= 0.5f || absSpeed >= 2f)
			TickFastMotionHits(_speedKmh);
	}

	public void DispatchHit(in VehicleUnitHitEvent _hit)
	{
		if (_hit.Unit == null || _hit.Vehicle == null)
			return;

		EntityId id = _hit.Unit.GetEntityId();
		float now = Time.time;
		if (m_LastHitTimeByUnit.TryGetValue(id, out float last) && now - last < c_HitThrottleSec)
			return;
		m_LastHitTimeByUnit[id] = now;

		// Concrete Ensure — interface GetComponentsInChildren is unreliable / empty when
		// the receiver was added at runtime and never baked into the prefab.
		UnitVehicleImpactReceiver receiver = UnitVehicleImpactReceiver.Ensure(_hit.Unit.gameObject);
		receiver?.OnVehicleUnitHit(in _hit);
	}
	#endregion

	#region Private Methods
	private static Transform GetHolder()
	{
		if (s_HolderCache != null)
			return s_HolderCache;

		GameObject holderGo = GameObject.Find(c_HolderName);
		if (holderGo == null)
		{
			holderGo = new GameObject(c_HolderName);
			holderGo.hideFlags = HideFlags.DontSave;
		}

		s_HolderCache = holderGo.transform;
		return s_HolderCache;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void BootstrapLayerMatrix()
	{
		s_LayerMatrixReady = false;
		EnsureLayerCollisionMatrix();
	}

	public static void EnsureLayerCollisionMatrix()
	{
		if (s_LayerMatrixReady)
			return;

		int vehicle = LayerMask.NameToLayer("Vehicle");
		int unit = LayerMask.NameToLayer("Unit");
		int target = LayerMask.NameToLayer("Target");
		int blocker = LayerMask.NameToLayer(c_BlockerLayerName);

		if (blocker < 0)
		{
			Debug.LogWarning(
				$"[{nameof(VehicleUnitBlocker)}] Layer '{c_BlockerLayerName}' missing — unit hull proxy disabled until layer exists.");
		}

		if (vehicle >= 0)
		{
			Physics.IgnoreLayerCollision(vehicle, vehicle, false);
			if (unit >= 0)
				Physics.IgnoreLayerCollision(vehicle, unit, true);
			if (target >= 0)
				Physics.IgnoreLayerCollision(vehicle, target, true);
		}

		if (blocker >= 0)
		{
			if (vehicle >= 0)
				Physics.IgnoreLayerCollision(blocker, vehicle, true);
			Physics.IgnoreLayerCollision(blocker, blocker, true);
			if (unit >= 0)
				Physics.IgnoreLayerCollision(blocker, unit, false);
			if (target >= 0)
				Physics.IgnoreLayerCollision(blocker, target, false);
		}

		s_LayerMatrixReady = true;
	}

	private void EnsureNavObstacle()
	{
		if (!TryGetComponent(out m_NavObstacle))
			m_NavObstacle = gameObject.AddComponent<NavMeshObstacle>();

		m_NavObstacle.shape = NavMeshObstacleShape.Box;
		m_NavObstacle.center = m_LocalBoxCenter;
		m_NavObstacle.size = m_LocalBoxSize;
		m_NavObstacle.carving = true;
		m_NavObstacle.carveOnlyStationary = true;
		m_NavObstacle.enabled = true;
	}

	private void ApplyCarveState(bool _carve, bool _force)
	{
		if (!_force && m_CarveActive == _carve)
			return;

		m_CarveActive = _carve;
		EnsureNavObstacle();
		m_NavObstacle.carving = _carve;
		m_NavObstacle.carveOnlyStationary = true;
		m_NavObstacle.center = m_LocalBoxCenter;
		m_NavObstacle.size = m_LocalBoxSize;
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

	private void FollowVehicle(bool _force)
	{
		if (m_Vehicle == null)
		{
			Destroy(gameObject);
			return;
		}

		Transform t = m_Vehicle.transform;
		Vector3 pos = t.position;
		Quaternion rot = t.rotation;
		bool moved = _force ||
		             (pos - m_LastPosition).sqrMagnitude > 1e-8f ||
		             Quaternion.Angle(rot, m_LastRotation) > 0.01f;

		if (moved)
		{
			// Always keep Transform in sync (gizmos / NavMeshObstacle). Physics pose via
			// MovePosition when not forcing — ContinuousSpeculative needs the Move* path.
			transform.SetPositionAndRotation(pos, rot);
			if (m_BlockBody != null)
			{
				if (_force)
				{
					m_BlockBody.position = pos;
					m_BlockBody.rotation = rot;
				}
				else
				{
					m_BlockBody.MovePosition(pos);
					m_BlockBody.MoveRotation(rot);
				}
			}

			m_LastPosition = pos;
			m_LastRotation = rot;
		}

		Vector3 scale = t.lossyScale;
		if (!m_ScaleInitialized || (scale - m_LastScale).sqrMagnitude > 1e-8f)
		{
			transform.localScale = scale;
			m_LastScale = scale;
			m_ScaleInitialized = true;
		}

		if (m_NavObstacle != null)
		{
			m_NavObstacle.center = m_LocalBoxCenter;
			m_NavObstacle.size = m_LocalBoxSize;
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

			if (col is WheelCollider)
			{
				TryIgnoreCollision(m_BlockCollider, col, false);
				continue;
			}

			TryIgnoreCollision(m_BlockCollider, col, true);
		}
	}

	/// <summary>
	/// Physics.IgnoreCollision throws if either collider belongs to a prefab asset
	/// (not a live scene instance) — e.g. nested prefab refs during Mission Prep spawn.
	/// </summary>
	private static void TryIgnoreCollision(Collider _a, Collider _b, bool _ignore)
	{
		if (!CanIgnoreCollision(_a, _b))
			return;

		Physics.IgnoreCollision(_a, _b, _ignore);
	}

	private static bool CanIgnoreCollision(Collider _a, Collider _b)
	{
		if (_a == null || _b == null || _a == _b)
			return false;

		return IsLiveSceneCollider(_a) && IsLiveSceneCollider(_b);
	}

	private static bool IsLiveSceneCollider(Collider _collider)
	{
		if (_collider == null)
			return false;

		GameObject go = _collider.gameObject;
		if (go == null)
			return false;

		// Prefab assets / broken nested refs are not part of a loaded scene.
		return go.scene.IsValid() && go.scene.isLoaded;
	}

	private void TryEmitSolidHit(Collision _collision)
	{
		if (_collision == null || _collision.collider == null || m_Vehicle == null)
			return;

		RtsUnitMember unit = _collision.collider.GetComponentInParent<RtsUnitMember>();
		if (unit == null)
			return;

		Vector3 point = _collision.contactCount > 0 ? _collision.GetContact(0).point : unit.transform.position;
		Vector3 normal = _collision.contactCount > 0 ? _collision.GetContact(0).normal : Vector3.up;
		// Kinematic MovePosition often reports ~0 relative velocity — prefer planar vehicle speed.
		float contactRel = _collision.relativeVelocity.magnitude;
		float rel = Mathf.Max(contactRel, m_LastPlanarSpeedMs);
		var hit = new VehicleUnitHitEvent(m_Vehicle, unit, point, normal, rel, true);
		DispatchHit(in hit);
	}

	private void TickFastMotionHits(float _speedKmh)
	{
		if (m_Vehicle == null)
			return;

		Transform t = m_Vehicle.transform;
		Vector3 worldCenter = t.TransformPoint(m_LocalBoxCenter);
		Vector3 worldHalf = Vector3.Scale(m_LocalBoxSize * 0.5f, AbsScale(t.lossyScale));
		int mask = 0;
		int unit = LayerMask.NameToLayer("Unit");
		if (unit >= 0)
			mask |= 1 << unit;
		int target = LayerMask.NameToLayer("Target");
		if (target >= 0)
			mask |= 1 << target;
		if (mask == 0)
			return;

		int count = Physics.OverlapBoxNonAlloc(
			worldCenter, worldHalf, s_OverlapBuffer, t.rotation, mask, QueryTriggerInteraction.Ignore);

		float relMs = Mathf.Max(Mathf.Abs(_speedKmh) / 3.6f, m_LastPlanarSpeedMs);
		for (int i = 0; i < count; i++)
		{
			Collider col = s_OverlapBuffer[i];
			if (col == null)
				continue;
			RtsUnitMember unitMember = col.GetComponentInParent<RtsUnitMember>();
			if (unitMember == null)
				continue;

			if (m_BlockCollider != null && Physics.GetIgnoreCollision(m_BlockCollider, col))
				continue;

			Vector3 toUnit = unitMember.transform.position - t.position;
			toUnit.y = 0f;
			Vector3 normal = toUnit.sqrMagnitude > 1e-4f ? toUnit.normalized : t.forward;
			var hit = new VehicleUnitHitEvent(
				m_Vehicle, unitMember, unitMember.transform.position, normal, relMs, false);
			DispatchHit(in hit);
		}
	}

	private float ResolvePlanarVehicleSpeedMs(float _speedKmh)
	{
		float fromReport = Mathf.Abs(_speedKmh) / 3.6f;
		float fromBody = 0f;
		if (m_Vehicle != null && m_Vehicle.TryGetComponent(out Rigidbody body))
		{
			Vector3 flat = body.linearVelocity;
			flat.y = 0f;
			fromBody = flat.magnitude;
		}

		return Mathf.Max(fromReport, fromBody);
	}

	private static Vector3 AbsScale(Vector3 _scale)
	{
		return new Vector3(Mathf.Abs(_scale.x), Mathf.Abs(_scale.y), Mathf.Abs(_scale.z));
	}
	#endregion
}
