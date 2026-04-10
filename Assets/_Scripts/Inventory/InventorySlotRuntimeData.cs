using System;
using UnityEngine;

/// <summary>
/// Снимок предмета в ячейке (для UI и логики переносов). Один предмет — одна ячейка, без стаков.
/// </summary>
[Serializable]
public struct InventorySlotRuntimeData
{
	public string DisplayName;
	public string LocalizationKey;
	[Tooltip("Ссылка на SO для будущих статов, крафта и т.д.")]
	public ItemDefinition Definition;
	[Tooltip("Постоянное состояние конкретного экземпляра предмета.")]
	public ItemInstanceState InstanceState;
	[Tooltip("Объект в мире, пока лежит на земле; очищается после переноса в инвентарь персонажа.")]
	public WorldPickupItem WorldSource;

	public static InventorySlotRuntimeData FromDefinition(ItemDefinition _definition)
	{
		string name = _definition != null ? _definition.GetLocalizedDisplayName() : LocalizationManager.Get("item.generic", "Item");
		return new InventorySlotRuntimeData
		{
			DisplayName = name,
			LocalizationKey = _definition != null ? _definition.LocalizationKey : null,
			Definition = _definition,
			InstanceState = ItemInstanceState.CreateForDefinition(_definition),
			WorldSource = null
		};
	}

	public static InventorySlotRuntimeData FromDisplayName(string _displayName, string _localizationKey = null)
	{
		return new InventorySlotRuntimeData
		{
			DisplayName = string.IsNullOrWhiteSpace(_displayName) ? LocalizationManager.Get("item.generic", "Item") : _displayName,
			LocalizationKey = _localizationKey,
			Definition = null,
			InstanceState = null,
			WorldSource = null
		};
	}

	public bool IsEmpty => string.IsNullOrEmpty(DisplayName) && Definition == null;
}
