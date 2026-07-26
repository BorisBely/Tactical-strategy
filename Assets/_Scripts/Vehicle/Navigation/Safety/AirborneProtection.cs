using CombatVehicleSystem;

namespace VehicleNavigation
{
	/// <summary>
	/// Cuts throttle and brake while airborne. Prevents gear switching during jumps.
	/// </summary>
	public sealed class AirborneProtection : ISafetyLimiter
	{
		private bool m_WasAirborne;

		public SafetyOutput Apply(SafetyInput _input)
		{
			var output = new SafetyOutput { Command = _input.ProposedCommand };

			bool isAirborne = _input.State.IsAirborne;

			if (isAirborne)
			{
				output.Command.Throttle = 0f;
				output.Command.BrakeMode = VehicleBrakeMode.None;
				m_WasAirborne = true;
				output.Triggered = true;
				output.Warning = "Airborne: throttle/brake zeroed";
			}
			else if (m_WasAirborne)
			{
				m_WasAirborne = false;
				output.Warning = "Airborne: landed, controls restored";
			}

			return output;
		}
	}
}
