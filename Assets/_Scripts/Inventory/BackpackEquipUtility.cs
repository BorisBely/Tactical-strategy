/// <summary>
/// Проверки для экипировки рюкзака в слот спины.
/// </summary>
public static class BackpackEquipUtility
{
	#region Public Methods
	public static bool CanEquipToBack(InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty || _item.Definition == null || !_item.Definition.IsEquipment)
			return false;

		return _item.Definition.EquipmentKind == EquipmentKind.Backpack;
	}
	#endregion
}
