using System;
using CombatVehicleSystem;

namespace VehicleNavigation
{
	public sealed class CommandSanitizer : ISafetyLimiter
	{
		public SafetyOutput Apply(SafetyInput _input)
		{
			var output = new SafetyOutput { Command = _input.ProposedCommand };

			if (output.Command.BrakeMode == VehicleBrakeMode.Hard && Math.Abs(output.Command.Throttle) > 0.01f)
			{
				output.Command.Throttle = 0f;
				output.Triggered = true;
				output.Warning = "Sanitizer: throttle+hardBrake conflict resolved";
			}

			if (output.Command.HoldPosition && Math.Abs(output.Command.Throttle) > 0.01f)
			{
				output.Command.Throttle = 0f;
				output.Triggered = true;
				output.Warning = "Sanitizer: HoldPosition+throttle conflict resolved";
			}

			if (output.Command.HoldPosition && output.Command.BrakeMode == VehicleBrakeMode.None)
			{
				output.Command.BrakeMode = VehicleBrakeMode.Soft;
			}

			return output;
		}
	}
}
