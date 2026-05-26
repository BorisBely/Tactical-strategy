using UnityEngine;

/// <summary>
/// Синхронизация UI-слота магазина с данными оружия: мгновенный путь (сумка/лут) или reload-анимация для экипированного main hand.
/// </summary>
public static class WeaponMagazineModificationApplier
{
	#region Public Properties
	/// <summary>Если false, eject-event reload не кладёт магазин в сумку (координатор заберёт его сам, напр. на панель «земля»).</summary>
	public static bool ShouldAddUiEjectedMagazineToBag { get; set; } = true;
	#endregion

	#region Public Methods
	public static bool IsMagazineSlot(ItemModificationSlotDescriptor _slotDescriptor)
	{
		return _slotDescriptor.Kind == ItemModificationSlotKind.Magazine;
	}

	public static bool IsEquippedMainHandWeapon(CharacterInventory _inventory, bool _isMainHand, InventorySlotRuntimeData _weaponSlot)
	{
		if (_inventory == null || !_isMainHand || _weaponSlot.IsEmpty || _weaponSlot.InstanceState == null)
			return false;

		if (!_inventory.HasMainHandEquipment)
			return false;

		InventorySlotRuntimeData mainHand = _inventory.MainHandEquipment;
		return mainHand.InstanceState != null &&
		       ReferenceEquals(mainHand.InstanceState, _weaponSlot.InstanceState);
	}

	public static bool TryGetReloadController(CharacterInventory _inventory, out UnitWeaponReloadController _reloadController)
	{
		_reloadController = null;
		if (_inventory == null)
			return false;

		_reloadController = _inventory.GetComponentInParent<UnitWeaponReloadController>();
		return _reloadController != null;
	}

	public static bool TryGetWeaponRuntime(CharacterInventory _inventory, out UnitWeaponRuntime _weaponRuntime)
	{
		_weaponRuntime = null;
		if (_inventory == null)
			return false;

		_weaponRuntime = _inventory.GetComponentInParent<UnitWeaponRuntime>();
		return _weaponRuntime != null;
	}

	public static bool CanStartUiMagazineModification(CharacterInventory _inventory)
	{
		if (!TryGetReloadController(_inventory, out UnitWeaponReloadController reloadController))
			return false;

		return !reloadController.IsReloadBusy;
	}

	/// <summary>Магазин уже снят с источника; вставка произойдёт на animation event.</summary>
	public static bool TryStartEquippedMagazineInstall(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _magazineFromSource,
		bool _mirrorAnimationOnly = false)
	{
		if (_inventory == null || _magazineFromSource.IsEmpty)
			return false;

		if (!TryGetReloadController(_inventory, out UnitWeaponReloadController reloadController))
			return false;

		if (reloadController.IsReloadBusy)
			return false;

		InventorySlotRuntimeData magazine = MissionPrepInventoryCopyUtility.CloneSlot(_magazineFromSource);
		return reloadController.TryStartUiMagazineInstall(magazine, _mirrorAnimationOnly);
	}

	public static bool TryStartEquippedMagazineEject(CharacterInventory _inventory, bool _mirrorAnimationOnly = false)
	{
		if (_inventory == null)
			return false;

		if (!TryGetReloadController(_inventory, out UnitWeaponReloadController reloadController))
			return false;

		if (reloadController.IsReloadBusy)
			return false;

		return reloadController.TryStartUiMagazineEject(_mirrorAnimationOnly);
	}

	public static void RefreshEquippedWeaponVisuals(CharacterInventory _inventory)
	{
		if (!TryGetWeaponRuntime(_inventory, out UnitWeaponRuntime weaponRuntime))
			return;

		weaponRuntime.RefreshFromEquipment();
	}
	#endregion
}
