namespace VehicleNavigation
{
	/// <summary>
	/// Short post-turn alignment maneuver: straightens the vehicle after a U-turn
	/// and hands off to PrecisionArrivalController. Max travel distance ~2 m.
	/// </summary>
	public sealed class PostTurnAlignmentManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.PostTurnAlignment;

		public PostTurnAlignmentManeuver()
		{
			AllowReverse = false;
			SpeedScale = 0.28f;
			LookAheadOverride = 1.0f;
			IsArrivalManeuver = true;
		}
	}
}
