namespace VehicleNavigation
{
	public interface ISpeedLimiter
	{
		SpeedLimitResult GetLimit(NavigationContext _ctx);
	}
}
