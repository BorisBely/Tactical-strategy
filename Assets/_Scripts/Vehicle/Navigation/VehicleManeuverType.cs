namespace VehicleNavigation
{
	/// <summary>High-level maneuver type produced by the driving planner.</summary>
	public enum VehicleManeuverType
	{
		Forward,
		Reverse,
		TurnAround,
		ThreePointTurn,
		Parking,
		ApproachWithHeading,
		Arrival,
		Unstuck,
		Stop
	}
}
