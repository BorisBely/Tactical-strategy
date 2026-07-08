using System.Collections.Generic;

public enum MagazineCaliberVisualPreference
{
	Undefined = 0,
	Five56 = 1,
	Ak = 2
}

/// <summary>
/// Определяет визуальный тип магазинных подсумков по магазинам и оружию юнита.
/// </summary>
public static class MagazineCaliberPreferenceResolver
{
	#region Public Methods
	public static MagazineCaliberVisualPreference Resolve(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return MagazineCaliberVisualPreference.Undefined;

		int five56Count = 0;
		int akCount = 0;
		CountMagazineSlot(_inventory.MainHandEquipment, ref five56Count, ref akCount);

		IReadOnlyList<InventorySlotRuntimeData> bagItems = _inventory.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
			CountMagazineSlot(bagItems[i], ref five56Count, ref akCount);

		return ResolveFromCounts(five56Count, akCount, _inventory.MainHandEquipment);
	}

	public static MagazineCaliberVisualPreference Resolve(MissionPrepPresetSnapshot _snapshot)
	{
		if (_snapshot == null)
			return MagazineCaliberVisualPreference.Undefined;

		int five56Count = 0;
		int akCount = 0;
		CountMagazineSlot(_snapshot.MainHandEquipment, ref five56Count, ref akCount);

		IReadOnlyList<InventorySlotRuntimeData> bagItems = _snapshot.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
			CountMagazineSlot(bagItems[i], ref five56Count, ref akCount);

		return ResolveFromCounts(five56Count, akCount, _snapshot.MainHandEquipment);
	}
	#endregion

	#region Private Methods
	private static void CountMagazineSlot(InventorySlotRuntimeData _slot, ref int _five56Count, ref int _akCount)
	{
		ItemDefinition definition = _slot.Definition;
		if (definition == null)
			return;

		if (definition.MagazineDefinition != null)
		{
			CountCaliber(definition.MagazineDefinition.SupportedCaliber, ref _five56Count, ref _akCount);
			return;
		}

		ItemDefinition insertedMagazine = _slot.InstanceState?.WeaponState?.InsertedMagazineDefinition;
		if (insertedMagazine != null && insertedMagazine.MagazineDefinition != null)
			CountCaliber(insertedMagazine.MagazineDefinition.SupportedCaliber, ref _five56Count, ref _akCount);
	}

	private static void CountCaliber(CaliberType _caliber, ref int _five56Count, ref int _akCount)
	{
		switch (_caliber)
		{
			case CaliberType.Five56By45:
				_five56Count++;
				break;
			case CaliberType.Five45By39:
			case CaliberType.Seven62By39:
			case CaliberType.Seven62By51:
			case CaliberType.Seven62By54R:
				_akCount++;
				break;
		}
	}

	private static MagazineCaliberVisualPreference ResolveFromCounts(
		int _five56Count,
		int _akCount,
		InventorySlotRuntimeData _mainHand)
	{
		if (_five56Count > _akCount)
			return MagazineCaliberVisualPreference.Five56;

		if (_akCount > _five56Count)
			return MagazineCaliberVisualPreference.Ak;

		WeaponDefinition weapon = _mainHand.Definition != null ? _mainHand.Definition.WeaponDefinition : null;
		if (weapon == null)
			return MagazineCaliberVisualPreference.Undefined;

		return weapon.SupportedCaliber switch
		{
			CaliberType.Five56By45 => MagazineCaliberVisualPreference.Five56,
			CaliberType.Five45By39 => MagazineCaliberVisualPreference.Ak,
			CaliberType.Seven62By39 => MagazineCaliberVisualPreference.Ak,
			CaliberType.Seven62By51 => MagazineCaliberVisualPreference.Ak,
			CaliberType.Seven62By54R => MagazineCaliberVisualPreference.Ak,
			_ => MagazineCaliberVisualPreference.Undefined
		};
	}
	#endregion
}
