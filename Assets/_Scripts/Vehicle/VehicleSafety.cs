using UnityEngine;

/// <summary>
/// Passive safety guards. Runs before VehicleController in execution order.
/// Only clamps extreme values (NaN, Inf, speed > 300 m/s, angular > 50 rad/s).
/// Does NOT damp or stabilise — that belongs in VehicleRecovery.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class VehicleSafety : MonoBehaviour
{
	[Header("Linear")]
	[SerializeField] private float m_MaxLinearSpeed = 300f;
	[SerializeField] private bool m_ClampNaN = true;
	[SerializeField] private bool m_ClampInfinity = true;

	[Header("Angular")]
	[SerializeField] private float m_MaxAngularSpeed = 50f;
	[SerializeField] private bool m_LogEmergency = true;

	private Rigidbody m_Body;

	private void Awake()
	{
		m_Body = GetComponent<Rigidbody>();
	}

	private void FixedUpdate()
	{
		if (m_Body == null || m_Body.isKinematic)
			return;

		Vector3 vel = m_Body.linearVelocity;
		Vector3 ang = m_Body.angularVelocity;

		bool badVel = false;
		bool badAng = false;

		if (m_ClampNaN)
		{
			if (float.IsNaN(vel.x) || float.IsNaN(vel.y) || float.IsNaN(vel.z)) badVel = true;
			if (float.IsNaN(ang.x) || float.IsNaN(ang.y) || float.IsNaN(ang.z)) badAng = true;
		}

		if (m_ClampInfinity)
		{
			if (float.IsInfinity(vel.x) || float.IsInfinity(vel.y) || float.IsInfinity(vel.z)) badVel = true;
			if (float.IsInfinity(ang.x) || float.IsInfinity(ang.y) || float.IsInfinity(ang.z)) badAng = true;
		}

		if (vel.sqrMagnitude > m_MaxLinearSpeed * m_MaxLinearSpeed)
			badVel = true;

		if (ang.sqrMagnitude > m_MaxAngularSpeed * m_MaxAngularSpeed)
			badAng = true;

		if (badVel || badAng)
		{
			m_Body.linearVelocity = Vector3.zero;
			m_Body.angularVelocity = Vector3.zero;
			if (m_LogEmergency)
				Debug.Log($"[VehicleSafety:{name}] EMERGENCY clamp vel=({vel.x:F1},{vel.y:F1},{vel.z:F1}) ang=({ang.x:F1},{ang.y:F1},{ang.z:F1})", this);
		}
	}
}
