using System.Collections.Generic;
using UnityEngine;

public enum ItemModificationSlotKind
{
	Magazine = 0,
	Attachment = 1,
	RocketProjectile = 2
}

public readonly struct ItemModificationSlotDescriptor
{
	public readonly ItemModificationSlotKind Kind;
	public readonly WeaponAttachmentSlotType AttachmentSlotType;
	public readonly int WeaponSlotIndex;
	public readonly int DisplayIndex;

	public ItemModificationSlotDescriptor(ItemModificationSlotKind _kind, WeaponAttachmentSlotType _attachmentSlotType, int _weaponSlotIndex, int _displayIndex)
	{
		Kind = _kind;
		AttachmentSlotType = _attachmentSlotType;
		WeaponSlotIndex = _weaponSlotIndex;
		DisplayIndex = _displayIndex;
	}
}

/// <summary>
/// Data-level rules for weapon modification slots. UI layers use this without knowing weapon internals.
/// </summary>
public static class ItemModificationUtility
{
	#region Constants
	private const string c_MagazineSlotLabelKey = "weapon.mod_slot.magazine";
	private const string c_SecondaryMagazineSlotLabelKey = "weapon.mod_slot.magazine_secondary";
	private const string c_RocketProjectileSlotLabelKey = "weapon.mod_slot.rocket_projectile";
	public const int RocketProjectileWeaponSlotIndex = -3;
	#endregion

	#region Public Methods
	public static bool IsModifiableWeapon(ItemDefinition _definition)
	{
		if (_definition == null)
			return false;

		if (_definition.IsRocketLauncher &&
		    (_definition.RocketLauncherType == RocketLauncherType.Rpg7 ||
		     _definition.RocketLauncherType == RocketLauncherType.Disposable))
			return true;

		WeaponDefinition weapon = _definition.WeaponDefinition;
		if (!_definition.IsEquipment || weapon == null)
			return false;

		if (weapon.SupportedMagazineType != MagazineType.None)
			return true;

		WeaponAttachmentSlotDefinition[] slots = weapon.AttachmentSlots;
		if (slots == null)
			return false;

		return slots.Length > 0;
	}

	/// <summary>Одноразовый гранатомёт: слот снаряда только для отображения, снять нельзя.</summary>
	public static bool IsRocketProjectileSlotLocked(ItemDefinition _definition)
	{
		return _definition != null &&
		       _definition.IsRocketLauncher &&
		       _definition.RocketLauncherType == RocketLauncherType.Disposable;
	}

	public static void BuildSlotDescriptors(ItemDefinition _definition, List<ItemModificationSlotDescriptor> _outSlots)
	{
		if (_outSlots == null)
			return;

		_outSlots.Clear();
		if (_definition == null)
			return;

		int displayIndex = 0;
		if (_definition.IsRocketLauncher &&
		    (_definition.RocketLauncherType == RocketLauncherType.Rpg7 ||
		     _definition.RocketLauncherType == RocketLauncherType.Disposable))
		{
			_outSlots.Add(new ItemModificationSlotDescriptor(
				ItemModificationSlotKind.RocketProjectile,
				default,
				RocketProjectileWeaponSlotIndex,
				displayIndex++));
			return;
		}

		WeaponDefinition weapon = _definition.WeaponDefinition;
		if (weapon == null)
			return;

		if (weapon.SupportedMagazineType != MagazineType.None &&
		    !WeaponBuiltInMagazineUtility.IsMagazineSlotLocked(weapon))
			_outSlots.Add(new ItemModificationSlotDescriptor(ItemModificationSlotKind.Magazine, default, -1, displayIndex++));

		if (weapon.UsesDualMagazineSlots &&
		    !WeaponBuiltInMagazineUtility.IsMagazineSlotLocked(weapon))
			_outSlots.Add(new ItemModificationSlotDescriptor(ItemModificationSlotKind.Magazine, default, -2, displayIndex++));

		WeaponAttachmentSlotDefinition[] slots = weapon.AttachmentSlots;
		if (slots == null)
			return;

		for (int i = 0; i < slots.Length; i++)
		{
			if (!WeaponAttachmentSlotPolicy.IsWeaponSlotEnabled(weapon, i))
				continue;

			_outSlots.Add(new ItemModificationSlotDescriptor(ItemModificationSlotKind.Attachment, slots[i].SlotType, i, displayIndex++));
		}
	}

