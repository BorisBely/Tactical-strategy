namespace VehicleNavigation
{
	public sealed class ParkingManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Parking;

		public float TargetHeadingYaw { get; }

		public ParkingManeuver(float _targetHeadingYaw)
		{
			TargetHeadingYaw = _targetHeadingYaw;
			AllowReverse = true;
			SpeedScale = 0.22f;
			LookAheadOverride = 1.6f;
			IsArrivalManeuver = true;
		}
	}
}
