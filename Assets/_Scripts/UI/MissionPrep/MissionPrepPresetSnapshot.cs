using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сохранённое состояние одного пресета снаряжения: броня + инвентарь.
/// </summary>
[Serializable]
public sealed class MissionPrepPresetSnapshot
{
	#region Serialized Fields
	[SerializeField, Min(0)] private int m_ArmorVisualIndex;
	[SerializeField] private InventorySlotRuntimeData m_MainHandEquipment;
	[SerializeField] private List<InventorySlotRuntimeData> m_BagItems = new List<InventorySlotRuntimeData>();
	#endregion

	#region Public Properties
	public int ArmorVisualIndex => m_ArmorVisualIndex;
	public InventorySlotRuntimeData MainHandEquipment => m_MainHandEquipment;
	public IReadOnlyList<InventorySlotRuntimeData> BagItems => m_BagItems;
	#endregion

	#region Public Methods
	public void SetArmorVisualIndex(int _index)
	{
		m_ArmorVisualIndex = Mathf.Clamp(_index, 0, MissionPrepUnitArmorVisualController.ArmorVariantCount - 1);
	}

	public void SetFromInventory(CharacterInventory _inventory, int _armorVisualIndex)
	{
		SetArmorVisualIndex(_armorVisualIndex);

		if (_inventory == null)
		{
			m_MainHandEquipment = default;
			m_BagItems.Clear();
			return;
		}

		m_MainHandEquipment = MissionPrepInventoryCopyUtility.CloneSlot(_inventory.MainHandEquipment);
		m_BagItems.Clear();

		IReadOnlyList<InventorySlotRuntimeData> bag = _inventory.BagItems;
		for (int i = 0; i < bag.Count; i++)
			m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(bag[i]));
	}

	public void ApplyToInventory(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		_inventory.Clear();

		if (!m_MainHandEquipment.IsEmpty)
			_inventory.RestoreAfterFailedDrop(true, MissionPrepInventoryCopyUtility.CloneSlot(m_MainHandEquipment));

		for (int i = 0; i < m_BagItems.Count; i++)
		{
			InventorySlotRuntimeData bagSlot = MissionPrepInventoryCopyUtility.CloneSlot(m_BagItems[i]);
			if (!bagSlot.IsEmpty)
				_inventory.TryAdd(bagSlot);
		}
	}

	public bool HasInventoryContent()
	{
		if (!m_MainHandEquipment.IsEmpty)
			return true;

		for (int i = 0; i < m_BagItems.Count; i++)
		{
			if (!m_BagItems[i].IsEmpty)
				return true;
		}

		return false;
	}

	public void ReplaceInventory(InventorySlotRuntimeData _mainHand, List<InventorySlotRuntimeData> _bagItems)
	{
		m_MainHandEquipment = _mainHand;
		m_BagItems.Clear();

		if (_bagItems == null)
			return;

		for (int i = 0; i < _bagItems.Count; i++)
		{
			if (!_bagItems[i].IsEmpty)
				m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(_bagItems[i]));
		}
	}
	#endregion
}
