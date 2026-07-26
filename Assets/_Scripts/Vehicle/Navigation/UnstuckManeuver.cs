namespace VehicleNavigation
{
	public sealed class UnstuckManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Unstuck;

		public float SteerSign { get; }

		public UnstuckManeuver(float _steerSign)
		{
			SteerSign = _steerSign;
			AllowReverse = true;
			SpeedScale = 0.35f;
			LookAheadOverride = 1.5f;
		}
	}
}
