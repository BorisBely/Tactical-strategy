using System.Collections.Generic;

/// <summary>
/// Категории фильтра панели «доступное снаряжение» (без «Все»).
/// </summary>
public enum MissionPrepAvailableEquipmentFilterCategory : byte
{
	Weapons = 0,
	Mods = 1,
	Ammo = 2,
	Equipment = 3,
	Extra = 4
}

/// <summary>
/// Классификация предметов каталога доступного снаряжения по фильтр-категориям.
/// </summary>
public static class MissionPrepAvailableEquipmentFilterClassifier
{
	public static MissionPrepAvailableEquipmentFilterCategory GetCategory(ItemDefinition _definition)
	{
		if (_definition == null)
			return MissionPrepAvailableEquipmentFilterCategory.Extra;

		if (_definition.WeaponDefinition != null || _definition.IsRocketLauncher)
			return MissionPrepAvailableEquipmentFilterCategory.Weapons;

		if (_definition.WeaponAttachmentDefinition != null)
			return MissionPrepAvailableEquipmentFilterCategory.Mods;

		if (_definition.MagazineDefinition != null ||
		    _definition.AmmoDefinition != null ||
		    _definition.IsRpgRocketAmmo)
			return MissionPrepAvailableEquipmentFilterCategory.Ammo;

		if (_definition.IsEquipment &&
		    (_definition.EquipmentKind == EquipmentKind.Helmet ||
		     _definition.EquipmentKind == EquipmentKind.Backpack))
			return MissionPrepAvailableEquipmentFilterCategory.Equipment;

		return MissionPrepAvailableEquipmentFilterCategory.Extra;
	}

	public static bool Matches(ItemDefinition _definition, MissionPrepAvailableEquipmentFilterCategory _category)
	{
		return GetCategory(_definition) == _category;
	}

	public static void FilterInPlace(
		List<InventorySlotRuntimeData> _slots,
		MissionPrepAvailableEquipmentFilterCategory _category)
	{
		if (_slots == null || _slots.Count == 0)
			return;

		for (int i = _slots.Count - 1; i >= 0; i--)
		{
			if (!Matches(_slots[i].Definition, _category))
				_slots.RemoveAt(i);
		}

		SortByGroup(_slots);
	}

	private static void SortByGroup(List<InventorySlotRuntimeData> _slots)
	{
		if (_slots == null || _slots.Count <= 1)
			return;

		_slots.Sort(MissionPrepAvailableEquipmentGroupClassifier.CompareSlots);
	}

	public static string GetLocalizationKey(MissionPrepAvailableEquipmentFilterCategory _category)
	{
		return _category switch
		{
			MissionPrepAvailableEquipmentFilterCategory.Weapons => "mission_prep.equipment.filter.weapons",
			MissionPrepAvailableEquipmentFilterCategory.Mods => "mission_prep.equipment.filter.mods",
			MissionPrepAvailableEquipmentFilterCategory.Ammo => "mission_prep.equipment.filter.ammo",
			MissionPrepAvailableEquipmentFilterCategory.Equipment => "mission_prep.equipment.filter.equipment",
			MissionPrepAvailableEquipmentFilterCategory.Extra => "mission_prep.equipment.filter.extra",
			_ => "mission_prep.equipment.filter.extra"
		};
	}
}
