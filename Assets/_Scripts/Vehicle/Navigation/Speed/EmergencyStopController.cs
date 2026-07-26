using CombatVehicleSystem;

namespace VehicleNavigation
{
	public sealed class EmergencyStopController
	{
		public bool IsActive { get; private set; }
		public StopReason Reason { get; private set; }

		public VehicleCommand EmergencyCommand => new VehicleCommand
		{
			Steer = 0f,
			Throttle = 0f,
			BrakeMode = VehicleBrakeMode.Hard,
			FireHeld = false,
			AimWorldPoint = UnityEngine.Vector3.zero,
			HasAimPoint = false
		};

		public void ActivatePlayerStop()
		{
			IsActive = true;
			Reason = StopReason.Player;
		}

		public void ActivateObstacle()
		{
			IsActive = true;
			Reason = StopReason.Obstacle;
		}

		public void Activate(StopReason _reason)
		{
			IsActive = true;
			Reason = _reason;
		}

		public void Deactivate()
		{
			IsActive = false;
			Reason = StopReason.None;
		}
	}
}
