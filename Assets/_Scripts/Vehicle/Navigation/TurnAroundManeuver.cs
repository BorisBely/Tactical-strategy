namespace VehicleNavigation
{
	public sealed class TurnAroundManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.TurnAround;

		/// <summary>-1 = left, +1 = right.</summary>
		public float TurnSign { get; }

		public TurnAroundManeuver(float _turnSign)
		{
			TurnSign = _turnSign;
			AllowReverse = false;
			SpeedScale = 0.8f;
			LookAheadOverride = 1.8f;
		}
	}
}
