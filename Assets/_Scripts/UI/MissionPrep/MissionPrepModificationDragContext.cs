using System;

public enum MissionPrepModificationDragSourceKind
{
	None = 0,
	AvailableCatalog = 1,
	PresetBag = 2,
	ModificationSlot = 3,
	PresetMainHandWeapon = 4,
	PresetBagWeapon = 5,
	AvailableWeapon = 6,
	PresetBagHelmet = 7,
	AvailableHelmet = 8,
	PresetHeadHelmet = 9,
	PresetBagBackpack = 10,
	AvailableBackpack = 11,
	PresetBackBackpack = 12
}

public readonly struct MissionPrepModificationDragPayload
{
	public readonly MissionPrepModificationDragSourceKind SourceKind;
	public readonly InventorySlotRuntimeData Item;
	public readonly bool IsPresetMainHand;
	public readonly bool IsPresetHead;
	public readonly bool IsPresetBack;
	public readonly int PresetBagIndex;
	public readonly ItemModificationSlotDescriptor SourceSlotDescriptor;
	public readonly bool SourceWeaponIsMainHand;
	public readonly int SourceWeaponBagIndex;

	public bool HasItem => SourceKind != MissionPrepModificationDragSourceKind.None && !Item.IsEmpty;

	public MissionPrepModificationDragPayload(
		MissionPrepModificationDragSourceKind _sourceKind,
		InventorySlotRuntimeData _item,
		bool _isPresetMainHand,
		int _presetBagIndex,
		ItemModificationSlotDescriptor _sourceSlotDescriptor = default,
		bool _sourceWeaponIsMainHand = false,
		int _sourceWeaponBagIndex = -1,
		bool _isPresetHead = false,
		bool _isPresetBack = false)
	{
		SourceKind = _sourceKind;
		Item = _item;
		IsPresetMainHand = _isPresetMainHand;
		IsPresetHead = _isPresetHead;
		IsPresetBack = _isPresetBack;
		PresetBagIndex = _presetBagIndex;
		SourceSlotDescriptor = _sourceSlotDescriptor;
		SourceWeaponIsMainHand = _sourceWeaponIsMainHand;
		SourceWeaponBagIndex = _sourceWeaponBagIndex;
	}
}

public static class MissionPrepModificationDragContext
{
	#region Private Fields
	private static MissionPrepModificationDragPayload s_Current;
	private static bool s_DropConsumed;
	#endregion

	#region Events
	public static event Action Changed;
	#endregion

	#region Public Properties
	public static MissionPrepModificationDragPayload Current => s_Current;
	public static bool HasActiveModificationItem => s_Current.HasItem && ItemModificationUtility.IsModificationItem(s_Current.Item);
	public static bool WasDropConsumed => s_DropConsumed;
	#endregion

	#region Public Methods
	public static void BeginAvailable(InventorySlotRuntimeData _item)
	{
		Begin(new MissionPrepModificationDragPayload(
			ResolveAvailableSourceKind(_item),
			_item,
			_isPresetMainHand: false,
			_presetBagIndex: -1));
	}

	public static void BeginPreset(
		InventorySlotRuntimeData _item,
		bool _isMainHand,
		int _bagIndex,
		bool _isHead = false,
		bool _isBack = false)
	{
		Begin(new MissionPrepModificationDragPayload(
			ResolvePresetSourceKind(_item, _isMainHand, _isHead, _isBack),
			_item,
			_isMainHand,
			_bagIndex,
			_isPresetHead: _isHead,
			_isPresetBack: _isBack));
	}

	public static void BeginModificationSlot(
		ItemModificationSlotDescriptor _slotDescriptor,
		InventorySlotRuntimeData _item,
		bool _weaponIsMainHand,
		int _weaponBagIndex)
	{
		Begin(new MissionPrepModificationDragPayload(
			MissionPrepModificationDragSourceKind.ModificationSlot,
			_item,
			_isPresetMainHand: false,
			_presetBagIndex: -1,
			_sourceSlotDescriptor: _slotDescriptor,
			_sourceWeaponIsMainHand: _weaponIsMainHand,
			_sourceWeaponBagIndex: _weaponBagIndex));
	}

	public static void NotifyDropConsumed()
	{
		s_DropConsumed = true;
	}

	public static void Clear()
	{
		s_Current = default;
		s_DropConsumed = false;
		InventoryEquipmentEquipHoverContext.ClearAll();
		Changed?.Invoke();
	}

	public static void ResetAfterDrag()
	{
		s_Current = default;
		s_DropConsumed = false;
		InventoryEquipmentEquipHoverContext.ClearAll();
		Changed?.Invoke();
	}
	#endregion

	#region Private Methods
	private static void Begin(MissionPrepModificationDragPayload _payload)
	{
		s_DropConsumed = false;
		s_Current = IsPayloadValid(_payload) ? _payload : default;
		InventoryEquipmentEquipHoverContext.ClearAll();
		Changed?.Invoke();
	}

	private static MissionPrepModificationDragSourceKind ResolveAvailableSourceKind(InventorySlotRuntimeData _item)
	{
		if (ItemModificationUtility.IsModificationItem(_item))
			return MissionPrepModificationDragSourceKind.AvailableCatalog;

		if (MissionPrepWeaponEquipUtility.CanEquipToMainHand(_item))
			return MissionPrepModificationDragSourceKind.AvailableWeapon;

		if (MissionPrepHelmetEquipUtility.CanEquipToHead(_item))
			return MissionPrepModificationDragSourceKind.AvailableHelmet;

		if (MissionPrepBackpackEquipUtility.CanEquipToBack(_item))
			return MissionPrepModificationDragSourceKind.AvailableBackpack;

		return MissionPrepModificationDragSourceKind.None;
	}

