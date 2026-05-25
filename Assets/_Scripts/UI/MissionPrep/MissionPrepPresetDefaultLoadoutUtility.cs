using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сборка стартового инвентаря пресета из полей <see cref="MissionPrepEquipmentPresetCatalog.PresetEntry"/>.
/// </summary>
public static class MissionPrepPresetDefaultLoadoutUtility
{
	#region Public Methods
	public static void ApplyToSnapshot(
		MissionPrepPresetSnapshot _snapshot,
		MissionPrepEquipmentPresetCatalog.PresetEntry _entry)
	{
		if (_snapshot == null || _entry == null)
			return;

		_snapshot.SetArmorVisualIndex(_entry.DefaultArmorVisualIndex);

		var bagItems = new List<InventorySlotRuntimeData>();

		if (_entry.MagazineItem != null && !_entry.PutLoadedMagazineInWeapon)
		{
			if (TryBuildLoadedMagazineSlot(
				    _entry.MagazineItem,
				    _entry.AmmoForMagazine,
				    _entry.RoundsPerMagazine,
				    out InventorySlotRuntimeData magazineSlot))
				bagItems.Add(magazineSlot);
		}

		for (int i = 0; i < _entry.SpareLoadedMagazinesInBag; i++)
		{
			if (TryBuildLoadedMagazineSlot(
				    _entry.MagazineItem,
				    _entry.AmmoForMagazine,
				    _entry.RoundsPerMagazine,
				    out InventorySlotRuntimeData spareMagazine))
				bagItems.Add(spareMagazine);
		}

		if (_entry.MagazineItem != null && _entry.MagazineItem.MagazineDefinition != null)
		{
			for (int i = 0; i < _entry.SpareEmptyMagazinesInBag; i++)
				bagItems.Add(InventorySlotRuntimeData.FromDefinition(_entry.MagazineItem));
		}

		if (_entry.AmmoBoxItems != null)
		{
			for (int i = 0; i < _entry.AmmoBoxItems.Length; i++)
			{
				ItemDefinition ammoBox = _entry.AmmoBoxItems[i];
				if (ammoBox == null || ammoBox.AmmoDefinition == null)
					continue;

				bagItems.Add(InventorySlotRuntimeData.FromDefinition(ammoBox));
			}
		}

		InventorySlotRuntimeData mainHand = default;
		if (_entry.WeaponItem != null)
		{
			mainHand = InventorySlotRuntimeData.FromDefinition(_entry.WeaponItem);

			if (_entry.PutLoadedMagazineInWeapon &&
			    _entry.MagazineItem != null &&
			    TryBuildLoadedMagazineSlot(
				    _entry.MagazineItem,
				    _entry.AmmoForMagazine,
				    _entry.RoundsPerMagazine,
				    out InventorySlotRuntimeData weaponMagazine))
			{
				WeaponRuntimeState weaponState = mainHand.InstanceState?.WeaponState;
				if (weaponState != null && weaponState.TryInsertMagazine(weaponMagazine))
					weaponState.TryChamberRoundFromMagazine();
				else
					bagItems.Insert(0, weaponMagazine);
			}
		}

		_snapshot.ReplaceInventory(mainHand, bagItems);
	}

	public static bool EntryDefinesInventory(MissionPrepEquipmentPresetCatalog.PresetEntry _entry)
	{
		if (_entry == null)
			return false;

		if (_entry.WeaponItem != null)
			return true;

		if (_entry.MagazineItem != null)
			return true;

		if (_entry.AmmoBoxItems == null)
			return false;

		for (int i = 0; i < _entry.AmmoBoxItems.Length; i++)
		{
			if (_entry.AmmoBoxItems[i] != null)
				return true;
		}

		return false;
	}
	#endregion

	#region Private Methods
	private static bool TryBuildLoadedMagazineSlot(
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
