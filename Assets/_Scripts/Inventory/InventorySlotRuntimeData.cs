using System;
using UnityEngine;

/// <summary>
/// Снимок предмета в ячейке (для UI и логики переносов).
/// </summary>
[Serializable]
public struct InventorySlotRuntimeData
{
	public string DisplayName;
	public int StackCount;
	[Tooltip("Ссылка на SO для будущих статов, крафта и т.д.")]
	public ItemDefinition Definition;

	public static InventorySlotRuntimeData FromDefinition(ItemDefinition _definition, int _stack = 1)
	{
		string name = _definition != null ? _definition.DisplayName : "Предмет";
		return new InventorySlotRuntimeData
		{
			DisplayName = name,
			StackCount = Mathf.Max(1, _stack),
			Definition = _definition
		};
	}

	public static InventorySlotRuntimeData FromDisplayName(string _displayName, int _stack = 1)
	{
		return new InventorySlotRuntimeData
		{
			DisplayName = string.IsNullOrWhiteSpace(_displayName) ? "Предмет" : _displayName,
			StackCount = Mathf.Max(1, _stack),
			Definition = null
		};
	}

	public bool IsEmpty => string.IsNullOrEmpty(DisplayName) && Definition == null;
}
