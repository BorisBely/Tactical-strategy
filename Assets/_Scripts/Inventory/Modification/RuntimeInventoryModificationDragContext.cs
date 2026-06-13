using System;

public enum RuntimeInventoryModificationDragSourceKind
{
	None = 0,
	CharacterBag = 1,
	CharacterMainHand = 2,
	GroundPanel = 3,
	ModificationSlot = 4,
	CharacterBagWeapon = 5,
	GroundWeapon = 6,
	CharacterBagHelmet = 7,
	GroundHelmet = 8,
	CharacterHeadHelmet = 9,
	CharacterBagBackpack = 10,
	GroundBackpack = 11,
	CharacterBackBackpack = 12
}

public readonly struct RuntimeInventoryModificationDragPayload
{
	public readonly RuntimeInventoryModificationDragSourceKind SourceKind;
	public readonly InventorySlotRuntimeData Item;
	public readonly bool IsMainHand;
	public readonly int SlotIndex;
	public readonly ItemModificationSlotDescriptor SourceSlotDescriptor;
	public readonly bool SourceWeaponIsMainHand;
	public readonly int SourceWeaponBagIndex;

	public bool HasItem => SourceKind != RuntimeInventoryModificationDragSourceKind.None && !Item.IsEmpty;

	public RuntimeInventoryModificationDragPayload(
		RuntimeInventoryModificationDragSourceKind _sourceKind,
		InventorySlotRuntimeData _item,
		bool _isMainHand,
		int _slotIndex,
		ItemModificationSlotDescriptor _sourceSlotDescriptor = default,
		bool _sourceWeaponIsMainHand = false,
		int _sourceWeaponBagIndex = -1)
	{
		SourceKind = _sourceKind;
		Item = _item;
		IsMainHand = _isMainHand;
		SlotIndex = _slotIndex;
		SourceSlotDescriptor = _sourceSlotDescriptor;
		SourceWeaponIsMainHand = _sourceWeaponIsMainHand;
		SourceWeaponBagIndex = _sourceWeaponBagIndex;
	}
}

public static class RuntimeInventoryModificationDragContext
{
	#region Private Fields
	private static RuntimeInventoryModificationDragPayload s_Current;
	private static bool s_DropConsumed;
	private static InventorySlotView s_SourceSlotView;
	#endregion

	#region Events
	public static event Action Changed;
	#endregion

	#region Public Properties
	public static RuntimeInventoryModificationDragPayload Current => s_Current;
	public static InventorySlotView SourceSlotView => s_SourceSlotView;
	public static bool HasActiveModificationItem => s_Current.HasItem && ItemModificationUtility.IsModificationItem(s_Current.Item);
	public static bool HasActiveWeaponEquipDrag => s_Current.HasItem && IsWeaponEquipDragSource(s_Current.SourceKind);
	public static bool HasActiveHelmetEquipDrag => s_Current.HasItem && IsHelmetEquipDragSource(s_Current.SourceKind);
	public static bool HasActiveBackpackEquipDrag => s_Current.HasItem && IsBackpackEquipDragSource(s_Current.SourceKind);
	public static bool WasDropConsumed => s_DropConsumed;
	#endregion

	#region Public Methods
	public static bool IsWeaponEquipDragSource(RuntimeInventoryModificationDragSourceKind _sourceKind)
	{
		return _sourceKind == RuntimeInventoryModificationDragSourceKind.CharacterBagWeapon ||
		       _sourceKind == RuntimeInventoryModificationDragSourceKind.GroundWeapon;
	}

	public static bool IsHelmetEquipDragSource(RuntimeInventoryModificationDragSourceKind _sourceKind)
	{
		return _sourceKind == RuntimeInventoryModificationDragSourceKind.CharacterBagHelmet ||
		       _sourceKind == RuntimeInventoryModificationDragSourceKind.GroundHelmet;
	}

	public static bool IsBackpackEquipDragSource(RuntimeInventoryModificationDragSourceKind _sourceKind)
	{
		return _sourceKind == RuntimeInventoryModificationDragSourceKind.CharacterBagBackpack ||
		       _sourceKind == RuntimeInventoryModificationDragSourceKind.GroundBackpack;
	}

	public static void BeginCharacter(
		InventorySlotRuntimeData _item,
		bool _isMainHand,
		int _bagIndex,
		InventorySlotView _sourceSlot = null,
		bool _isHead = false,
		bool _isBack = false)
	{
		SetSourceSlot(_sourceSlot);
		Begin(new RuntimeInventoryModificationDragPayload(
			ResolveCharacterSourceKind(_item, _isMainHand, _isHead, _isBack),
			_item,
			_isMainHand,
			_bagIndex));
	}

	public static void BeginGround(InventorySlotRuntimeData _item, int _groundSlotIndex, InventorySlotView _sourceSlot = null)
	{
		SetSourceSlot(_sourceSlot);
		Begin(new RuntimeInventoryModificationDragPayload(
			ResolveGroundSourceKind(_item),
			_item,
			_isMainHand: false,
			_groundSlotIndex));
	}

