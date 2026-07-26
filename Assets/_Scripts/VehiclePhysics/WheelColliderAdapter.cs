using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WheelCollider))]
public sealed class WheelColliderAdapter : MonoBehaviour, IWheelInterface
{
	#region Private Fields
	private WheelCollider m_Collider;
	private Transform m_Visual;
	private float m_BaseRadius;
	private float m_StaticLoad;
	private float m_CurrentLoad;
	private float m_CurrentSlipForward;
	private float m_CurrentSlipSideways;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Collider = GetComponent<WheelCollider>();
		m_BaseRadius = m_Collider.radius;
	}
	#endregion

	#region IWheelInterface
	public bool IsGrounded
	{
		get
		{
			if (m_Collider == null)
				return false;
			return m_Collider.GetGroundHit(out _);
		}
	}

	public float Load
	{
		get
		{
			if (m_Collider == null)
				return 0f;
			if (m_Collider.GetGroundHit(out WheelHit hit))
				m_CurrentLoad = Mathf.Max(0f, hit.force);
			return m_CurrentLoad;
		}
	}

	public float SlipForward
	{
		get
		{
			if (m_Collider == null)
				return 0f;
			if (m_Collider.GetGroundHit(out WheelHit hit))
				m_CurrentSlipForward = Mathf.Abs(hit.forwardSlip);
			return m_CurrentSlipForward;
		}
	}

	public float SlipSideways
	{
		get
		{
			if (m_Collider == null)
				return 0f;
			if (m_Collider.GetGroundHit(out WheelHit hit))
				m_CurrentSlipSideways = Mathf.Abs(hit.sidewaysSlip);
			return m_CurrentSlipSideways;
		}
	}

	public float SuspensionTravel
	{
		get
		{
			if (m_Collider == null)
				return 0f;
			if (m_Collider.GetGroundHit(out WheelHit hit))
			{
				float compression = m_Collider.radius - Vector3.Distance(transform.position, hit.point);
				return Mathf.Max(0f, compression);
			}
			return 0f;
		}
	}

	public float SuspensionTravelRatio
	{
		get
		{
			float dist = m_Collider != null ? m_Collider.suspensionDistance : 0.18f;
			return dist > 0.001f ? SuspensionTravel / dist : 0f;
		}
	}

	public float Radius => m_Collider != null ? m_Collider.radius : 0.45f;

	public float AngularVelocity => m_Collider != null ? m_Collider.rpm : 0f;

	public Vector3 HitNormal
	{
		get
		{
			if (m_Collider != null && m_Collider.GetGroundHit(out WheelHit hit))
				return hit.normal;
			return Vector3.up;
		}
	}

	public Vector3 HitPoint
	{
		get
		{
			if (m_Collider != null && m_Collider.GetGroundHit(out WheelHit hit))
				return hit.point;
			return transform.position;
		}
	}

	public Collider HitCollider
	{
		get
		{
			if (m_Collider != null && m_Collider.GetGroundHit(out WheelHit hit))
				return hit.collider;
			return null;
		}
	}

	public void SetMotorTorque(float torque)
	{
		if (m_Collider != null)
			m_Collider.motorTorque = torque;
	}

	public void SetBrakeTorque(float torque)
	{
		if (m_Collider != null)
			m_Collider.brakeTorque = torque;
	}

	public void SetSteerAngle(float angle)
	{
		if (m_Collider != null)
			m_Collider.steerAngle = angle;
	}

	public void ApplySuspension(SuspensionState state)
	{
		if (m_Collider == null)
			return;

		m_Collider.suspensionDistance = state.travel;

		JointSpring spring = m_Collider.suspensionSpring;
		spring.spring = state.springRate;
		spring.damper = state.damperCompression;
		spring.targetPosition = state.targetPosition;
		m_Collider.suspensionSpring = spring;
	}

	public void ApplyFriction(TireFrictionParams friction)
	{
		if (m_Collider == null)
			return;

		WheelFrictionCurve forward = m_Collider.forwardFriction;
		forward.extremumSlip = friction.extremumSlip;
		forward.extremumValue = friction.extremumValue;
		forward.asymptoteSlip = friction.asymptoteSlip;
		forward.asymptoteValue = friction.asymptoteValue;
		forward.stiffness = friction.stiffness;
		m_Collider.forwardFriction = forward;

		WheelFrictionCurve sideways = m_Collider.sidewaysFriction;
		sideways.extremumSlip = friction.extremumSlip * 0.5f;
		sideways.extremumValue = friction.extremumValue * 0.9f;
		sideways.asymptoteSlip = friction.asymptoteSlip * 0.5f;
		sideways.asymptoteValue = friction.extremumValue * 0.75f;
		sideways.stiffness = friction.stiffness * 0.7f;
		m_Collider.sidewaysFriction = sideways;
	}
	#endregion

	#region Public Methods
	public void BindVisual(Transform visual)
	{
		m_Visual = visual;
	}

	public void ConfigureBase(float radius, float mass)
	{
		if (m_Collider == null)
			return;

		m_BaseRadius = radius;
		m_Collider.radius = radius;
		m_Collider.mass = mass;
		m_Collider.wheelDampingRate = 0.25f;
		m_Collider.forceAppPointDistance = -1f;
		m_Collider.center = Vector3.zero;
	}

	public void SyncVisual()
	{
		if (m_Collider == null || m_Visual == null)
			return;

		m_Collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
		m_Visual.SetPositionAndRotation(pos, rot);
	}
	#endregion
}
