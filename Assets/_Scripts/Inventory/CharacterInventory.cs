using System;
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
	[Tooltip("Доп. подъём точки спавна выброшенного лута — уменьшает провал сквозь коллайдер при старте физики.")]
	[SerializeField] private float m_DropHeightOffset = 0.12f;
	[Tooltip("Поворот спавна: forward по горизонтали от transform этого объекта.")]
	[SerializeField] private bool m_DropUseHorizontalForward = true;
	#endregion

	#region Private Fields
	[SerializeField] private InventorySlotRuntimeData m_MainHandEquipment;
	[SerializeField] private InventorySlotRuntimeData m_HeadEquipment;
	[SerializeField] private InventorySlotRuntimeData m_BackEquipment;
	[FormerlySerializedAs("m_Items")]
	[SerializeField] private List<InventorySlotRuntimeData> m_BagItems = new List<InventorySlotRuntimeData>();
	private int m_InventoryChangeBatchDepth;
	private bool m_HasPendingInventoryChange;
	#endregion

	#region Public Properties
	public event Action<CharacterInventory> InventoryChanged;

	/// <summary>Слот основного оружия (первый на панели при LeadingEquipmentSlotCount ≥ 1).</summary>
	public InventorySlotRuntimeData MainHandEquipment => m_MainHandEquipment;
	public InventorySlotRuntimeData HeadEquipment => m_HeadEquipment;
	public InventorySlotRuntimeData BackEquipment => m_BackEquipment;

	public IReadOnlyList<InventorySlotRuntimeData> BagItems => m_BagItems;
	public int BagCount => m_BagItems.Count;
	public bool HasMainHandEquipment => !m_MainHandEquipment.IsEmpty;
	public bool HasHeadEquipment => !m_HeadEquipment.IsEmpty;
	public bool HasBackEquipment => !m_BackEquipment.IsEmpty;

	/// <summary>Число предметов в сумке + занятые слоты экипировки.</summary>
	public int TotalItemCount => BagCount + (HasMainHandEquipment ? 1 : 0) + (HasHeadEquipment ? 1 : 0) +
	                             (HasBackEquipment ? 1 : 0);
	public float TotalWeightKg => CalculateTotalWeightKg();
	public float BagWeightKg => CalculateBagWeightKg();
	public float ArmorWeightKg => CalculateArmorWeightKg();
	public float CargoWeightKg => TotalWeightKg - ArmorWeightKg;
	public float TotalMaxWeightKg => MaxBagWeightKg + ArmorWeightKg;
	public float MaxBagWeightKg
	{
		get
		{
			if (HasBackEquipment && m_BackEquipment.Definition != null)
			{
				float limit = ItemWeightDefaults.GetBackpackWeightLimit(m_BackEquipment.Definition.LocalizationKey);
				if (limit > 0f)
					return limit;
			}
			return ItemWeightDefaults.DefaultBagWeightLimitKg;
		}
	}
	public bool IsBagOverweight => CargoWeightKg > MaxBagWeightKg;
	public int MaxBagCapacity => (int)MaxBagWeightKg;
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

		float itemWeight = _data.Definition != null ? _data.Definition.WeightKg : 0f;
		if (CargoWeightKg + itemWeight > MaxBagWeightKg)
		{
			Debug.LogWarning($"[Инвентарь] {name} | превышен лимит веса (груз {CargoWeightKg:F1} + {itemWeight:F1} > {MaxBagWeightKg:F1} кг), предмет не добавлен.");
			return false;
		}

		InventorySlotRuntimeData copy = _data;
		EnsureSlotHasInstanceState(ref copy);
		copy.WorldSource = null;
		m_BagItems.Add(copy);
		NotifyInventoryChanged();
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
		NotifyInventoryChanged();
		return true;
	}

	public bool TrySetBagItemAt(int _index, InventorySlotRuntimeData _updated)
	{
		if (_index < 0 || _index >= m_BagItems.Count)
			return false;

		m_BagItems[_index] = _updated;
		NotifyInventoryChanged();
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
		NotifyInventoryChanged();
		return true;
	}

	/// <summary>Снять шлем со слота головы. Снимает и визуал с юнита.</summary>
	public bool TryRemoveHeadEquipment(out InventorySlotRuntimeData _removed)
	{
		if (m_HeadEquipment.IsEmpty)
		{
			_removed = default;
			return false;
		}

		_removed = m_HeadEquipment;
		m_HeadEquipment = default;
		ClearHeadEquipmentVisual();
		NotifyInventoryChanged();
		return true;
	}

	/// <summary>Снять рюкзак со слота спины. Снимает и визуал с юнита.</summary>
	public bool TryRemoveBackEquipment(out InventorySlotRuntimeData _removed)
	{
		if (m_BackEquipment.IsEmpty)
		{
			_removed = default;
			return false;
		}

		_removed = m_BackEquipment;
		m_BackEquipment = default;
		ClearBackEquipmentVisual();
		NotifyInventoryChanged();
		return true;
	}

	/// <summary>Переместить предмет из сумки в слот головы и обновить модель на юните.</summary>
	public bool TryMoveBagItemToHead(int _bagIndex, UnitHeadEquipment _headEquipment, UnitIndividualTraits _traits, UnitCharacterAppearance _appearance)
	{
		if (_headEquipment == null || _bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!HelmetEquipUtility.CanEquipToHead(picked))
			return false;

		InventorySlotRuntimeData previousHead = m_HeadEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_HeadEquipment = picked;

		if (!previousHead.IsEmpty)
			m_BagItems.Insert(_bagIndex, previousHead);

		_headEquipment.TryEquip(m_HeadEquipment.Definition, _traits, _appearance);
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(m_HeadEquipment, this);
		return true;
	}

	/// <summary>Экипировать шлем извне (земля); прежний шлем уходит в сумку.</summary>
	public bool TryEquipExternalItemToHead(
		InventorySlotRuntimeData _item,
		UnitHeadEquipment _headEquipment,
		UnitIndividualTraits _traits,
		UnitCharacterAppearance _appearance)
	{
		if (_headEquipment == null || !HelmetEquipUtility.CanEquipToHead(_item))
			return false;

		if (!m_HeadEquipment.IsEmpty && !TryUnequipHeadToBag())
			return false;

		m_HeadEquipment = _item;
		_headEquipment.TryEquip(m_HeadEquipment.Definition, _traits, _appearance);
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(m_HeadEquipment, this);
		return true;
	}

	/// <summary>Снять шлем в сумку.</summary>
	public bool TryUnequipHeadToBag()
	{
		if (m_HeadEquipment.IsEmpty)
			return false;

		InventorySlotRuntimeData head = m_HeadEquipment;
		m_HeadEquipment = default;
		ClearHeadEquipmentVisual();
		m_BagItems.Add(head);
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayInventoryRemoveSoundFromSlot(head, this);
		return true;
	}

	/// <summary>Переместить предмет из сумки в слот спины и обновить модель на юните.</summary>
	public bool TryMoveBagItemToBack(int _bagIndex, UnitBackEquipment _backEquipment)
	{
		if (_backEquipment == null || _bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!BackpackEquipUtility.CanEquipToBack(picked))
			return false;

		InventorySlotRuntimeData previousBack = m_BackEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_BackEquipment = picked;

		if (!previousBack.IsEmpty)
			m_BagItems.Insert(_bagIndex, previousBack);

		_backEquipment.TryEquip(m_BackEquipment.Definition);
		DropExcessBagItemsToGround();
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(m_BackEquipment, this);
		return true;
	}

	/// <summary>Экипировать рюкзак извне (земля); прежний рюкзак уходит в сумку.</summary>
	public bool TryEquipExternalItemToBack(InventorySlotRuntimeData _item, UnitBackEquipment _backEquipment)
	{
		if (_backEquipment == null || !BackpackEquipUtility.CanEquipToBack(_item))
			return false;

		if (!m_BackEquipment.IsEmpty && !TryUnequipBackToBag())
			return false;

		m_BackEquipment = _item;
		_backEquipment.TryEquip(m_BackEquipment.Definition);
		DropExcessBagItemsToGround();
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(m_BackEquipment, this);
		return true;
	}

	/// <summary>Снять рюкзак в сумку. Если после снятия превышен лимит — лишние предметы выбрасываются на землю.</summary>
	public bool TryUnequipBackToBag()
	{
		if (m_BackEquipment.IsEmpty)
			return false;

		InventorySlotRuntimeData back = m_BackEquipment;
		m_BackEquipment = default;
		ClearBackEquipmentVisual();
		m_BagItems.Add(back);
		DropExcessBagItemsToGround();
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayInventoryRemoveSoundFromSlot(back, this);
		return true;
	}

	private void DropExcessBagItemsToGround()
	{
		while (CargoWeightKg > MaxBagWeightKg && m_BagItems.Count > 0)
		{
			InventorySlotRuntimeData item = m_BagItems[0];
			m_BagItems.RemoveAt(0);

			if (TrySpawnItemOnGround(item))
				continue;

			m_BagItems.Insert(0, item);
			break;
		}
	}

	private bool TrySpawnItemOnGround(InventorySlotRuntimeData _data)
	{
		ItemDefinition def = _data.Definition;
		if (def == null || def.DropWorldPrefab == null)
			return false;

		GetDropWorldPose(out Vector3 pos, out Quaternion rot);
		UnityEngine.Object instanceObj = Instantiate(def.DropWorldPrefab, pos + Vector3.up * 0.08f, rot);
		GameObject go = instanceObj as GameObject;
		if (go == null)
			return false;

		WorldPickupItem pickup = go.GetComponent<WorldPickupItem>();
		if (pickup == null)
		{
			Destroy(go);
			return false;
		}

		Rigidbody[] bodies = go.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			bodies[i].linearVelocity = Vector3.zero;
			bodies[i].angularVelocity = Vector3.zero;
		}

		pickup.ConfigureForDroppedFromInventory(_data);
		return true;
	}

	/// <summary>Переместить предмет из сумки в слот основного оружия и обновить модель на юните. Старый слот оружия, если был, вставляется на место строки в сумке.</summary>
	public bool TryMoveBagItemToMainHand(int _bagIndex, UnitEquipment _equipment)
	{
		if (_equipment == null || _bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		InventorySlotRuntimeData picked = m_BagItems[_bagIndex];
		if (!WeaponEquipUtility.CanEquipToMainHand(picked))
			return false;

		InventorySlotRuntimeData previousMain = m_MainHandEquipment;
		m_BagItems.RemoveAt(_bagIndex);
		m_MainHandEquipment = picked;

		if (!previousMain.IsEmpty)
			m_BagItems.Insert(_bagIndex, previousMain);

		_equipment.TryEquip(m_MainHandEquipment.Definition);
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayEquipmentAddSoundFromSlot(this, m_MainHandEquipment, _useMainHandPosition: true);
		return true;
	}

	/// <summary>Экипировать предмет извне (земля, доступный каталог) в основную руку; прежнее оружие уходит в сумку.</summary>
	public bool TryEquipExternalItemToMainHand(InventorySlotRuntimeData _item, UnitEquipment _equipment)
	{
		if (_equipment == null || !WeaponEquipUtility.CanEquipToMainHand(_item))
			return false;

		if (!m_MainHandEquipment.IsEmpty && !TryUnequipMainHandToBag())
			return false;

		m_MainHandEquipment = _item;
		_equipment.TryEquip(m_MainHandEquipment.Definition);
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayEquipmentAddSoundFromSlot(this, m_MainHandEquipment, _useMainHandPosition: true);
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
		NotifyInventoryChanged();
		ItemInventoryAudioUtility.TryPlayEquipmentRemoveSoundFromSlot(mh, this, _useMainHandPosition: true);
		return true;
	}

	public void Clear()
	{
		m_MainHandEquipment = default;
		m_HeadEquipment = default;
		m_BackEquipment = default;
		m_BagItems.Clear();
		ClearUnitEquipmentVisual();
		ClearHeadEquipmentVisual();
		ClearBackEquipmentVisual();
		NotifyInventoryChanged();
	}

	/// <summary>
	/// Группирует несколько мутаций инвентаря в одно событие <see cref="InventoryChanged"/>.
	/// </summary>
	public void BeginBatchInventoryChanges()
	{
		m_InventoryChangeBatchDepth++;
	}

	/// <summary>
	/// Завершает batch и отправляет одно событие, если за время batch были изменения.
	/// </summary>
	public void EndBatchInventoryChanges()
	{
		if (m_InventoryChangeBatchDepth <= 0)
			return;

		m_InventoryChangeBatchDepth--;
		if (m_InventoryChangeBatchDepth > 0 || !m_HasPendingInventoryChange)
			return;

		m_HasPendingInventoryChange = false;
		InventoryChanged?.Invoke(this);
	}

	/// <summary>Синхронизировать панель рюкзака (слоты экипировки + сумка).</summary>
	public void RepaintInventoryPanel(InventoryPanelView _panel)
	{
		if (_panel == null)
			return;

		if (Application.isPlaying)
		{
			RuntimeInventoryModificationCoordinator runtimeCoordinator = RuntimeInventoryModificationCoordinator.Instance;
			InventoryPanelView characterPanel = InventoryScreenBindings.Instance != null
				? InventoryScreenBindings.Instance.CharacterInventoryPanel
				: null;
			if (runtimeCoordinator != null && characterPanel == _panel &&
			    runtimeCoordinator.TryRepaintCharacterAndGroundPanels(this))
				return;
		}

		_panel.RepaintFromCharacterInventory(this);
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

		if (_isHeadEquipmentSlot)
		{
			_slot = m_HeadEquipment;
			return !_slot.IsEmpty;
		}

		if (_isBackEquipmentSlot)
		{
			_slot = m_BackEquipment;
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

		EnsureSlotHasInstanceState(ref _slot);

		if (_isMainHandEquipmentSlot)
		{
			m_MainHandEquipment = _slot;
			NotifyInventoryChanged();
			return true;
		}

		if (_isHeadEquipmentSlot)
		{
			if (!HelmetEquipUtility.CanEquipToHead(_slot))
				return false;

			m_HeadEquipment = _slot;
			NotifyInventoryChanged();
			return true;
		}

		if (_isBackEquipmentSlot)
		{
			if (!BackpackEquipUtility.CanEquipToBack(_slot))
				return false;

			m_BackEquipment = _slot;
			NotifyInventoryChanged();
			return true;
		}

		if (_bagIndex < 0 || _bagIndex >= m_BagItems.Count)
			return false;

		m_BagItems[_bagIndex] = _slot;
		NotifyInventoryChanged();
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
			ClearUnitEquipmentVisual();
			NotifyInventoryChanged();
			return true;
		}

		if (_isHeadEquipmentSlot)
		{
			m_HeadEquipment = default;
			ClearHeadEquipmentVisual();
			NotifyInventoryChanged();
			return true;
		}

		if (_isBackEquipmentSlot)
		{
			m_BackEquipment = default;
			ClearBackEquipmentVisual();
			NotifyInventoryChanged();
			return true;
		}

		m_BagItems.RemoveAt(_bagIndex);
		NotifyInventoryChanged();
		return true;
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
		EnsureSlotHasInstanceState(ref m_HeadEquipment);
		EnsureSlotHasInstanceState(ref m_BackEquipment);

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

	private void ClearHeadEquipmentVisual()
	{
		UnitHeadEquipment headEquipment = GetComponentInChildren<UnitHeadEquipment>(true);
		if (headEquipment != null)
			headEquipment.ClearHead();
	}

	private void ClearBackEquipmentVisual()
	{
		UnitBackEquipment backEquipment = GetComponentInChildren<UnitBackEquipment>(true);
		if (backEquipment != null)
			backEquipment.ClearBack();
	}

	/// <summary>Вернуть предмет после неудачного выброса (спавн/панель отклонили перенос).</summary>
	public void RestoreAfterFailedDrop(bool _toMainHand, InventorySlotRuntimeData _data)
	{
		RestoreAfterFailedDrop(_toMainHand, false, _data);
	}

	public void RestoreAfterFailedDrop(bool _toMainHand, bool _toHead, InventorySlotRuntimeData _data)
	{
		RestoreAfterFailedDrop(_toMainHand, _toHead, false, _data);
	}

	public void RestoreAfterFailedDrop(bool _toMainHand, bool _toHead, bool _toBack, InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return;

		if (_toMainHand)
		{
			m_MainHandEquipment = _data;
			UnitEquipment equipment = GetComponentInChildren<UnitEquipment>(true);
			if (equipment != null && _data.Definition != null)
				equipment.TryEquip(_data.Definition);
			NotifyInventoryChanged();
		}
		else if (_toHead)
		{
			m_HeadEquipment = _data;
			UnitHeadEquipment headEquipment = GetComponentInChildren<UnitHeadEquipment>(true);
			UnitIndividualTraits traits = GetComponentInChildren<UnitIndividualTraits>(true);
			UnitCharacterAppearance appearance = GetComponentInChildren<UnitCharacterAppearance>(true);
			if (headEquipment != null && _data.Definition != null)
				headEquipment.TryEquip(_data.Definition, traits, appearance);
			NotifyInventoryChanged();
		}
		else if (_toBack)
		{
			m_BackEquipment = _data;
			UnitBackEquipment backEquipment = GetComponentInChildren<UnitBackEquipment>(true);
			if (backEquipment != null && _data.Definition != null)
				backEquipment.TryEquip(_data.Definition);
			NotifyInventoryChanged();
		}
		else
			TryAdd(_data);
	}

	private void NotifyInventoryChanged()
	{
		if (m_InventoryChangeBatchDepth > 0)
		{
			m_HasPendingInventoryChange = true;
			return;
		}

		InventoryChanged?.Invoke(this);
	}

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

	private float CalculateTotalWeightKg()
	{
		float total = 0f;

		if (!m_MainHandEquipment.IsEmpty && m_MainHandEquipment.Definition != null)
			total += m_MainHandEquipment.Definition.WeightKg + ItemWeightDefaults.GetWeaponModificationWeight(m_MainHandEquipment);
		if (!m_HeadEquipment.IsEmpty && m_HeadEquipment.Definition != null)
			total += m_HeadEquipment.Definition.WeightKg;
		if (!m_BackEquipment.IsEmpty && m_BackEquipment.Definition != null)
			total += m_BackEquipment.Definition.WeightKg;

		for (int i = 0; i < m_BagItems.Count; i++)
		{
			if (!m_BagItems[i].IsEmpty && m_BagItems[i].Definition != null)
				total += m_BagItems[i].Definition.WeightKg + ItemWeightDefaults.GetWeaponModificationWeight(m_BagItems[i]);
		}

		total += CalculateArmorWeightKg();
		return total;
	}

	private float CalculateArmorWeightKg()
	{
		UnitArmor armor = GetComponentInChildren<UnitArmor>(true);
		if (armor != null)
			return armor.GetWeightKg();
		return 0f;
	}
	#endregion
}

