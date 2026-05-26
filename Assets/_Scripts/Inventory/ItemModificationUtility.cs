using System.Collections.Generic;
using UnityEngine;

public enum ItemModificationSlotKind
{
	Magazine = 0,
	Attachment = 1
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
	#endregion

	#region Public Methods
	public static bool IsModifiableWeapon(ItemDefinition _definition)
	{
		WeaponDefinition weapon = _definition != null ? _definition.WeaponDefinition : null;
		if (_definition == null || !_definition.IsEquipment || weapon == null)
			return false;

		if (weapon.SupportedMagazineType != MagazineType.None)
			return true;

		WeaponAttachmentSlotDefinition[] slots = weapon.AttachmentSlots;
		if (slots == null)
			return false;

		for (int i = 0; i < slots.Length; i++)
		{
			if (slots[i].SlotType != WeaponAttachmentSlotType.Rail)
				return true;
		}

		return false;
	}

	public static void BuildSlotDescriptors(ItemDefinition _definition, List<ItemModificationSlotDescriptor> _outSlots)
	{
		if (_outSlots == null)
			return;

		_outSlots.Clear();
		WeaponDefinition weapon = _definition != null ? _definition.WeaponDefinition : null;
		if (weapon == null)
			return;

		int displayIndex = 0;
		if (weapon.SupportedMagazineType != MagazineType.None)
			_outSlots.Add(new ItemModificationSlotDescriptor(ItemModificationSlotKind.Magazine, default, -1, displayIndex++));

		WeaponAttachmentSlotDefinition[] slots = weapon.AttachmentSlots;
		if (slots == null)
			return;

		for (int i = 0; i < slots.Length; i++)
		{
			WeaponAttachmentSlotType slotType = slots[i].SlotType;
			if (slotType == WeaponAttachmentSlotType.Rail)
				continue;

			_outSlots.Add(new ItemModificationSlotDescriptor(ItemModificationSlotKind.Attachment, slotType, i, displayIndex++));
		}
	}

	public static string GetSlotLabel(ItemModificationSlotDescriptor _slot)
	{
		string key = GetSlotLabelKey(_slot);
		string fallback = GetSlotFallbackLabel(_slot);
		return LocalizationManager.Get(key, fallback);
	}

	public static string GetSlotLabelKey(ItemModificationSlotDescriptor _slot)
	{
		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return c_MagazineSlotLabelKey;

		return _slot.AttachmentSlotType switch
		{
			WeaponAttachmentSlotType.Muzzle => "weapon.mod_slot.muzzle",
			WeaponAttachmentSlotType.UnderBarrel => "weapon.mod_slot.underbarrel",
			WeaponAttachmentSlotType.Optic => "weapon.mod_slot.optic",
			WeaponAttachmentSlotType.Stock => "weapon.mod_slot.stock",
			_ => "weapon.mod_slot.attachment"
		};
	}

	public static bool IsModificationItem(InventorySlotRuntimeData _item)
	{
		return IsMagazineItem(_item) || IsAttachmentItem(_item);
	}

	public static bool IsMagazineItem(InventorySlotRuntimeData _item)
	{
		return !_item.IsEmpty && _item.Definition != null && _item.Definition.MagazineDefinition != null;
	}

	public static bool IsAttachmentItem(InventorySlotRuntimeData _item)
	{
		return !_item.IsEmpty && _item.Definition != null && _item.Definition.WeaponAttachmentDefinition != null;
	}

	public static bool CanAcceptItem(ItemModificationSlotDescriptor _slot, InventorySlotRuntimeData _weaponSlot, InventorySlotRuntimeData _candidate)
	{
		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null || _candidate.IsEmpty)
			return false;

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return weaponState.CanAcceptMagazineItem(_candidate);

