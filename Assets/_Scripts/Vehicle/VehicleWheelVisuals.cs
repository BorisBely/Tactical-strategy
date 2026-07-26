using UnityEngine;

/// <summary>
/// Визуал колёс: steer передних, spin всех, raycast-подвеска на неровностях.
/// Steer в пространстве родителя (Y), spin вокруг оси меша — важно из‑за rest-pose 90° по X у FBX.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleWheelVisuals : MonoBehaviour
{
	#region Nested
	[System.Serializable]
	public struct WheelBinding
	{
		public Transform Wheel;
		public bool IsSteering;
		public float Radius;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleMotor m_Motor;
	[SerializeField] private WheelBinding[] m_Wheels = System.Array.Empty<WheelBinding>();
	[SerializeField, Min(0.05f)] private float m_SuspensionRayLength = 0.85f;
	[SerializeField, Min(0.01f)] private float m_SuspensionSmooth = 12f;
	[SerializeField] private LayerMask m_GroundMask = ~0;
	[SerializeField] private bool m_ApplyBodyTilt = true;
	[SerializeField, Range(0f, 12f)] private float m_MaxBodyTiltDegrees = 6f;
	#endregion

	#region Private Fields
	private Transform m_BodyTiltTarget;
	private Vector3[] m_BaseLocalPositions;
	private Quaternion[] m_BaseLocalRotations;
	private float[] m_SpinAngles;
	private float[] m_SuspensionOffsets;
	private bool m_Initialized;
	private bool m_Dirty = true;
	private Quaternion m_BaseBodyLocalRotation = Quaternion.identity;
	#endregion

	#region Public Methods
	public void SetMotor(VehicleMotor _motor) => m_Motor = _motor;

	public void SetWheels(WheelBinding[] _wheels)
	{
		m_Wheels = _wheels ?? System.Array.Empty<WheelBinding>();
		m_Initialized = false;
		m_Dirty = true;
	}

	public void SetBodyTiltTarget(Transform _target)
	{
		m_BodyTiltTarget = _target;
		if (_target != null)
			m_BaseBodyLocalRotation = _target.localRotation;
	}

	public void MarkDirty() => m_Dirty = true;
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		if (m_Motor == null)
			TryGetComponent(out m_Motor);

		bool moving = m_Motor != null && (m_Motor.IsMoving || Mathf.Abs(m_Motor.CurrentSpeed) > 0.01f);
		if (!moving && !m_Dirty)
			return;

		EnsureInit();
		if (m_Wheels == null || m_Wheels.Length == 0)
			return;

		float signedSpeed = m_Motor != null ? m_Motor.SignedSpeed : 0f;
		float steer = m_Motor != null ? m_Motor.SteerAngle : 0f;
		float dt = Time.deltaTime;

		Vector3 avgNormal = Vector3.zero;
		int hitCount = 0;

		for (int i = 0; i < m_Wheels.Length; i++)
		{
			WheelBinding binding = m_Wheels[i];
			if (binding.Wheel == null)
				continue;

			float radius = binding.Radius > 0.01f ? binding.Radius : 0.45f;
			float spinDelta = (signedSpeed / radius) * Mathf.Rad2Deg * dt;
			m_SpinAngles[i] = Mathf.Repeat(m_SpinAngles[i] + spinDelta, 360f);

			Vector3 rayOrigin = binding.Wheel.parent != null
				? binding.Wheel.parent.TransformPoint(m_BaseLocalPositions[i] + Vector3.up * 0.35f)
				: binding.Wheel.position + Vector3.up * 0.35f;

			float targetOffset = 0f;
			if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, m_SuspensionRayLength, m_GroundMask,
				    QueryTriggerInteraction.Ignore))
			{
				float compression = (m_SuspensionRayLength * 0.5f) - hit.distance;
				targetOffset = Mathf.Clamp(compression, -0.25f, 0.2f);
				avgNormal += hit.normal;
				hitCount++;
			}

			m_SuspensionOffsets[i] = Mathf.Lerp(m_SuspensionOffsets[i], targetOffset, 1f - Mathf.Exp(-m_SuspensionSmooth * dt));

			Vector3 localPos = m_BaseLocalPositions[i];
			localPos.y += m_SuspensionOffsets[i];
			binding.Wheel.localPosition = localPos;

			// Parent-space yaw (steer) * rest pose * mesh-local spin (X).
			// Старый порядок base*steer*spin при rest-pose 90°X превращал yaw в roll (развал наружу).
			Quaternion steerRot = binding.IsSteering
				? Quaternion.Euler(0f, steer, 0f)
				: Quaternion.identity;
			Quaternion spinRot = Quaternion.Euler(m_SpinAngles[i], 0f, 0f);
			binding.Wheel.localRotation = steerRot * m_BaseLocalRotations[i] * spinRot;
		}

		if (m_ApplyBodyTilt && m_BodyTiltTarget != null && hitCount > 0)
		{
			avgNormal /= hitCount;
			Quaternion tilt = Quaternion.FromToRotation(Vector3.up, avgNormal);
			Vector3 euler = tilt.eulerAngles;
			float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -m_MaxBodyTiltDegrees, m_MaxBodyTiltDegrees);
			float roll = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.z), -m_MaxBodyTiltDegrees, m_MaxBodyTiltDegrees);
			Quaternion target = m_BaseBodyLocalRotation * Quaternion.Euler(pitch, 0f, roll);
			m_BodyTiltTarget.localRotation = Quaternion.Slerp(
				m_BodyTiltTarget.localRotation, target, 1f - Mathf.Exp(-8f * dt));
		}

		if (!moving)
			m_Dirty = false;
	}
	#endregion

	#region Private Methods
	private void EnsureInit()
	{
		if (m_Initialized && m_BaseLocalPositions != null && m_BaseLocalPositions.Length == m_Wheels.Length)
			return;

		int count = m_Wheels != null ? m_Wheels.Length : 0;
		m_BaseLocalPositions = new Vector3[count];
		m_BaseLocalRotations = new Quaternion[count];
		m_SpinAngles = new float[count];
		m_SuspensionOffsets = new float[count];
		for (int i = 0; i < count; i++)
		{
			if (m_Wheels[i].Wheel == null)
				continue;
			m_BaseLocalPositions[i] = m_Wheels[i].Wheel.localPosition;
			m_BaseLocalRotations[i] = m_Wheels[i].Wheel.localRotation;
		}

		m_Initialized = true;
	}
	#endregion
}
