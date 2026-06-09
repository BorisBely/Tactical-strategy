using UnityEngine;

/// <summary>
/// Сборка слотов магазинов с патронами для стартового инвентаря и спавна.
/// </summary>
public static class InventoryLoadedMagazineUtility
{
	#region Public Methods
	public static bool IsMagazineDefinition(ItemDefinition _definition)
	{
		return _definition != null && _definition.MagazineDefinition != null;
	}

	public static bool TryBuildLoadedMagazineSlot(
		ItemDefinition _magazineItem,
		AmmoDefinition _ammo,
		int _roundsPerMagazine,
		out InventorySlotRuntimeData _slot)
	{
		_slot = default;

		if (_magazineItem == null || _magazineItem.MagazineDefinition == null)
			return false;

		_slot = InventorySlotRuntimeData.FromDefinition(_magazineItem);

		int rounds = ResolveRoundsPerMagazine(_magazineItem.MagazineDefinition, _roundsPerMagazine);
		if (_ammo == null || rounds <= 0 || !MagazineCanHoldAmmo(_magazineItem.MagazineDefinition, _ammo))
			return true;

		MagazineRuntimeState magazineState = _slot.InstanceState?.MagazineState;
		if (magazineState != null)
			magazineState.Configure(_magazineItem.MagazineDefinition, _ammo, rounds);

		return true;
	}
	#endregion

	#region Private Methods
	private static int ResolveRoundsPerMagazine(MagazineDefinition _magazine, int _roundsPerMagazine)
	{
		if (_magazine == null)
			return 0;

		if (_roundsPerMagazine < 0)
			return _magazine.Capacity;

		return Mathf.Clamp(_roundsPerMagazine, 0, _magazine.Capacity);
	}

	private static bool MagazineCanHoldAmmo(MagazineDefinition _magazine, AmmoDefinition _ammo)
	{
		if (_magazine == null || _ammo == null)
			return false;

		if (_magazine.SupportedCaliber == CaliberType.None)
			return true;

		return _ammo.Caliber == _magazine.SupportedCaliber;
	}
	#endregion
}
