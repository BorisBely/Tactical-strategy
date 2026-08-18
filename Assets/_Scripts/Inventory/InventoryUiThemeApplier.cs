using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Навешивается на InventoryRoot / MissionPrepScreenRoot — красит панели по <see cref="InventoryUiTheme"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryUiThemeApplier : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private bool m_ApplyOnAwake = true;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_ApplyOnAwake)
			Apply();
	}
	#endregion

	#region Public Methods
	public void Apply()
	{
		ApplyUnder(transform);
	}

	public static void ApplyUnder(Transform _root)
	{
		if (_root == null)
			return;

		string[] panelNames =
		{
			"RuntimeUnitInventoryPanel",
			"RuntimeGroundOrPartnerPanel",
			"RuntimeUnitSummary",
			"RuntimePartnerSummary",
			"RuntimeUnitHealthPanel",
			"RuntimePartnerHealthPanel",
			"PrepUnitList",
			"PrepVehicleList",
			"PrepPresetEquipmentPanel",
			"PrepAvailableEquipmentPanel",
			"PrepStatsPanel",
			// legacy names (до rename)
			"UnitInventory",
			"Ground",
			"Health",
			"Health 2",
			"PresetEquipmentPanel",
			"AvailableEquipmentPanel",
			"Units (2)",
			"unit_list",
			"unit_list (1)"
		};

		for (int i = 0; i < panelNames.Length; i++)
		{
			Transform panel = FindDeep(_root, panelNames[i]);
			if (panel != null)
				InventoryUiTheme.ApplyPanelChrome(panel.gameObject);
		}

		MissionPrepCollapsibleColumn[] columns = _root.GetComponentsInChildren<MissionPrepCollapsibleColumn>(true);
		for (int i = 0; i < columns.Length; i++)
		{
			MissionPrepCollapsibleColumn column = columns[i];
			if (column == null)
				continue;

			Button toggle = column.GetComponentInChildren<Button>(true);
			if (toggle == null)
				continue;

			Image toggleImage = toggle.targetGraphic as Image ?? toggle.GetComponent<Image>();
			if (toggleImage != null)
				InventoryUiTheme.ApplyImageColor(toggleImage, InventoryUiTheme.TitleBar);
		}
	}
	#endregion

	#region Private Methods
	private static Transform FindDeep(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		if (_parent.name == _name)
			return _parent;

		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform found = FindDeep(_parent.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}