	public static string GetSlotLabel(ItemModificationSlotDescriptor _slot, WeaponDefinition _weapon = null)
	{
		if (_slot.Kind == ItemModificationSlotKind.Attachment &&
		    _slot.AttachmentSlotType == WeaponAttachmentSlotType.Rail &&
		    _weapon != null)
		{
			int railIndex = ResolveRailSocketIndex(_weapon, _slot);
			if (railIndex >= 0)
			{
				string railKey = railIndex switch
				{
					0 => "weapon.mod_slot.rail_1",
					1 => "weapon.mod_slot.rail_2",
					_ => "weapon.mod_slot.rail_3"
				};
				string railFallback = railIndex switch
				{
					0 => "Tactical LCU",
					1 => "Flashlight",
					_ => "LCU"
				};
				return LocalizationManager.Get(railKey, railFallback);
			}
		}

		string key = GetSlotLabelKey(_slot);
		string fallback = GetSlotFallbackLabel(_slot);
		return LocalizationManager.Get(key, fallback);
	}

	public static string FormatEmptySlotLabel(ItemModificationSlotDescriptor _slot, WeaponDefinition _weapon = null)
	{
		string slotLabel = GetSlotLabel(_slot, _weapon);
		string empty = LocalizationManager.Get("weapon.mod_slot.empty", "Empty");
		if (string.IsNullOrWhiteSpace(slotLabel))
			return empty;

		string template = LocalizationManager.Get("weapon.mod_slot.empty_named", "{0} ({1})");
		return string.Format(template, slotLabel, empty);
	}

	public static string GetSlotLabelKey(ItemModificationSlotDescriptor _slot)
	{
		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return _slot.WeaponSlotIndex == -2 ? c_SecondaryMagazineSlotLabelKey : c_MagazineSlotLabelKey;

		if (_slot.Kind == ItemModificationSlotKind.RocketProjectile)
			return c_RocketProjectileSlotLabelKey;

		return _slot.AttachmentSlotType switch
		{
			WeaponAttachmentSlotType.Muzzle => "weapon.mod_slot.muzzle",
			WeaponAttachmentSlotType.UnderBarrel => "weapon.mod_slot.underbarrel",
			WeaponAttachmentSlotType.Rail => "weapon.mod_slot.rail",
			WeaponAttachmentSlotType.Optic => "weapon.mod_slot.optic",
			WeaponAttachmentSlotType.Stock => "weapon.mod_slot.stock",
			WeaponAttachmentSlotType.SideRail => "weapon.mod_slot.side_rail",
			_ => "weapon.mod_slot.attachment"
		};
	}

	public static bool IsModificationItem(InventorySlotRuntimeData _item)
	{
		return IsMagazineItem(_item) || IsAttachmentItem(_item) || IsRocketProjectileItem(_item);
	}

	public static bool IsMagazineItem(InventorySlotRuntimeData _item)
	{
		return !_item.IsEmpty && _item.Definition != null && _item.Definition.MagazineDefinition != null;
	}

	public static bool IsRocketProjectileItem(InventorySlotRuntimeData _item)
	{
		return !_item.IsEmpty && _item.Definition != null && _item.Definition.IsRpgRocketAmmo;
	}

	public static bool IsAttachmentItem(InventorySlotRuntimeData _item)
	{
		return !_item.IsEmpty && _item.Definition != null && _item.Definition.WeaponAttachmentDefinition != null;
	}

	public static bool IsRecoilGraphPreviewAttachment(WeaponAttachmentDefinition _attachment)
	{
		return _attachment != null &&
		       (_attachment.SupportsSlot(WeaponAttachmentSlotType.UnderBarrel) ||
		        _attachment.SupportsSlot(WeaponAttachmentSlotType.Muzzle) ||
		        _attachment.SupportsSlot(WeaponAttachmentSlotType.Stock));
	}

	public static bool IsRecoilGraphPreviewItem(InventorySlotRuntimeData _item)
	{
		if (!IsAttachmentItem(_item))
			return false;

		return IsRecoilGraphPreviewAttachment(_item.Definition.WeaponAttachmentDefinition);
	}

