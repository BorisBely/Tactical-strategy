using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Хранилище лута на юните. UI панели инвентаря — только отображение; источник правды — этот список.
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
	private readonly List<InventorySlotRuntimeData> m_Items = new List<InventorySlotRuntimeData>();
	#endregion

	#region Public Properties
	public IReadOnlyList<InventorySlotRuntimeData> Items => m_Items;
	public int Count => m_Items.Count;
	#endregion

	#region Public Methods
	/// <summary>Добавить копию предмета (ссылка на <see cref="InventorySlotRuntimeData.WorldSource"/> не сохраняется).</summary>
	public bool TryAdd(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return false;

		InventorySlotRuntimeData copy = _data;
		copy.WorldSource = null;
		m_Items.Add(copy);
		return true;
	}

	public bool TryRemoveAt(int _index, out InventorySlotRuntimeData _removed)
	{
		if (_index < 0 || _index >= m_Items.Count)
		{
			_removed = default;
			return false;
		}

		_removed = m_Items[_index];
		m_Items.RemoveAt(_index);
		return true;
	}

	public void Clear()
	{
		m_Items.Clear();
	}

	/// <summary>Синхронизировать общую UI-панель рюкзака с содержимым этого инвентаря.</summary>
	public void RepaintInventoryPanel(InventoryPanelView _panel)
	{
		if (_panel == null)
			return;

		_panel.ClearAllSlots();
		for (int i = 0; i < m_Items.Count; i++)
			_panel.TryAdd(m_Items[i]);
	}

	/// <summary>Точка перед юнитом для <see cref="ItemDefinition.DropWorldPrefab"/>.</summary>
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
}
