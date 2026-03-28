using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Панель со списком ячеек. Если задан префаб — каждое новое добавление (без стака) создаёт новую ячейку под контент.
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
	#endregion

	#region Private Fields
	private readonly List<InventorySlotView> m_Slots = new List<InventorySlotView>();
	private readonly List<InventorySlotView> m_SpawnedSlots = new List<InventorySlotView>();
	#endregion

	#region Public Properties
	public IReadOnlyList<InventorySlotView> Slots => m_Slots;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		RefreshSlotsFromHierarchy();
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

	/// <summary>Первый занятый слот с совпадающим именем и тем же Definition (стак).</summary>
	public bool TryStackOrAdd(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return TryAdd(_data);

		EnsureSlotsCached();

		for (int i = 0; i < m_Slots.Count; i++)
		{
			var s = m_Slots[i];
			if (s == null || !s.HasItem)
				continue;
			var d = s.Data;
			if (d.Definition == _data.Definition && d.Definition != null &&
			    d.DisplayName == _data.DisplayName)
			{
				d.StackCount += _data.StackCount;
				s.SetItem(d);
				return true;
			}
		}

		return TryAdd(_data);
	}

	public void ClearAllSlots()
	{
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
		m_SpawnedSlots.Add(created);
		m_Slots.Add(created);
		return created;
	}
	#endregion
}
