using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Панель со списком ячеек. Если задан префаб — каждое добавление создаёт новую ячейку под контент.
/// Без префаба — ищется пустая ячейка в иерархии.
/// </summary>
[DisallowMultipleComponent]
public class InventoryPanelView : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("RectTransform Content (родитель для ячеек). Обязателен, если используется Slot Prefab.")]
	[SerializeField] private Transform m_SlotsContainer;
	[Tooltip("Если задан — каждый TryAdd создаёт новый экземпляр под контент (пустые старые не переиспользуются).")]
	[SerializeField] private InventorySlotView m_SlotPrefab;
	[Tooltip("После ClearAllSlots уничтожать ячейки, созданные из префаба (ручные в сцене не трогаем).")]
	[SerializeField] private bool m_DestroySpawnedSlotsOnClearAll = true;

	[Header("Панель рюкзака персонажа")]
	[Tooltip("Сколько первых ячеек под снаряжение (0 = только сумка). Обычно 1 = основное оружие, далее броня и т.д.")]
	[SerializeField] private int m_LeadingEquipmentSlotCount;

	[Header("Слот экипированного оружия (первая leading-ячейка)")]
	[Tooltip("Применяется при создании ячейки в RepaintFromCharacterInventory / RepaintFromPresetSnapshot.")]
	[SerializeField] private InventoryEquipmentSlotAppearance m_EquipmentSlotAppearance = new InventoryEquipmentSlotAppearance();

	[Header("Связи Canvas (опционально)")]
	[Tooltip("Для панели инвентаря персонажа: зона drag-and-drop с «земли». Заполняется на общем Canvas.")]
	[SerializeField] private InventoryCharacterBagDropZone m_CharacterBagDropZone;
	[Tooltip("Для панели «земля»: зона сброса из рюкзака.")]
	[SerializeField] private InventoryGroundDropZone m_GroundDropZone;
	#endregion

	#region Private Fields
	private readonly List<InventorySlotView> m_Slots = new List<InventorySlotView>();
	private readonly List<InventorySlotView> m_SpawnedSlots = new List<InventorySlotView>();
	#endregion

	#region Public Properties
	public IReadOnlyList<InventorySlotView> Slots => m_Slots;
	public int LeadingEquipmentSlotCount => m_LeadingEquipmentSlotCount;
	public InventoryEquipmentSlotAppearance EquipmentSlotAppearance => m_EquipmentSlotAppearance;
	public InventoryCharacterBagDropZone CharacterBagDropZone => m_CharacterBagDropZone;
	public InventoryGroundDropZone GroundDropZone => m_GroundDropZone;

	/// <summary>Префаб и контент заданы — ячейки создаются в runtime, в сцене их может не быть.</summary>
	public bool IsConfiguredForDynamicRepaint => m_SlotPrefab != null && m_SlotsContainer != null;

	/// <summary>Родитель для динамических ячеек (Content в ScrollRect).</summary>
	public Transform SlotsContainerTransform => m_SlotsContainer;
	#endregion

	#region Runtime Configuration
	public void SetLeadingEquipmentSlotCount(int _count)
	{
		m_LeadingEquipmentSlotCount = Mathf.Max(0, _count);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		RefreshSlotsFromHierarchy();
		if (m_CharacterBagDropZone != null)
			m_CharacterBagDropZone.BindBagPanel(this);
		if (m_GroundDropZone != null)
			m_GroundDropZone.BindGroundPanel(this);
	}
	#endregion

	#region Public Methods
	/// <summary>Пересобрать список слотов из иерархии (ручные + уже созданные из префаба).</summary>
	public void RefreshSlotsFromHierarchy()
	{
		m_Slots.Clear();
		Transform root = m_SlotsContainer != null ? m_SlotsContainer : transform;
		CollectDirectChildSlotViews(root, m_Slots);
		m_SpawnedSlots.RemoveAll(_s => _s == null);
		for (int i = m_SpawnedSlots.Count - 1; i >= 0; i--)
		{
			if (!m_Slots.Contains(m_SpawnedSlots[i]))
				m_SpawnedSlots.RemoveAt(i);
		}

		for (int i = 0; i < m_Slots.Count; i++)
		{
			InventorySlotView s = m_Slots[i];
			if (s != null && s.IsRuntimeSpawned && !m_SpawnedSlots.Contains(s))
				m_SpawnedSlots.Add(s);
		}
	}

	/// <summary>Убрать ячейку из учёта панели перед перетаскиванием (иерархию не трогает).</summary>
	public void DetachSlotForDrag(InventorySlotView _slot)
	{
		if (_slot == null)
			return;
		m_Slots.Remove(_slot);
		m_SpawnedSlots.Remove(_slot);
	}

	/// <summary>Перепривязать перетаскиваемую ячейку к контенту этой панели (рюкзак / земля).</summary>
	public bool AdoptDraggedSlot(InventorySlotView _slot)
	{
		if (_slot == null || m_SlotsContainer == null)
			return false;

		_slot.transform.SetParent(m_SlotsContainer, false);
		_slot.transform.SetAsLastSibling();
		if (!m_Slots.Contains(_slot))
			m_Slots.Add(_slot);
		if (_slot.IsRuntimeSpawned && !m_SpawnedSlots.Contains(_slot))
			m_SpawnedSlots.Add(_slot);

		RebuildContentLayout();
		return true;
	}

	public void RebuildContentLayout()
	{
		if (m_SlotsContainer is RectTransform rt)
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
	}

	public void SetRuntimeSlotPrefab(InventorySlotView _slotPrefab)
	{
		if (_slotPrefab == null)
			return;

		m_SlotPrefab = _slotPrefab;
	}

	/// <summary>Ячейка из префаба только для drag-visual (не учитывается в списке панели).</summary>
	public InventorySlotView CreateDetachedDragVisual(InventorySlotRuntimeData _data, Transform _canvasRoot)
	{
		if (m_SlotPrefab == null || _canvasRoot == null || _data.IsEmpty)
			return null;

		InventorySlotView created = Instantiate(m_SlotPrefab, _canvasRoot);
		created.gameObject.name = $"{m_SlotPrefab.name}_DragVisual";
		created.MarkRuntimeSpawned();
		created.SetItem(_data);
		return created;
	}

	/// <summary>После переноса предмета с «земли»: убрать пустую строку из префаба или оставить пустую ручную ячейку.</summary>
	public void NotifyGroundSlotItemTakenAway(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		RuntimeInlineModificationBuilder.ClearAllRowsImmediate(this);

		if (_slot.IsRuntimeSpawned)
		{
			if (_slot.HasItem)
				_slot.Clear();
			m_Slots.Remove(_slot);
			m_SpawnedSlots.Remove(_slot);
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(_slot.gameObject, transform);
		}

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.NotifyGroundListingRemoved();
	}

	/// <summary>Убрать строку «земли» для лута (выход из радиуса подбора и т.п.).</summary>
	public bool TryRemoveGroundListingForPickup(WorldPickupItem _pickup)
	{
		if (_pickup == null)
			return false;

		_pickup.ClearGroundUiListing();
		RuntimeInlineModificationBuilder.ClearAllRowsImmediate(this);

		RefreshSlotsFromHierarchy();
		for (int i = 0; i < m_Slots.Count; i++)
		{
			InventorySlotView slot = m_Slots[i];
			if (slot == null || !slot.HasItem)
				continue;
			if (slot.Data.WorldSource != _pickup)
				continue;

			if (slot.IsRuntimeSpawned)
			{
				slot.Clear();
				m_Slots.Remove(slot);
				m_SpawnedSlots.Remove(slot);
				EditorSelectionGuard.DestroyRuntimeSpawnedSlot(slot.gameObject, transform);
			}
			else
				slot.Clear();

			RefreshSlotsFromHierarchy();
			RebuildContentLayout();
			RuntimeInventoryModificationCoordinator.Instance?.NotifyGroundListingRemoved();
			return true;
		}

		RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.NotifyGroundListingRemoved();
		return false;
	}

	public bool TryAdd(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return false;

		if (m_SlotPrefab != null && m_SlotsContainer != null)
		{
			InventorySlotView created = SpawnNewSlotFromPrefab();
			created.SetItem(_data);
			return true;
		}

		EnsureSlotsCached();

		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null && !m_Slots[i].HasItem)
			{
				m_Slots[i].SetItem(_data);
				return true;
			}
		}

		return false;
	}

	/// <summary>Перерисовать ячейки: сначала слоты снаряжения (оружие в первом), затем сумка. Нужны Slot Prefab и Slots Container.</summary>
	public void RepaintFromCharacterInventory(CharacterInventory _inventory)
	{
		if (_inventory == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		ClearAllSlots();

		int lead = Mathf.Max(0, m_LeadingEquipmentSlotCount);
		InventorySlotRuntimeData main = _inventory.MainHandEquipment;
		InventorySlotRuntimeData head = _inventory.HeadEquipment;
		InventorySlotRuntimeData back = _inventory.BackEquipment;
		IReadOnlyList<InventorySlotRuntimeData> bag = _inventory.BagItems;

		for (int i = 0; i < lead; i++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab(i);
			if (i == 0 && !main.IsEmpty)
				cell.SetItem(main);
			else if (i == 1 && !head.IsEmpty)
				cell.SetItem(head);
			else if (i == 2 && !back.IsEmpty)
				cell.SetItem(back);
		}

		for (int b = 0; b < bag.Count; b++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(bag[b]);
		}

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
	}

	/// <summary>Перерисовать панель из снимка пресета (слот оружия + сумка).</summary>
	public void RepaintFromPresetSnapshot(MissionPrepPresetSnapshot _snapshot)
	{
		if (_snapshot == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		ClearAllSlots();

		int lead = Mathf.Max(0, m_LeadingEquipmentSlotCount);
		InventorySlotRuntimeData main = _snapshot.MainHandEquipment;
		InventorySlotRuntimeData head = _snapshot.HeadEquipment;
		InventorySlotRuntimeData back = _snapshot.BackEquipment;
		IReadOnlyList<InventorySlotRuntimeData> bag = _snapshot.BagItems;

		for (int i = 0; i < lead; i++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab(i);
			if (i == 0 && !main.IsEmpty)
				cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(main));
			else if (i == 1 && !head.IsEmpty)
				cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(head));
			else if (i == 2 && !back.IsEmpty)
				cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(back));
		}

		for (int b = 0; b < bag.Count; b++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(bag[b]));
		}

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
	}

	/// <summary>Статический список ячеек (панель «доступное снаряжение»). Пустые записи пропускаются.</summary>
	public void RepaintFromSlotList(IReadOnlyList<InventorySlotRuntimeData> _slots)
	{
		if (_slots == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		ClearAllSlots();

		for (int i = 0; i < _slots.Count; i++)
		{
			InventorySlotRuntimeData data = _slots[i];
			if (data.IsEmpty)
				continue;

			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(data);
		}

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
	}

	/// <summary>Индекс ячейки среди <see cref="InventorySlotView"/> на панели (без inline-строк модификации).</summary>
	public int GetInventorySlotListIndex(InventorySlotView _slot)
	{
		if (_slot == null)
			return -1;

		RefreshSlotsFromHierarchy();
		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] == _slot)
				return i;
		}

		return -1;
	}

	/// <summary>Индекс ячейки среди прямых детей контента (0 = первая строка).</summary>
	public int GetInventorySlotContainerIndex(InventorySlotView _slot)
	{
		if (_slot == null || m_SlotsContainer == null)
			return -1;

		Transform t = _slot.transform;
		for (int i = 0; i < m_SlotsContainer.childCount; i++)
		{
			if (m_SlotsContainer.GetChild(i) == t)
				return i;
		}

		return -1;
	}

	public void ClearAllSlots()
	{
		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null && m_Slots[i].HasItem)
			{
				InventorySlotRuntimeData d = m_Slots[i].Data;
				if (d.WorldSource != null)
					d.WorldSource.ClearGroundUiListing();
			}
		}

		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null)
				m_Slots[i].Clear();
		}

		if (IsConfiguredForDynamicRepaint && m_DestroySpawnedSlotsOnClearAll && m_SlotsContainer != null)
		{
			var toKill = new List<GameObject>(m_Slots.Count + m_SpawnedSlots.Count);
			for (int i = 0; i < m_SpawnedSlots.Count; i++)
			{
				if (m_SpawnedSlots[i] != null)
					toKill.Add(m_SpawnedSlots[i].gameObject);
			}

			for (int i = 0; i < m_SlotsContainer.childCount; i++)
			{
				Transform child = m_SlotsContainer.GetChild(i);
				if (child == null)
					continue;

				InventorySlotView slot = child.GetComponent<InventorySlotView>();
				if (slot == null || !slot.IsRuntimeSpawned)
					continue;

				GameObject go = child.gameObject;
				if (!toKill.Contains(go))
					toKill.Add(go);
			}

			m_Slots.Clear();
			m_SpawnedSlots.Clear();
			if (toKill.Count > 0)
				EditorSelectionGuard.DestroyRuntimeSpawnedSlotsBatch(toKill, transform);
			return;
		}

		if (m_DestroySpawnedSlotsOnClearAll && m_SpawnedSlots.Count > 0)
		{
			var toKill = new List<GameObject>(m_SpawnedSlots.Count);
			for (int i = 0; i < m_SpawnedSlots.Count; i++)
			{
				if (m_SpawnedSlots[i] != null)
					toKill.Add(m_SpawnedSlots[i].gameObject);
			}

			m_Slots.Clear();
			m_SpawnedSlots.Clear();
			EditorSelectionGuard.DestroyRuntimeSpawnedSlotsBatch(toKill, transform);
		}
	}
	#endregion

	#region Private Methods
	private void EnsureSlotsCached()
	{
		if (m_Slots.Count == 0)
			RefreshSlotsFromHierarchy();
	}

	private static void CollectDirectChildSlotViews(Transform _container, List<InventorySlotView> _outSlots)
	{
		if (_container == null || _outSlots == null)
			return;

		for (int i = 0; i < _container.childCount; i++)
		{
			Transform child = _container.GetChild(i);
			if (child == null || !child.gameObject.activeInHierarchy)
				continue;

			InventorySlotView slot = child.GetComponent<InventorySlotView>();
			if (slot != null)
				_outSlots.Add(slot);
		}
	}

	private InventorySlotView SpawnNewSlotFromPrefab(int _equipmentSlotIndex = -1)
	{
		InventorySlotView created = Instantiate(m_SlotPrefab, m_SlotsContainer);
		created.gameObject.name = $"{m_SlotPrefab.name}_{m_SpawnedSlots.Count}";
		created.Clear();
		created.MarkRuntimeSpawned();
		m_SpawnedSlots.Add(created);
		m_Slots.Add(created);

		if (_equipmentSlotIndex == 0)
			InventorySlotUiUtility.ConfigureMainHandEquipmentSlot(created, m_EquipmentSlotAppearance);
		else if (_equipmentSlotIndex == 1)
			InventorySlotUiUtility.ConfigureHeadEquipmentSlot(created, m_EquipmentSlotAppearance);
		else if (_equipmentSlotIndex == 2)
			InventorySlotUiUtility.ConfigureBackEquipmentSlot(created, m_EquipmentSlotAppearance);

		return created;
	}
	#endregion
}
