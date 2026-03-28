using TMPro;
using UnityEngine;

/// <summary>
/// Одна ячейка инвентаря: хранит данные предмета и показывает имя в TMP.
/// </summary>
[DisallowMultipleComponent]
public class InventorySlotView : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private TMP_Text m_NameText;
	[SerializeField] private GameObject m_OccupiedRoot;
	[SerializeField] private GameObject m_EmptyRoot;
	#endregion

	#region Private Fields
	private InventorySlotRuntimeData m_Data;
	private bool m_HasItem;
	#endregion

	#region Public Properties
	public bool HasItem => m_HasItem;
	public InventorySlotRuntimeData Data => m_Data;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		if (m_NameText == null)
			m_NameText = GetComponentInChildren<TMP_Text>(true);
	}
	#endregion

	#region Public Methods
	public void SetItem(InventorySlotRuntimeData _data)
	{
		m_Data = _data;
		m_HasItem = !_data.IsEmpty;

		RefreshVisuals();
	}

	public void Clear()
	{
		m_Data = default;
		m_HasItem = false;
		RefreshVisuals();
	}

	public bool TryTakeItem(out InventorySlotRuntimeData _data)
	{
		if (!m_HasItem)
		{
			_data = default;
			return false;
		}

		_data = m_Data;
		Clear();
		return true;
	}
	#endregion

	#region Private Methods
	private void RefreshVisuals()
	{
		if (m_NameText != null)
			m_NameText.text = m_HasItem ? FormatLabel(m_Data) : string.Empty;

		if (m_OccupiedRoot != null)
			m_OccupiedRoot.SetActive(m_HasItem);
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(!m_HasItem);
	}

	private static string FormatLabel(InventorySlotRuntimeData _data)
	{
		if (_data.StackCount > 1)
			return $"{_data.DisplayName} x{_data.StackCount}";
		return _data.DisplayName;
	}
	#endregion
}
