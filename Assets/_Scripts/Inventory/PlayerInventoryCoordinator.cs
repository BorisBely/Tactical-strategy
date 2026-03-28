using UnityEngine;

/// <summary>
/// Связка двух общих панелей UI (земля + инвентарь). В сцене одна пара панелей;
/// позже сюда можно добавить привязку к выбранному юниту и подгрузку данных.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInventoryCoordinator : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private InventoryPanelView m_GroundPanel;
	[SerializeField] private InventoryPanelView m_CharacterInventoryPanel;
	#endregion

	#region Public Properties
	public InventoryPanelView GroundPanel => m_GroundPanel;
	public InventoryPanelView CharacterInventoryPanel => m_CharacterInventoryPanel;
	#endregion

	#region Public Methods
	public bool TryMoveGroundSlotToCharacter(int _groundSlotIndex)
	{
		return TryMoveSlot(m_GroundPanel, m_CharacterInventoryPanel, _groundSlotIndex);
	}

	public bool TryMoveCharacterSlotToGround(int _characterSlotIndex)
	{
		return TryMoveSlot(m_CharacterInventoryPanel, m_GroundPanel, _characterSlotIndex);
	}

	private static bool TryMoveSlot(InventoryPanelView _from, InventoryPanelView _to, int _index)
	{
		if (_from == null || _to == null)
			return false;

		var slots = _from.Slots;
		if (_index < 0 || _index >= slots.Count)
			return false;

		if (!slots[_index].TryTakeItem(out var data))
			return false;

		if (!_to.TryStackOrAdd(data))
		{
			slots[_index].SetItem(data);
			return false;
		}

		return true;
	}
	#endregion
}
