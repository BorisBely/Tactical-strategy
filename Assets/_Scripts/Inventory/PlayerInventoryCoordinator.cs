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

	public bool TryMoveCharacterSlotToGround(int _characterSlotIndex)
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;

		if (inv == null || m_GroundPanel == null || m_CharacterInventoryPanel == null)
			return false;

		if (!inv.TryRemoveAt(_characterSlotIndex, out InventorySlotRuntimeData data))
			return false;

		return TryCompleteCharacterToGroundTransfer(inv, data, null);
	}

	/// <summary>
	/// Ctrl + ЛКМ по занятой ячейке: один путь с данными, без дублирования в списке.
	/// Индекс в рюкзаке считается только по занятым слотам (как порядок в <see cref="CharacterInventory"/>).
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
		int invIndex = GetCharacterInventoryIndexForOccupiedSlot(m_CharacterInventoryPanel, _slot, _inv);
		if (invIndex < 0)
			return false;

		if (!_inv.TryRemoveAt(invIndex, out InventorySlotRuntimeData data))
			return false;

		return TryCompleteCharacterToGroundTransfer(_inv, data, null);
	}

	private static int GetCharacterInventoryIndexForOccupiedSlot(InventoryPanelView _bag, InventorySlotView _target,
		CharacterInventory _inv)
	{
		if (_bag == null || _target == null || _inv == null || !_target.HasItem)
			return -1;

		int filledIndex = 0;
		var slots = _bag.Slots;
		for (int i = 0; i < slots.Count; i++)
		{
			InventorySlotView s = slots[i];
			if (s == null || !s.HasItem)
				continue;
			if (s == _target)
			{
				if (filledIndex >= _inv.Count)
					return -1;
				return filledIndex;
			}

			filledIndex++;
		}

		return -1;
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

	/// <summary>
	/// Завершение drag-and-drop: та же ячейка переезжает в UI рюкзака, данные — в <see cref="CharacterInventory"/>.
	/// </summary>
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
			inv.TryRemoveAt(inv.Count - 1, out _);
			return false;
		}

		return true;
	}

	/// <summary>
	/// Завершение drag-and-drop: ячейка рюкзака переезжает на панель «земля», слот удаляется из <see cref="CharacterInventory"/>.
	/// </summary>
	public bool TryAcceptDraggedCharacterSlot(InventoryCharacterToGroundDrag _drag)
	{
		if (_drag == null || !_drag.WasDraggingThisFrame)
			return false;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		CharacterInventory inv = bindings != null ? bindings.ActiveCharacterInventory : null;
		InventorySlotView slot = _drag.SlotView;
		int index = _drag.CapturedCharacterSlotIndex;

		if (inv == null || m_GroundPanel == null || m_CharacterInventoryPanel == null || slot == null || !slot.HasItem)
			return false;
		if (index < 0 || index >= inv.Count)
			return false;

		if (!inv.TryRemoveAt(index, out InventorySlotRuntimeData data))
			return false;

		return TryCompleteCharacterToGroundTransfer(inv, data, slot);
	}

	private bool TryCompleteCharacterToGroundTransfer(CharacterInventory _inv, InventorySlotRuntimeData _data,
		InventorySlotView _adoptExistingSlotOrNull)
	{
		WorldPickupItem spawned = null;
		ItemDefinition def = _data.Definition;
		if (def != null && def.DropWorldPrefab != null)
		{
			spawned = SpawnDropWorldPickup(_inv, def, _data.DisplayName);
			if (spawned == null)
			{
				_inv.TryAdd(_data);
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
				_inv.TryAdd(_data);
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
			_inv.TryAdd(_data);
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
}
