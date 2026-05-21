using UnityEngine;

/// <summary>
/// Копирование ячеек инвентаря для пресетов предмиссии (без ссылок на лут на земле).
/// </summary>
public static class MissionPrepInventoryCopyUtility
{
	#region Public Methods
	public static InventorySlotRuntimeData CloneSlot(InventorySlotRuntimeData _source)
	{
		if (_source.IsEmpty)
			return default;

		InventorySlotRuntimeData copy = _source.Definition != null
			? InventorySlotRuntimeData.FromDefinition(_source.Definition)
			: InventorySlotRuntimeData.FromDisplayName(_source.DisplayName, _source.LocalizationKey);

		if (_source.InstanceState != null)
			copy.InstanceState = CloneInstanceState(_source.InstanceState);

		copy.WorldSource = null;
		return copy;
	}

	public static ItemInstanceState CloneInstanceState(ItemInstanceState _source)
	{
		if (_source == null)
			return null;

		return JsonUtility.FromJson<ItemInstanceState>(JsonUtility.ToJson(_source));
	}
	#endregion
}
