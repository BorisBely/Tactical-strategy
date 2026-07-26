using CombatVehicleSystem;

namespace VehicleNavigation
{
	/// <summary>
	/// Aborts recovery when vehicle is in a dangerous attitude.
	/// Recovery (reverse+gas+steer) often makes things worse when already tilted.
	/// </summary>
	public sealed class RecoveryProtection : ISafetyLimiter
	{
		private const float c_MaxRollForRecovery = 30f;
		private const float c_MaxPitchForRecovery = 35f;

		public SafetyOutput Apply(SafetyInput _input)
		{
			var output = new SafetyOutput { Command = _input.ProposedCommand };

			if (!_input.IsRecovering)
				return output;

			float roll = NormaliseAngle(_input.EulerAngles.z);
			float pitch = NormaliseAngle(_input.EulerAngles.x);

			if (System.Math.Abs(roll) > c_MaxRollForRecovery ||
			    System.Math.Abs(pitch) > c_MaxPitchForRecovery ||
			    _input.State.IsAirborne)
			{
				output.Command.Throttle = 0f;
				output.Command.Steer = 0f;
				output.Command.BrakeMode = VehicleBrakeMode.Soft;
				output.ShouldAbortRecovery = true;
				output.Triggered = true;
				output.Warning = $"RecoveryAbort: roll={roll:F1}° pitch={pitch:F1}° airborne={_input.State.IsAirborne}";
			}

			return output;
		}

		private static float NormaliseAngle(float _a)
		{
			while (_a > 180f) _a -= 360f;
			while (_a < -180f) _a += 360f;
			return _a;
		}
	}
}
