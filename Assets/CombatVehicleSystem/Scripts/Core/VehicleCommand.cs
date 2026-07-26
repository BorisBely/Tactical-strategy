using UnityEngine;

namespace CombatVehicleSystem
{
	/// <summary>
	/// External control packet. Filled by TPS / AI each frame; never read from Input inside the package.
	/// </summary>
	[System.Serializable]
	public struct VehicleCommand
	{
		#region Public Fields
		[Range(-1f, 1f)] public float Steer;
		[Range(-1f, 1f)] public float Throttle;
		public VehicleBrakeMode BrakeMode;
		public bool FireHeld;
		public Vector3 AimWorldPoint;
		public bool HasAimPoint;
		#endregion

		#region Public Properties
		/// <summary>
		/// Legacy binary brake. Maps to Hard when set true; clears to None when false.
		/// </summary>
		public bool Brake
		{
			get => BrakeMode != VehicleBrakeMode.None;
			set => BrakeMode = value ? VehicleBrakeMode.Hard : VehicleBrakeMode.None;
		}
		#endregion

		#region Public Methods
		public static VehicleCommand Idle => new VehicleCommand
		{
			Steer = 0f,
			Throttle = 0f,
			// None while airborne / pre-contact: Soft on air wheels still spins Unity 6 WC hulls.
			// SoftPark is applied only after CountGroundedWheels > 0.
			BrakeMode = VehicleBrakeMode.None,
			FireHeld = false,
			AimWorldPoint = Vector3.zero,
			HasAimPoint = false
		};

		public static VehicleCommand SoftPark => new VehicleCommand
		{
			Steer = 0f,
			Throttle = 0f,
			BrakeMode = VehicleBrakeMode.Soft,
			FireHeld = false,
			AimWorldPoint = Vector3.zero,
			HasAimPoint = false
		};
		#endregion
	}
}
