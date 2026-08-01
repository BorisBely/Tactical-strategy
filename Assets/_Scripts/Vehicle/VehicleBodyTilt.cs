using CombatVehicleSystem;
using UnityEngine;

/// <summary>
/// Visual pitch/roll for the hull mesh group. Keeps seats / wheel colliders / wheel meshes on root.
/// Call RebuildHierarchy() after door hinges are bound so doors tilt with the body (same Transform refs).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class VehicleBodyTilt : MonoBehaviour
{
	#region Constants
	private const string c_VisualRootName = "BodyVisualRoot";
	private const string c_HullMeshName = "BodyHullMesh";
	private const float c_MaxPitch = 16f;
	private const float c_MaxRoll = 14f;
	private const float c_Smooth = 8f;
	private const float c_RayHeight = 1.2f;
	private const float c_RayLength = 3.5f;
	#endregion

	#region Serialized Fields
	[SerializeField] private WheeledMotor m_Motor;
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private Transform m_VisualRoot;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	#endregion

	#region Private Fields
	private Quaternion m_BaseLocalRotation = Quaternion.identity;
	private float m_Pitch;
	private float m_Roll;
	private bool m_HierarchyReady;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Motor == null)
			TryGetComponent(out m_Motor);
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
		EnsureVisualRoot();
		RelocateRootHullMesh();
	}

	private void LateUpdate()
	{
		if (!m_HierarchyReady || m_VisualRoot == null)
			return;

		float dt = Time.deltaTime;
		if (ShouldFreezeTilt())
		{
			m_Pitch = Mathf.LerpAngle(m_Pitch, 0f, 1f - Mathf.Exp(-c_Smooth * dt));
			m_Roll = Mathf.LerpAngle(m_Roll, 0f, 1f - Mathf.Exp(-c_Smooth * dt));
			m_VisualRoot.localRotation = m_BaseLocalRotation * Quaternion.Euler(m_Pitch, 0f, m_Roll);
			return;
		}

		SampleResidualTilt(out float targetPitch, out float targetRoll);
		m_Pitch = Mathf.LerpAngle(m_Pitch, targetPitch, 1f - Mathf.Exp(-c_Smooth * dt));
		m_Roll = Mathf.LerpAngle(m_Roll, targetRoll, 1f - Mathf.Exp(-c_Smooth * dt));

		m_VisualRoot.localRotation = m_BaseLocalRotation * Quaternion.Euler(m_Pitch, 0f, m_Roll);
	}
	#endregion

	#region Public Methods
	public void BindMotor(WheeledMotor _motor)
	{
		m_Motor = _motor;
	}

	/// <summary>
	/// Reparent body/door meshes under the visual root. Must run after hinges exist.
	/// </summary>
	public void RebuildHierarchy()
	{
		EnsureVisualRoot();
		RelocateRootHullMesh();
		ReparentBodyPieces();
		m_BaseLocalRotation = m_VisualRoot != null ? m_VisualRoot.localRotation : Quaternion.identity;
		m_HierarchyReady = m_VisualRoot != null;
	}
	#endregion

	#region Private Methods
	private bool ShouldFreezeTilt()
	{
		if (m_Vehicle != null &&
		    m_Vehicle.Board != null &&
		    m_Vehicle.Board.ShouldKeepChassisParked)
			return true;

		Rigidbody body = m_Vehicle != null ? m_Vehicle.GetComponent<Rigidbody>() : null;
		return body != null && body.isKinematic;
	}

	private void EnsureVisualRoot()
	{
		if (m_VisualRoot != null)
			return;

		Transform existing = transform.Find(c_VisualRootName);
		if (existing != null)
		{
			m_VisualRoot = existing;
			return;
		}

		GameObject go = new GameObject(c_VisualRootName);
		go.transform.SetParent(transform, false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		m_VisualRoot = go.transform;
	}

	private void RelocateRootHullMesh()
	{
		EnsureVisualRoot();
		if (m_VisualRoot == null)
			return;
		if (!TryGetComponent(out MeshFilter rootFilter) || rootFilter.sharedMesh == null)
			return;
		if (!TryGetComponent(out MeshRenderer rootRenderer))
			return;

		Transform hull = m_VisualRoot.Find(c_HullMeshName);
		if (hull == null)
		{
			GameObject hullGo = new GameObject(c_HullMeshName);
			hullGo.transform.SetParent(m_VisualRoot, false);
			hullGo.layer = gameObject.layer;

			MeshFilter filter = hullGo.AddComponent<MeshFilter>();
			filter.sharedMesh = rootFilter.sharedMesh;

			MeshRenderer renderer = hullGo.AddComponent<MeshRenderer>();
			renderer.sharedMaterials = rootRenderer.sharedMaterials;
			renderer.shadowCastingMode = rootRenderer.shadowCastingMode;
			renderer.receiveShadows = rootRenderer.receiveShadows;
			renderer.lightProbeUsage = rootRenderer.lightProbeUsage;
			renderer.reflectionProbeUsage = rootRenderer.reflectionProbeUsage;
		}

		rootRenderer.enabled = false;
	}

	private void ReparentBodyPieces()
	{
		EnsureVisualRoot();
		if (m_VisualRoot == null)
			return;

		for (int i = transform.childCount - 1; i >= 0; i--)
		{
			Transform child = transform.GetChild(i);
			if (child == null || child == m_VisualRoot)
				continue;
			if (!ShouldTiltWithBody(child))
				continue;
			if (child.parent == m_VisualRoot)
				continue;
			child.SetParent(m_VisualRoot, true);
		}
	}

	private static bool ShouldTiltWithBody(Transform _child)
	{
		string n = _child.name;
		if (n == c_VisualRootName || n == c_HullMeshName)
			return false;

		if (n.StartsWith("Seat_", System.StringComparison.Ordinal) ||
		    n.StartsWith("Litter_", System.StringComparison.Ordinal) ||
		    n.StartsWith("Approach_", System.StringComparison.Ordinal) ||
		    n.StartsWith("Exit_", System.StringComparison.Ordinal) ||
		    n.StartsWith("WheelCollider_", System.StringComparison.Ordinal) ||
		    n.StartsWith("VehiclePath", System.StringComparison.Ordinal) ||
		    n.StartsWith("Selection", System.StringComparison.Ordinal))
			return false;

		if (n.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return false;

		if (n.StartsWith("Hinge_", System.StringComparison.Ordinal))
			return true;

		return _child.GetComponentInChildren<MeshRenderer>(true) != null;
	}

	private void SampleResidualTilt(out float _pitch, out float _roll)
	{
		_pitch = 0f;
		_roll = 0f;

		if (!TrySampleGroundNormal(out Vector3 avgNormal))
			return;

		if (!TryGetYawBasis(out Quaternion yawOnly))
			return;

		Vector3 groundLocal = Quaternion.Inverse(yawOnly) * avgNormal.normalized;
		if (groundLocal.sqrMagnitude < 0.0001f)
			return;

		float groundPitch = Mathf.Atan2(groundLocal.z, groundLocal.y) * Mathf.Rad2Deg;
		float groundRoll = -Mathf.Atan2(groundLocal.x, groundLocal.y) * Mathf.Rad2Deg;

		Vector3 upLocal = Quaternion.Inverse(yawOnly) * transform.up;
		if (upLocal.sqrMagnitude < 0.0001f)
			return;

		float chassisPitch = Mathf.Atan2(upLocal.z, upLocal.y) * Mathf.Rad2Deg;
		float chassisRoll = -Mathf.Atan2(upLocal.x, upLocal.y) * Mathf.Rad2Deg;

		_pitch = Mathf.Clamp(Mathf.DeltaAngle(chassisPitch, groundPitch), -c_MaxPitch, c_MaxPitch);
		_roll = Mathf.Clamp(Mathf.DeltaAngle(chassisRoll, groundRoll), -c_MaxRoll, c_MaxRoll);
	}

	private bool TrySampleGroundNormal(out Vector3 _avgNormal)
	{
		_avgNormal = Vector3.zero;
		int hits = 0;

		if (m_Motor != null && m_Motor.Axles != null)
		{
			for (int i = 0; i < m_Motor.Axles.Length; i++)
			{
				WheelAxle axle = m_Motor.Axles[i];
				if (axle == null)
					continue;

				Vector3 samplePos;
				if (axle.Collider != null)
				{
					if (axle.Collider.GetGroundHit(out WheelHit wheelHit))
					{
						_avgNormal += wheelHit.normal;
						hits++;
						continue;
					}

					samplePos = axle.Collider.transform.position;
				}
				else if (axle.Visual != null)
				{
					samplePos = axle.Visual.position;
				}
				else
				{
					continue;
				}

				if (RaycastDown(samplePos, out RaycastHit hit))
				{
					_avgNormal += hit.normal;
					hits++;
				}
			}
		}

		if (hits == 0)
		{
			Vector3 origin = transform.position + Vector3.up * c_RayHeight;
			Vector3 right = transform.right;
			Vector3 forward = transform.forward;
			right.y = 0f;
			forward.y = 0f;
			if (right.sqrMagnitude > 0.001f) right.Normalize();
			if (forward.sqrMagnitude > 0.001f) forward.Normalize();

			TryAddRay(origin + right * 1.1f + forward * 1.4f, ref _avgNormal, ref hits);
			TryAddRay(origin - right * 1.1f + forward * 1.4f, ref _avgNormal, ref hits);
			TryAddRay(origin + right * 1.1f - forward * 1.4f, ref _avgNormal, ref hits);
			TryAddRay(origin - right * 1.1f - forward * 1.4f, ref _avgNormal, ref hits);
		}

		if (hits == 0)
			return false;

		_avgNormal /= hits;
		return _avgNormal.sqrMagnitude > 0.0001f;
	}

	private void TryAddRay(Vector3 _origin, ref Vector3 _avgNormal, ref int _hits)
	{
		if (!RaycastDown(_origin, out RaycastHit hit))
			return;
		_avgNormal += hit.normal;
		_hits++;
	}

	private bool RaycastDown(Vector3 _nearPoint, out RaycastHit _hit)
	{
		Vector3 origin = _nearPoint;
		origin.y = transform.position.y + c_RayHeight;
		if (!Physics.Raycast(origin, Vector3.down, out _hit, c_RayLength, m_GroundMask,
			    QueryTriggerInteraction.Ignore))
			return false;
		if (_hit.collider != null && _hit.collider.transform.IsChildOf(transform))
			return false;
		return true;
	}

	private bool TryGetYawBasis(out Quaternion _yawOnly)
	{
		Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
		if (flatForward.sqrMagnitude < 0.0001f)
			flatForward = Vector3.ProjectOnPlane(transform.right, Vector3.up);
		if (flatForward.sqrMagnitude < 0.0001f)
		{
			_yawOnly = Quaternion.identity;
			return false;
		}

		_yawOnly = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
		return true;
	}
	#endregion
}
