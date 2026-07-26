using UnityEngine;

namespace CombatVehicleSystem
{
	public enum DrivingPhase
	{
		Cruise,
		Precision,
		Parking,
		Recovery
	}

	[System.Serializable]
	public struct VehicleCommand
	{
		[Range(-1f, 1f)] public float Steer;
		[Range(-1f, 1f)] public float Throttle;
		public VehicleBrakeMode BrakeMode;
		public bool FireHeld;
		public Vector3 AimWorldPoint;
		public bool HasAimPoint;

		public bool HoldPosition;
		public DrivingPhase Phase;

		public bool Brake
		{
			get => BrakeMode != VehicleBrakeMode.None;
			set => BrakeMode = value ? VehicleBrakeMode.Hard : VehicleBrakeMode.None;
		}

		public static VehicleCommand Idle => new VehicleCommand
		{
			Steer = 0f,
			Throttle = 0f,
			BrakeMode = VehicleBrakeMode.None,
			FireHeld = false,
			AimWorldPoint = Vector3.zero,
			HasAimPoint = false,
			HoldPosition = false,
			Phase = DrivingPhase.Cruise
		};

		public static VehicleCommand SoftPark => new VehicleCommand
		{
			Steer = 0f,
			Throttle = 0f,
			BrakeMode = VehicleBrakeMode.Soft,
			FireHeld = false,
			AimWorldPoint = Vector3.zero,
			HasAimPoint = false,
			HoldPosition = false,
			Phase = DrivingPhase.Parking
		};
	}
}