	public static void BeginModificationSlot(
		ItemModificationSlotDescriptor _slotDescriptor,
		InventorySlotRuntimeData _item,
		bool _weaponIsMainHand,
		int _weaponBagIndex)
	{
		Begin(new RuntimeInventoryModificationDragPayload(
			RuntimeInventoryModificationDragSourceKind.ModificationSlot,
			_item,
			_isMainHand: false,
			_slotIndex: -1,
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
		s_SourceSlotView = null;

		if (hadPayload)
			Changed?.Invoke();
	}
	#endregion

	#region Private Methods
	private static void SetSourceSlot(InventorySlotView _sourceSlot)
	{
		s_SourceSlotView = _sourceSlot;
	}

	private static void Begin(RuntimeInventoryModificationDragPayload _payload)
	{
		s_DropConsumed = false;
		s_Current = IsPayloadValid(_payload) ? _payload : default;
		Changed?.Invoke();
	}

	private static RuntimeInventoryModificationDragSourceKind ResolveCharacterSourceKind(
		InventorySlotRuntimeData _item,
		bool _isMainHand,
		bool _isHead,
		bool _isBack)
	{
		if (_isMainHand)
			return RuntimeInventoryModificationDragSourceKind.None;

		if (_isHead)
			return HelmetEquipUtility.CanEquipToHead(_item)
				? RuntimeInventoryModificationDragSourceKind.CharacterHeadHelmet
				: RuntimeInventoryModificationDragSourceKind.None;

		if (_isBack)
			return BackpackEquipUtility.CanEquipToBack(_item)
				? RuntimeInventoryModificationDragSourceKind.CharacterBackBackpack
				: RuntimeInventoryModificationDragSourceKind.None;

		if (ItemModificationUtility.IsModificationItem(_item))
			return RuntimeInventoryModificationDragSourceKind.CharacterBag;

		if (WeaponEquipUtility.CanEquipToMainHand(_item))
			return RuntimeInventoryModificationDragSourceKind.CharacterBagWeapon;

		if (HelmetEquipUtility.CanEquipToHead(_item))
			return RuntimeInventoryModificationDragSourceKind.CharacterBagHelmet;

		if (BackpackEquipUtility.CanEquipToBack(_item))
			return RuntimeInventoryModificationDragSourceKind.CharacterBagBackpack;

		return RuntimeInventoryModificationDragSourceKind.None;
	}

	private static RuntimeInventoryModificationDragSourceKind ResolveGroundSourceKind(InventorySlotRuntimeData _item)
	{
		if (ItemModificationUtility.IsModificationItem(_item))
			return RuntimeInventoryModificationDragSourceKind.GroundPanel;

		if (WeaponEquipUtility.CanEquipToMainHand(_item))
			return RuntimeInventoryModificationDragSourceKind.GroundWeapon;

		if (HelmetEquipUtility.CanEquipToHead(_item))
			return RuntimeInventoryModificationDragSourceKind.GroundHelmet;

		if (BackpackEquipUtility.CanEquipToBack(_item))
			return RuntimeInventoryModificationDragSourceKind.GroundBackpack;

		return RuntimeInventoryModificationDragSourceKind.None;
	}

	private static bool IsPayloadValid(RuntimeInventoryModificationDragPayload _payload)
	{
		if (!_payload.HasItem)
			return false;

		switch (_payload.SourceKind)
		{
			case RuntimeInventoryModificationDragSourceKind.ModificationSlot:
			case RuntimeInventoryModificationDragSourceKind.CharacterMainHand:
			case RuntimeInventoryModificationDragSourceKind.CharacterHeadHelmet:
			case RuntimeInventoryModificationDragSourceKind.CharacterBackBackpack:
				return true;
			case RuntimeInventoryModificationDragSourceKind.CharacterBag:
			case RuntimeInventoryModificationDragSourceKind.GroundPanel:
				return ItemModificationUtility.IsModificationItem(_payload.Item);
			case RuntimeInventoryModificationDragSourceKind.CharacterBagWeapon:
			case RuntimeInventoryModificationDragSourceKind.GroundWeapon:
				return WeaponEquipUtility.CanEquipToMainHand(_payload.Item);
			case RuntimeInventoryModificationDragSourceKind.CharacterBagHelmet:
			case RuntimeInventoryModificationDragSourceKind.GroundHelmet:
				return HelmetEquipUtility.CanEquipToHead(_payload.Item);
			case RuntimeInventoryModificationDragSourceKind.CharacterBagBackpack:
			case RuntimeInventoryModificationDragSourceKind.GroundBackpack:
				return BackpackEquipUtility.CanEquipToBack(_payload.Item);
			default:
				return false;
		}
	}
	#endregion
}
