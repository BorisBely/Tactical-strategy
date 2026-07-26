using UnityEngine;
using System.Text;

public sealed class WcMultiTest : MonoBehaviour
{
	[Header("Wheel")]
	[SerializeField] private float m_Radius = 0.45f;
	[SerializeField] private float m_SuspDist = 0.30f;
	[SerializeField] private float m_Spring = 50000f;
	[SerializeField] private float m_Damper = 4000f;
	[SerializeField, Range(0f,1f)] private float m_TargetPos = 0.55f;
	[SerializeField] private float m_WheelMass = 100f;
	[SerializeField] private float m_WheelLocalY = 0.526f;
	[SerializeField] private float m_WheelBaseZ = 1.61f;
	[SerializeField] private float m_TrackWidth = 0.94f;
	[Header("Body")]
	[SerializeField] private float m_Mass = 2400f;
	[SerializeField] private Vector3 m_Com = new Vector3(0f, 0.40f, 0f);
	[SerializeField] private float m_SpawnY = 0.274f;
	[Header("Test")]
	[SerializeField, Range(1,4)] private int m_WheelCount = 4;
	[SerializeField] private bool m_UseSubsteps = true;

	private Rigidbody m_Body;
	private WheelCollider[] m_Wheels;
	private string[] m_Names;
	private int m_Frame;

	private void Awake()
	{
		m_Body = gameObject.AddComponent<Rigidbody>();
		m_Body.mass = m_Mass;
		m_Body.centerOfMass = m_Com;
		m_Body.angularDamping = 15f;
		m_Body.maxAngularVelocity = 1f;
		m_Body.interpolation = RigidbodyInterpolation.Interpolate;
		m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		transform.position = new Vector3(0f, m_SpawnY, 0f);

		string[] allNames = { "WC_FL", "WC_FR", "WC_RL", "WC_RR" };
		float[] allX = { -m_TrackWidth, m_TrackWidth, -m_TrackWidth, m_TrackWidth };
		float[] allZ = { m_WheelBaseZ, m_WheelBaseZ, -m_WheelBaseZ, -m_WheelBaseZ };

		m_Wheels = new WheelCollider[m_WheelCount];
		m_Names = new string[m_WheelCount];
		for (int i = 0; i < m_WheelCount; i++)
		{
			m_Names[i] = allNames[i];
			GameObject go = new GameObject(allNames[i]);
			go.transform.SetParent(transform, false);
			go.transform.localPosition = new Vector3(allX[i], m_WheelLocalY, allZ[i]);
			go.layer = gameObject.layer;

			WheelCollider wc = go.AddComponent<WheelCollider>();
			wc.radius = m_Radius;
			wc.mass = m_WheelMass;
			wc.center = Vector3.zero;
			wc.suspensionDistance = m_SuspDist;
			wc.forceAppPointDistance = 0f;

			JointSpring s = wc.suspensionSpring;
			s.spring = m_Spring;
			s.damper = m_Damper;
			s.targetPosition = m_TargetPos;
			wc.suspensionSpring = s;

			if (m_UseSubsteps)
				wc.ConfigureVehicleSubsteps(10f, 30, 20);

			m_Wheels[i] = wc;
		}

		var sb = new StringBuilder();
		sb.Append($"WC_MULTI count={m_WheelCount} bodyY={transform.position.y:F3} ");

		// Remove ALL non-WheelCollider colliders from body.
		int removed = 0;
		foreach (var c in GetComponentsInChildren<Collider>(true))
		{
			if (c is WheelCollider) continue;
			Destroy(c);
			removed++;
		}
		sb.Append($"bodyCollidersRemoved={removed} ");

		for (int i = 0; i < m_Wheels.Length; i++)
			sb.Append($"[{m_Names[i]} lY={m_Wheels[i].transform.localPosition.y:F2}] ");
		Debug.Log(sb.ToString());
	}

	private void FixedUpdate()
	{
		if (m_Frame >= 20) return;
		m_Frame++;

		int g = 0;
		var sb = new StringBuilder();
		sb.Append($"[WC{m_WheelCount}] F{m_Frame:D2} y={transform.position.y:F3} velY={m_Body.linearVelocity.y:F2} ang={m_Body.angularVelocity.magnitude*Mathf.Rad2Deg:F0}");

		for (int i = 0; i < m_Wheels.Length; i++)
		{
			bool h = m_Wheels[i].GetGroundHit(out WheelHit hit);
			if (h) g++;
			m_Wheels[i].GetWorldPose(out Vector3 wp, out _);
			sb.Append($" [{m_Names[i]} g={h} F={hit.force:F0} wcY={wp.y:F2}]");
		}

		sb.Append($" g={g}/{m_WheelCount}");
		Debug.Log(sb.ToString());
	}
}
