namespace VehicleNavigation
{
	public sealed class StopManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Stop;

		public StopManeuver()
		{
			AllowReverse = false;
			SpeedScale = 0f;
		}

		public override bool IsComplete(ManeuverContext _ctx)
		{
			return _ctx.SpeedKmh <= 0.15f;
		}
	}
}
