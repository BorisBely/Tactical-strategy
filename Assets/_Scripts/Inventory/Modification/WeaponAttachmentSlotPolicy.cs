using System.Collections.Generic;

/// <summary>
/// Платформенные ограничения слотов модулей. Сокеты и данные в assets не трогаем — только runtime/UI.
/// </summary>
public static class WeaponAttachmentSlotPolicy
{
	#region Private Fields
	private static readonly HashSet<WeaponAttachmentSlotType> s_StockAkDisabledSlotTypes = new HashSet<WeaponAttachmentSlotType>
	{
		WeaponAttachmentSlotType.Optic,
		WeaponAttachmentSlotType.Stock,
		WeaponAttachmentSlotType.UnderBarrel,
		WeaponAttachmentSlotType.Rail
	};

	private static readonly HashSet<WeaponAttachmentSlotType> s_Mod1AkDisabledSlotTypes = new HashSet<WeaponAttachmentSlotType>
	{
		WeaponAttachmentSlotType.Stock
	};

	private static readonly HashSet<WeaponAttachmentSlotType> s_M4BasicOpticNoStockDisabledSlotTypes =
		new HashSet<WeaponAttachmentSlotType>
		{
			WeaponAttachmentSlotType.Stock,
			WeaponAttachmentSlotType.UnderBarrel,
			WeaponAttachmentSlotType.Rail,
			WeaponAttachmentSlotType.SideRail
		};

	private static readonly HashSet<WeaponAttachmentSlotType> s_M4TacticalNoStockDisabledSlotTypes =
		new HashSet<WeaponAttachmentSlotType>
		{
			WeaponAttachmentSlotType.Stock
		};

	private static readonly HashSet<WeaponAttachmentSlotType> s_MachineGunSideRailsDisabledSlotTypes =
		new HashSet<WeaponAttachmentSlotType>
		{
			WeaponAttachmentSlotType.Stock
		};
	#endregion

	#region Public Methods
	public static bool IsSlotTypeEnabled(WeaponDefinition _weapon, WeaponAttachmentSlotType _slotType)
	{
		if (_weapon == null)
			return false;

		if (!TryGetDisabledSlotTypes(_weapon.AttachmentSlotProfile, out HashSet<WeaponAttachmentSlotType> disabledTypes))
			return true;

		return !disabledTypes.Contains(_slotType);
	}

	public static bool IsWeaponSlotEnabled(WeaponDefinition _weapon, int _weaponSlotIndex)
	{
		if (_weapon == null || _weaponSlotIndex < 0)
			return false;

		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null || _weaponSlotIndex >= slots.Length)
			return false;

		if (!IsSlotTypeEnabled(_weapon, slots[_weaponSlotIndex].SlotType))
			return false;

		if (slots[_weaponSlotIndex].SlotType != WeaponAttachmentSlotType.Rail)
			return true;

		int railIndex = GetRailSocketIndex(slots, _weaponSlotIndex);
		return !IsRailSocketDisabled(_weapon.AttachmentSlotProfile, railIndex);
	}

	public static bool IsModificationSlotEnabled(WeaponDefinition _weapon, ItemModificationSlotDescriptor _slot)
	{
		if (_weapon == null || _slot.Kind != ItemModificationSlotKind.Attachment)
			return true;

		return IsWeaponSlotEnabled(_weapon, _slot.WeaponSlotIndex);
	}
	#endregion

	#region Private Methods
	private static bool IsRailSocketDisabled(WeaponAttachmentSlotProfile _profile, int _railSocketIndex)
	{
		return _profile == WeaponAttachmentSlotProfile.MachineGunSideRails && _railSocketIndex == 0;
	}

	private static int GetRailSocketIndex(WeaponAttachmentSlotDefinition[] _slots, int _weaponSlotIndex)
	{
		int railIndex = 0;
		for (int i = 0; i < _slots.Length && i <= _weaponSlotIndex; i++)
		{
			if (_slots[i].SlotType != WeaponAttachmentSlotType.Rail)
				continue;
			if (i == _weaponSlotIndex)
				return railIndex;
			railIndex++;
		}

		return -1;
	}

	private static bool TryGetDisabledSlotTypes(
		WeaponAttachmentSlotProfile _profile,
		out HashSet<WeaponAttachmentSlotType> _disabledTypes)
	{
		switch (_profile)
		{
			case WeaponAttachmentSlotProfile.StockAk:
				_disabledTypes = s_StockAkDisabledSlotTypes;
				return true;
			case WeaponAttachmentSlotProfile.Mod1Ak:
				_disabledTypes = s_Mod1AkDisabledSlotTypes;
				return true;
			case WeaponAttachmentSlotProfile.M4BasicOpticNoStock:
				_disabledTypes = s_M4BasicOpticNoStockDisabledSlotTypes;
				return true;
			case WeaponAttachmentSlotProfile.M4TacticalNoStock:
				_disabledTypes = s_M4TacticalNoStockDisabledSlotTypes;
				return true;
			case WeaponAttachmentSlotProfile.MachineGunSideRails:
				_disabledTypes = s_MachineGunSideRailsDisabledSlotTypes;
				return true;
			default:
				_disabledTypes = null;
				return false;
		}
	}
	#endregion
}
