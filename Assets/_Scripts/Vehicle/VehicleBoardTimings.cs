/// <summary>Длительности фаз посадки/высадки (секунды).</summary>
public static class VehicleBoardTimings
{
	public const float LivingBoardSeconds = 2.2f;
	public const float DriverBoardSeconds = 2.6f;
	public const float GunnerBoardSeconds = 2.8f;
	public const float WoundedLoadSeconds = 5.0f;
	public const float VictimMountAfterLoadSeconds = 0.5f;
	public const float LivingDisembarkSeconds = 1.7f;
	public const float WoundedDisembarkSeconds = 3.8f;
	public const float DoorTurnoverPauseSeconds = 0.35f;

	public static float GetBoardSeconds(VehicleSeatId _seatId, bool _isUnconscious)
	{
		if (_isUnconscious)
			return VictimMountAfterLoadSeconds;

		switch (_seatId)
		{
			case VehicleSeatId.Driver:
				return DriverBoardSeconds;
			case VehicleSeatId.Gunner:
				return GunnerBoardSeconds;
			default:
				return LivingBoardSeconds;
		}
	}

	public static float GetDisembarkSeconds(VehicleSeatId _seatId, bool _isUnconscious)
	{
		if (_isUnconscious || VehicleSeatLayout.IsLitterSeat(_seatId))
			return WoundedDisembarkSeconds;

		return LivingDisembarkSeconds;
	}
}
