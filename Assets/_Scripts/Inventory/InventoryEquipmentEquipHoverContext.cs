using System;

/// <summary>
/// Наведение на шлем/оружие в доступном снаряжении или сумке — подсветка слота экипировки без drag.
/// </summary>
public static class InventoryEquipmentEquipHoverContext
{
	#region Private Fields
	private static InventorySlotRuntimeData s_HoveredHelmet;
	private static InventorySlotRuntimeData s_HoveredWeapon;
	#endregion

	#region Events
	public static event Action Changed;
	#endregion

	#region Public Properties
	public static bool HasActiveHelmetEquipHover =>
		!s_HoveredHelmet.IsEmpty && HelmetEquipUtility.CanEquipToHead(s_HoveredHelmet);

	public static bool HasActiveWeaponEquipHover =>
		!s_HoveredWeapon.IsEmpty && WeaponEquipUtility.CanEquipToMainHand(s_HoveredWeapon);
	#endregion

	#region Public Methods
	public static void SetHoveredHelmet(InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty || !HelmetEquipUtility.CanEquipToHead(_item))
		{
			ClearHoveredHelmet(_item);
			return;
		}

		if (!s_HoveredHelmet.IsEmpty && s_HoveredHelmet.Definition == _item.Definition)
			return;

		s_HoveredHelmet = _item;
		Changed?.Invoke();
	}

	public static void SetHoveredWeapon(InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty || !WeaponEquipUtility.CanEquipToMainHand(_item))
		{
			ClearHoveredWeapon(_item);
			return;
		}

		if (!s_HoveredWeapon.IsEmpty && s_HoveredWeapon.Definition == _item.Definition)
			return;

		s_HoveredWeapon = _item;
		Changed?.Invoke();
	}

	public static void ClearHoveredHelmet(InventorySlotRuntimeData _item)
	{
		if (s_HoveredHelmet.IsEmpty)
			return;

		if (!_item.IsEmpty && !s_HoveredHelmet.IsEmpty && s_HoveredHelmet.Definition != _item.Definition)
			return;

		s_HoveredHelmet = default;
		Changed?.Invoke();
	}

	public static void ClearHoveredWeapon(InventorySlotRuntimeData _item)
	{
		if (s_HoveredWeapon.IsEmpty)
			return;

		if (!_item.IsEmpty && !s_HoveredWeapon.IsEmpty && s_HoveredWeapon.Definition != _item.Definition)
			return;

		s_HoveredWeapon = default;
		Changed?.Invoke();
	}

	public static void ClearAll()
	{
		bool hadHover = HasActiveHelmetEquipHover || HasActiveWeaponEquipHover;
		s_HoveredHelmet = default;
		s_HoveredWeapon = default;

		if (hadHover)
			Changed?.Invoke();
	}
	#endregion
}