	public static bool CanAcceptItem(ItemModificationSlotDescriptor _slot, InventorySlotRuntimeData _weaponSlot, InventorySlotRuntimeData _candidate)
	{
		return string.Equals(
			ExplainCanAcceptItem(_slot, _weaponSlot, _candidate),
			ItemModificationDiagnostics.AcceptedReason,
			System.StringComparison.Ordinal);
	}

	/// <summary>Индекс физической планки 0..2 для слота оружия; -1 если не Rail.</summary>
	public static int ResolveRailSocketIndexForSlot(WeaponDefinition _weapon, ItemModificationSlotDescriptor _slot)
	{
		return ResolveRailSocketIndex(_weapon, _slot);
	}

	/// <summary>Подробная причина отказа или строка <c>accepted</c>.</summary>
	public static string ExplainCanAcceptItem(
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotRuntimeData _candidate)
	{
		return ItemModificationDiagnostics.ExplainCanAcceptItem(_slot, _weaponSlot, _candidate);
	}

	/// <summary>
	/// Совместимость предмета каталога/инвентаря с выбранным оружием: модуль, магазин или патроны под калибр.
	/// </summary>
	public static bool IsCompatibleWithWeapon(InventorySlotRuntimeData _weaponSlot, InventorySlotRuntimeData _candidate)
	{
		if (_weaponSlot.IsEmpty || _candidate.IsEmpty || _candidate.Definition == null)
			return false;

		ItemDefinition weaponDefinition = _weaponSlot.Definition;
		if (weaponDefinition != null &&
		    weaponDefinition.IsRocketLauncher &&
		    weaponDefinition.RocketLauncherType == RocketLauncherType.Rpg7)
		{
			return IsRocketProjectileItem(_candidate) ||
			       (weaponDefinition.RpgRocketItemDefinition != null &&
			        _candidate.Definition == weaponDefinition.RpgRocketItemDefinition);
		}

		WeaponDefinition weapon = weaponDefinition != null ? weaponDefinition.WeaponDefinition : null;
		if (weapon == null)
			return false;

		if (IsAttachmentItem(_candidate))
		{
			s_DescriptorBuffer.Clear();
			BuildSlotDescriptors(weaponDefinition, s_DescriptorBuffer);
			for (int i = 0; i < s_DescriptorBuffer.Count; i++)
			{
				ItemModificationSlotDescriptor descriptor = s_DescriptorBuffer[i];
				if (descriptor.Kind != ItemModificationSlotKind.Attachment)
					continue;

				if (CanAcceptItem(descriptor, _weaponSlot, _candidate))
					return true;
			}

			return false;
		}

		if (IsMagazineItem(_candidate))
		{
			WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
			return weaponState != null &&
				(weaponState.CanAcceptMagazineItem(_candidate, WeaponRuntimeState.PrimaryMagazineSlotIndex) ||
				 weaponState.CanAcceptMagazineItem(_candidate, WeaponRuntimeState.SecondaryMagazineSlotIndex));
		}

		AmmoDefinition ammoDefinition = _candidate.Definition.AmmoDefinition;
		if (ammoDefinition != null)
		{
			if (weapon.SupportedCaliber == CaliberType.None)
				return false;

			return ammoDefinition.Caliber == weapon.SupportedCaliber;
		}

		return false;
	}

	public static bool TryGetInstalledItem(ItemModificationSlotDescriptor _slot, InventorySlotRuntimeData _weaponSlot, out InventorySlotRuntimeData _installedItem)
	{
		_installedItem = default;
		if (_slot.Kind == ItemModificationSlotKind.RocketProjectile)
		{
			if (IsRocketProjectileSlotLocked(_weaponSlot.Definition))
				return TryBuildDisposableRocketProjectileStub(_weaponSlot.Definition, out _installedItem);

			RocketLauncherRuntimeState rocketState = GetRocketLauncherState(_weaponSlot);
			return rocketState != null && rocketState.TryBuildLoadedRocketItem(out _installedItem);
		}

		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null)
			return false;

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
		{
			if (_slot.WeaponSlotIndex == -2)
			{
				_installedItem = BuildSecondaryMagazineSlotItem(weaponState);
				return !_installedItem.IsEmpty;
			}

			_installedItem = weaponState.CurrentMagazineItem;
			return !_installedItem.IsEmpty;
		}

