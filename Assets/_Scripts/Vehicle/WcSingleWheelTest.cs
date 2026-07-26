using UnityEngine;

/// <summary>
/// Minimal test: Rigidbody + 1 WheelCollider.
/// Place on empty GameObject in scene with ground Plane at y=0.
/// Run, observe. If even 1 wheel bounces → WC/PhysX issue.
/// </summary>
public sealed class WcSingleWheelTest : MonoBehaviour
{
	[Header("Wheel")]
	[SerializeField] private float m_Radius = 0.45f;
	[SerializeField] private float m_SuspDist = 0.30f;
	[SerializeField] private float m_Spring = 50000f;
	[SerializeField] private float m_Damper = 4000f;
	[SerializeField, Range(0f,1f)] private float m_TargetPos = 0.55f;
	[SerializeField] private float m_WheelMass = 100f;
	[SerializeField] private float m_WheelLocalY = 0.526f;
	[Header("Body")]
	[SerializeField] private float m_Mass = 600f;
	[SerializeField] private Vector3 m_Com = new Vector3(0f, 0.40f, 0f);
	[SerializeField] private float m_SpawnY = 0.274f;

	private Rigidbody m_Body;
	private WheelCollider m_Wheel;
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

		GameObject go = new GameObject("WC");
		go.transform.SetParent(transform, false);
		go.transform.localPosition = new Vector3(0f, m_WheelLocalY, 0f);
		go.layer = gameObject.layer;

		m_Wheel = go.AddComponent<WheelCollider>();
		m_Wheel.radius = m_Radius;
		m_Wheel.mass = m_WheelMass;
		m_Wheel.center = Vector3.zero;
		m_Wheel.suspensionDistance = m_SuspDist;
		m_Wheel.forceAppPointDistance = 0f;

		JointSpring s = m_Wheel.suspensionSpring;
		s.spring = m_Spring;
		s.damper = m_Damper;
		s.targetPosition = m_TargetPos;
		m_Wheel.suspensionSpring = s;

		Debug.Log($"[WC1] Awake bodyY={transform.position.y:F3} hubY={go.transform.position.y:F2} radius={m_Radius} susp={m_SuspDist}");
	}

	private void FixedUpdate()
	{
		if (m_Frame >= 30) return;
		m_Frame++;

		bool g = m_Wheel.GetGroundHit(out WheelHit h);
		m_Wheel.GetWorldPose(out Vector3 wp, out _);
		float bottom = wp.y - m_Radius;

		Debug.Log(
			$"[WC1] F{m_Frame:D2} y={transform.position.y:F3} " +
			$"velY={m_Body.linearVelocity.y:F2} " +
			$"ang={m_Body.angularVelocity.magnitude*Mathf.Rad2Deg:F0}°/s " +
			$"g={g} F={h.force:F0} " +
			$"wcY={wp.y:F2} bottom={bottom:F2}");
	}
}
