using System;

public enum MissionPrepModificationDragSourceKind
{
	None = 0,
	AvailableCatalog = 1,
	PresetBag = 2,
	ModificationSlot = 3,
	PresetMainHandWeapon = 4,
	PresetBagWeapon = 5,
	AvailableWeapon = 6
}

public readonly struct MissionPrepModificationDragPayload
{
	public readonly MissionPrepModificationDragSourceKind SourceKind;
	public readonly InventorySlotRuntimeData Item;
	public readonly bool IsPresetMainHand;
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
		int _sourceWeaponBagIndex = -1)
	{
		SourceKind = _sourceKind;
		Item = _item;
		IsPresetMainHand = _isPresetMainHand;
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

	public static void BeginPreset(InventorySlotRuntimeData _item, bool _isMainHand, int _bagIndex)
	{
		Begin(new MissionPrepModificationDragPayload(
			ResolvePresetSourceKind(_item, _isMainHand),
			_item,
			_isMainHand,
			_bagIndex));
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
		bool hadPayload = s_Current.HasItem;
		s_Current = default;

		if (hadPayload)
			Changed?.Invoke();
	}

	public static void ResetAfterDrag()
	{
		bool hadPayload = s_Current.HasItem;
		s_Current = default;
		s_DropConsumed = false;

		if (hadPayload)
			Changed?.Invoke();
	}
	#endregion

	#region Private Methods
	private static void Begin(MissionPrepModificationDragPayload _payload)
	{
		s_DropConsumed = false;
		s_Current = IsPayloadValid(_payload) ? _payload : default;
		Changed?.Invoke();
	}

	private static MissionPrepModificationDragSourceKind ResolveAvailableSourceKind(InventorySlotRuntimeData _item)
	{
		if (ItemModificationUtility.IsModificationItem(_item))
			return MissionPrepModificationDragSourceKind.AvailableCatalog;

		if (MissionPrepWeaponEquipUtility.CanEquipToMainHand(_item))
			return MissionPrepModificationDragSourceKind.AvailableWeapon;

		return MissionPrepModificationDragSourceKind.None;
	}

	private static MissionPrepModificationDragSourceKind ResolvePresetSourceKind(InventorySlotRuntimeData _item, bool _isMainHand)
	{
		if (_isMainHand)
			return MissionPrepWeaponEquipUtility.CanEquipToMainHand(_item)
				? MissionPrepModificationDragSourceKind.PresetMainHandWeapon
				: MissionPrepModificationDragSourceKind.None;

		if (ItemModificationUtility.IsModificationItem(_item))
			return MissionPrepModificationDragSourceKind.PresetBag;

		if (MissionPrepWeaponEquipUtility.CanEquipToMainHand(_item))
			return MissionPrepModificationDragSourceKind.PresetBagWeapon;

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
				return true;
			case MissionPrepModificationDragSourceKind.PresetBag:
			case MissionPrepModificationDragSourceKind.AvailableCatalog:
				return ItemModificationUtility.IsModificationItem(_payload.Item);
			case MissionPrepModificationDragSourceKind.PresetBagWeapon:
			case MissionPrepModificationDragSourceKind.AvailableWeapon:
				return MissionPrepWeaponEquipUtility.CanEquipToMainHand(_payload.Item);
			default:
				return false;
		}
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
