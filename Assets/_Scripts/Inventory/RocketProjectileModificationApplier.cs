using UnityEngine;

/// <summary>
/// UI-слот снаряда РПГ: анимированная установка через <see cref="UnitRocketLauncherOrderController"/>.
/// </summary>
public static class RocketProjectileModificationApplier
{
	#region Public Methods
	public static bool IsRocketProjectileSlot(ItemModificationSlotDescriptor _slotDescriptor)
	{
		return _slotDescriptor.Kind == ItemModificationSlotKind.RocketProjectile;
	}

	public static bool TryGetOrderController(CharacterInventory _inventory, out UnitRocketLauncherOrderController _orderController)
	{
		_orderController = null;
		if (_inventory == null)
			return false;

		_orderController = _inventory.GetComponentInParent<UnitRocketLauncherOrderController>();
		return _orderController != null;
	}

	public static bool CanStartUiRocketModification(CharacterInventory _inventory)
	{
		if (!TryGetOrderController(_inventory, out UnitRocketLauncherOrderController orderController))
			return false;

		if (orderController.IsBusy)
			return false;

		UnitBusyState busyState = _inventory.GetComponentInParent<UnitBusyState>();
		if (busyState != null && busyState.IsBusy)
			return false;

		if (WeaponMagazineModificationApplier.TryGetReloadController(_inventory, out UnitWeaponReloadController reloadController) &&
		    reloadController.IsReloadBusy)
			return false;

		return true;
	}

	public static bool TryStartEquippedRocketInstall(
		CharacterInventory _inventory,
		int _launcherBagIndex,
		InventorySlotRuntimeData _rocketFromSource,
		bool _mirrorAnimationOnly = false)
	{
		if (_inventory == null || _rocketFromSource.IsEmpty || _launcherBagIndex < 0)
			return false;

		if (!TryGetOrderController(_inventory, out UnitRocketLauncherOrderController orderController))
			return false;

		InventorySlotRuntimeData rocket = MissionPrepInventoryCopyUtility.CloneSlot(_rocketFromSource);
		return orderController.TryStartUiRocketInstall(_launcherBagIndex, rocket, _mirrorAnimationOnly);
	}
	#endregion
}
