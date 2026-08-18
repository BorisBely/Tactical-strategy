using CombatVehicleSystem;
using UnityEngine;

/// <summary>
/// Soft tire carcass before WheelCollider suspension: absorbs high-frequency ground hits
/// via a short spring-damper, without changing suspension tuning or WheelCollider.radius.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WheeledMotor))]
public sealed class VehicleTireCompliance : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(0.005f)] private float m_MaxTireDeflection = 0.04f;
	[SerializeField, Min(0f)] private float m_TireSpring = 12000f;
	[SerializeField, Min(0f)] private float m_TireDamper = 1800f;
	[SerializeField, Min(0f)] private float m_ActivationSpeed = 0.35f;
	[SerializeField, Min(0.1f)] private float m_ReboundSpeed = 2.5f;
	[SerializeField, Min(0f)] private float m_MaxForcePerWheel = 12000f;
	[SerializeField] private bool m_ApplyVisualOffset = true;
	#endregion

	#region Private Fields
	private Rigidbody m_Body;
	private float[] m_Deflection = System.Array.Empty<float>();
	private float[] m_DeflectionVel = System.Array.Empty<float>();
	private Vector3[] m_VisualWorldOffset = System.Array.Empty<Vector3>();
	private bool m_AnyActive;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		TryGetComponent(out m_Body);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Integrate tire deflection and apply absorption forces. Call from FixedUpdate / TickPhysics
	/// before SyncVisuals. Uses WheelCollider.GetGroundHit only — no extra raycasts.
	/// </summary>
	public void TickForces(WheelAxle[] _axles)
	{
		if (!isActiveAndEnabled || m_Body == null || m_Body.isKinematic || _axles == null || _axles.Length == 0)
		{
			ClearState(0);
			return;
		}

		EnsureBuffers(_axles.Length);

		float dt = Time.fixedDeltaTime;
		if (dt <= 0f)
			return;

		m_AnyActive = false;
		for (int i = 0; i < _axles.Length; i++)
		{
			m_VisualWorldOffset[i] = Vector3.zero;

			WheelAxle axle = _axles[i];
			WheelCollider col = axle != null ? axle.Collider : null;
			if (col == null || !col.GetGroundHit(out WheelHit hit))
			{
				RelaxTire(i, dt);
				continue;
			}

			Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
			Vector3 pointVel = m_Body.GetPointVelocity(hit.point);
			float closing = -Vector3.Dot(pointVel, normal);

			float prev = m_Deflection[i];
			float target;
			if (closing > m_ActivationSpeed)
			{
				// Compress tire from impact closing speed (clamped to max deflection).
				float impactPush = (closing - m_ActivationSpeed) * dt;
				target = Mathf.Min(m_MaxTireDeflection, prev + impactPush);
			}
			else
			{
				target = Mathf.MoveTowards(prev, 0f, m_ReboundSpeed * dt);
			}

			m_Deflection[i] = target;
			m_DeflectionVel[i] = (target - prev) / dt;

			if (target <= 0.0005f && closing <= m_ActivationSpeed)
			{
				m_Deflection[i] = 0f;
				m_DeflectionVel[i] = 0f;
				continue;
			}

			m_AnyActive = true;

			// Down along contact normal: soak rigid WheelCollider punch into soft carcass.
			float absorb = m_TireSpring * m_Deflection[i] + m_TireDamper * Mathf.Max(0f, closing);
			absorb = Mathf.Min(absorb, m_MaxForcePerWheel);
			if (absorb > 0.01f)
				m_Body.AddForceAtPosition(-normal * absorb, hit.point, ForceMode.Force);

			if (m_ApplyVisualOffset && m_Deflection[i] > 0.0005f)
				m_VisualWorldOffset[i] = -normal * m_Deflection[i];
		}
	}

	public bool TryGetVisualWorldOffset(int _index, out Vector3 _offset)
	{
		if (!m_ApplyVisualOffset || !m_AnyActive || _index < 0 || _index >= m_VisualWorldOffset.Length)
		{
			_offset = Vector3.zero;
			return false;
		}

		_offset = m_VisualWorldOffset[_index];
		return _offset.sqrMagnitude > 0.0000001f;
	}
	#endregion

	#region Private Methods
	private void EnsureBuffers(int _count)
	{
		if (m_Deflection != null && m_Deflection.Length == _count)
			return;

		m_Deflection = new float[_count];
		m_DeflectionVel = new float[_count];
		m_VisualWorldOffset = new Vector3[_count];
	}

	private void ClearState(int _count)
	{
		m_AnyActive = false;
		if (_count <= 0)
		{
			for (int i = 0; i < m_Deflection.Length; i++)
			{
				m_Deflection[i] = 0f;
				m_DeflectionVel[i] = 0f;
				m_VisualWorldOffset[i] = Vector3.zero;
			}
			return;
		}

		EnsureBuffers(_count);
		for (int i = 0; i < _count; i++)
		{
			m_Deflection[i] = 0f;
			m_DeflectionVel[i] = 0f;
			m_VisualWorldOffset[i] = Vector3.zero;
		}
	}

	private void RelaxTire(int _index, float _dt)
	{
		float prev = m_Deflection[_index];
		if (prev <= 0.0005f)
		{
			m_Deflection[_index] = 0f;
			m_DeflectionVel[_index] = 0f;
			return;
		}

		float next = Mathf.MoveTowards(prev, 0f, m_ReboundSpeed * _dt);
		m_DeflectionVel[_index] = (next - prev) / Mathf.Max(0.0001f, _dt);
		m_Deflection[_index] = next;
		if (next > 0.0005f)
			m_AnyActive = true;
	}
	#endregion
}
