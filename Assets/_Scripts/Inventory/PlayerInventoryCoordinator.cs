using UnityEngine;

/// <summary>
/// Связка панели «земля» и UI рюкзака. Предметы в рюкзаке хранятся в <see cref="CharacterInventory"/> активного юнита
/// (<see cref="InventoryScreenBindings.ActiveCharacterInventory"/>).
/// </summary>
[DisallowMultipleComponent]
public class PlayerInventoryCoordinator : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private InventoryPanelView m_GroundPanel;
	[SerializeField] private InventoryPanelView m_CharacterInventoryPanel;
	#endregion

	#region Public Properties
	public InventoryPanelView GroundPanel => m_GroundPanel;
	public InventoryPanelView CharacterInventoryPanel => m_CharacterInventoryPanel;
	#endregion

	#region Public Methods
	public bool TryMoveGroundSlotToCharacter(int _groundSlotIndex)
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;

		if (inv == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		var groundSlots = m_GroundPanel.Slots;
		if (_groundSlotIndex < 0 || _groundSlotIndex >= groundSlots.Count)
			return false;

		InventorySlotView slot = groundSlots[_groundSlotIndex];
		if (!slot.TryTakeItem(out InventorySlotRuntimeData data))
			return false;

		InventorySlotRuntimeData forInv = data;
		forInv.WorldSource = null;

		if (!inv.TryAdd(forInv))
		{
			slot.SetItem(data);
			return false;
		}

		if (data.WorldSource != null)
			data.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(slot);
		inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	/// <summary>
	/// Сброс в мир: <c>-1</c> — слот основного оружия, иначе индекс в <see cref="CharacterInventory.BagItems"/>.
	/// </summary>
	public bool TryMoveCharacterSlotToGround(int _characterSlotIndex)
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;

		if (inv == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		InventorySlotRuntimeData data;
		if (_characterSlotIndex == -1)
		{
			if (!inv.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else
		{
			if (!inv.TryRemoveBagAt(_characterSlotIndex, out data))
				return false;
		}

		return TryCompleteCharacterToGroundTransfer(inv, data, null, _characterSlotIndex == -1);
	}

	/// <summary>
	/// Ctrl + ЛКМ по занятой ячейке: быстрый перенос на землю / с земли.
	/// </summary>
	public bool TryQuickTransferCtrlClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem)
			return false;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;
		if (inv == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		if (IsSlotOnPanel(_slot, m_GroundPanel))
			return TryQuickTransferGroundToCharacterInternal(inv, _slot);

		if (IsSlotOnPanel(_slot, m_CharacterInventoryPanel))
			return TryQuickTransferCharacterToGroundInternal(inv, _slot);

		return false;
	}

	/// <summary>
	/// Определить, откуда снять предмет: слот основного оружия (первый на панели) или сумка.
	/// </summary>
	public bool TryResolveCharacterInventorySlot(InventorySlotView _slot, CharacterInventory _inv, out bool _isMainHand,
		out int _bagIndex)
	{
		_isMainHand = false;
		_bagIndex = -1;

		if (m_CharacterInventoryPanel == null || _slot == null || _inv == null || !_slot.HasItem)
			return false;

		if (!IsSlotOnPanel(_slot, m_CharacterInventoryPanel))
			return false;

		int containerIndex = m_CharacterInventoryPanel.GetInventorySlotContainerIndex(_slot);
		if (containerIndex < 0)
			return false;

		int lead = m_CharacterInventoryPanel.LeadingEquipmentSlotCount;
		if (containerIndex < lead)
		{
			if (containerIndex != 0)
				return false;
			_isMainHand = true;
			return _inv.HasMainHandEquipment;
		}

		_bagIndex = containerIndex - lead;
		return _bagIndex >= 0 && _bagIndex < _inv.BagCount;
	}

	private static bool IsSlotOnPanel(InventorySlotView _slot, InventoryPanelView _panel)
	{
		if (_panel == null)
			return false;
		var slots = _panel.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			if (slots[i] == _slot)
				return true;
		}

		return false;
	}

	private bool TryQuickTransferGroundToCharacterInternal(CharacterInventory _inv, InventorySlotView _slot)
	{
		if (!_slot.TryTakeItem(out InventorySlotRuntimeData data))
			return false;

		InventorySlotRuntimeData forInv = data;
		forInv.WorldSource = null;

		if (!_inv.TryAdd(forInv))
		{
			_slot.SetItem(data);
			return false;
		}

		if (data.WorldSource != null)
			data.WorldSource.OnTransferredToCharacterInventory();

		m_GroundPanel.NotifyGroundSlotItemTakenAway(_slot);
		_inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	private bool TryQuickTransferCharacterToGroundInternal(CharacterInventory _inv, InventorySlotView _slot)
	{
		if (!TryResolveCharacterInventorySlot(_slot, _inv, out bool isMainHand, out int bagIndex))
			return false;

		InventorySlotRuntimeData data;
		if (isMainHand)
		{
			if (!_inv.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else
		{
			if (!_inv.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		return TryCompleteCharacterToGroundTransfer(_inv, data, null, isMainHand);
	}

	private static WorldPickupItem SpawnDropWorldPickup(CharacterInventory _inv, ItemDefinition _def, string _displayName)
	{
		_inv.GetDropWorldPose(out Vector3 pos, out Quaternion rot);
		GameObject go = Object.Instantiate(_def.DropWorldPrefab, pos, rot);
		WorldPickupItem pickup = go.GetComponent<WorldPickupItem>();
		if (pickup == null)
		{
			Object.Destroy(go);
			return null;
		}

		pickup.ConfigureForDroppedFromInventory(_def, _displayName);
		return pickup;
	}

	public bool TryAcceptDraggedGroundSlot(InventoryGroundToCharacterDrag _drag)
	{
		if (_drag == null || !_drag.WasDraggingThisFrame)
			return false;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;
		InventorySlotView slot = _drag.SlotView;
		if (inv == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;

		InventorySlotRuntimeData data = slot.Data;
		InventorySlotRuntimeData forInv = data;
		forInv.WorldSource = null;

		if (!inv.TryAdd(forInv))
			return false;

		if (data.WorldSource != null)
			data.WorldSource.OnTransferredToCharacterInventory();

		slot.SetItem(forInv);
		if (!m_CharacterInventoryPanel.AdoptDraggedSlot(slot))
		{
			if (inv.BagCount > 0)
				inv.TryRemoveBagAt(inv.BagCount - 1, out _);
			return false;
		}

		return true;
	}

	public bool TryAcceptDraggedCharacterSlot(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null || !_drag.WasDraggingThisFrame)
			return false;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;
		InventorySlotView slot = _drag.SlotView;

		if (inv == null || m_GroundPanel == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;

		InventorySlotRuntimeData data;
		if (_drag.CapturedFromMainHandEquipmentSlot)
		{
			if (!inv.TryRemoveMainHandEquipment(out data))
				return false;
		}
		else
		{
			int bagIndex = _drag.CapturedBagIndex;
			if (bagIndex < 0 || bagIndex >= inv.BagCount)
				return false;
			if (!inv.TryRemoveBagAt(bagIndex, out data))
				return false;
		}

		return TryCompleteCharacterToGroundTransfer(inv, data, slot, _drag.CapturedFromMainHandEquipmentSlot);
	}

	private bool TryCompleteCharacterToGroundTransfer(CharacterInventory _inv, InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull, bool _removedFromMainHandSlot)
	{
		WorldPickupItem spawned = null;
		ItemDefinition def = _data.Definition;
		if (def != null && def.DropWorldPrefab != null)
		{
			spawned = SpawnDropWorldPickup(_inv, def, _data.DisplayName);
			if (spawned == null)
			{
				_inv.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
				_inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
				return false;
			}
		}

		InventorySlotRuntimeData groundData = _data;
		groundData.WorldSource = spawned;

		bool placed;
		if (_adoptExistingSlotOrNull != null)
		{
			if (!m_GroundPanel.AdoptDraggedSlot(_adoptExistingSlotOrNull))
			{
				_inv.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
				_inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
				if (spawned != null)
					Object.Destroy(spawned.gameObject);
				return false;
			}

			_adoptExistingSlotOrNull.SetItem(groundData);
			placed = true;
		}
		else
			placed = m_GroundPanel.TryAdd(groundData);

		if (!placed)
		{
			_inv.RestoreAfterFailedDrop(_removedFromMainHandSlot, _data);
			_inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
			if (spawned != null)
				Object.Destroy(spawned.gameObject);
			return false;
		}

		if (spawned != null)
			spawned.RegisterListedInGroundUi();

		_inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}

	#endregion

	#region Equipment
	/// <summary>
	/// Двойной клик: слот оружия — снять в сумку; сумка — экипировать.
	/// Если в руках уже тот же <see cref="ItemDefinition"/>, что и у строки сумки, повторный клик по сумке снимает с рук в сумку (без обмена).
	/// </summary>
	public bool TryEquipFromCharacterBagDoubleClick(InventorySlotView _slot)
	{
		if (_slot == null || !_slot.HasItem)
			return false;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;
		if (inv == null || m_CharacterInventoryPanel == null)
			return false;

		if (!TryResolveCharacterInventorySlot(_slot, inv, out bool isMainHand, out int bagIndex))
			return false;

		if (isMainHand)
		{
			if (!inv.TryUnequipMainHandToBag())
				return false;
			inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		InventorySlotRuntimeData data = _slot.Data;
		if (data.Definition == null || !data.Definition.IsEquipment)
			return false;

		if (inv.HasMainHandEquipment && inv.MainHandEquipment.Definition == data.Definition)
		{
			if (!inv.TryUnequipMainHandToBag())
				return false;
			inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
			return true;
		}

		UnitEquipment equipment = inv.GetComponentInChildren<UnitEquipment>(true);
		if (equipment == null)
		{
			Debug.LogWarning($"{nameof(PlayerInventoryCoordinator)}: на юните нет {nameof(UnitEquipment)}.", this);
			return false;
		}

		if (!inv.TryMoveBagItemToMainHand(bagIndex, equipment))
			return false;

		inv.RepaintInventoryPanel(m_CharacterInventoryPanel);
		return true;
	}
	#endregion
}
