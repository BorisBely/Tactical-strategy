using UnityEngine;

/// <summary>
/// Minimal reproduction test: Rigidbody + 4 WheelColliders + gravity.
/// Place on an empty GameObject in a scene with a ground Plane at y=0.
/// No project dependencies.
/// </summary>
public sealed class WcIsolationTest : MonoBehaviour
{
	[Header("WheelCollider params (mirrors VehicleHierarchyBinder)")]
	[SerializeField] private float m_WheelRadius = 0.45f;
	[SerializeField] private float m_SuspensionDistance = 0.30f;
	[SerializeField] private float m_Spring = 50000f;
	[SerializeField] private float m_Damper = 4000f;
	[SerializeField, Range(0f, 1f)] private float m_TargetPosition = 0.55f;
	[SerializeField] private float m_WheelMass = 100f;
	[SerializeField] private float m_ForceAppPoint = 0f;
	[SerializeField] private float m_WheelLocalY = 0.526f;   // from prefab
	[SerializeField] private float m_WheelBaseZ = 1.61f;      // avg of |1.69| and |1.53|
	[SerializeField] private float m_TrackWidth = 0.94f;      // from prefab
	[SerializeField] private float m_BodyMass = 2400f;
	[SerializeField] private Vector3 m_CenterOfMass = new Vector3(0f, 0.40f, 0f);
	[SerializeField] private float m_AngularDamping = 15f;
	[SerializeField] private float m_MaxAngular = 1f;         // ~57 deg/s
	[SerializeField] private float m_SpawnRootY = 0.274f;     // body Y at spawn

	private Rigidbody m_Body;
	private WheelCollider[] m_Wheels;
	private int m_Frame;

	private void Awake()
	{
		m_Body = gameObject.AddComponent<Rigidbody>();
		m_Body.mass = m_BodyMass;
		m_Body.centerOfMass = m_CenterOfMass;
		m_Body.linearDamping = 0.05f;
		m_Body.angularDamping = m_AngularDamping;
		m_Body.maxAngularVelocity = m_MaxAngular;
		m_Body.interpolation = RigidbodyInterpolation.Interpolate;
		m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		m_Body.isKinematic = false;

		// Place body at spawn height.
		transform.position = new Vector3(0f, m_SpawnRootY, 0f);

		// Create 4 WheelColliders as children.
		m_Wheels = new WheelCollider[4];
		string[] names = { "WC_FL", "WC_FR", "WC_RL", "WC_RR" };
		float[] xSigns = { -1f, 1f, -1f, 1f };
		float[] zSigns = { 1f, 1f, -1f, -1f };
		bool[] steerAxle = { true, true, false, false };

		for (int i = 0; i < 4; i++)
		{
			GameObject go = new GameObject(names[i]);
			go.transform.SetParent(transform, false);
			go.transform.localPosition = new Vector3(xSigns[i] * m_TrackWidth, m_WheelLocalY, zSigns[i] * m_WheelBaseZ);
			go.transform.localRotation = Quaternion.identity;
			go.layer = gameObject.layer;

			WheelCollider wc = go.AddComponent<WheelCollider>();
			wc.radius = m_WheelRadius;
			wc.mass = m_WheelMass;
			wc.center = Vector3.zero;
			wc.wheelDampingRate = 0.25f;
			wc.suspensionDistance = m_SuspensionDistance;
			wc.forceAppPointDistance = m_ForceAppPoint;

			JointSpring spring = wc.suspensionSpring;
			spring.spring = m_Spring;
			spring.damper = m_Damper;
			spring.targetPosition = m_TargetPosition;
			wc.suspensionSpring = spring;

			WheelFrictionCurve fwd = wc.forwardFriction;
			fwd.stiffness = 3f;
			wc.forwardFriction = fwd;

			WheelFrictionCurve side = wc.sidewaysFriction;
			side.stiffness = 2f;
			wc.sidewaysFriction = side;

			m_Wheels[i] = wc;
		}

		Debug.Log($"[WC_TEST] Awake — body at y={transform.position.y:F3}, {m_Wheels.Length} wheels");
	}

	private void FixedUpdate()
	{
		if (m_Frame >= 25)
			return;

		m_Frame++;
		int g = 0;
		for (int i = 0; i < m_Wheels.Length; i++)
			if (m_Wheels[i] != null && m_Wheels[i].GetGroundHit(out _))
				g++;

		Debug.Log(
			$"[WC_TEST] F{m_Frame:D2} y={transform.position.y:F3} " +
			$"velY={m_Body.linearVelocity.y:F2} " +
			$"ang={m_Body.angularVelocity.magnitude * Mathf.Rad2Deg:F0}°/s " +
			$"g={g}");
	}
}
