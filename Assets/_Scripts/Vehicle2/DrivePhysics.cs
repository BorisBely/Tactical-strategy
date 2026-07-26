using UnityEngine;

public enum TestAxle { All, Front, Rear, Left, Right }

public class DrivePhysics
{
	private readonly VehicleData m_Data;
	private float m_SmoothedTorque;
	private float m_SmoothedAccel;
	private float m_CruiseIntegral;
	private float m_TestTimer;

	public static bool ConstTorqueTest = false;
	public static bool AntiStuckDisabled = true;
	public static float ForwardStiffnessOverride = 0f;
	public static float SidewaysStiffnessOverride = 0f;
	public static bool UseDirectForce = true;
	public static float DirectForceValue = 10f;
	public static float ConstTorqueValue = 2400f;
	public static float ConstTorqueDuration = 12f;
	public static TestAxle ConstTorqueAxle = TestAxle.Rear;

	public static float DesiredSpeedMs;
	public static float AccelPGain = 2.0f;
	public static float MaxAccel = 4.0f;
	public static float MaxDecel = 6.0f;
	public static float JerkLimit = 6.0f;
	public static float CruiseThreshold = 0.4f;
	public static float CruiseIGain = 0.15f;
	public static float RearTorqueFraction = 0.65f;

	private const float k_HoldBrake  = 600f;
	private const float k_CoastDrag  = 0.05f;

	public float MotorTorque { get; private set; }
	public float BrakeTorque { get; private set; }
	public float CoastDrag   { get; private set; }
	public BrakeSource BrakeSource { get; private set; }

	public DrivePhysics(VehicleData data) { m_Data = data; }

	public void Apply(DriveMode mode, float appliedThrottle, float speedMs, float dt)
	{
		if (ConstTorqueTest)
		{
			m_TestTimer += dt;
			if (m_TestTimer < ConstTorqueDuration)
			{
				MotorTorque = ConstTorqueValue;
				BrakeTorque = 0f; CoastDrag = 0f; BrakeSource = BrakeSource.None;
				return;
			}
			ConstTorqueTest = false;
			Debug.Log($"[ConstTorqueTest] DONE at t={m_TestTimer:F1}s speed={speedMs*3.6f:F0}km/h");
		}

		if (UseDirectForce)
		{
			MotorTorque = 0f; BrakeTorque = 0f; CoastDrag = 0f; BrakeSource = BrakeSource.None;
			return;
		}

		switch (mode)
		{
			case DriveMode.Brake:
				MotorTorque = 0f; BrakeTorque = m_Data.BrakeTorque; CoastDrag = 0f;
				BrakeSource = BrakeSource.PlayerBrake;
				m_SmoothedTorque = 0f; m_SmoothedAccel = 0f; m_CruiseIntegral = 0f;
				return;

			case DriveMode.Hold:
				MotorTorque = 0f; BrakeTorque = k_HoldBrake; CoastDrag = 0f;
				BrakeSource = BrakeSource.Hold;
				m_SmoothedTorque = 0f; m_SmoothedAccel = 0f; m_CruiseIntegral = 0f;
				return;

			case DriveMode.Coast:
				MotorTorque = 0f; BrakeTorque = 0f; CoastDrag = k_CoastDrag;
				BrakeSource = BrakeSource.None;
				m_SmoothedAccel = 0f; m_CruiseIntegral = 0f;
				return;

			default: // Drive
				float targetMs = DesiredSpeedMs > 0f ? DesiredSpeedMs : m_Data.MaxSpeedMs;
				float speedError = targetMs - speedMs;

				float desiredAccel = Mathf.Clamp(speedError * AccelPGain, -MaxDecel, MaxAccel);

				if (Mathf.Abs(speedError) < CruiseThreshold && speedMs > 0.3f)
				{
					m_CruiseIntegral += speedError * CruiseIGain * dt;
					m_CruiseIntegral = Mathf.Clamp(m_CruiseIntegral, -0.5f, MaxAccel * 0.4f);
					desiredAccel = m_CruiseIntegral;
				}
				else
				{
					m_CruiseIntegral = 0f;
				}

				float jerkStep = JerkLimit * dt;
				m_SmoothedAccel = Mathf.MoveTowards(m_SmoothedAccel, desiredAccel, jerkStep);

				float desiredForce = m_SmoothedAccel * m_Data.Mass;

				float speedNorm = Mathf.Clamp01(speedMs / m_Data.MaxSpeedMs);
				float torqueScale = 1f - speedNorm * 0.55f;

				float totalTorque = desiredForce * m_Data.WheelRadius * 0.25f * torqueScale;

				float ramp = m_Data.TorqueRampRate > 0f ? m_Data.TorqueRampRate * dt : 999f;
				m_SmoothedTorque = Mathf.MoveTowards(m_SmoothedTorque, totalTorque, ramp);
				MotorTorque = m_SmoothedTorque;

				RearTorqueFraction = Mathf.Lerp(0.55f, 0.75f, speedNorm);

				BrakeTorque = 0f; CoastDrag = 0f; BrakeSource = BrakeSource.None;
				return;
		}
	}
}
