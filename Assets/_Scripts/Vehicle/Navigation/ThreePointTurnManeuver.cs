namespace VehicleNavigation
{
	public sealed class ThreePointTurnManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.ThreePointTurn;

		public float TurnSign { get; }

		public ThreePointTurnManeuver(float _turnSign)
		{
			TurnSign = _turnSign;
			AllowReverse = true;
			SpeedScale = 0.36f;
			LookAheadOverride = 1.8f;
		}
	}
}
