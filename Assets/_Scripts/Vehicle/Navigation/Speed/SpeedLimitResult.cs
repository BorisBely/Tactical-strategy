namespace VehicleNavigation
{
	public readonly struct SpeedLimitResult
	{
		public readonly float SpeedKmh;
		public readonly StopReason Reason;
		public readonly int Priority;
		public readonly bool IsHardLimit;

		public SpeedLimitResult(float _speedKmh, StopReason _reason, int _priority, bool _isHardLimit)
		{
			SpeedKmh = _speedKmh;
			Reason = _reason;
			Priority = _priority;
			IsHardLimit = _isHardLimit;
		}

		public static SpeedLimitResult Unlimited => new SpeedLimitResult(999f, StopReason.None, 0, false);
	}
}
