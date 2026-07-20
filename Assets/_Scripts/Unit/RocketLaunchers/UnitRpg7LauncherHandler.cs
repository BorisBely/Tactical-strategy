using UnityEngine;

/// <summary>
/// Логика многоразового РПГ-7: выстрел или перезарядка ракетой из сумки / слота снаряда.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitRpg7LauncherHandler : MonoBehaviour
{
	#region Public Methods
	public bool CanHandle(ItemDefinition _launcher)
	{
		return _launcher != null && _launcher.RocketLauncherType == RocketLauncherType.Rpg7;
	}

	public bool IsLoaded(InventorySlotRuntimeData _slot)
	{
		RocketLauncherRuntimeState state = _slot.InstanceState != null
			? _slot.InstanceState.RocketLauncherState
			: null;
		return state != null && state.IsLoaded;
	}

	public bool HasRocketInBag(CharacterInventory _inventory, ItemDefinition _launcher)
	{
		if (_inventory == null || _launcher == null)
			return false;

		ItemDefinition rocketDef = _launcher.RpgRocketItemDefinition;
		for (int i = 0; i < _inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = _inventory.BagItems[i];
			if (slot.IsEmpty || slot.Definition == null)
				continue;

			if (rocketDef != null && slot.Definition == rocketDef)
				return true;

			if (slot.Definition.IsRpgRocketAmmo)
				return true;
		}

		return false;
	}

	public bool CanAcceptRocketItem(ItemDefinition _launcher, InventorySlotRuntimeData _rocket)
	{
		if (!CanHandle(_launcher) || _rocket.IsEmpty || _rocket.Definition == null)
			return false;

		if (_rocket.Definition.IsRpgRocketAmmo)
			return true;

		ItemDefinition expected = _launcher.RpgRocketItemDefinition;
		return expected != null && _rocket.Definition == expected;
	}

	public bool ShouldReload(InventorySlotRuntimeData _slot, CharacterInventory _inventory)
	{
		if (!CanHandle(_slot.Definition))
			return false;

		if (IsLoaded(_slot))
			return false;

		return HasRocketInBag(_inventory, _slot.Definition);
	}

	public bool TryConsumeRocketFromBag(
		CharacterInventory _inventory,
		ItemDefinition _launcher,
		out int _removedBagIndex,
		out InventorySlotRuntimeData _consumedRocket)
	{
		_removedBagIndex = -1;
		_consumedRocket = default;
		if (_inventory == null || _launcher == null)
			return false;

		ItemDefinition rocketDef = _launcher.RpgRocketItemDefinition;
		for (int i = 0; i < _inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = _inventory.BagItems[i];
			if (slot.IsEmpty || slot.Definition == null)
				continue;

			bool match = (rocketDef != null && slot.Definition == rocketDef) || slot.Definition.IsRpgRocketAmmo;
			if (!match)
				continue;

			if (_inventory.TryRemoveBagAt(i, out InventorySlotRuntimeData removed))
			{
				_removedBagIndex = i;
				_consumedRocket = removed;
				return true;
			}
		}

		return false;
	}

	public void MarkUnloaded(ref InventorySlotRuntimeData _slot)
	{
		if (_slot.InstanceState == null)
			_slot.InstanceState = ItemInstanceState.CreateForDefinition(_slot.Definition);

		_slot.InstanceState.EnsureRocketLauncherState(_slot.Definition);
		_slot.InstanceState.RocketLauncherState.ClearLoadedRocket();
	}

	public void MarkLoaded(ref InventorySlotRuntimeData _slot, InventorySlotRuntimeData _rocket = default)
	{
		if (_slot.InstanceState == null)
			_slot.InstanceState = ItemInstanceState.CreateForDefinition(_slot.Definition);

		_slot.InstanceState.EnsureRocketLauncherState(_slot.Definition);
		if (!_rocket.IsEmpty && _rocket.Definition != null)
			_slot.InstanceState.RocketLauncherState.SetLoadedRocket(_rocket);
		else
			_slot.InstanceState.RocketLauncherState.SetLoaded(true);
	}

	public bool TryEjectLoadedRocket(ref InventorySlotRuntimeData _slot, out InventorySlotRuntimeData _rocket)
	{
		_rocket = default;
		if (_slot.InstanceState == null)
			return false;

		_slot.InstanceState.EnsureRocketLauncherState(_slot.Definition);
		return _slot.InstanceState.RocketLauncherState.TryEjectLoadedRocket(out _rocket);
	}
	#endregion
}
