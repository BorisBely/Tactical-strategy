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
	[SerializeField, Min(0)] private int m_CamouflageIndex;
	[SerializeField] private InventorySlotRuntimeData m_MainHandEquipment;
	[SerializeField] private InventorySlotRuntimeData m_HeadEquipment;
	[SerializeField] private InventorySlotRuntimeData m_BackEquipment;
	[SerializeField] private List<InventorySlotRuntimeData> m_BagItems = new List<InventorySlotRuntimeData>();
	#endregion

	#region Public Properties
	public int ArmorVisualIndex => m_ArmorVisualIndex;
	public int CamouflageIndex => m_CamouflageIndex;
	public InventorySlotRuntimeData MainHandEquipment => m_MainHandEquipment;
	public InventorySlotRuntimeData HeadEquipment => m_HeadEquipment;
	public InventorySlotRuntimeData BackEquipment => m_BackEquipment;
	public IReadOnlyList<InventorySlotRuntimeData> BagItems => m_BagItems;
	public int BagCount => m_BagItems.Count;
	public float TotalWeightKg => CalculateWeightKg();
	public float BagWeightKg => CalculateBagWeightKg();
	public float ArmorWeightKg => CalculateArmorWeightKg();
	public float CargoWeightKg => TotalWeightKg - ArmorWeightKg;
	public float TotalMaxWeightKg => MaxBagWeightKg + ArmorWeightKg;
	public float MaxBagWeightKg => CalculateMaxBagWeightKg();
	public int MaxBagCapacity => (int)MaxBagWeightKg;
	public bool IsBagOverweight => CargoWeightKg > MaxBagWeightKg;
	public bool CanAddToBag(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return true;
		return CargoWeightKg + _data.Definition.WeightKg <= MaxBagWeightKg;
	}
	#endregion

	#region Public Methods
	public void SetArmorVisualIndex(int _index)
	{
		m_ArmorVisualIndex = Mathf.Clamp(_index, 0, MissionPrepUnitArmorVisualController.ArmorVariantCount - 1);
	}

	public void SetCamouflageIndex(int _index)
	{
		m_CamouflageIndex = UnitCamouflagePatternUtility.ClampIndex(_index);
	}

	public void SetFromInventory(CharacterInventory _inventory, int _armorVisualIndex)
	{
		SetArmorVisualIndex(_armorVisualIndex);

		if (_inventory == null)
		{
			m_MainHandEquipment = default;
			m_HeadEquipment = default;
			m_BackEquipment = default;
			m_BagItems.Clear();
			return;
		}

		m_MainHandEquipment = MissionPrepInventoryCopyUtility.CloneSlot(_inventory.MainHandEquipment);
		m_HeadEquipment = MissionPrepInventoryCopyUtility.CloneSlot(_inventory.HeadEquipment);
		m_BackEquipment = MissionPrepInventoryCopyUtility.CloneSlot(_inventory.BackEquipment);
		m_BagItems.Clear();

		IReadOnlyList<InventorySlotRuntimeData> bag = _inventory.BagItems;
		for (int i = 0; i < bag.Count; i++)
			m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(bag[i]));
	}

	public void ApplyToInventory(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		_inventory.BeginBatchInventoryChanges();
		try
		{
			_inventory.Clear();

			if (!m_MainHandEquipment.IsEmpty)
				_inventory.RestoreAfterFailedDrop(true, MissionPrepInventoryCopyUtility.CloneSlot(m_MainHandEquipment));

			if (!m_HeadEquipment.IsEmpty)
				_inventory.RestoreAfterFailedDrop(false, true, MissionPrepInventoryCopyUtility.CloneSlot(m_HeadEquipment));

			if (!m_BackEquipment.IsEmpty)
				_inventory.RestoreAfterFailedDrop(false, false, true, MissionPrepInventoryCopyUtility.CloneSlot(m_BackEquipment));

			for (int i = 0; i < m_BagItems.Count; i++)
			{
				InventorySlotRuntimeData bagSlot = MissionPrepInventoryCopyUtility.CloneSlot(m_BagItems[i]);
				if (!bagSlot.IsEmpty)
					_inventory.TryAdd(bagSlot);
			}
		}
		finally
		{
			_inventory.EndBatchInventoryChanges();
		}
	}

	public bool HasInventoryContent()
	{
		if (!m_MainHandEquipment.IsEmpty)
			return true;

		if (!m_HeadEquipment.IsEmpty)
			return true;

		if (!m_BackEquipment.IsEmpty)
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
		ReplaceInventory(_mainHand, default, default, _bagItems);
	}

	public void ReplaceInventory(
		InventorySlotRuntimeData _mainHand,
		InventorySlotRuntimeData _head,
		List<InventorySlotRuntimeData> _bagItems)
	{
		ReplaceInventory(_mainHand, _head, default, _bagItems);
	}

	public void ReplaceInventory(
		InventorySlotRuntimeData _mainHand,
		InventorySlotRuntimeData _head,
		InventorySlotRuntimeData _back,
		List<InventorySlotRuntimeData> _bagItems)
	{
		m_MainHandEquipment = _mainHand;
		m_HeadEquipment = _head;
		m_BackEquipment = _back;
		m_BagItems.Clear();

		if (_bagItems == null)
			return;

		for (int i = 0; i < _bagItems.Count; i++)
		{
			if (!_bagItems[i].IsEmpty)
				m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(_bagItems[i]));
		}
	}

	public bool TryAddToBag(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return false;

		float itemWeight = _data.Definition != null ? _data.Definition.WeightKg : 0f;
		if (CargoWeightKg + itemWeight > MaxBagWeightKg)
			return false;

		InventorySlotRuntimeData copy = MissionPrepInventoryCopyUtility.CloneSlot(_data);
		m_BagItems.Add(copy);
		return true;
	}

	public bool TryUnequipMainHandToBag()
	{
		if (m_MainHandEquipment.IsEmpty)
			return false;

		m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(m_MainHandEquipment));
		m_MainHandEquipment = default;
		return true;
	}

	public bool TryUnequipHeadToBag()
	{
		if (m_HeadEquipment.IsEmpty)
			return false;

		m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(m_HeadEquipment));
		m_HeadEquipment = default;
		return true;
	}

	public bool TryUnequipBackToBag()
	{
		if (m_BackEquipment.IsEmpty)
			return false;

		m_BagItems.Add(MissionPrepInventoryCopyUtility.CloneSlot(m_BackEquipment));
		m_BackEquipment = default;
		TrimExcessBagItemsFromStart();
		return true;
	}

	private void TrimExcessBagItemsFromStart()
	{
		while (CargoWeightKg > MaxBagWeightKg && m_BagItems.Count > 0)
			m_BagItems.RemoveAt(0);
	}

	public bool TryClearMainHand()
	{
		if (m_MainHandEquipment.IsEmpty)
			return false;

		m_MainHandEquipment = default;
		return true;
	}

	public bool TryClearHead()
	{
		if (m_HeadEquipment.IsEmpty)
			return false;

		m_HeadEquipment = default;
		return true;
	}

	public bool TryClearBack()
	{
		if (m_BackEquipment.IsEmpty)
			return false;

		m_BackEquipment = default;
		return true;
	}

	public bool TryRemoveBagItemAt(int _bagIndex)
	{
		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		m_BagItems.RemoveAt(_bagIndex);
		return true;
	}

	public bool TryGetInventorySlot(bool _isMainHandEquipmentSlot, int _bagIndex, out InventorySlotRuntimeData _slot)
	{
		return TryGetInventorySlot(_isMainHandEquipmentSlot, false, _bagIndex, out _slot);
	}

	public bool TryGetInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		int _bagIndex,
		out InventorySlotRuntimeData _slot)
	{
		return TryGetInventorySlot(_isMainHandEquipmentSlot, _isHeadEquipmentSlot, false, _bagIndex, out _slot);
	}

	public bool TryGetInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		bool _isBackEquipmentSlot,
		int _bagIndex,
		out InventorySlotRuntimeData _slot)
	{
		if (_isMainHandEquipmentSlot)
		{
			_slot = m_MainHandEquipment;
			return !_slot.IsEmpty;
		}

		if (_isBackEquipmentSlot)
		{
			_slot = m_BackEquipment;
			return !_slot.IsEmpty;
		}

		if (_isHeadEquipmentSlot)
		{
			_slot = m_HeadEquipment;
			return !_slot.IsEmpty;
		}

		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
		{
			_slot = default;
			return false;
		}

		_slot = m_BagItems[_bagIndex];
		return !_slot.IsEmpty;
	}

	public bool TrySetInventorySlot(bool _isMainHandEquipmentSlot, int _bagIndex, InventorySlotRuntimeData _slot)
	{
		return TrySetInventorySlot(_isMainHandEquipmentSlot, false, _bagIndex, _slot);
	}

	public bool TrySetInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		int _bagIndex,
		InventorySlotRuntimeData _slot)
	{
		return TrySetInventorySlot(_isMainHandEquipmentSlot, _isHeadEquipmentSlot, false, _bagIndex, _slot);
	}

	public bool TrySetInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		bool _isBackEquipmentSlot,
		int _bagIndex,
		InventorySlotRuntimeData _slot)
	{
		if (_slot.IsEmpty)
			return false;

		if (_isMainHandEquipmentSlot)
		{
			m_MainHandEquipment = _slot;
			return true;
		}

		if (_isBackEquipmentSlot)
		{
			if (!BackpackEquipUtility.CanEquipToBack(_slot))
				return false;

			m_BackEquipment = _slot;
			return true;
		}

		if (_isHeadEquipmentSlot)
		{
			if (!HelmetEquipUtility.CanEquipToHead(_slot))
				return false;

			m_HeadEquipment = _slot;
			return true;
		}

		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		m_BagItems[_bagIndex] = _slot;
		return true;
	}

	public bool TryRemoveInventorySlot(bool _isMainHandEquipmentSlot, int _bagIndex, out InventorySlotRuntimeData _removedSlot)
	{
		return TryRemoveInventorySlot(_isMainHandEquipmentSlot, false, _bagIndex, out _removedSlot);
	}

	public bool TryRemoveInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		int _bagIndex,
		out InventorySlotRuntimeData _removedSlot)
	{
		return TryRemoveInventorySlot(_isMainHandEquipmentSlot, _isHeadEquipmentSlot, false, _bagIndex, out _removedSlot);
	}

	public bool TryRemoveInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		bool _isBackEquipmentSlot,
		int _bagIndex,
		out InventorySlotRuntimeData _removedSlot)
	{
		if (!TryGetInventorySlot(_isMainHandEquipmentSlot, _isHeadEquipmentSlot, _isBackEquipmentSlot, _bagIndex, out _removedSlot))
			return false;

		if (_isMainHandEquipmentSlot)
		{
			m_MainHandEquipment = default;
			return true;
		}

		if (_isBackEquipmentSlot)
		{
			m_BackEquipment = default;
			return true;
		}

		if (_isHeadEquipmentSlot)
		{
			m_HeadEquipment = default;
			return true;
		}

		m_BagItems.RemoveAt(_bagIndex);
		return true;
	}

	public bool TryMoveBagItemToHead(int _bagIndex)
	{
		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!HelmetEquipUtility.CanEquipToHead(picked))
			return false;

		InventorySlotRuntimeData previousHead = m_HeadEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_HeadEquipment = MissionPrepInventoryCopyUtility.CloneSlot(picked);

		if (!previousHead.IsEmpty)
			m_BagItems.Insert(_bagIndex, MissionPrepInventoryCopyUtility.CloneSlot(previousHead));

		return true;
	}

	public bool TryEquipExternalItemToHead(InventorySlotRuntimeData _item)
	{
		if (!HelmetEquipUtility.CanEquipToHead(_item))
			return false;

		if (!m_HeadEquipment.IsEmpty)
			TryUnequipHeadToBag();

		m_HeadEquipment = MissionPrepInventoryCopyUtility.CloneSlot(_item);
		return true;
	}

	public bool TryMoveBagItemToBack(int _bagIndex)
	{
		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!BackpackEquipUtility.CanEquipToBack(picked))
			return false;

		InventorySlotRuntimeData previousBack = m_BackEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_BackEquipment = MissionPrepInventoryCopyUtility.CloneSlot(picked);

		if (!previousBack.IsEmpty)
			m_BagItems.Insert(_bagIndex, MissionPrepInventoryCopyUtility.CloneSlot(previousBack));

		TrimExcessBagItemsFromStart();
		return true;
	}

	public bool TryEquipExternalItemToBack(InventorySlotRuntimeData _item)
	{
		if (!BackpackEquipUtility.CanEquipToBack(_item))
			return false;

		if (!m_BackEquipment.IsEmpty)
			TryUnequipBackToBag();

		m_BackEquipment = MissionPrepInventoryCopyUtility.CloneSlot(_item);
		TrimExcessBagItemsFromStart();
		return true;
	}

	public bool TryMoveBagItemToMainHand(int _bagIndex)
	{
		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!MissionPrepWeaponEquipUtility.CanEquipToMainHand(picked))
			return false;

		InventorySlotRuntimeData previousMain = m_MainHandEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_MainHandEquipment = MissionPrepInventoryCopyUtility.CloneSlot(picked);

		if (!previousMain.IsEmpty)
			m_BagItems.Insert(_bagIndex, MissionPrepInventoryCopyUtility.CloneSlot(previousMain));

		return true;
	}
	#endregion

	#region Weight / Capacity
	private float CalculateBagWeightKg()
	{
		float total = 0f;
		for (int i = 0; i < m_BagItems.Count; i++)
		{
			if (!m_BagItems[i].IsEmpty && m_BagItems[i].Definition != null)
				total += m_BagItems[i].Definition.WeightKg + ItemWeightDefaults.GetWeaponModificationWeight(m_BagItems[i]);
		}
		return total;
	}

	private float CalculateWeightKg()
	{
		float total = CalculateBagWeightKg();

		if (!m_MainHandEquipment.IsEmpty && m_MainHandEquipment.Definition != null)
			total += m_MainHandEquipment.Definition.WeightKg + ItemWeightDefaults.GetWeaponModificationWeight(m_MainHandEquipment);
		if (!m_HeadEquipment.IsEmpty && m_HeadEquipment.Definition != null)
			total += m_HeadEquipment.Definition.WeightKg;
		if (!m_BackEquipment.IsEmpty && m_BackEquipment.Definition != null)
			total += m_BackEquipment.Definition.WeightKg;

		total += CalculateArmorWeightKg();
		return total;
	}

	private float CalculateArmorWeightKg()
	{
		if (m_ArmorVisualIndex == MissionPrepUnitArmorVisualController.HeavyArmorIndex)
			return UnitArmorCombatDesign.HeavyArmorWeightKg;

		return UnitArmorCombatDesign.LightArmorWeightKg;
	}

	private float CalculateMaxBagWeightKg()
	{
		if (!m_BackEquipment.IsEmpty && m_BackEquipment.Definition != null)
		{
			float limit = ItemWeightDefaults.GetBackpackWeightLimit(m_BackEquipment.Definition.LocalizationKey);
			if (limit > 0f)
				return limit;
		}

		return ItemWeightDefaults.DefaultBagWeightLimitKg;
	}
	#endregion
}
