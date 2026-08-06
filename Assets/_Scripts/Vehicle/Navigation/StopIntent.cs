namespace VehicleNavigation
{
	/// <summary>
	/// Declares why the driver wants to slow or stop. Hard braking is reserved for emergency intents.
	/// </summary>
	public enum StopIntent
	{
		None = 0,
		Goal = 1,
		GearChange = 2,
		SafetyEmergency = 3,
		PlayerEmergency = 4
	}
}
