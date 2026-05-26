/// <summary>
/// Проверки для переноса оружия в слот основной руки (пресет и runtime).
/// </summary>
public static class WeaponEquipUtility
{
	#region Public Methods
	public static bool CanEquipToMainHand(InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty || _item.Definition == null || !_item.Definition.IsEquipment)
			return false;

		if (_item.InstanceState != null &&
		    _item.InstanceState.WeaponState != null &&
		    _item.InstanceState.WeaponState.IsTerminallyBroken)
			return false;

		return true;
	}
	#endregion
}
