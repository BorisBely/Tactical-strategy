using UnityEngine;

public sealed class StabilityController
{
	#region Nested Types
	public enum Level { Inactive, Safety, Recovery }
	public enum SafetyAction { None, AntiRoll, TractionControl, YawStability }
	public enum RecoveryAction { None, AntiFlip, AirborneStabilization, BounceSuppression }
	#endregion

	#region Private Fields
	private readonly VehiclePhysicsProfile.StabilitySettings m_Settings;
	private Rigidbody m_Body;
	private Transform m_Transform;
	private float m_AirborneTime;
	private float m_BounceCooldown;
	private Level m_ActiveLevel = Level.Inactive;
	private SafetyAction m_ActiveSafety = SafetyAction.None;
	private RecoveryAction m_ActiveRecovery = RecoveryAction.None;
	private int m_NumericalGuardTrips;
	#endregion

	#region Constructor
	public StabilityController(VehiclePhysicsProfile.StabilitySettings settings)
	{
		m_Settings = settings;
	}
	#endregion

	#region Public Properties
	public Level ActiveLevel => m_ActiveLevel;
	public SafetyAction ActiveSafety => m_ActiveSafety;
	public RecoveryAction ActiveRecovery => m_ActiveRecovery;
	public int NumericalGuardTrips => m_NumericalGuardTrips;
	public float AirborneTime => m_AirborneTime;
	#endregion

	#region Public Methods
	public void Bind(Rigidbody body, Transform transform)
	{
		m_Body = body;
		m_Transform = transform;
	}

	public void Tick(IWheelInterface[] wheels, float dt)
	{
		if (m_Body == null)
			return;

		m_BounceCooldown -= dt;

		bool anyGrounded = false;
		int groundedCount = 0;

		if (wheels != null)
		{
			for (int i = 0; i < wheels.Length; i++)
			{
				if (wheels[i] != null && wheels[i].IsGrounded)
				{
					anyGrounded = true;
					groundedCount++;
				}
			}
		}

		if (!anyGrounded)
		{
			m_AirborneTime += dt;
		}
		else
		{
			m_AirborneTime = 0f;
		}

		m_ActiveLevel = Level.Inactive;
		m_ActiveSafety = SafetyAction.None;
		m_ActiveRecovery = RecoveryAction.None;

		NumericalGuard(dt);

		if (m_Settings.NumericalGuardEnabled)
			ApplySafety(dt);

		ApplyRecovery(wheels, groundedCount, dt);
	}
	#endregion

	#region Private Methods
	private void NumericalGuard(float dt)
	{
		Vector3 vel = m_Body.linearVelocity;
		Vector3 angVel = m_Body.angularVelocity;

		bool hadNaN = false;

		if (float.IsNaN(vel.x) || float.IsNaN(vel.y) || float.IsNaN(vel.z) ||
		    float.IsInfinity(vel.x) || float.IsInfinity(vel.y) || float.IsInfinity(vel.z))
		{
			m_Body.linearVelocity = Vector3.zero;
			hadNaN = true;
		}

		if (float.IsNaN(angVel.x) || float.IsNaN(angVel.y) || float.IsNaN(angVel.z) ||
		    float.IsInfinity(angVel.x) || float.IsInfinity(angVel.y) || float.IsInfinity(angVel.z))
		{
			m_Body.angularVelocity = Vector3.zero;
			hadNaN = true;
		}

		if (vel.magnitude > 300f)
		{
			m_Body.linearVelocity = vel.normalized * 300f;
			hadNaN = true;
		}

		if (angVel.magnitude > 50f)
		{
			m_Body.angularVelocity = angVel.normalized * 50f;
			hadNaN = true;
		}

		if (hadNaN)
		{
			m_NumericalGuardTrips++;
			m_ActiveLevel = Level.Recovery;
		}
	}

	private void ApplySafety(float dt)
	{
		float angSpeed = m_Body.angularVelocity.magnitude * Mathf.Rad2Deg;

		if (angSpeed > m_Settings.MaxAngularSpeed)
		{
			float excess = (angSpeed - m_Settings.MaxAngularSpeed) / Mathf.Max(1f, m_Settings.MaxAngularSpeed);
			float dampFactor = 1f - Mathf.Clamp01(excess) * 0.5f;
			m_Body.angularVelocity *= Mathf.Lerp(1f, dampFactor, dt * 10f);
			m_ActiveLevel = Level.Safety;
			m_ActiveSafety = SafetyAction.YawStability;
		}
	}

	private void ApplyRecovery(IWheelInterface[] wheels, int groundedCount, float dt)
	{
		if (m_Transform == null)
			return;

		// anti-flip: если крен > 60°
		Vector3 up = m_Transform.up;
		float rollAngle = Vector3.Angle(up, Vector3.up);

		if (rollAngle > 60f && m_Settings.AntiFlipTorque > 0f)
		{
			Vector3 torqueAxis = Vector3.Cross(up, Vector3.up).normalized;
			float flipFactor = Mathf.Clamp01((rollAngle - 60f) / 30f);
			m_Body.AddTorque(torqueAxis * m_Settings.AntiFlipTorque * flipFactor, ForceMode.Force);
			m_ActiveLevel = Level.Recovery;
			m_ActiveRecovery = RecoveryAction.AntiFlip;
		}

		// airborne stabilization
		if (m_AirborneTime > m_Settings.MaxAirborneTime && groundedCount == 0)
		{
			m_Body.angularVelocity *= Mathf.Exp(-2f * dt);
			m_ActiveLevel = Level.Recovery;
			m_ActiveRecovery = RecoveryAction.AirborneStabilization;
		}

		// bounce suppression
		if (m_Body.linearVelocity.y < -5f && groundedCount > 0 && m_BounceCooldown <= 0f)
		{
			m_Body.linearVelocity = new Vector3(
				m_Body.linearVelocity.x,
				m_Body.linearVelocity.y * 0.5f,
				m_Body.linearVelocity.z);
			m_BounceCooldown = 0.5f;
			m_ActiveLevel = Level.Recovery;
			m_ActiveRecovery = RecoveryAction.BounceSuppression;
		}
	}
	#endregion
}
