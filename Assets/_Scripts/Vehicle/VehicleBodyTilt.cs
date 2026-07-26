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
 	private const float c_MaxTiltDeg = 20f;
 	private const float c_Smooth = 8f;
 	#endregion

 	#region Serialized Fields
 	[SerializeField] private WheeledMotor m_Motor;
 	[SerializeField] private VehicleController m_Vehicle;
 	[SerializeField] private Transform m_VisualRoot;
 	#endregion

	#region Private Fields
	private Quaternion m_BaseLocalRotation = Quaternion.identity;
	private float m_Pitch;
	private float m_Roll;
	private bool m_HierarchyReady;
	private readonly Vector3[] m_LastContactPoints = new Vector3[4];
	private readonly bool[] m_HasLastContact = new bool[4];
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
			m_VisualRoot.localRotation = Quaternion.Euler(m_Pitch, 0f, m_Roll);
			return;
		}

		ComputeTiltFromWheelContacts(out float targetPitch, out float targetRoll);
		m_Pitch = Mathf.LerpAngle(m_Pitch, targetPitch, 1f - Mathf.Exp(-c_Smooth * dt));
		m_Roll = Mathf.LerpAngle(m_Roll, targetRoll, 1f - Mathf.Exp(-c_Smooth * dt));

		m_VisualRoot.localRotation = Quaternion.Euler(m_Pitch, 0f, m_Roll);
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

	/// <summary>
	/// Compute visual pitch/roll from the plane formed by the four wheel contact points.
	/// No raycasts — uses live WheelHit.point directly. The visual root tilts to match
	/// the ground plane, clamped to ±c_MaxTiltDeg.
	/// </summary>
	private void ComputeTiltFromWheelContacts(out float _pitch, out float _roll)
	{
		_pitch = 0f;
		_roll = 0f;

		WheelCollider[] wcs = null;
		if (m_Motor != null && m_Motor.Axles != null)
		{
			wcs = new WheelCollider[m_Motor.Axles.Length];
			for (int i = 0; i < m_Motor.Axles.Length; i++)
				wcs[i] = m_Motor.Axles[i]?.Collider;
		}
		else
		{
			wcs = GetComponentsInChildren<WheelCollider>();
		}

		if (wcs == null || wcs.Length == 0)
			return;

		// Update per-wheel contact cache
		for (int i = 0; i < wcs.Length && i < m_LastContactPoints.Length; i++)
		{
			WheelCollider wc = wcs[i];
			if (wc == null) continue;
			if (wc.GetGroundHit(out WheelHit hit))
			{
				m_LastContactPoints[i] = hit.point;
				m_HasLastContact[i] = true;
			}
		}

		// Assign by name or index
		Vector3 fl = Vector3.zero, fr = Vector3.zero, rl = Vector3.zero, rr = Vector3.zero;
		int validCount = 0;
		for (int i = 0; i < wcs.Length && i < m_HasLastContact.Length; i++)
		{
			if (!m_HasLastContact[i])
				continue;

			WheelCollider wc = wcs[i];
			string name = wc != null ? wc.name : "";
			Vector3 pt = m_LastContactPoints[i];

			if (name.IndexOf("_FL", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("FrontLeft", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				fl = pt;
				validCount++;
			}
			else if (name.IndexOf("_FR", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
			         name.IndexOf("FrontRight", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				fr = pt;
				validCount++;
			}
			else if (name.IndexOf("_RL", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
			         name.IndexOf("RearLeft", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				rl = pt;
				validCount++;
			}
			else if (name.IndexOf("_RR", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
			         name.IndexOf("RearRight", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				rr = pt;
				validCount++;
			}
			else
			{
				// Fallback: assign by index order (0=FL, 1=FR, 2=RL, 3=RR)
				switch (i)
				{
					case 0: fl = pt; validCount++; break;
					case 1: fr = pt; validCount++; break;
					case 2: rl = pt; validCount++; break;
					case 3: rr = pt; validCount++; break;
				}
			}
		}

		if (validCount < 3)
			return;

		Vector3 avgFront = (fl + fr) * 0.5f;
		Vector3 avgRear = (rl + rr) * 0.5f;
		Vector3 avgLeft = (fl + rl) * 0.5f;
		Vector3 avgRight = (fr + rr) * 0.5f;

		Vector3 forward = avgFront - avgRear;
		Vector3 right = avgRight - avgLeft;

		if (forward.sqrMagnitude < 0.0001f || right.sqrMagnitude < 0.0001f)
			return;

		Vector3 normal = Vector3.Cross(right, forward).normalized;
		if (normal.y < 0f)
			normal = -normal;

		Vector3 forwardProjected = Vector3.ProjectOnPlane(forward, normal);
		if (forwardProjected.sqrMagnitude < 0.0001f)
			return;

		Quaternion groundRotation = Quaternion.LookRotation(forwardProjected.normalized, normal);
		Quaternion inverted = Quaternion.Inverse(transform.rotation) * groundRotation;

		Vector3 euler = inverted.eulerAngles;
		_pitch = NormalizeAngle(euler.x);
		_roll = NormalizeAngle(euler.z);

		_pitch = Mathf.Clamp(_pitch, -c_MaxTiltDeg, c_MaxTiltDeg);
		_roll = Mathf.Clamp(_roll, -c_MaxTiltDeg, c_MaxTiltDeg);
	}

	private static float NormalizeAngle(float _angle)
	{
		while (_angle > 180f)
			_angle -= 360f;
		while (_angle < -180f)
			_angle += 360f;
		return _angle;
	}
	#endregion
}
