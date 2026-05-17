using UnityEngine;

/// <summary>
/// Root controller for the pre-mission briefing UI: connects unit list selection to the equipment panel.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepScreenController : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private MissionPrepUnitListView m_UnitList;
	[SerializeField] private MissionPrepEquipmentPanelView m_EquipmentPanel;
	[SerializeField] private bool m_HideEquipmentUntilSelection = false;
	[SerializeField] private GameObject m_ScreenRoot;
	private MissionPrepUnitCellView m_CurrentUnitCell;
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		if (m_UnitList != null)
			m_UnitList.UnitCellSelected += HandleUnitSelected;

		if (m_EquipmentPanel != null)
		{
			m_EquipmentPanel.WeaponSelected += HandleWeaponSelected;
			m_EquipmentPanel.CreateNewPresetRequested += HandleCreateNewPresetRequested;
		}

		if (m_HideEquipmentUntilSelection && m_EquipmentPanel != null)
			m_EquipmentPanel.SetVisible(false);
	}

	private void OnDisable()
	{
		if (m_UnitList != null)
			m_UnitList.UnitCellSelected -= HandleUnitSelected;

		if (m_EquipmentPanel != null)
		{
			m_EquipmentPanel.WeaponSelected -= HandleWeaponSelected;
			m_EquipmentPanel.CreateNewPresetRequested -= HandleCreateNewPresetRequested;
		}
	}
	#endregion

	#region Public Methods
	public void ShowScreen(bool _visible)
	{
		if (m_ScreenRoot != null)
			m_ScreenRoot.SetActive(_visible);

		if (!_visible && m_HideEquipmentUntilSelection && m_EquipmentPanel != null)
			m_EquipmentPanel.SetVisible(false);
	}
	#endregion

	#region Private Methods
	private void HandleUnitSelected(MissionPrepUnitCellView _cell)
	{
		m_CurrentUnitCell = _cell;

		if (m_EquipmentPanel == null)
			return;

		m_EquipmentPanel.SetVisible(true);
		m_EquipmentPanel.RefreshPresetDropdown();
	}

	private void HandleWeaponSelected(ItemDefinition _weaponDefinition, int _weaponIndex)
	{
		// Применить выбранное оружие (_weaponDefinition / _weaponIndex) к m_CurrentUnitCell — следующий слой над UI.
	}

	private void HandleCreateNewPresetRequested()
	{
		// Hook for future: open create-preset flow.
	}
	#endregion
}
