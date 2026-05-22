using System;

public enum MissionPrepModificationDragSourceKind
{
	None = 0,
	AvailableCatalog = 1,
	PresetBag = 2,
	ModificationSlot = 3
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
			MissionPrepModificationDragSourceKind.AvailableCatalog,
			_item,
			_isPresetMainHand: false,
			_presetBagIndex: -1));
	}

	public static void BeginPreset(InventorySlotRuntimeData _item, bool _isMainHand, int _bagIndex)
	{
		MissionPrepModificationDragSourceKind sourceKind = !_isMainHand && ItemModificationUtility.IsModificationItem(_item)
			? MissionPrepModificationDragSourceKind.PresetBag
			: MissionPrepModificationDragSourceKind.None;

		Begin(new MissionPrepModificationDragPayload(sourceKind, _item, _isMainHand, _bagIndex));
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
		s_Current = default;
		s_DropConsumed = false;
	}
	#endregion

	#region Private Methods
	private static void Begin(MissionPrepModificationDragPayload _payload)
	{
		s_DropConsumed = false;
		bool valid = _payload.SourceKind == MissionPrepModificationDragSourceKind.ModificationSlot
			? _payload.HasItem
			: ItemModificationUtility.IsModificationItem(_payload.Item);
		s_Current = valid ? _payload : default;
		Changed?.Invoke();
	}
	#endregion
}