	private static MissionPrepModificationDragSourceKind ResolvePresetSourceKind(
		InventorySlotRuntimeData _item,
		bool _isMainHand,
		bool _isHead,
		bool _isBack)
	{
		if (_isMainHand)
			return MissionPrepWeaponEquipUtility.CanEquipToMainHand(_item)
				? MissionPrepModificationDragSourceKind.PresetMainHandWeapon
				: MissionPrepModificationDragSourceKind.None;

		if (_isHead)
			return MissionPrepHelmetEquipUtility.CanEquipToHead(_item)
				? MissionPrepModificationDragSourceKind.PresetHeadHelmet
				: MissionPrepModificationDragSourceKind.None;

		if (_isBack)
			return MissionPrepBackpackEquipUtility.CanEquipToBack(_item)
				? MissionPrepModificationDragSourceKind.PresetBackBackpack
				: MissionPrepModificationDragSourceKind.None;

		if (ItemModificationUtility.IsModificationItem(_item))
			return MissionPrepModificationDragSourceKind.PresetBag;

		if (MissionPrepWeaponEquipUtility.CanEquipToMainHand(_item))
			return MissionPrepModificationDragSourceKind.PresetBagWeapon;

		if (MissionPrepHelmetEquipUtility.CanEquipToHead(_item))
			return MissionPrepModificationDragSourceKind.PresetBagHelmet;

		if (MissionPrepBackpackEquipUtility.CanEquipToBack(_item))
			return MissionPrepModificationDragSourceKind.PresetBagBackpack;

		return MissionPrepModificationDragSourceKind.None;
	}

	private static bool IsPayloadValid(MissionPrepModificationDragPayload _payload)
	{
		if (!_payload.HasItem)
			return false;

		switch (_payload.SourceKind)
		{
			case MissionPrepModificationDragSourceKind.ModificationSlot:
			case MissionPrepModificationDragSourceKind.PresetMainHandWeapon:
			case MissionPrepModificationDragSourceKind.PresetHeadHelmet:
			case MissionPrepModificationDragSourceKind.PresetBackBackpack:
				return true;
			case MissionPrepModificationDragSourceKind.PresetBag:
			case MissionPrepModificationDragSourceKind.AvailableCatalog:
				return ItemModificationUtility.IsModificationItem(_payload.Item);
			case MissionPrepModificationDragSourceKind.PresetBagWeapon:
			case MissionPrepModificationDragSourceKind.AvailableWeapon:
				return MissionPrepWeaponEquipUtility.CanEquipToMainHand(_payload.Item);
			case MissionPrepModificationDragSourceKind.PresetBagHelmet:
			case MissionPrepModificationDragSourceKind.AvailableHelmet:
				return MissionPrepHelmetEquipUtility.CanEquipToHead(_payload.Item);
			case MissionPrepModificationDragSourceKind.PresetBagBackpack:
			case MissionPrepModificationDragSourceKind.AvailableBackpack:
				return MissionPrepBackpackEquipUtility.CanEquipToBack(_payload.Item);
			default:
				return false;
		}
	}
	#endregion
}

/// <summary>
/// Проверки для переноса шлема в слот головы пресета.
/// </summary>
public static class MissionPrepHelmetEquipUtility
{
	#region Public Methods
	public static bool CanEquipToHead(InventorySlotRuntimeData _item) =>
		HelmetEquipUtility.CanEquipToHead(_item);

	public static bool IsHelmetEquipDragSource(MissionPrepModificationDragSourceKind _sourceKind)
	{
		return _sourceKind == MissionPrepModificationDragSourceKind.PresetBagHelmet ||
		       _sourceKind == MissionPrepModificationDragSourceKind.AvailableHelmet;
	}
	#endregion
}

/// <summary>
/// Проверки для переноса рюкзака в слот спины пресета.
/// </summary>
public static class MissionPrepBackpackEquipUtility
{
	#region Public Methods
	public static bool CanEquipToBack(InventorySlotRuntimeData _item) =>
		BackpackEquipUtility.CanEquipToBack(_item);

	public static bool IsBackpackEquipDragSource(MissionPrepModificationDragSourceKind _sourceKind)
	{
		return _sourceKind == MissionPrepModificationDragSourceKind.PresetBagBackpack ||
		       _sourceKind == MissionPrepModificationDragSourceKind.AvailableBackpack;
	}
	#endregion
}

/// <summary>
/// Проверки для переноса оружия в слот основной руки пресета.
/// </summary>
public static class MissionPrepWeaponEquipUtility
{
	#region Public Methods
	public static bool CanEquipToMainHand(InventorySlotRuntimeData _item) =>
		WeaponEquipUtility.CanEquipToMainHand(_item);

	public static bool IsWeaponEquipDragSource(MissionPrepModificationDragSourceKind _sourceKind)
	{
		return _sourceKind == MissionPrepModificationDragSourceKind.PresetBagWeapon ||
		       _sourceKind == MissionPrepModificationDragSourceKind.AvailableWeapon;
	}
	#endregion
}
