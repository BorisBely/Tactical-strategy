using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds unit/vehicle row views and forwards selection to the pre-mission screen controller.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitListView : MonoBehaviour
{
	#region Events
	public event Action<MissionPrepUnitCellView> UnitCellSelected;
	#endregion

	#region Private Fields
	[SerializeField] private MissionPrepUnitCellView[] m_UnitCells = Array.Empty<MissionPrepUnitCellView>();
	private MissionPrepUnitCellView m_SelectedCell;
	private readonly List<MissionPrepVehicleSeatSlotView> m_SeatSlots = new List<MissionPrepVehicleSeatSlotView>(32);
	#endregion

	#region Public Properties
	public int UnitCellCount => m_UnitCells != null ? m_UnitCells.Length : 0;
	public MissionPrepUnitCellView SelectedCell => m_SelectedCell;
	#endregion

	#region Public Methods
	public MissionPrepUnitCellView GetUnitCell(int _index)
	{
		if (m_UnitCells == null || _index < 0 || _index >= m_UnitCells.Length)
			return null;

		return m_UnitCells[_index];
	}

	public void ClearAllUnitBindings()
	{
		SetSelectedCell(null);

		if (m_UnitCells == null)
			return;

		for (int i = 0; i < m_UnitCells.Length; i++)
		{
			if (m_UnitCells[i] != null)
				m_UnitCells[i].ClearBinding();
		}
	}

	public void SetSelectedCell(MissionPrepUnitCellView _cell)
	{
		if (m_SelectedCell == _cell)
			return;

		if (m_SelectedCell != null)
			m_SelectedCell.SetSelected(false);

		m_SelectedCell = _cell;

		if (m_SelectedCell != null)
			m_SelectedCell.SetSelected(true);
	}

	/// <summary>Заменяет набор ячеек (машины + юниты). Переподписывает клики.</summary>
	public void SetUnitCells(MissionPrepUnitCellView[] _cells)
	{
		SubscribeCells(false);
		m_UnitCells = _cells != null ? _cells : Array.Empty<MissionPrepUnitCellView>();
		if (isActiveAndEnabled)
			SubscribeCells(true);
	}

	public void SetSeatSlots(IReadOnlyList<MissionPrepVehicleSeatSlotView> _slots)
	{
		m_SeatSlots.Clear();
		if (_slots == null)
			return;

		for (int i = 0; i < _slots.Count; i++)
		{
			if (_slots[i] != null)
				m_SeatSlots.Add(_slots[i]);
		}
	}

	public void RefreshSeatSlots()
	{
		for (int i = 0; i < m_SeatSlots.Count; i++)
		{
			if (m_SeatSlots[i] != null)
				m_SeatSlots[i].Refresh();
		}
	}

	public void NotifyUnitCellSelected(MissionPrepUnitCellView _cell)
	{
		HandleUnitCellClicked(_cell);
	}
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		SubscribeCells(true);
	}

	private void OnDisable()
	{
		SubscribeCells(false);
	}
	#endregion

	#region Private Methods
	private void SubscribeCells(bool _subscribe)
	{
		if (m_UnitCells == null)
			return;

		for (int i = 0; i < m_UnitCells.Length; i++)
		{
			MissionPrepUnitCellView cell = m_UnitCells[i];
			if (cell == null)
				continue;

			if (_subscribe)
				cell.Clicked += HandleUnitCellClicked;
			else
				cell.Clicked -= HandleUnitCellClicked;
		}
	}

	private void HandleUnitCellClicked(MissionPrepUnitCellView _cell)
	{
		UnitCellSelected?.Invoke(_cell);
	}
	#endregion
}
