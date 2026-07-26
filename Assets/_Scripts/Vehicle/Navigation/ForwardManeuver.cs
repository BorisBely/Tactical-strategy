namespace VehicleNavigation
{
	public sealed class ForwardManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Forward;

		public ForwardManeuver(float _speedScale = 1f)
		{
			AllowReverse = false;
			SpeedScale = _speedScale;
		}
	}
}