		WeaponAttachmentDefinition attachment = _candidate.Definition != null ? _candidate.Definition.WeaponAttachmentDefinition : null;
		return attachment != null &&
		       attachment.RequiredSlot == _slot.AttachmentSlotType &&
		       _slot.WeaponSlotIndex >= 0;
	}

	/// <summary>
	/// Совместимость предмета каталога/инвентаря с выбранным оружием: модуль, магазин или патроны под калибр.
	/// </summary>
	public static bool IsCompatibleWithWeapon(InventorySlotRuntimeData _weaponSlot, InventorySlotRuntimeData _candidate)
	{
		if (_weaponSlot.IsEmpty || _candidate.IsEmpty || _candidate.Definition == null)
			return false;

		ItemDefinition weaponDefinition = _weaponSlot.Definition;
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
			return weaponState != null && weaponState.CanAcceptMagazineItem(_candidate);
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
		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null)
			return false;

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
		{
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

		_installedItem = InventorySlotRuntimeData.FromDisplayName(attachments[_slot.WeaponSlotIndex].name);
		return true;
	}

	public static bool TryInstallAtSlot(
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotRuntimeData _candidate,
		out InventorySlotRuntimeData _replacedItem)
	{
		_replacedItem = default;
		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null || !CanAcceptItem(_slot, _weaponSlot, _candidate))
			return false;

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return TryInstallMagazine(weaponState, _candidate, out _replacedItem);

		return TryInstallAttachment(_slot, weaponState, _candidate, out _replacedItem);
	}

	public static bool TryClearSlot(
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weaponSlot,
		out InventorySlotRuntimeData _removedItem)
	{
		_removedItem = default;
		WeaponRuntimeState weaponState = GetWeaponState(_weaponSlot);
		if (weaponState == null)
			return false;

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return weaponState.TryEjectMagazine(out _removedItem);

		if (!TryGetInstalledItem(_slot, _weaponSlot, out _removedItem))
			return false;

		WeaponAttachmentDefinition[] attachments = BuildAttachmentArray(weaponState);
		ItemDefinition[] items = BuildAttachmentItemArray(weaponState, attachments.Length);
		if (_slot.WeaponSlotIndex < 0 || _slot.WeaponSlotIndex >= attachments.Length)
			return false;

		attachments[_slot.WeaponSlotIndex] = null;
		items[_slot.WeaponSlotIndex] = null;
		weaponState.SetEquippedAttachmentSlotItems(TrimEmptyAttachments(attachments), TrimEmptyAttachmentItems(items));
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

		for (int i = 0; i < _descriptorBuffer.Count; i++)
		{
			ItemModificationSlotDescriptor descriptor = _descriptorBuffer[i];
			bool hasInstalledItem = TryGetInstalledItem(descriptor, _weaponData, out _);
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

	private static bool TryInstallMagazine(WeaponRuntimeState _weaponState, InventorySlotRuntimeData _candidate, out InventorySlotRuntimeData _replacedItem)
	{
		_replacedItem = default;
		if (_weaponState.HasMagazine && !_weaponState.TryEjectMagazine(out _replacedItem))
			return false;

		if (_weaponState.TryInsertMagazine(_candidate))
			return true;

		if (!_replacedItem.IsEmpty)
			_weaponState.TryInsertMagazine(_replacedItem);

		_replacedItem = default;
		return false;
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
			return false;

		if (items[_slot.WeaponSlotIndex] != null)
			_replacedItem = InventorySlotRuntimeData.FromDefinition(items[_slot.WeaponSlotIndex]);
		else if (attachments[_slot.WeaponSlotIndex] != null)
			_replacedItem = InventorySlotRuntimeData.FromDisplayName(attachments[_slot.WeaponSlotIndex].name);

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
			return "Magazine";

		return _slot.AttachmentSlotType switch
		{
			WeaponAttachmentSlotType.Muzzle => "Muzzle",
			WeaponAttachmentSlotType.UnderBarrel => "Underbarrel",
			WeaponAttachmentSlotType.Optic => "Optic",
			WeaponAttachmentSlotType.Stock => "Stock",
			_ => "Attachment"
		};
	}
	#endregion
}
