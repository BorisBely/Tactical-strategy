/// <summary>
/// Проверки для экипировки шлема в слот головы.
/// </summary>
public static class HelmetEquipUtility
{
	#region Public Methods
	public static bool CanEquipToHead(InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty || _item.Definition == null || !_item.Definition.IsEquipment)
			return false;

		return _item.Definition.EquipmentKind == EquipmentKind.Helmet;
	}
	#endregion
}