		ItemDefinition[] items = weaponState.EquippedAttachmentItems;
		if (items != null &&
		    _slot.WeaponSlotIndex >= 0 &&
		    _slot.WeaponSlotIndex < items.Length &&
		    items[_slot.WeaponSlotIndex] != null)
		{
			_installedItem = InventorySlotRuntimeData.FromDefinition(items[_slot.WeaponSlotIndex]);
			return true;
		}

		WeaponAttachmentDefinition[] attachments = weaponState.EquippedAttachments;
		if (attachments == null ||
		    _slot.WeaponSlotIndex < 0 ||
		    _slot.WeaponSlotIndex >= attachments.Length ||
		    attachments[_slot.WeaponSlotIndex] == null)
			return false;

		WeaponAttachmentDefinition attachment = attachments[_slot.WeaponSlotIndex];
		if (TryResolveAttachmentItemDefinition(weaponState, _slot.WeaponSlotIndex, attachment, out ItemDefinition itemDefinition))
		{
			_installedItem = InventorySlotRuntimeData.FromDefinition(itemDefinition);
			return true;
		}

		_installedItem = InventorySlotRuntimeData.FromDisplayName(attachment.name);
		return true;
	}

	public static bool TryInstallAtSlot(
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotRuntimeData _candidate,
		out InventorySlotRuntimeData _replacedItem)
	{
		_replacedItem = default;
		if (_slot.Kind == ItemModificationSlotKind.RocketProjectile)
			return TryInstallRocketProjectile(_weaponSlot, _candidate, out _replacedItem);

		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null)
		{
			ItemModificationDiagnostics.LogInstallRejected("TryInstallAtSlot", _slot, _weaponSlot, _candidate, "WeaponRuntimeState is null");
			return false;
		}

		string acceptReason = ExplainCanAcceptItem(_slot, _weaponSlot, _candidate);
		if (!string.Equals(acceptReason, ItemModificationDiagnostics.AcceptedReason, System.StringComparison.Ordinal))
		{
			ItemModificationDiagnostics.LogInstallRejected("TryInstallAtSlot", _slot, _weaponSlot, _candidate, acceptReason);
			return false;
		}

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return TryInstallMagazine(weaponState, _candidate, _slot, out _replacedItem);

		return TryInstallAttachment(_slot, weaponState, _candidate, out _replacedItem);
	}

	public static bool TryClearSlot(
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weaponSlot,
		out InventorySlotRuntimeData _removedItem)
	{
		_removedItem = default;
		if (_slot.Kind == ItemModificationSlotKind.RocketProjectile)
		{
			if (IsRocketProjectileSlotLocked(_weaponSlot.Definition))
			{
				ItemModificationDiagnostics.LogClearRejected("TryClearSlot", _slot, _weaponSlot, "disposable launcher projectile cannot be removed");
				return false;
			}

			RocketLauncherRuntimeState rocketState = GetRocketLauncherState(_weaponSlot);
			if (rocketState == null)
			{
				ItemModificationDiagnostics.LogClearRejected("TryClearSlot", _slot, _weaponSlot, "RocketLauncherRuntimeState is null");
				return false;
			}

			if (!rocketState.TryEjectLoadedRocket(out _removedItem))
			{
				ItemModificationDiagnostics.LogClearRejected("TryClearSlot", _slot, _weaponSlot, "rocket projectile slot is empty");
				return false;
			}

			ItemModificationDiagnostics.LogClearAccepted("TryClearSlot", _slot, _weaponSlot, _removedItem);
			return true;
		}

		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null)
		{
			ItemModificationDiagnostics.LogClearRejected("TryClearSlot", _slot, _weaponSlot, "WeaponRuntimeState is null");
			return false;
		}

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
		{
			if (WeaponBuiltInMagazineUtility.IsMagazineSlotLocked(weaponState.WeaponDefinition))
			{
				ItemModificationDiagnostics.LogClearRejected("TryClearSlot", _slot, _weaponSlot, "built-in magazine cannot be removed");
				return false;
			}

			int slotIndex = _slot.WeaponSlotIndex == -2 ? WeaponRuntimeState.SecondaryMagazineSlotIndex : WeaponRuntimeState.PrimaryMagazineSlotIndex;
			return weaponState.TryEjectMagazine(slotIndex, out _removedItem);
		}

		if (!TryGetInstalledItem(_slot, _weaponSlot, out _removedItem))
		{
			ItemModificationDiagnostics.LogClearRejected("TryClearSlot", _slot, _weaponSlot, "slot is empty (no attachment at index)");
			return false;
		}

		WeaponAttachmentDefinition[] attachments = BuildAttachmentArray(weaponState);
		ItemDefinition[] items = BuildAttachmentItemArray(weaponState, attachments.Length);
		if (_slot.WeaponSlotIndex < 0 || _slot.WeaponSlotIndex >= attachments.Length)
		{
			ItemModificationDiagnostics.LogClearRejected(
				"TryClearSlot",
				_slot,
				_weaponSlot,
				$"slot index {_slot.WeaponSlotIndex} out of attachment array range [0..{attachments.Length - 1}]");
			return false;
		}

		attachments[_slot.WeaponSlotIndex] = null;
		items[_slot.WeaponSlotIndex] = null;
		weaponState.SetEquippedAttachmentSlotItems(TrimEmptyAttachments(attachments), TrimEmptyAttachmentItems(items));
		ItemModificationDiagnostics.LogClearAccepted("TryClearSlot", _slot, _weaponSlot, _removedItem);
		return true;
	}

	/// <summary>Установленные модули всегда; пустые слоты — только при <paramref name="_expandEmptySlots"/>.</summary>
	public static void BuildVisibleModificationDescriptors(
		InventorySlotRuntimeData _weaponData,
		bool _expandEmptySlots,
		List<ItemModificationSlotDescriptor> _descriptorBuffer,
		List<ItemModificationSlotDescriptor> _outVisibleDescriptors)
	{
		if (_outVisibleDescriptors == null)
			return;

		_outVisibleDescriptors.Clear();
		if (_weaponData.IsEmpty || !IsModifiableWeapon(_weaponData.Definition))
			return;

		BuildSlotDescriptors(_weaponData.Definition, _descriptorBuffer);
		if (_descriptorBuffer == null)
			return;

		WeaponDefinition weapon = _weaponData.Definition != null ? _weaponData.Definition.WeaponDefinition : null;
		WeaponRuntimeState weaponState = GetWeaponState(_weaponData);

		for (int i = 0; i < _descriptorBuffer.Count; i++)
		{
			ItemModificationSlotDescriptor descriptor = _descriptorBuffer[i];
			bool hasInstalledItem = TryGetInstalledItem(descriptor, _weaponData, out _);
			if (!WeaponOpticSlotUtility.ShouldShowModificationSlot(weapon, weaponState, descriptor, _expandEmptySlots, hasInstalledItem))
				continue;

			if (descriptor.Kind == ItemModificationSlotKind.Magazine && _expandEmptySlots)
			{
				if (IsConflictingMagazineSlotOccupied(weaponState, descriptor, _descriptorBuffer))
					continue;
			}

			if (hasInstalledItem || _expandEmptySlots)
				_outVisibleDescriptors.Add(descriptor);
		}
	}

	/// <summary>Только установленные модули (для пресета: всегда показывать то, что реально стоит на оружии).</summary>
	public static void BuildInstalledModificationDescriptors(
		InventorySlotRuntimeData _weaponData,
		List<ItemModificationSlotDescriptor> _descriptorBuffer,
		List<ItemModificationSlotDescriptor> _outInstalledDescriptors)
	{
		BuildVisibleModificationDescriptors(_weaponData, _expandEmptySlots: false, _descriptorBuffer, _outInstalledDescriptors);
	}

	public static bool HasAnyInstalledModification(InventorySlotRuntimeData _weaponData)
	{
		if (_weaponData.IsEmpty || !IsModifiableWeapon(_weaponData.Definition))
			return false;

		BuildSlotDescriptors(_weaponData.Definition, s_DescriptorBuffer);
		for (int i = 0; i < s_DescriptorBuffer.Count; i++)
		{
			if (TryGetInstalledItem(s_DescriptorBuffer[i], _weaponData, out _))
				return true;
		}

		return false;
	}
	#endregion

	#region Private Methods
	private static readonly List<ItemModificationSlotDescriptor> s_DescriptorBuffer = new List<ItemModificationSlotDescriptor>(8);

	private static WeaponRuntimeState GetWeaponState(InventorySlotRuntimeData _weaponSlot)
	{
		return _weaponSlot.InstanceState != null ? _weaponSlot.InstanceState.WeaponState : null;
	}

	private static RocketLauncherRuntimeState GetRocketLauncherState(InventorySlotRuntimeData _weaponSlot)
	{
		if (_weaponSlot.InstanceState == null)
			return null;

		if (_weaponSlot.InstanceState.RocketLauncherState == null && _weaponSlot.Definition != null)
			_weaponSlot.InstanceState.EnsureRocketLauncherState(_weaponSlot.Definition);

		return _weaponSlot.InstanceState.RocketLauncherState;
	}

	private static bool TryInstallRocketProjectile(
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotRuntimeData _candidate,
		out InventorySlotRuntimeData _replacedItem)
	{
		_replacedItem = default;
		ItemModificationSlotDescriptor descriptor = new ItemModificationSlotDescriptor(
			ItemModificationSlotKind.RocketProjectile,
			default,
			RocketProjectileWeaponSlotIndex,
			0);

		if (IsRocketProjectileSlotLocked(_weaponSlot.Definition))
		{
			ItemModificationDiagnostics.LogInstallRejected(
				"TryInstallRocketProjectile",
				descriptor,
				_weaponSlot,
				_candidate,
				"disposable launcher projectile cannot be replaced");
			return false;
		}

		string acceptReason = ItemModificationDiagnostics.ExplainCanAcceptItem(descriptor, _weaponSlot, _candidate);
		if (!string.Equals(acceptReason, ItemModificationDiagnostics.AcceptedReason, System.StringComparison.Ordinal))
		{
			ItemModificationDiagnostics.LogInstallRejected("TryInstallRocketProjectile", descriptor, _weaponSlot, _candidate, acceptReason);
			return false;
		}

		if (_weaponSlot.InstanceState == null)
		{
			ItemModificationDiagnostics.LogInstallRejected("TryInstallRocketProjectile", descriptor, _weaponSlot, _candidate, "InstanceState is null");
			return false;
		}

		_weaponSlot.InstanceState.EnsureRocketLauncherState(_weaponSlot.Definition);
		RocketLauncherRuntimeState state = _weaponSlot.InstanceState.RocketLauncherState;
		if (state.HasLoadedRocketItem)
			state.TryEjectLoadedRocket(out _replacedItem);
		else if (state.IsLoaded)
			state.ClearLoadedRocket();

		state.SetLoadedRocket(_candidate);
		ItemModificationDiagnostics.LogInstallAccepted("TryInstallRocketProjectile", descriptor, _weaponSlot, _candidate);
		return true;
	}

	private static bool TryBuildDisposableRocketProjectileStub(ItemDefinition _launcher, out InventorySlotRuntimeData _installedItem)
	{
		_installedItem = default;
		if (_launcher == null)
			return false;

		if (_launcher.RpgRocketItemDefinition != null)
		{
			_installedItem = InventorySlotRuntimeData.FromDefinition(_launcher.RpgRocketItemDefinition);
			return true;
		}

		_installedItem = InventorySlotRuntimeData.FromDisplayName(
			LocalizationManager.Get("item.ammo.rpg_rocket", "Projectile"),
			"item.ammo.rpg_rocket");
		return !_installedItem.IsEmpty;
	}

	private static bool TryInstallMagazine(WeaponRuntimeState _weaponState, InventorySlotRuntimeData _candidate, ItemModificationSlotDescriptor _slot, out InventorySlotRuntimeData _replacedItem)
	{
		_replacedItem = default;
		int slotIndex = _slot.WeaponSlotIndex == -2 ? WeaponRuntimeState.SecondaryMagazineSlotIndex : WeaponRuntimeState.PrimaryMagazineSlotIndex;

		if (slotIndex == WeaponRuntimeState.PrimaryMagazineSlotIndex && _weaponState.HasPrimaryMagazine)
			_weaponState.TryEjectMagazine(WeaponRuntimeState.PrimaryMagazineSlotIndex, out _replacedItem);

		if (slotIndex == WeaponRuntimeState.SecondaryMagazineSlotIndex && _weaponState.HasSecondaryMagazine)
			_weaponState.TryEjectMagazine(WeaponRuntimeState.SecondaryMagazineSlotIndex, out _replacedItem);

		if (_weaponState.TryInsertMagazine(_candidate, slotIndex))
			return true;

		if (!_replacedItem.IsEmpty)
			_weaponState.TryInsertMagazine(_replacedItem);

		_replacedItem = default;
		return false;
	}

	private static InventorySlotRuntimeData BuildSecondaryMagazineSlotItem(WeaponRuntimeState _weaponState)
	{
		return _weaponState != null ? _weaponState.CurrentSecondaryMagazineItem : default;
	}

	private static bool TryInstallAttachment(
		ItemModificationSlotDescriptor _slot,
		WeaponRuntimeState _weaponState,
		InventorySlotRuntimeData _candidate,
		out InventorySlotRuntimeData _replacedItem)
	{
		_replacedItem = default;
		WeaponAttachmentDefinition attachment = _candidate.Definition.WeaponAttachmentDefinition;
		WeaponAttachmentDefinition[] attachments = BuildAttachmentArray(_weaponState);
		ItemDefinition[] items = BuildAttachmentItemArray(_weaponState, attachments.Length);
		if (_slot.WeaponSlotIndex < 0 || _slot.WeaponSlotIndex >= attachments.Length)
		{
			ItemModificationDiagnostics.LogInstallRejected(
				"TryInstallAttachment",
				_slot,
				InventorySlotRuntimeData.FromDefinition(_candidate.Definition),
				_candidate,
				$"slot index {_slot.WeaponSlotIndex} out of attachment array range [0..{attachments.Length - 1}]");
			return false;
		}

		if (items[_slot.WeaponSlotIndex] != null)
			_replacedItem = InventorySlotRuntimeData.FromDefinition(items[_slot.WeaponSlotIndex]);
		else if (attachments[_slot.WeaponSlotIndex] != null &&
		         TryResolveAttachmentItemDefinition(_weaponState, _slot.WeaponSlotIndex, attachments[_slot.WeaponSlotIndex], out ItemDefinition replacedDefinition))
			_replacedItem = InventorySlotRuntimeData.FromDefinition(replacedDefinition);
		else if (attachments[_slot.WeaponSlotIndex] != null)
			_replacedItem = InventorySlotRuntimeData.FromDisplayName(attachments[_slot.WeaponSlotIndex].name);

		if (WeaponOpticSlotUtility.IsOpticSlotType(_slot.AttachmentSlotType))
			WeaponOpticSlotUtility.ClearConflictingOpticSlot(_weaponState.WeaponDefinition, _slot.AttachmentSlotType, attachments, items);

		if (WeaponLaserSlotUtility.IsLaserAttachment(attachment))
			WeaponLaserSlotUtility.ClearConflictingLaserSlots(_slot.WeaponSlotIndex, attachments, items);

		attachments[_slot.WeaponSlotIndex] = attachment;
		items[_slot.WeaponSlotIndex] = _candidate.Definition;
		_weaponState.SetEquippedAttachmentSlotItems(TrimEmptyAttachments(attachments), TrimEmptyAttachmentItems(items));
		return true;
	}

	private static WeaponAttachmentDefinition[] BuildAttachmentArray(WeaponRuntimeState _weaponState)
	{
		int length = ResolveAttachmentSlotCount(_weaponState);
		WeaponAttachmentDefinition[] result = new WeaponAttachmentDefinition[length];
		WeaponAttachmentDefinition[] source = _weaponState.EquippedAttachments;
		if (source == null)
			return result;

		int copyCount = Mathf.Min(source.Length, result.Length);
		for (int i = 0; i < copyCount; i++)
			result[i] = source[i];

		return result;
	}

	private static ItemDefinition[] BuildAttachmentItemArray(WeaponRuntimeState _weaponState, int _length)
	{
		ItemDefinition[] result = new ItemDefinition[_length];
		ItemDefinition[] source = _weaponState.EquippedAttachmentItems;
		if (source == null)
			return result;

		int copyCount = Mathf.Min(source.Length, result.Length);
		for (int i = 0; i < copyCount; i++)
			result[i] = source[i];

		return result;
	}

	private static int ResolveAttachmentSlotCount(WeaponRuntimeState _weaponState)
	{
		WeaponDefinition weapon = _weaponState.WeaponDefinition;
		WeaponAttachmentSlotDefinition[] slots = weapon != null ? weapon.AttachmentSlots : null;
		int fromWeapon = slots != null ? slots.Length : 0;
		int fromAttachments = _weaponState.EquippedAttachments != null ? _weaponState.EquippedAttachments.Length : 0;
		int fromItems = _weaponState.EquippedAttachmentItems != null ? _weaponState.EquippedAttachmentItems.Length : 0;
		return Mathf.Max(fromWeapon, fromAttachments, fromItems);
	}

	private static bool TryResolveAttachmentItemDefinition(
		WeaponRuntimeState _weaponState,
		int _weaponSlotIndex,
		WeaponAttachmentDefinition _attachment,
		out ItemDefinition _itemDefinition)
	{
		_itemDefinition = null;
		if (_weaponState == null || _attachment == null)
			return false;

		ItemDefinition[] items = _weaponState.EquippedAttachmentItems;
		if (items == null || items.Length == 0)
			return false;

		if (_weaponSlotIndex >= 0 &&
		    _weaponSlotIndex < items.Length &&
		    items[_weaponSlotIndex] != null &&
		    items[_weaponSlotIndex].WeaponAttachmentDefinition == _attachment)
		{
			_itemDefinition = items[_weaponSlotIndex];
			return true;
		}

		for (int i = 0; i < items.Length; i++)
		{
			if (items[i] != null && items[i].WeaponAttachmentDefinition == _attachment)
			{
				_itemDefinition = items[i];
				return true;
			}
		}

		return false;
	}

	private static int ResolveRailSocketIndex(WeaponDefinition _weapon, ItemModificationSlotDescriptor _slot)
	{
		if (_slot.Kind != ItemModificationSlotKind.Attachment ||
		    _slot.AttachmentSlotType != WeaponAttachmentSlotType.Rail ||
		    _weapon == null ||
		    _slot.WeaponSlotIndex < 0)
			return -1;

		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null)
			return -1;

		int railIndex = 0;
		for (int i = 0; i < slots.Length && i <= _slot.WeaponSlotIndex; i++)
		{
			if (slots[i].SlotType != WeaponAttachmentSlotType.Rail)
				continue;
			if (i == _slot.WeaponSlotIndex)
				return railIndex;
			railIndex++;
		}

		return -1;
	}

	private static WeaponAttachmentDefinition[] TrimEmptyAttachments(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null)
			return null;

		for (int i = 0; i < _attachments.Length; i++)
		{
			if (_attachments[i] != null)
				return _attachments;
		}

		return null;
	}

	private static ItemDefinition[] TrimEmptyAttachmentItems(ItemDefinition[] _items)
	{
		if (_items == null)
			return null;

		for (int i = 0; i < _items.Length; i++)
		{
			if (_items[i] != null)
				return _items;
		}

		return null;
	}

	private static string GetSlotFallbackLabel(ItemModificationSlotDescriptor _slot)
	{
		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return _slot.WeaponSlotIndex == -2 ? "Side Magazine" : "Magazine";

		if (_slot.Kind == ItemModificationSlotKind.RocketProjectile)
			return "Projectile";

		return _slot.AttachmentSlotType switch
		{
			WeaponAttachmentSlotType.Muzzle => "Muzzle",
			WeaponAttachmentSlotType.UnderBarrel => "Tactical grip",
			WeaponAttachmentSlotType.Rail => "Rail",
			WeaponAttachmentSlotType.Optic => "Optic",
			WeaponAttachmentSlotType.Stock => "Stock",
			WeaponAttachmentSlotType.SideRail => "Side rail",
			_ => "Attachment"
		};
	}

	private static bool IsConflictingMagazineSlotOccupied(
		WeaponRuntimeState _weaponState,
		ItemModificationSlotDescriptor _slot,
		List<ItemModificationSlotDescriptor> _allSlots)
	{
		if (_weaponState == null)
			return false;

		bool isSecondarySlot = _slot.WeaponSlotIndex == -2;

		if (isSecondarySlot && _weaponState.HasPrimaryMagazine)
			return true;

		if (!isSecondarySlot && _slot.WeaponSlotIndex == -1 && _weaponState.HasSecondaryMagazine)
			return true;

		return false;
	}
	#endregion
}
