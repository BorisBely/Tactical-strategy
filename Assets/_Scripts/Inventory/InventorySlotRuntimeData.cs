using System;
using UnityEngine;

/// <summary>
/// Снимок предмета в ячейке (для UI и логики переносов). Один предмет — одна ячейка, без стаков.
/// </summary>
[Serializable]
public struct InventorySlotRuntimeData
{
	public string DisplayName;
	[Tooltip("Ссылка на SO для будущих статов, крафта и т.д.")]
	public ItemDefinition Definition;
	[Tooltip("Объект в мире, пока лежит на земле; очищается после переноса в инвентарь персонажа.")]
	public WorldPickupItem WorldSource;

	public static InventorySlotRuntimeData FromDefinition(ItemDefinition _definition)
	{
		string name = _definition != null ? _definition.DisplayName : "Предмет";
		return new InventorySlotRuntimeData
		{
			DisplayName = name,
			Definition = _definition,
			WorldSource = null
		};
	}

	public static InventorySlotRuntimeData FromDisplayName(string _displayName)
	{
		return new InventorySlotRuntimeData
		{
			DisplayName = string.IsNullOrWhiteSpace(_displayName) ? "Предмет" : _displayName,
			Definition = null,
			WorldSource = null
		};
	}

	public bool IsEmpty => string.IsNullOrEmpty(DisplayName) && Definition == null;
}
