using System.Collections.Generic;

public enum MagazineCaliberVisualPreference
{
	Undefined = 0,
	Five56 = 1,
	Ak = 2,
	TwelveGauge = 3
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
		int twelveGaugeCount = 0;
		CountMagazineSlot(_inventory.MainHandEquipment, ref five56Count, ref akCount, ref twelveGaugeCount);

		IReadOnlyList<InventorySlotRuntimeData> bagItems = _inventory.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
			CountMagazineSlot(bagItems[i], ref five56Count, ref akCount, ref twelveGaugeCount);

		return ResolveFromCounts(five56Count, akCount, twelveGaugeCount, _inventory.MainHandEquipment);
	}

	public static MagazineCaliberVisualPreference Resolve(MissionPrepPresetSnapshot _snapshot)
	{
		if (_snapshot == null)
			return MagazineCaliberVisualPreference.Undefined;

		int five56Count = 0;
		int akCount = 0;
		int twelveGaugeCount = 0;
		CountMagazineSlot(_snapshot.MainHandEquipment, ref five56Count, ref akCount, ref twelveGaugeCount);

		IReadOnlyList<InventorySlotRuntimeData> bagItems = _snapshot.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
			CountMagazineSlot(bagItems[i], ref five56Count, ref akCount, ref twelveGaugeCount);

		return ResolveFromCounts(five56Count, akCount, twelveGaugeCount, _snapshot.MainHandEquipment);
	}
	#endregion

	#region Private Methods
	private static void CountMagazineSlot(
		InventorySlotRuntimeData _slot,
		ref int _five56Count,
		ref int _akCount,
		ref int _twelveGaugeCount)
	{
		ItemDefinition definition = _slot.Definition;
		if (definition == null)
			return;

		if (definition.MagazineDefinition != null)
		{
			CountCaliber(definition.MagazineDefinition.SupportedCaliber, ref _five56Count, ref _akCount, ref _twelveGaugeCount);
			return;
		}

		ItemDefinition insertedMagazine = _slot.InstanceState?.WeaponState?.InsertedMagazineDefinition;
		if (insertedMagazine != null && insertedMagazine.MagazineDefinition != null)
			CountCaliber(insertedMagazine.MagazineDefinition.SupportedCaliber, ref _five56Count, ref _akCount, ref _twelveGaugeCount);
	}

	private static void CountCaliber(
		CaliberType _caliber,
		ref int _five56Count,
		ref int _akCount,
		ref int _twelveGaugeCount)
	{
		switch (_caliber)
		{
			case CaliberType.Five56By45:
				_five56Count++;
				break;
			case CaliberType.Five45By39:
			case CaliberType.Seven62By39:
				_akCount++;
				break;
			case CaliberType.TwelveGauge:
				_twelveGaugeCount++;
				break;
			// Seven62By51 / Seven62By54R и прочие — только PouchDeco_Mag_0 (Undefined).
		}
	}

	private static MagazineCaliberVisualPreference ResolveFromCounts(
		int _five56Count,
		int _akCount,
		int _twelveGaugeCount,
		InventorySlotRuntimeData _mainHand)
	{
		int maxCount = _five56Count;
		if (_akCount > maxCount)
			maxCount = _akCount;
		if (_twelveGaugeCount > maxCount)
			maxCount = _twelveGaugeCount;

		if (maxCount > 0)
		{
			int winners = 0;
			MagazineCaliberVisualPreference winner = MagazineCaliberVisualPreference.Undefined;
			if (_five56Count == maxCount)
			{
				winners++;
				winner = MagazineCaliberVisualPreference.Five56;
			}

			if (_akCount == maxCount)
			{
				winners++;
				winner = MagazineCaliberVisualPreference.Ak;
			}

			if (_twelveGaugeCount == maxCount)
			{
				winners++;
				winner = MagazineCaliberVisualPreference.TwelveGauge;
			}

			if (winners == 1)
				return winner;
		}

		WeaponDefinition weapon = _mainHand.Definition != null ? _mainHand.Definition.WeaponDefinition : null;
		if (weapon == null)
			return MagazineCaliberVisualPreference.Undefined;

		return MapWeaponCaliberToPreference(weapon.SupportedCaliber);
	}

	/// <summary>
	/// M-платформа (5.56) → M4-подсумки; AK (5.45 / 7.62×39) → AK-подсумки;
	/// 12 gauge → PouchDeco_Mag_12; остальное (снайпер 7.62×51, Мосин/СВД/ПКМ 7.62×54R) → Mag_0.
	/// </summary>
	private static MagazineCaliberVisualPreference MapWeaponCaliberToPreference(CaliberType _caliber)
	{
		return _caliber switch
		{
			CaliberType.Five56By45 => MagazineCaliberVisualPreference.Five56,
			CaliberType.Five45By39 => MagazineCaliberVisualPreference.Ak,
			CaliberType.Seven62By39 => MagazineCaliberVisualPreference.Ak,
			CaliberType.TwelveGauge => MagazineCaliberVisualPreference.TwelveGauge,
			_ => MagazineCaliberVisualPreference.Undefined
		};
	}
	#endregion
}
