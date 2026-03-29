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
	public InventoryCharacterBagDropZone CharacterBagDropZone => m_CharacterBagDropZone;
	public InventoryGroundDropZone GroundDropZone => m_GroundDropZone;
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
		m_Slots.AddRange(root.GetComponentsInChildren<InventorySlotView>(true));
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

	/// <summary>После переноса предмета с «земли»: убрать пустую строку из префаба или оставить пустую ручную ячейку.</summary>
	public void NotifyGroundSlotItemTakenAway(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		if (_slot.IsRuntimeSpawned)
		{
			m_Slots.Remove(_slot);
			m_SpawnedSlots.Remove(_slot);
			Destroy(_slot.gameObject);
		}

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
	}

	/// <summary>Убрать строку «земли» для лута (выход из радиуса подбора и т.п.).</summary>
	public bool TryRemoveGroundListingForPickup(WorldPickupItem _pickup)
	{
		if (_pickup == null)
			return false;

		RefreshSlotsFromHierarchy();
		for (int i = 0; i < m_Slots.Count; i++)
		{
			InventorySlotView slot = m_Slots[i];
			if (slot == null || !slot.HasItem)
				continue;
			if (slot.Data.WorldSource != _pickup)
				continue;

			_pickup.ClearGroundUiListing();

			if (slot.IsRuntimeSpawned)
			{
				m_Slots.Remove(slot);
				m_SpawnedSlots.Remove(slot);
				Destroy(slot.gameObject);
			}
			else
				slot.Clear();

			RefreshSlotsFromHierarchy();
			RebuildContentLayout();
			return true;
		}

		if (_pickup.IsListedInGroundUi)
			_pickup.ClearGroundUiListing();

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
		IReadOnlyList<InventorySlotRuntimeData> bag = _inventory.BagItems;

		for (int i = 0; i < lead; i++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			if (i == 0 && !main.IsEmpty)
				cell.SetItem(main);
		}

		for (int b = 0; b < bag.Count; b++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(bag[b]);
		}

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
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

		if (m_DestroySpawnedSlotsOnClearAll && m_SpawnedSlots.Count > 0)
		{
			for (int i = 0; i < m_SpawnedSlots.Count; i++)
			{
				if (m_SpawnedSlots[i] != null)
					Destroy(m_SpawnedSlots[i].gameObject);
			}

			m_SpawnedSlots.Clear();
			RefreshSlotsFromHierarchy();
		}
	}
	#endregion

	#region Private Methods
	private void EnsureSlotsCached()
	{
		if (m_Slots.Count == 0)
			RefreshSlotsFromHierarchy();
	}

	private InventorySlotView SpawnNewSlotFromPrefab()
	{
		InventorySlotView created = Instantiate(m_SlotPrefab, m_SlotsContainer);
		created.gameObject.name = $"{m_SlotPrefab.name}_{m_SpawnedSlots.Count}";
		created.Clear();
		created.MarkRuntimeSpawned();
		m_SpawnedSlots.Add(created);
		m_Slots.Add(created);
		return created;
	}
	#endregion
}
