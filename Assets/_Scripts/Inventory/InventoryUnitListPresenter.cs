using UnityEngine;

/// <summary>
/// Показывает одну строку <see cref="MissionPrepUnitCellView"/> для активного юнита в окне инвентаря.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryUnitListPresenter : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private MissionPrepUnitListView m_UnitList;
	[SerializeField] private MissionPrepUnitCellView m_UnitCellPrefab;
	[SerializeField] private RectTransform m_CellsContentParent;
	#endregion

	#region Private Fields
	private MissionPrepUnitCellView m_RuntimeCell;
	#endregion

	#region Public Methods
	public void RefreshForInventory(CharacterInventory _inventory)
	{
		ClearRuntimeCell();

		if (_inventory == null || m_UnitCellPrefab == null || m_CellsContentParent == null)
		{
			if (m_UnitList != null)
				m_UnitList.SetUnitCells(System.Array.Empty<MissionPrepUnitCellView>());
			return;
		}

		m_RuntimeCell = Instantiate(m_UnitCellPrefab, m_CellsContentParent);
		m_RuntimeCell.gameObject.name = m_UnitCellPrefab.name;

		GameObject unitRoot = ResolveUnitRoot(_inventory);
		UnitCellDisplayBinder.Apply(m_RuntimeCell, unitRoot);
		m_RuntimeCell.SetInteractionEnabled(false);
		m_RuntimeCell.SetSelected(true);

		if (m_UnitList != null)
			m_UnitList.SetUnitCells(new[] { m_RuntimeCell });
	}

	public void Clear()
	{
		ClearRuntimeCell();
		if (m_UnitList != null)
			m_UnitList.SetUnitCells(System.Array.Empty<MissionPrepUnitCellView>());
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitList == null)
			m_UnitList = GetComponentInChildren<MissionPrepUnitListView>(true);
	}
	#endregion

	#region Private Methods
	private void ClearRuntimeCell()
	{
		if (m_RuntimeCell != null)
		{
			Destroy(m_RuntimeCell.gameObject);
			m_RuntimeCell = null;
		}

		if (m_CellsContentParent == null)
			return;

		for (int i = m_CellsContentParent.childCount - 1; i >= 0; i--)
		{
			Transform child = m_CellsContentParent.GetChild(i);
			if (child != null)
				Destroy(child.gameObject);
		}
	}

	private static GameObject ResolveUnitRoot(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return null;

		RtsUnitMember member = _inventory.GetComponentInParent<RtsUnitMember>(true);
		if (member != null)
			return member.gameObject;

		return _inventory.transform.root.gameObject;
	}

	#endregion
}
