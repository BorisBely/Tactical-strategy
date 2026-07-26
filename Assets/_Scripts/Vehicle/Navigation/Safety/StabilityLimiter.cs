using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Stability protections: wheel lift, slip, roll angle, pitch.
	/// These catch the vehicle losing stability BEFORE it fully rolls over.
	/// </summary>
	public sealed class StabilityLimiter : ISafetyLimiter
	{
		private readonly float m_SlipWarning;
		private readonly float m_SlipCritical;
		private readonly float m_RollWarning;
		private readonly float m_RollCritical;
		private readonly float m_RollAbort;
		private readonly float m_PitchWarning;
		private readonly float m_PitchCritical;
		private readonly WheeledMotor m_WheeledMotor;

		public StabilityLimiter(
			WheeledMotor _motor,
			float _slipWarning = 0.15f,
			float _slipCritical = 0.3f,
			float _rollWarning = 20f,
			float _rollCritical = 25f,
			float _rollAbort = 35f,
			float _pitchWarning = 20f,
			float _pitchCritical = 30f)
		{
			m_WheeledMotor = _motor;
			m_SlipWarning = _slipWarning;
			m_SlipCritical = _slipCritical;
			m_RollWarning = _rollWarning;
			m_RollCritical = _rollCritical;
			m_RollAbort = _rollAbort;
			m_PitchWarning = _pitchWarning;
			m_PitchCritical = _pitchCritical;
		}

		public SafetyOutput Apply(SafetyInput _input)
		{
			var output = new SafetyOutput { Command = _input.ProposedCommand };

			// --- Wheel lift ---
			int liftedLeft = 0, liftedRight = 0;
			if (m_WheeledMotor != null && m_WheeledMotor.Axles != null)
			{
				foreach (var axle in m_WheeledMotor.Axles)
				{
					if (axle?.Collider == null) continue;
					if (!axle.Collider.isGrounded)
					{
						if (axle.Collider.transform.localPosition.x < 0f) liftedLeft++;
						else liftedRight++;
					}
				}
			}
			bool oneSideLifted = (liftedLeft >= 2 || liftedRight >= 2);
			if (oneSideLifted)
			{
				output.Command.Throttle *= 0.3f;
				output.Command.Steer *= 0.5f;
				output.Triggered = true;
				output.Warning = $"WheelLift: left={liftedLeft} right={liftedRight} off ground";
				if (_input.IsRecovering)
					output.ShouldAbortRecovery = true;
			}

			// --- Slip ---
			float maxSlip = GetMaxSlip();
			if (maxSlip > m_SlipCritical)
			{
				output.Command.Throttle = 0f;
				output.Command.Steer *= 0.5f;
				output.Triggered = true;
				output.Warning = $"SlipCritical: {maxSlip:F2}>{m_SlipCritical}";
			}
			else if (maxSlip > m_SlipWarning)
			{
				output.Command.Throttle *= 0.5f;
				output.Command.Steer *= 0.8f;
				output.Triggered = true;
				output.Warning = $"SlipWarning: {maxSlip:F2}>{m_SlipWarning}";
			}

			// --- Roll angle (emergency, should rarely trigger) ---
			float roll = Mathf.Abs(NormaliseAngle(_input.EulerAngles.z));
			if (roll > m_RollAbort)
			{
				output.Command.Throttle = 0f;
				output.Command.BrakeMode = VehicleBrakeMode.Hard;
				output.Command.Steer = 0f;
				output.Triggered = true;
				output.Warning = $"RollAbort: {roll:F1}°>{m_RollAbort}";
				output.ShouldAbortRecovery = true;
			}
			else if (roll > m_RollCritical)
			{
				output.Command.Throttle = 0f;
				output.Command.BrakeMode = VehicleBrakeMode.Soft;
				output.Command.Steer *= 0.3f;
				output.Triggered = true;
				output.Warning = $"RollCritical: {roll:F1}°>{m_RollCritical}";
			}
			else if (roll > m_RollWarning)
			{
				output.Command.Throttle *= 0.5f;
				output.Triggered = true;
				output.Warning = $"RollWarning: {roll:F1}°>{m_RollWarning}";
			}

			// --- Pitch ---
			float pitch = Mathf.Abs(NormaliseAngle(_input.EulerAngles.x));
			if (pitch > m_PitchCritical)
			{
				output.Command.Throttle = Mathf.Clamp(output.Command.Throttle * 0.3f, -0.2f, 0.2f);
				output.Command.BrakeMode = VehicleBrakeMode.Soft;
				output.Triggered = true;
				output.Warning = $"PitchCritical: {pitch:F1}°>{m_PitchCritical}";
			}
			else if (pitch > m_PitchWarning)
			{
				output.Command.Throttle *= 0.5f;
				output.Triggered = true;
				output.Warning = $"PitchWarning: {pitch:F1}°>{m_PitchWarning}";
			}

			return output;
		}

		private float GetMaxSlip()
		{
			float max = 0f;
			if (m_WheeledMotor?.Axles == null) return 0f;
			foreach (var axle in m_WheeledMotor.Axles)
			{
				if (axle?.Collider == null) continue;
				if (axle.Collider.GetGroundHit(out WheelHit hit))
					max = Mathf.Max(max, Mathf.Abs(hit.sidewaysSlip));
			}
			return max;
		}

		private static float NormaliseAngle(float _angle)
		{
			while (_angle > 180f) _angle -= 360f;
			while (_angle < -180f) _angle += 360f;
			return _angle;
		}
	}
}
