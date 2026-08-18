using UnityEngine;

/// <summary>Привязка данных машины к <see cref="MissionPrepUnitCellView"/>.</summary>
public static class VehicleCellDisplayBinder
{
	public static void Apply(MissionPrepUnitCellView _cell, VehicleController _vehicle)
	{
		if (_cell == null)
			return;

		if (_vehicle == null)
		{
			_cell.ClearBinding();
			return;
		}

		_cell.BindToVehicle(_vehicle, ResolveVehicleName(_vehicle));
		_cell.SetRankDisplayName(LocalizationManager.Get("mission_prep.vehicle.kind", "Машина"));
		_cell.SetPresetDisplayName(ResolveSeatSummary(_vehicle));
		_cell.SetHealthStatusText(string.Empty);
		_cell.SetArmorStatusText(string.Empty);
	}

	public static string ResolveVehicleName(VehicleController _vehicle)
	{
		if (_vehicle == null)
			return string.Empty;

		string name = _vehicle.name;
		if (name.EndsWith("(Clone)"))
			name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
		return name;
	}

	public static string ResolveSeatSummary(VehicleController _vehicle)
	{
		if (_vehicle == null || _vehicle.Seats == null)
			return string.Empty;

		var seats = new System.Collections.Generic.List<VehicleSeatLayout.SeatBinding>(8);
		_vehicle.Seats.CollectConfiguredBoardingSeats(seats);
		string template = LocalizationManager.Get("mission_prep.vehicle.seats_count", "{0} мест");
		return string.Format(template, seats.Count);
	}
}
