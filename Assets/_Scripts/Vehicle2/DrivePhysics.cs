using UnityEngine;

public class DrivePhysics
{
	private readonly VehicleData m_Data;
	private float m_SmoothedTorque;

	private const float k_HoldBrake  = 600f;
	private const float k_CoastDrag  = 0.15f;
	private const float k_MinFloor   = 0.12f;

	public float MotorTorque { get; private set; }
	public float BrakeTorque { get; private set; }
	public float CoastDrag   { get; private set; }
	public BrakeSource BrakeSource { get; private set; }

	public DrivePhysics(VehicleData data) { m_Data = data; }

	public void Apply(DriveMode mode, float appliedThrottle, float speedMs, float dt)
	{
		switch (mode)
		{
			case DriveMode.Brake:
				MotorTorque = 0f; BrakeTorque = m_Data.BrakeTorque; CoastDrag = 0f;
				BrakeSource = BrakeSource.PlayerBrake; m_SmoothedTorque = 0f;
				return;

			case DriveMode.Hold:
				MotorTorque = 0f; BrakeTorque = k_HoldBrake; CoastDrag = 0f;
				BrakeSource = BrakeSource.Hold; m_SmoothedTorque = 0f;
				return;

			case DriveMode.Coast:
				MotorTorque = 0f; BrakeTorque = 0f; CoastDrag = k_CoastDrag;
				BrakeSource = BrakeSource.None;
				return;

			default: // Drive
				float thr = appliedThrottle;
				if (speedMs > 0.3f)
					thr = thr > 0.01f ? Mathf.Max(thr, k_MinFloor)
						: thr < -0.01f ? Mathf.Min(thr, -k_MinFloor) : 0f;

				float absThr = Mathf.Abs(thr);
				float targetTorque = absThr * m_Data.MaxMotorTorque;
				if (thr < -0.01f) targetTorque = absThr * m_Data.ReverseTorque;
				if (speedMs >= m_Data.MaxSpeedMs && thr > 0f) targetTorque = 0f;
				targetTorque *= Mathf.Sign(thr);

				float ramp = m_Data.TorqueRampRate > 0f ? m_Data.TorqueRampRate * dt : 999f;
				m_SmoothedTorque = Mathf.MoveTowards(m_SmoothedTorque, targetTorque, ramp);
				MotorTorque = m_SmoothedTorque;
				BrakeTorque = 0f; CoastDrag = 0f; BrakeSource = BrakeSource.None;
				return;
		}
	}
}
