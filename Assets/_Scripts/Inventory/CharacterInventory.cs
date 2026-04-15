using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Инвентарь юнита: первый слот UI — основное оружие (снаряжение), далее — сумка.
/// Сброс на землю из слота оружия снимает модель с рук.
/// </summary>
[DisallowMultipleComponent]
public class CharacterInventory : MonoBehaviour
{
	#region Serialized Fields
	[Header("Выброс предмета на землю")]
	[Tooltip("Позиция и направление «вперёд» берутся с transform этого объекта (компонент на юните).")]
	[SerializeField, Min(0.05f)] private float m_DropDistance = 0.5f;
	[SerializeField] private float m_DropHeightOffset = 0.02f;
	[Tooltip("Поворот спавна: forward по горизонтали от transform этого объекта.")]
	[SerializeField] private bool m_DropUseHorizontalForward = true;
	#endregion

	#region Private Fields
	[SerializeField] private InventorySlotRuntimeData m_MainHandEquipment;
	[FormerlySerializedAs("m_Items")]
	[SerializeField] private List<InventorySlotRuntimeData> m_BagItems = new List<InventorySlotRuntimeData>();
	#endregion

	#region Public Properties
	/// <summary>Слот основного оружия (первый на панели при LeadingEquipmentSlotCount ≥ 1).</summary>
	public InventorySlotRuntimeData MainHandEquipment => m_MainHandEquipment;

	public IReadOnlyList<InventorySlotRuntimeData> BagItems => m_BagItems;
	public int BagCount => m_BagItems.Count;
	public bool HasMainHandEquipment => !m_MainHandEquipment.IsEmpty;

	/// <summary>Число предметов в сумке + занятый слот оружия (для общих оценок).</summary>
	public int TotalItemCount => BagCount + (HasMainHandEquipment ? 1 : 0);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureRuntimeStatesInitialized();
	}
	#endregion

	#region Public Methods
	/// <summary>Добавить в сумку (не в слот оружия).</summary>
	public bool TryAdd(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return false;

		InventorySlotRuntimeData copy = _data;
		EnsureSlotHasInstanceState(ref copy);
		copy.WorldSource = null;
		m_BagItems.Add(copy);
		return true;
	}

	public bool TryRemoveBagAt(int _index, out InventorySlotRuntimeData _removed)
	{
		if (_index < 0 || _index >= m_BagItems.Count)
		{
			_removed = default;
			return false;
		}

		_removed = m_BagItems[_index];
		m_BagItems.RemoveAt(_index);
		return true;
	}

	public bool TrySetBagItemAt(int _index, InventorySlotRuntimeData _updated)
	{
		if (_index < 0 || _index >= m_BagItems.Count)
			return false;

		m_BagItems[_index] = _updated;
		return true;
	}

	/// <summary>Снять основное оружие со слота (например выброс на землю). Снимает и визуал с юнита.</summary>
	public bool TryRemoveMainHandEquipment(out InventorySlotRuntimeData _removed)
	{
		if (m_MainHandEquipment.IsEmpty)
		{
			_removed = default;
			return false;
		}

		_removed = m_MainHandEquipment;
		m_MainHandEquipment = default;
		ClearUnitEquipmentVisual();
		return true;
	}

	/// <summary>Переместить предмет из сумки в слот основного оружия и обновить модель на юните. Старый слот оружия, если был, вставляется на место строки в сумке.</summary>
	public bool TryMoveBagItemToMainHand(int _bagIndex, UnitEquipment _equipment)
	{
		if (_equipment == null || _bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (picked.Definition == null || !picked.Definition.IsEquipment)
			return false;

		if (picked.InstanceState != null &&
		    picked.InstanceState.WeaponState != null &&
		    picked.InstanceState.WeaponState.IsTerminallyBroken)
			return false;

		InventorySlotRuntimeData previousMain = m_MainHandEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_MainHandEquipment = picked;

		if (!previousMain.IsEmpty)
			m_BagItems.Insert(_bagIndex, previousMain);

		_equipment.TryEquip(m_MainHandEquipment.Definition);
		return true;
	}

	/// <summary>Двойной клик по слоту оружия: убрать в сумку и снять модель с рук.</summary>
	public bool TryUnequipMainHandToBag()
	{
		if (m_MainHandEquipment.IsEmpty)
			return false;

		InventorySlotRuntimeData mh = m_MainHandEquipment;
		m_MainHandEquipment = default;
		ClearUnitEquipmentVisual();
		m_BagItems.Add(mh);
		return true;
	}

	public void Clear()
	{
		m_MainHandEquipment = default;
		m_BagItems.Clear();
		ClearUnitEquipmentVisual();
	}

	/// <summary>Синхронизировать панель рюкзака (слоты экипировки + сумка).</summary>
	public void RepaintInventoryPanel(InventoryPanelView _panel)
	{
		if (_panel == null)
			return;

		_panel.RepaintFromCharacterInventory(this);
	}

	public void GetDropWorldPose(out Vector3 _position, out Quaternion _rotation)
	{
		Transform origin = transform;
		Vector3 forward = origin.forward;
		if (m_DropUseHorizontalForward)
		{
			forward.y = 0f;
			if (forward.sqrMagnitude < 1e-6f)
				forward = Vector3.forward;
			forward.Normalize();
			_position = origin.position + forward * m_DropDistance + Vector3.up * m_DropHeightOffset;
			_rotation = Quaternion.LookRotation(forward, Vector3.up);
		}
		else
		{
			_position = origin.position + forward.normalized * m_DropDistance + Vector3.up * m_DropHeightOffset;
			_rotation = origin.rotation;
		}
	}
	#endregion

	#region Private Methods
	private void EnsureRuntimeStatesInitialized()
	{
		EnsureSlotHasInstanceState(ref m_MainHandEquipment);

		for (int i = 0; i < m_BagItems.Count; i++)
		{
			InventorySlotRuntimeData slot = m_BagItems[i];
			EnsureSlotHasInstanceState(ref slot);
			m_BagItems[i] = slot;
		}
	}

	private static void EnsureSlotHasInstanceState(ref InventorySlotRuntimeData _data)
	{
		if (_data.Definition == null || _data.InstanceState != null)
			return;

		_data.InstanceState = ItemInstanceState.CreateForDefinition(_data.Definition);
	}

	private void ClearUnitEquipmentVisual()
	{
		UnitEquipment equipment = GetComponentInChildren<UnitEquipment>(true);
		if (equipment != null)
			equipment.ClearAllEquipment();
	}

	/// <summary>Вернуть предмет после неудачного выброса (спавн/панель отклонили перенос).</summary>
	public void RestoreAfterFailedDrop(bool _toMainHand, InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return;

		if (_toMainHand)
		{
			m_MainHandEquipment = _data;
			UnitEquipment equipment = GetComponentInChildren<UnitEquipment>(true);
			if (equipment != null && _data.Definition != null)
				equipment.TryEquip(_data.Definition);
		}
		else
			TryAdd(_data);
	}
	#endregion
}

