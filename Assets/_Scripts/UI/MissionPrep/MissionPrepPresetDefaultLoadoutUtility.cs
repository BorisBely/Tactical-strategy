using System.Collections.Generic;
using UnityEngine;

public static class MissionPrepPresetDefaultLoadoutUtility
{
	public static void ApplyToSnapshot(
		MissionPrepPresetSnapshot _snapshot,
		MissionPrepEquipmentPresetCatalog.PresetEntry _entry,
		GrenadeThrowData _grenadeThrowData = null,
		ItemDefinition[] _alwaysIncludeItems = null)
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

		if (_entry.ExtraBagItems != null)
		{
			for (int i = 0; i < _entry.ExtraBagItems.Length; i++)
			{
				ItemDefinition extraItem = _entry.ExtraBagItems[i];
				if (extraItem == null)
					continue;

				bagItems.Add(InventorySlotRuntimeData.FromDefinition(extraItem));
			}
		}

		if (_alwaysIncludeItems != null)
		{
			for (int i = 0; i < _alwaysIncludeItems.Length; i++)
			{
				ItemDefinition item = _alwaysIncludeItems[i];
				if (item == null)
					continue;

				bagItems.Add(InventorySlotRuntimeData.FromDefinition(item));
			}
		}

		if (_grenadeThrowData != null && _grenadeThrowData.ItemMappings != null)
		{
			for (int i = 0; i < _grenadeThrowData.ItemMappings.Count; i++)
			{
				ItemDefinition grenade = _grenadeThrowData.ItemMappings[i].Item;
				if (grenade == null || !grenade.IsGrenade)
					continue;

				bagItems.Add(InventorySlotRuntimeData.FromDefinition(grenade));
				bagItems.Add(InventorySlotRuntimeData.FromDefinition(grenade));
			}
		}

		InventorySlotRuntimeData mainHand = default;
		InventorySlotRuntimeData head = default;
		InventorySlotRuntimeData back = default;
		if (_entry.WeaponItem != null)
		{
			mainHand = InventorySlotRuntimeData.FromDefinition(_entry.WeaponItem);

			WeaponRuntimeState weaponState = mainHand.InstanceState?.WeaponState;
			if (weaponState != null && weaponState.WeaponDefinition != null &&
			    weaponState.WeaponDefinition.UsesShellByShellReload)
			{
				AmmoDefinition ammo = _entry.AmmoForMagazine ?? weaponState.WeaponDefinition.BuiltInMagazineDefaultAmmo;
				WeaponBuiltInMagazineUtility.TryEnsureBuiltInMagazine(
					weaponState,
					ammo,
					_entry.RoundsPerMagazine);
			}
			else if (_entry.PutLoadedMagazineInWeapon &&
			         _entry.MagazineItem != null &&
			         TryBuildLoadedMagazineSlot(
				         _entry.MagazineItem,
				         _entry.AmmoForMagazine,
				         _entry.RoundsPerMagazine,
				         out InventorySlotRuntimeData weaponMagazine))
			{
				if (weaponState != null && weaponState.TryInsertMagazine(weaponMagazine))
					weaponState.TryChamberRoundFromMagazine();
				else
					bagItems.Insert(0, weaponMagazine);
			}
		}

		if (_entry.HeadItem != null)
		{
			InventorySlotRuntimeData headSlot = InventorySlotRuntimeData.FromDefinition(_entry.HeadItem);
			if (HelmetEquipUtility.CanEquipToHead(headSlot))
				head = headSlot;
		}

		if (_entry.BackItem != null)
		{
			InventorySlotRuntimeData backSlot = InventorySlotRuntimeData.FromDefinition(_entry.BackItem);
			if (BackpackEquipUtility.CanEquipToBack(backSlot))
				back = backSlot;
		}

		if (_entry.ExtraHeadItemsInBag != null)
		{
			for (int i = 0; i < _entry.ExtraHeadItemsInBag.Length; i++)
			{
				ItemDefinition headItem = _entry.ExtraHeadItemsInBag[i];
				if (headItem == null)
					continue;

				InventorySlotRuntimeData headSlot = InventorySlotRuntimeData.FromDefinition(headItem);
				if (HelmetEquipUtility.CanEquipToHead(headSlot))
					bagItems.Add(headSlot);
			}
		}

		_snapshot.ReplaceInventory(mainHand, head, back, bagItems);
	}

	public static void ApplyPresetEntryToInventory(
		CharacterInventory _inventory,
		MissionPrepEquipmentPresetCatalog.PresetEntry _entry,
		GrenadeThrowData _grenadeThrowData = null,
		ItemDefinition[] _alwaysIncludeItems = null)
	{
		if (_inventory == null || _entry == null)
			return;

		var snapshot = new MissionPrepPresetSnapshot();
		ApplyToSnapshot(snapshot, _entry, _grenadeThrowData, _alwaysIncludeItems);
		snapshot.ApplyToInventory(_inventory);
	}

	public static bool EntryDefinesInventory(
		MissionPrepEquipmentPresetCatalog.PresetEntry _entry,
		GrenadeThrowData _grenadeThrowData = null,
		ItemDefinition[] _alwaysIncludeItems = null)
	{
		if (_entry == null)
			return false;

		if (_entry.WeaponItem != null)
			return true;

		if (_entry.HeadItem != null)
			return true;

		if (_entry.BackItem != null)
			return true;

		if (_entry.ExtraHeadItemsInBag != null)
		{
			for (int i = 0; i < _entry.ExtraHeadItemsInBag.Length; i++)
			{
				if (_entry.ExtraHeadItemsInBag[i] != null)
					return true;
			}
		}

		if (_entry.MagazineItem != null)
			return true;

		if (_entry.AmmoBoxItems != null)
		{
			for (int i = 0; i < _entry.AmmoBoxItems.Length; i++)
			{
				if (_entry.AmmoBoxItems[i] != null)
					return true;
			}
		}

		if (_entry.ExtraBagItems != null)
		{
			for (int i = 0; i < _entry.ExtraBagItems.Length; i++)
			{
				if (_entry.ExtraBagItems[i] != null)
					return true;
			}
		}

		if (_alwaysIncludeItems != null)
		{
			for (int i = 0; i < _alwaysIncludeItems.Length; i++)
			{
				if (_alwaysIncludeItems[i] != null)
					return true;
			}
		}

		if (_grenadeThrowData != null && _grenadeThrowData.ItemMappings != null)
		{
			for (int i = 0; i < _grenadeThrowData.ItemMappings.Count; i++)
			{
				ItemDefinition item = _grenadeThrowData.ItemMappings[i].Item;
				if (item != null && item.IsGrenade)
					return true;
			}
		}

		return false;
	}

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
}
