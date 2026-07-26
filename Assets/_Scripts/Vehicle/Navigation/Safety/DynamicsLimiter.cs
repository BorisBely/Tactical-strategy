using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Dynamic limits: lateral acceleration + steering rate.
	/// RollLimiter prevents rollover by limiting speed based on lateral G-force.
	/// SteeringRateLimiter prevents jerk in steering changes.
	/// </summary>
	public sealed class DynamicsLimiter : ISafetyLimiter
	{
		private readonly float m_MaxLateralAccel;  // m/s²
		private readonly float m_MaxSteerRate;      // normalised units/sec
		private float m_PrevSteer;
		private bool m_HasPrevSteer;

		public DynamicsLimiter(float _maxLateralAccel = 6f, float _maxSteerRate = 1.2f)
		{
			m_MaxLateralAccel = _maxLateralAccel;
			m_MaxSteerRate = _maxSteerRate;
		}

		public SafetyOutput Apply(SafetyInput _input)
		{
			var output = new SafetyOutput { Command = _input.ProposedCommand };
			float speedKmh = Mathf.Max(0.5f, _input.State.SpeedKmh);
			float speedMs = speedKmh / 3.6f;
			float wheelBase = _input.Params.WheelBase;

			// --- RollLimiter: a = V²/R, limit speed ---
			float absSteer = Mathf.Abs(_input.ProposedCommand.Steer);
			if (absSteer > 0.01f && speedMs > 0.5f)
			{
				float maxSteerRad = _input.Params.MaxSteeringAngleRad * absSteer;
				float turnRadius = Mathf.Abs(wheelBase / Mathf.Tan(maxSteerRad));
				float lateralAccel = (speedMs * speedMs) / Mathf.Max(0.5f, turnRadius);
				if (lateralAccel > m_MaxLateralAccel)
				{
					float safeSpeedMs = Mathf.Sqrt(m_MaxLateralAccel * turnRadius);
					float safeSpeedKmh = safeSpeedMs * 3.6f;
					float ratio = safeSpeedKmh / speedKmh;
					output.Command.Throttle = Mathf.Clamp(output.Command.Throttle * ratio, -1f, 1f);
					output.Triggered = true;
					output.Warning = $"RollLimit: lat={lateralAccel:F1}>{m_MaxLateralAccel} safe={safeSpeedKmh:F1}kmh";
				}
			}

			// --- SteeringRateLimiter ---
			float steerTarget = output.Command.Steer;
			if (m_HasPrevSteer)
			{
				float maxDelta = m_MaxSteerRate * _input.DeltaTime;
				float delta = steerTarget - m_PrevSteer;
				if (Mathf.Abs(delta) > maxDelta)
				{
					output.Command.Steer = m_PrevSteer + Mathf.Sign(delta) * maxDelta;
					output.Triggered = output.Triggered || Mathf.Abs(delta) > maxDelta * 2f;
				}
			}
			m_PrevSteer = output.Command.Steer;
			m_HasPrevSteer = true;

			return output;
		}
	}
}
