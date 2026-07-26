using UnityEngine;

public sealed class EngineModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.EngineSettings m_Settings;
	private float m_RPM;
	private float m_Torque;
	private float m_TargetTorque;
	#endregion

	#region Constructor
	public EngineModel(VehiclePhysicsProfile.EngineSettings settings)
	{
		m_Settings = settings;
		m_RPM = settings.IdleRPM;
	}
	#endregion

	#region Public Properties
	public float RPM => m_RPM;
	public float Torque => m_Torque;
	public float TargetTorque => m_TargetTorque;
	public float RPMNormalized => Mathf.InverseLerp(m_Settings.IdleRPM, m_Settings.MaxRPM, m_RPM);
	#endregion

	#region Public Methods
	public void Tick(float throttle, float loadRPM, float dt)
	{
		throttle = Mathf.Clamp01(throttle);

		m_RPM = Mathf.Lerp(m_RPM, Mathf.Max(m_Settings.IdleRPM, loadRPM), dt * 8f);

		if (m_RPM >= m_Settings.MaxRPM)
		{
			m_Torque = 0f;
			m_TargetTorque = 0f;
			return;
		}

		float curveTorque = m_Settings.TorqueCurve.Evaluate(m_RPM);

		float target;
		if (throttle < 0.01f)
		{
			target = -m_Settings.EngineBrakeTorque;
		}
		else
		{
			target = curveTorque * throttle;
		}

		m_TargetTorque = target;

		float response = m_Settings.ThrottleResponse * dt;
		m_Torque = Mathf.Lerp(m_Torque, target, response);

		float inertiaEffect = m_Settings.EngineInertia > 0.001f
			? (target - m_Torque) / m_Settings.EngineInertia * dt
			: 0f;
		m_RPM += inertiaEffect * 60f;

		m_RPM = Mathf.Clamp(m_RPM, m_Settings.IdleRPM * 0.5f, m_Settings.MaxRPM);
	}

	public void SetLoadRPM(float driveshaftRPM, float currentGearRatio, float finalDrive)
	{
		float ratio = Mathf.Abs(currentGearRatio) > 0.001f ? currentGearRatio : 1f;
		float loadRPM = driveshaftRPM * ratio * finalDrive;
		m_RPM = Mathf.Lerp(m_RPM, Mathf.Max(m_Settings.IdleRPM, Mathf.Abs(loadRPM)), 0.3f);
	}
	#endregion
}
