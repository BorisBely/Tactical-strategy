/// <summary>Локализованные подписи мест машины для Mission Prep.</summary>
public static class MissionPrepVehicleSeatLabels
{
	public static string GetSeatLocalizationKey(VehicleSeatId _seatId)
	{
		switch (_seatId)
		{
			case VehicleSeatId.Driver:
				return "mission_prep.vehicle.seat.driver";
			case VehicleSeatId.Commander:
				return "mission_prep.vehicle.seat.commander";
			case VehicleSeatId.Gunner:
				return "mission_prep.vehicle.seat.gunner";
			case VehicleSeatId.RearLeft:
				return "mission_prep.vehicle.seat.rear_left";
			case VehicleSeatId.RearCenter:
				return "mission_prep.vehicle.seat.rear_center";
			case VehicleSeatId.RearRight:
				return "mission_prep.vehicle.seat.rear_right";
			case VehicleSeatId.Litter1:
				return "mission_prep.vehicle.seat.litter1";
			case VehicleSeatId.Litter2:
				return "mission_prep.vehicle.seat.litter2";
			default:
				return string.Empty;
		}
	}

	public static string GetSeatFallbackLabel(VehicleSeatId _seatId)
	{
		switch (_seatId)
		{
			case VehicleSeatId.Driver:
				return "Водитель";
			case VehicleSeatId.Commander:
				return "Командир";
			case VehicleSeatId.Gunner:
				return "Стрелок";
			case VehicleSeatId.RearLeft:
				return "Зад. левый";
			case VehicleSeatId.RearCenter:
				return "Зад. центр";
			case VehicleSeatId.RearRight:
				return "Зад. правый";
			case VehicleSeatId.Litter1:
				return "Носилки 1";
			case VehicleSeatId.Litter2:
				return "Носилки 2";
			default:
				return _seatId.ToString();
		}
	}

	public static string GetSeatLabel(VehicleSeatId _seatId)
	{
		string key = GetSeatLocalizationKey(_seatId);
		string fallback = GetSeatFallbackLabel(_seatId);
		return string.IsNullOrEmpty(key)
			? fallback
			: LocalizationManager.Get(key, fallback);
	}

	public static string GetEmptyLabel()
	{
		return LocalizationManager.Get("mission_prep.vehicle.seat.empty", "Пусто");
	}
}
