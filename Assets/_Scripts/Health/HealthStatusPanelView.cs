using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HealthStatusPanelView : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Transform m_SlotsContainer;
	[SerializeField] private HealthStatusSlotView m_SlotPrefab;
	[SerializeField] private bool m_DestroySpawnedSlotsOnClearAll = true;
	#endregion

	#region Private Fields
	private readonly List<HealthStatusSlotView> m_Slots = new List<HealthStatusSlotView>();
	private readonly List<HealthStatusSlotView> m_SpawnedSlots = new List<HealthStatusSlotView>();
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		RefreshSlotsFromHierarchy();
	}
	#endregion

	#region Public Methods
	public void SetRuntimeSlotPrefab(HealthStatusSlotView _slotPrefab)
	{
		if (_slotPrefab == null)
			return;

		m_SlotPrefab = _slotPrefab;
	}

	public void RefreshSlotsFromHierarchy()
	{
		m_Slots.Clear();
		Transform root = m_SlotsContainer != null ? m_SlotsContainer : transform;
		m_Slots.AddRange(root.GetComponentsInChildren<HealthStatusSlotView>(true));
		m_SpawnedSlots.RemoveAll(_slot => _slot == null);
		for (int i = m_SpawnedSlots.Count - 1; i >= 0; i--)
		{
			if (!m_Slots.Contains(m_SpawnedSlots[i]))
				m_SpawnedSlots.RemoveAt(i);
		}
	}

	public bool TryAdd(HealthStatusEntryData _data)
	{
		if (_data.IsEmpty)
			return false;

		if (m_SlotPrefab != null && m_SlotsContainer != null)
		{
			HealthStatusSlotView created = SpawnNewSlotFromPrefab();
			created.SetEntry(_data);
			return true;
		}

		EnsureSlotsCached();
		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null && !m_Slots[i].HasEntry)
			{
				m_Slots[i].SetEntry(_data);
				return true;
			}
		}

		return false;
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

	public void RebuildContentLayout()
	{
		if (m_SlotsContainer is RectTransform rt)
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
	}
	#endregion

	#region Private Methods
	private void EnsureSlotsCached()
	{
		if (m_Slots.Count == 0)
			RefreshSlotsFromHierarchy();
	}

	private HealthStatusSlotView SpawnNewSlotFromPrefab()
	{
		HealthStatusSlotView created = Instantiate(m_SlotPrefab, m_SlotsContainer);
		created.gameObject.name = $"{m_SlotPrefab.name}_{m_SpawnedSlots.Count}";
		created.Clear();
		created.MarkRuntimeSpawned();
		m_SpawnedSlots.Add(created);
		m_Slots.Add(created);
		return created;
	}
	#endregion
}
