using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инвентарь машины: 3 экип-слота турели + багаж 150 кг.
/// UI-маппинг leading-слотов: 0 = орудие, 1 = фронт-щит, 2 = окружной щит.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleInventory : MonoBehaviour
{
	#region Constants
	public const int LeadingEquipmentSlotCount = 3;
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField, Min(1f)] private float m_MaxCargoWeightKg = ItemWeightDefaults.VehicleCargoWeightLimitKg;
	[SerializeField] private InventorySlotRuntimeData m_TurretWeapon;
	[SerializeField] private InventorySlotRuntimeData m_FrontalShield;
	[SerializeField] private InventorySlotRuntimeData m_SurroundShield;
	[SerializeField] private List<InventorySlotRuntimeData> m_BagItems = new List<InventorySlotRuntimeData>();
	#endregion

	#region Private Fields
	private bool m_ExchangeModificationAllowed;
	private int m_InventoryChangeBatchDepth;
	private bool m_HasPendingInventoryChange;
	#endregion

	#region Events
	public event Action<VehicleInventory> InventoryChanged;
	#endregion

	#region Public Properties
	public VehicleController Vehicle => m_Vehicle;
	public float MaxCargoWeightKg => m_MaxCargoWeightKg;
	public InventorySlotRuntimeData TurretWeapon => m_TurretWeapon;
	public InventorySlotRuntimeData FrontalShield => m_FrontalShield;
	public InventorySlotRuntimeData SurroundShield => m_SurroundShield;
	/// <summary>UI alias: leading slot 0.</summary>
	public InventorySlotRuntimeData MainHandEquipment => m_TurretWeapon;
	/// <summary>UI alias: leading slot 1.</summary>
	public InventorySlotRuntimeData HeadEquipment => m_FrontalShield;
	/// <summary>UI alias: leading slot 2.</summary>
	public InventorySlotRuntimeData BackEquipment => m_SurroundShield;
	public IReadOnlyList<InventorySlotRuntimeData> BagItems => m_BagItems;
	public int BagCount => m_BagItems.Count;
	public bool HasTurretWeapon => !m_TurretWeapon.IsEmpty;
	public bool HasFrontalShield => !m_FrontalShield.IsEmpty;
	public bool HasSurroundShield => !m_SurroundShield.IsEmpty;
	public bool HasMainHandEquipment => HasTurretWeapon;
	public bool HasHeadEquipment => HasFrontalShield;
	public bool HasBackEquipment => HasSurroundShield;
	public bool HasAnyEquipmentSlotOccupied =>
		HasTurretWeapon || HasFrontalShield || HasSurroundShield;
	public bool CanUseGunnerSeat => HasTurretWeapon;
	public float CargoWeightKg => CalculateCargoWeightKg();
	public bool IsBagOverweight => CargoWeightKg > m_MaxCargoWeightKg;
	public bool CanDropToWorld => false;
	public bool CanModifyContents => m_ExchangeModificationAllowed || HasLivingOccupant();
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
		EnsureRuntimeStatesInitialized();
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle)
	{
		m_Vehicle = _vehicle;
	}

	public void SetExchangeModificationAllowed(bool _allowed)
	{
		m_ExchangeModificationAllowed = _allowed;
	}

	/// <summary>UI/reload: оповестить панели после мутации InstanceState без смены слота.</summary>
	public void NotifyContentsChanged()
	{
		NotifyInventoryChanged();
	}

	public void BeginInventoryChangeBatch()
	{
		m_InventoryChangeBatchDepth++;
	}

	public void EndInventoryChangeBatch()
	{
		m_InventoryChangeBatchDepth = Mathf.Max(0, m_InventoryChangeBatchDepth - 1);
		if (m_InventoryChangeBatchDepth == 0 && m_HasPendingInventoryChange)
			FlushInventoryChanged();
	}

	public bool TryAdd(InventorySlotRuntimeData _data)
	{
		if (!CanModifyContents || _data.IsEmpty)
			return false;

		float itemWeight = _data.Definition != null ? _data.Definition.WeightKg : 0f;
		if (CargoWeightKg + itemWeight > m_MaxCargoWeightKg)
			return false;

		InventorySlotRuntimeData copy = _data;
		EnsureSlotHasInstanceState(ref copy);
		copy.WorldSource = null;
		m_BagItems.Add(copy);
		NotifyInventoryChanged();
		return true;
	}

	public bool TryRemoveBagAt(int _index, out InventorySlotRuntimeData _removed)
	{
		_removed = default;
		if (!CanModifyContents || _index < 0 || _index >= m_BagItems.Count)
			return false;

		_removed = m_BagItems[_index];
		m_BagItems.RemoveAt(_index);
		NotifyInventoryChanged();
		return true;
	}

	/// <summary>Добавить предмет в багаж, игнорируя лимит веса (пустой короб после перезарядки).</summary>
	public bool ForceAddToBag(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return false;

		InventorySlotRuntimeData copy = _data;
		EnsureSlotHasInstanceState(ref copy);
		copy.WorldSource = null;
		m_BagItems.Add(copy);
		NotifyInventoryChanged();
		return true;
	}

	public bool TrySetBagItemAt(int _index, InventorySlotRuntimeData _updated)
	{
		if (!CanModifyContents || _index < 0 || _index >= m_BagItems.Count)
			return false;

		m_BagItems[_index] = _updated;
		NotifyInventoryChanged();
		return true;
	}

	public bool TryGetEquipmentSlot(VehicleEquipmentSlotId _slotId, out InventorySlotRuntimeData _slot)
	{
		_slot = GetEquipmentSlot(_slotId);
		return !_slot.IsEmpty;
	}

	public InventorySlotRuntimeData GetEquipmentSlot(VehicleEquipmentSlotId _slotId)
	{
		return _slotId switch
		{
			VehicleEquipmentSlotId.TurretWeapon => m_TurretWeapon,
			VehicleEquipmentSlotId.FrontalShield => m_FrontalShield,
			VehicleEquipmentSlotId.SurroundShield => m_SurroundShield,
			_ => default
		};
	}

	public bool CanAcceptInSlot(VehicleEquipmentSlotId _slotId, InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty || _item.Definition == null)
			return false;

		return _slotId switch
		{
			VehicleEquipmentSlotId.TurretWeapon => _item.Definition.IsTurretWeapon,
			VehicleEquipmentSlotId.FrontalShield =>
				_item.Definition.IsTurretFrontalShield,
			VehicleEquipmentSlotId.SurroundShield => _item.Definition.IsTurretSurroundShield,
			_ => false
		};
	}

	public bool TryEquipFromBag(int _bagIndex, VehicleEquipmentSlotId _slotId)
	{
		if (!CanModifyContents || _bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!CanAcceptInSlot(_slotId, picked))
			return false;

		InventorySlotRuntimeData previous = GetEquipmentSlot(_slotId);
		m_BagItems.RemoveAt(_bagIndex);
		SetEquipmentSlot(_slotId, picked);
		if (!previous.IsEmpty)
			m_BagItems.Insert(_bagIndex, previous);

		NotifyInventoryChanged();
		return true;
	}

	public bool TryEquipExternal(InventorySlotRuntimeData _item, VehicleEquipmentSlotId _slotId)
	{
		if (!CanModifyContents || !CanAcceptInSlot(_slotId, _item))
			return false;

		if (!GetEquipmentSlot(_slotId).IsEmpty && !TryUnequipToBag(_slotId))
			return false;

		SetEquipmentSlot(_slotId, _item);
		NotifyInventoryChanged();
		return true;
	}

	public bool TryUnequipToBag(VehicleEquipmentSlotId _slotId)
	{
		if (!CanModifyContents)
			return false;

		InventorySlotRuntimeData equipped = GetEquipmentSlot(_slotId);
		if (equipped.IsEmpty)
			return false;

		float itemWeight = equipped.Definition != null ? equipped.Definition.WeightKg : 0f;
		float weightWithoutSlot = CargoWeightKg - itemWeight;
		if (weightWithoutSlot + itemWeight > m_MaxCargoWeightKg &&
		    GetEquipmentSlotWeightInCargo(_slotId) <= 0f)
		{
		}

		SetEquipmentSlot(_slotId, default);
		m_BagItems.Add(equipped);
		NotifyInventoryChanged();
		return true;
	}

	public bool TryRemoveEquipment(VehicleEquipmentSlotId _slotId, out InventorySlotRuntimeData _removed)
	{
		_removed = default;
		if (!CanModifyContents)
			return false;

		_removed = GetEquipmentSlot(_slotId);
		if (_removed.IsEmpty)
			return false;

		SetEquipmentSlot(_slotId, default);
		NotifyInventoryChanged();
		return true;
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
			_slot = m_TurretWeapon;
			return !_slot.IsEmpty;
		}

		if (_isHeadEquipmentSlot)
		{
			_slot = m_FrontalShield;
			return !_slot.IsEmpty;
		}

		if (_isBackEquipmentSlot)
		{
			_slot = m_SurroundShield;
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

	public bool TrySetInventorySlot(
		bool _isMainHandEquipmentSlot,
		bool _isHeadEquipmentSlot,
		bool _isBackEquipmentSlot,
		int _bagIndex,
		InventorySlotRuntimeData _slot)
	{
		if (!CanModifyContents)
			return false;

		if (_isMainHandEquipmentSlot)
		{
			if (!_slot.IsEmpty && (_slot.Definition == null || !_slot.Definition.IsTurretWeapon))
				return false;
			m_TurretWeapon = _slot;
			NotifyInventoryChanged();
			return true;
		}

		if (_isHeadEquipmentSlot)
		{
			if (!_slot.IsEmpty && !CanAcceptInSlot(VehicleEquipmentSlotId.FrontalShield, _slot))
				return false;
			m_FrontalShield = _slot;
			NotifyInventoryChanged();
			return true;
		}

		if (_isBackEquipmentSlot)
		{
			if (!_slot.IsEmpty && (_slot.Definition == null || !_slot.Definition.IsTurretSurroundShield))
				return false;
			m_SurroundShield = _slot;
			NotifyInventoryChanged();
			return true;
		}

		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		m_BagItems[_bagIndex] = _slot;
		NotifyInventoryChanged();
		return true;
	}

	public void RepaintInventoryPanel(InventoryPanelView _panel)
	{
		if (_panel == null)
			return;
		_panel.RepaintFromVehicleInventory(this);
	}

	public VehicleEquipmentSlotId ResolveEquipmentSlotId(bool _isMainHand, bool _isHead, bool _isBack)
	{
		if (_isMainHand)
			return VehicleEquipmentSlotId.TurretWeapon;
		if (_isHead)
			return VehicleEquipmentSlotId.FrontalShield;
		return VehicleEquipmentSlotId.SurroundShield;
	}
	#endregion

	#region Private Methods
	private void SetEquipmentSlot(VehicleEquipmentSlotId _slotId, InventorySlotRuntimeData _slot)
	{
		EnsureSlotHasInstanceState(ref _slot);
		switch (_slotId)
		{
			case VehicleEquipmentSlotId.TurretWeapon:
				m_TurretWeapon = _slot;
				break;
			case VehicleEquipmentSlotId.FrontalShield:
				m_FrontalShield = _slot;
				break;
			case VehicleEquipmentSlotId.SurroundShield:
				m_SurroundShield = _slot;
				break;
		}
	}

	private bool HasLivingOccupant()
	{
		if (m_Vehicle == null || m_Vehicle.Seats == null)
			return false;
		return m_Vehicle.Seats.OccupantCount > 0;
	}

	private float GetEquipmentSlotWeightInCargo(VehicleEquipmentSlotId _slotId)
	{
		InventorySlotRuntimeData slot = GetEquipmentSlot(_slotId);
		return slot.IsEmpty || slot.Definition == null ? 0f : slot.Definition.WeightKg;
	}

	private float CalculateCargoWeightKg()
	{
		float total = 0f;
		if (!m_TurretWeapon.IsEmpty && m_TurretWeapon.Definition != null)
			total += m_TurretWeapon.Definition.WeightKg;
		if (!m_FrontalShield.IsEmpty && m_FrontalShield.Definition != null)
			total += m_FrontalShield.Definition.WeightKg;
		if (!m_SurroundShield.IsEmpty && m_SurroundShield.Definition != null)
			total += m_SurroundShield.Definition.WeightKg;
		for (int i = 0; i < m_BagItems.Count; i++)
		{
			if (!m_BagItems[i].IsEmpty && m_BagItems[i].Definition != null)
				total += m_BagItems[i].Definition.WeightKg;
		}

		return total;
	}

	private void EnsureRuntimeStatesInitialized()
	{
		EnsureSlotHasInstanceState(ref m_TurretWeapon);
		EnsureSlotHasInstanceState(ref m_FrontalShield);
		EnsureSlotHasInstanceState(ref m_SurroundShield);
		for (int i = 0; i < m_BagItems.Count; i++)
		{
			InventorySlotRuntimeData slot = m_BagItems[i];
			EnsureSlotHasInstanceState(ref slot);
			m_BagItems[i] = slot;
		}
	}

	private static void EnsureSlotHasInstanceState(ref InventorySlotRuntimeData _slot)
	{
		if (_slot.IsEmpty || _slot.Definition == null)
			return;
		if (_slot.InstanceState != null)
			return;
		_slot.InstanceState = ItemInstanceState.CreateForDefinition(_slot.Definition);
	}

	private void NotifyInventoryChanged()
	{
		if (m_InventoryChangeBatchDepth > 0)
		{
			m_HasPendingInventoryChange = true;
			return;
		}

		FlushInventoryChanged();
	}

	private void FlushInventoryChanged()
	{
		m_HasPendingInventoryChange = false;
		InventoryChanged?.Invoke(this);
	}
	#endregion
}
