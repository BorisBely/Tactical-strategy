using System.Collections.Generic;

/// <summary>
/// Собирает гранаты из инвентаря в порядке, в котором они должны занимать визуальные слоты.
/// </summary>
public static class GrenadeVisualOrderResolver
{
	#region Public Methods
	public static List<ItemDefinition> CollectOrderedGrenades(CharacterInventory _inventory)
	{
		List<ItemDefinition> grenades = new List<ItemDefinition>();
		if (_inventory == null)
			return grenades;

		IReadOnlyList<InventorySlotRuntimeData> bagItems = _inventory.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
			AddGrenadeIfPresent(bagItems[i], grenades);

		SortGrenades(grenades);
		return grenades;
	}

	public static List<ItemDefinition> CollectOrderedGrenades(MissionPrepPresetSnapshot _snapshot)
	{
		List<ItemDefinition> grenades = new List<ItemDefinition>();
		if (_snapshot == null)
			return grenades;

		IReadOnlyList<InventorySlotRuntimeData> bagItems = _snapshot.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
			AddGrenadeIfPresent(bagItems[i], grenades);

		SortGrenades(grenades);
		return grenades;
	}
	#endregion

	#region Private Methods
	private static void AddGrenadeIfPresent(InventorySlotRuntimeData _slot, List<ItemDefinition> _grenades)
	{
		ItemDefinition definition = _slot.Definition;
		if (definition != null && definition.IsGrenade)
			_grenades.Add(definition);
	}

	private static void SortGrenades(List<ItemDefinition> _grenades)
	{
		_grenades.Sort((a, b) =>
		{
			int orderA = GetSortOrder(a);
			int orderB = GetSortOrder(b);
			if (orderA != orderB)
				return orderA.CompareTo(orderB);

			return string.Compare(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty, System.StringComparison.Ordinal);
		});
	}

	private static int GetSortOrder(ItemDefinition _definition)
	{
		if (_definition == null)
			return 99;

		return _definition.GrenadeType switch
		{
			GrenadeType.Fragmentation => 0,
			GrenadeType.Smoke => 1,
			GrenadeType.Flash => 2,
			_ => 3
		};
	}
	#endregion
}
