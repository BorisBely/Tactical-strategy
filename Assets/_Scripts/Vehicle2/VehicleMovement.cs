using UnityEngine;

public class VehicleMovement
{
	private readonly Rigidbody m_Body;
	private const float k_BaseDrag = 0.05f;

	public float SpeedMs { get; private set; }

	public VehicleMovement(Rigidbody body) { m_Body = body; }

	public void Update(WheelState[] states)
	{
		SpeedMs = m_Body.linearVelocity.magnitude;
	}

	public void ApplyCoastDrag(float coastDrag)
	{
		m_Body.linearDamping = k_BaseDrag + coastDrag;
	}
}
