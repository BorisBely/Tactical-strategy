using UnityEngine;

/// <summary>
/// Взаимоисключающие слоты прицела: пикатинни (<see cref="WeaponAttachmentSlotType.Optic"/>) и боковая планка (<see cref="WeaponAttachmentSlotType.SideRail"/>).
/// На оружии может быть только один из них, оба, или один — см. <see cref="WeaponDefinition.AttachmentSlots"/>.
/// </summary>
public static class WeaponOpticSlotUtility
{
	public static bool HasAttachmentSlot(WeaponDefinition _weapon, WeaponAttachmentSlotType _slotType)
	{
		if (_weapon == null)
			return false;

		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null)
			return false;

		for (int i = 0; i < slots.Length; i++)
		{
			if (slots[i].SlotType == _slotType)
				return true;
		}

		return false;
	}

	public static bool UsesMutuallyExclusiveOpticMounts(WeaponDefinition _weapon) =>
		HasAttachmentSlot(_weapon, WeaponAttachmentSlotType.Optic) &&
		HasAttachmentSlot(_weapon, WeaponAttachmentSlotType.SideRail);

	public static int FindWeaponSlotIndex(WeaponDefinition _weapon, WeaponAttachmentSlotType _slotType)
	{
		if (_weapon == null)
			return -1;

		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null)
			return -1;

		for (int i = 0; i < slots.Length; i++)
		{
			if (slots[i].SlotType == _slotType)
				return i;
		}

		return -1;
	}

	public static bool IsOpticSlotType(WeaponAttachmentSlotType _slotType) =>
		_slotType == WeaponAttachmentSlotType.Optic || _slotType == WeaponAttachmentSlotType.SideRail;

	public static bool IsConflictingOpticSlotOccupied(
		WeaponDefinition _weapon,
		WeaponRuntimeState _weaponState,
		WeaponAttachmentSlotType _slotType)
	{
		if (_weapon == null || _weaponState == null || !UsesMutuallyExclusiveOpticMounts(_weapon))
			return false;

		if (_slotType == WeaponAttachmentSlotType.Optic)
			return HasEquippedAttachmentInSlot(_weaponState, WeaponAttachmentSlotType.SideRail);

		if (_slotType == WeaponAttachmentSlotType.SideRail)
			return HasEquippedAttachmentInSlot(_weaponState, WeaponAttachmentSlotType.Optic);

		return false;
	}

	public static bool ShouldShowModificationSlot(
		WeaponDefinition _weapon,
		WeaponRuntimeState _weaponState,
		ItemModificationSlotDescriptor _slot,
		bool _expandEmptySlots,
		bool _hasInstalledItem)
	{
		if (_slot.Kind != ItemModificationSlotKind.Attachment || !_expandEmptySlots)
			return _hasInstalledItem || _expandEmptySlots;

		if (!IsOpticSlotType(_slot.AttachmentSlotType))
			return true;

		if (!UsesMutuallyExclusiveOpticMounts(_weapon))
			return true;

		return !IsConflictingOpticSlotOccupied(_weapon, _weaponState, _slot.AttachmentSlotType);
	}

	public static void ClearConflictingOpticSlot(
		WeaponDefinition _weapon,
		WeaponAttachmentSlotType _installingSlotType,
		WeaponAttachmentDefinition[] _attachments,
		ItemDefinition[] _items)
	{
		if (_weapon == null || _attachments == null || !UsesMutuallyExclusiveOpticMounts(_weapon))
			return;

		WeaponAttachmentSlotType clearType = _installingSlotType switch
		{
			WeaponAttachmentSlotType.Optic => WeaponAttachmentSlotType.SideRail,
			WeaponAttachmentSlotType.SideRail => WeaponAttachmentSlotType.Optic,
			_ => default
		};

		if (!IsOpticSlotType(clearType))
			return;

		int clearIndex = FindWeaponSlotIndex(_weapon, clearType);
		if (clearIndex < 0 || clearIndex >= _attachments.Length)
			return;

		_attachments[clearIndex] = null;
		if (_items != null && clearIndex < _items.Length)
			_items[clearIndex] = null;
	}

	private static bool HasEquippedAttachmentInSlot(WeaponRuntimeState _weaponState, WeaponAttachmentSlotType _slotType)
	{
		WeaponDefinition weapon = _weaponState.WeaponDefinition;
		int slotIndex = FindWeaponSlotIndex(weapon, _slotType);
		if (slotIndex < 0)
			return false;

		WeaponAttachmentDefinition[] attachments = _weaponState.EquippedAttachments;
		if (attachments == null || slotIndex >= attachments.Length)
			return false;

		return attachments[slotIndex] != null;
	}
}
