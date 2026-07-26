using UnityEngine;

public enum BrakeSource { None, PlayerBrake, Hold }
public enum DriveMode { Drive, Brake, Coast, Hold }

public class VehicleEngine
{
	private readonly VehicleData m_Data;
	private readonly InputFilter m_ThrottleFilter;
	private readonly InputFilter m_SteerFilter;
	private readonly EngineFSM m_FSM;
	private readonly DrivePhysics m_Physics;

	public float MotorTorque     => m_Physics.MotorTorque;
	public float BrakeTorque     => m_Physics.BrakeTorque;
	public float SteerAngle      { get; private set; }
	public float TargetThrottle  { get; private set; }
	public float AppliedThrottle => m_ThrottleFilter.Value;
	public BrakeSource BrakeSource => m_Physics.BrakeSource;
	public DriveMode Mode        => m_FSM.Current;
	public DriveMode DesiredMode => m_FSM.Pending;
	public float ModeTimer       => m_FSM.TransitionTimer;
	public float CoastDrag       => m_Physics.CoastDrag;

	public VehicleEngine(VehicleData data)
	{
		m_Data = data;
		m_ThrottleFilter = new InputFilter(2.5f, 5.0f);
		m_SteerFilter = new InputFilter(3.0f, 4.5f);
		m_FSM = new EngineFSM(0.02f, 0.08f, 0.25f, 0.5f, 0.3f);
		m_Physics = new DrivePhysics(data);
	}

	public void Update(DriveCommand cmd, float dt, float speedMs)
	{
		float raw = Mathf.Clamp(cmd.Throttle, -1f, 1f);
		TargetThrottle = raw;

		// Steer
		float tgtSteer = cmd.Steer * m_Data.SteerAngleMax;
		m_SteerFilter.Update(tgtSteer, dt);
		SteerAngle = m_SteerFilter.Value;

		// 1) Smooth throttle — independent of mode
		m_ThrottleFilter.Update(raw, dt);

		// 2) Mode FSM — only uses raw throttle + speed + brake
		m_FSM.Update(cmd.Brake, raw, speedMs, dt);

		// 3) Physics — torque/brake/drag per mode
		m_Physics.Apply(m_FSM.Current, m_ThrottleFilter.Value, speedMs, dt);
	}
}
