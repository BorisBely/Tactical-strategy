/// <summary>
/// На оружии допускается только один лазерный модуль:
/// тактический ЛЦУ — сверху или справа, компактный — только справа.
/// </summary>
public static class WeaponLaserSlotUtility
{
	public static bool IsLaserAttachment(WeaponAttachmentDefinition _attachment)
	{
		return _attachment != null && _attachment.AttachmentType == WeaponAttachmentType.LaserDesignator;
	}

	public static int CountEquippedLaserAttachments(WeaponRuntimeState _weaponState)
	{
		return CountEquippedLasersExcept(_weaponState, -1);
	}

	public static bool HasLaserOnOtherSlot(WeaponRuntimeState _weaponState, int _exceptWeaponSlotIndex)
	{
		return CountEquippedLasersExcept(_weaponState, _exceptWeaponSlotIndex) > 0;
	}

	public static void ClearConflictingLaserSlots(
		int _installingWeaponSlotIndex,
		WeaponAttachmentDefinition[] _attachments,
		ItemDefinition[] _items)
	{
		if (_attachments == null)
			return;

		for (int i = 0; i < _attachments.Length; i++)
		{
			if (i == _installingWeaponSlotIndex)
				continue;
			if (!IsLaserAttachment(_attachments[i]))
				continue;

			_attachments[i] = null;
			if (_items != null && i < _items.Length)
				_items[i] = null;
		}
	}

	private static int CountEquippedLasersExcept(WeaponRuntimeState _weaponState, int _exceptWeaponSlotIndex)
	{
		if (_weaponState == null)
			return 0;

		WeaponAttachmentDefinition[] attachments = _weaponState.EquippedAttachments;
		if (attachments == null)
			return 0;

		int count = 0;
		for (int i = 0; i < attachments.Length; i++)
		{
			if (i == _exceptWeaponSlotIndex)
				continue;
			if (IsLaserAttachment(attachments[i]))
				count++;
		}

		return count;
	}
}
