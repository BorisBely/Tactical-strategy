namespace VehicleNavigation
{
	public sealed class ReverseManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Reverse;

		public ReverseManeuver(float _speedScale = 0.45f)
		{
			AllowReverse = true;
			SpeedScale = _speedScale;
			LookAheadOverride = 2.5f;
		}
	}
}
