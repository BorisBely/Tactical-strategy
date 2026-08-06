namespace VehicleNavigation
{
	public enum NavigationOutcome
	{
		None,
		InProgress,
		Succeeded,
		NoPath,
		NoFeasibleManeuver,
		Stuck,
		Cancelled
	}
}
