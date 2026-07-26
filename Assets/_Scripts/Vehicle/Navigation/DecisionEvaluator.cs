using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Validates the DrivingPlanner's mode choice against local geometry and recent memory.
	/// Overrides the mode when it would put the vehicle into a dead end.
	/// </summary>
	public sealed class DecisionEvaluator
	{
		public VehicleDrivingMode ChooseSafeMode(
			VehicleDrivingMode _proposed,
			float _flatToDestination,
			float _turnRadius,
			VehicleLocalGeometry.Sample _geometry,
			VehicleDriverMemory _memory)
		{
			bool frontBlocked = _geometry.FrontClearance < _turnRadius * 0.6f;
			bool backBlocked = _geometry.RearClearance < _turnRadius * 0.4f;
			bool reversingRepeatedly = _memory != null && _memory.HasToggledGearRecently(2, 4f);

			if (_proposed == VehicleDrivingMode.Forward && frontBlocked && !backBlocked)
			{
				// Instead of ramming, reverse a bit then turn around.
				return VehicleDrivingMode.Reverse;
			}

			if (_proposed == VehicleDrivingMode.Reverse && backBlocked)
			{
				// Cannot reverse safely — try a forward turn-around.
				return VehicleDrivingMode.TurnAround;
			}

			if (_proposed == VehicleDrivingMode.TurnAround && frontBlocked && !backBlocked)
			{
				// No room to swing forward; reverse first.
				return VehicleDrivingMode.Reverse;
			}

			if (reversingRepeatedly && _proposed == VehicleDrivingMode.TurnAround && _flatToDestination < 18f)
			{
				// Repeated gear toggles on close destination — just reverse into position.
				return VehicleDrivingMode.Reverse;
			}

			return _proposed;
		}
	}
}
