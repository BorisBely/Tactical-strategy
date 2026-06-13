using UnityEngine;

/// <summary>
/// Корень экрана предмиссии: выбор юнита, пресет снаряжения, инвентарь активного пресета.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepScreenController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private MissionPrepUnitListView m_UnitList;
	[SerializeField] private MissionPrepEquipmentPanelView m_EquipmentPanel;
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[SerializeField] private MissionPrepLoadoutCoordinator m_LoadoutCoordinator;
	[SerializeField] private bool m_HideEquipmentUntilSelection = false;
	[SerializeField] private GameObject m_ScreenRoot;

	[Header("Инвентарь пресета (встроен в окно пресетов)")]
	[Tooltip("Объект с Inventory Panel View. Slots Container и Slot Prefab задаются на нём.")]
	[SerializeField] private InventoryPanelView m_PresetInventoryPanel;

	[Header("Доступное снаряжение")]
	[SerializeField] private InventoryPanelView m_AvailableEquipmentPanel;
	[SerializeField] private MissionPrepAvailableEquipmentCatalog m_AvailableEquipmentCatalog;

	[Header("Префаб ячейки (предмиссия)")]
	[SerializeField] private InventorySlotView m_MissionPrepSlotPrefab;

	[SerializeField] private bool m_HideInventoryUntilUnitSelected = false;
	[SerializeField] private bool m_InventoryStartHidden = false;
	#endregion

	#region Private Fields
	private MissionPrepUnitCellView m_CurrentUnitCell;
	#endregion

	#region Public Properties
	public InventoryPanelView PresetInventoryPanel => m_PresetInventoryPanel;
	public InventoryPanelView AvailableEquipmentPanel => m_AvailableEquipmentPanel;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureLoadoutCoordinator();
		WireLoadoutCoordinator();
	}

	private void OnEnable()
	{
		if (m_UnitList != null)
			m_UnitList.UnitCellSelected += HandleUnitSelected;

		if (m_EquipmentPanel != null)
		{
			m_EquipmentPanel.PresetSelected += HandlePresetSelected;
			m_EquipmentPanel.ArmorVisualSelected += HandleArmorVisualSelected;
			m_EquipmentPanel.CamouflageVisualSelected += HandleCamouflageVisualSelected;
			m_EquipmentPanel.CreateNewPresetRequested += HandleCreateNewPresetRequested;
			m_EquipmentPanel.PresetListChanged += HandlePresetListChanged;
		}

		if (m_HideEquipmentUntilSelection && m_EquipmentPanel != null)
			m_EquipmentPanel.SetVisible(false);
		else if (m_EquipmentPanel != null)
			m_EquipmentPanel.SetVisible(true);

		if (m_InventoryStartHidden)
			SetInventoryVisible(false);
		else
			SetInventoryVisible(true);
	}

	private void OnDisable()
	{
		if (m_UnitList != null)
			m_UnitList.UnitCellSelected -= HandleUnitSelected;

		if (m_EquipmentPanel != null)
		{
			m_EquipmentPanel.PresetSelected -= HandlePresetSelected;
			m_EquipmentPanel.ArmorVisualSelected -= HandleArmorVisualSelected;
			m_EquipmentPanel.CamouflageVisualSelected -= HandleCamouflageVisualSelected;
			m_EquipmentPanel.CreateNewPresetRequested -= HandleCreateNewPresetRequested;
			m_EquipmentPanel.PresetListChanged -= HandlePresetListChanged;
		}

		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.ClearUnitBinding();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		TryResolvePresetInventoryPanelReference();

		if (m_PresetInventoryPanel != null &&
		    !m_PresetInventoryPanel.IsConfiguredForDynamicRepaint &&
		    m_PresetInventoryPanel.GetComponentInChildren<InventorySlotView>(true) == null)
			Debug.LogWarning($"{nameof(MissionPrepScreenController)} on {name}: назначьте Slot Prefab на Preset Inventory Panel.", this);
	}
#endif
	#endregion

	#region Public Methods
	public void ShowScreen(bool _visible)
	{
		if (m_ScreenRoot != null)
			m_ScreenRoot.SetActive(_visible);

		if (!_visible && m_HideEquipmentUntilSelection && m_EquipmentPanel != null)
			m_EquipmentPanel.SetVisible(false);
	}

	/// <summary>Подпись пресета для ячейки списка (по состоянию на юните).</summary>
	public string GetPresetLabelForUnit(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return string.Empty;

		if (!_unitRoot.TryGetComponent(out MissionPrepUnitPresetState state))
			return m_PresetCatalog != null ? m_PresetCatalog.GetPresetLabel(0) : string.Empty;

		if (m_LoadoutCoordinator != null &&
		    m_LoadoutCoordinator.TryGetPresetLabelForUnit(state, out string label))
			return label;

		if (m_PresetCatalog == null)
			return string.Empty;

		return m_PresetCatalog.GetPresetLabel(
			m_LoadoutCoordinator != null
				? m_LoadoutCoordinator.ClampPresetCatalogIndex(state.PresetCatalogIndex)
				: m_PresetCatalog.ClampPresetIndex(state.PresetCatalogIndex));
	}

	public void SetInventoryVisible(bool _visible)
	{
		if (m_PresetInventoryPanel != null)
			m_PresetInventoryPanel.gameObject.SetActive(_visible);
	}

	public void RefreshInventoryPanel()
	{
		if (m_LoadoutCoordinator != null)
		{
			m_LoadoutCoordinator.RepaintInventoryPanel();
			m_LoadoutCoordinator.RepaintAvailableEquipmentPanel();
		}
		else
		{
			if (m_PresetInventoryPanel != null)
				m_PresetInventoryPanel.ClearAllSlots();

			if (m_AvailableEquipmentPanel != null)
				m_AvailableEquipmentPanel.ClearAllSlots();
		}
	}
	#endregion

	#region Private Methods
	private void EnsureLoadoutCoordinator()
	{
		if (m_LoadoutCoordinator == null)
			TryGetComponent(out m_LoadoutCoordinator);

		if (m_LoadoutCoordinator == null)
			m_LoadoutCoordinator = gameObject.AddComponent<MissionPrepLoadoutCoordinator>();

		MissionPrepSharedPresetStore.GetOrCreate(this);
		MissionPrepRuntimePresetRegistry.GetOrCreate(this);
	}

	private void WireLoadoutCoordinator()
	{
		if (m_LoadoutCoordinator == null)
			return;

		TryResolvePresetInventoryPanelReference();
		TryResolveAvailableEquipmentPanelReference();
		TryResolveAvailableEquipmentCatalogReference();

		m_LoadoutCoordinator.Configure(
			m_PresetCatalog,
			m_PresetInventoryPanel,
			m_AvailableEquipmentPanel,
			m_AvailableEquipmentCatalog);

		ApplyMissionPrepSlotPrefab();

		if (m_EquipmentPanel != null)
			m_EquipmentPanel.SetLoadoutCoordinator(m_LoadoutCoordinator);
	}

	private void ApplyMissionPrepSlotPrefab()
	{
		if (m_MissionPrepSlotPrefab == null)
			return;

		if (m_PresetInventoryPanel != null)
			m_PresetInventoryPanel.SetRuntimeSlotPrefab(m_MissionPrepSlotPrefab);

		if (m_AvailableEquipmentPanel != null)
			m_AvailableEquipmentPanel.SetRuntimeSlotPrefab(m_MissionPrepSlotPrefab);
	}

	private void HandleUnitSelected(MissionPrepUnitCellView _cell)
	{
		m_CurrentUnitCell = _cell;

		if (m_UnitList != null)
			m_UnitList.SetSelectedCell(_cell);

		if (m_EquipmentPanel == null)
			return;

		m_EquipmentPanel.SetVisible(true);
		m_EquipmentPanel.BindToUnit(_cell != null ? _cell.BoundUnitRoot : null);
		RefreshUnitCellPresetLabel(_cell);

		OnInventoryUnitBindingChanged(_cell != null && _cell.BoundUnitRoot != null);
	}

	private void HandlePresetSelected(MissionPrepUnitPresetState _state, int _presetIndex)
	{
		RefreshAllUnitCellPresetLabels();
		RefreshInventoryPanel();
	}

	private void HandleArmorVisualSelected(MissionPrepUnitPresetState _state, int _armorIndex)
	{
		RefreshAllUnitCellPresetLabels();
		RefreshInventoryPanel();
	}

	private void HandleCamouflageVisualSelected(MissionPrepUnitPresetState _state, int _camouflageIndex)
	{
		RefreshAllUnitCellPresetLabels();
		RefreshInventoryPanel();
	}

	private void OnInventoryUnitBindingChanged(bool _hasBoundUnit)
	{
		if (m_HideInventoryUntilUnitSelected)
			SetInventoryVisible(_hasBoundUnit);
		else
			SetInventoryVisible(true);

		RefreshInventoryPanel();
	}

	private void RefreshUnitCellPresetLabel(MissionPrepUnitCellView _cell)
	{
		if (_cell == null)
			return;

		UnitCellDisplayBinder.Apply(_cell, _cell.BoundUnitRoot);
		_cell.SetInteractionEnabled(true);
	}

	private void RefreshAllUnitCellPresetLabels()
	{
		if (m_UnitList == null)
			return;

		for (int i = 0; i < m_UnitList.UnitCellCount; i++)
			RefreshUnitCellPresetLabel(m_UnitList.GetUnitCell(i));
	}

	private void HandlePresetListChanged()
	{
		RefreshAllUnitCellPresetLabels();
		RefreshInventoryPanel();
	}

	private void HandleCreateNewPresetRequested()
	{
		if (m_EquipmentPanel == null || m_LoadoutCoordinator == null)
			return;

		if (m_LoadoutCoordinator.TryCreateUserPreset(string.Empty, out _))
			m_EquipmentPanel.NotifyPresetCreated();
	}

	private void TryResolvePresetInventoryPanelReference()
	{
		// Явное поле в инспекторе не перезаписываем: эвристика «Units (2)» только для автоподбора без ссылки.
		if (m_PresetInventoryPanel != null && m_PresetInventoryPanel.IsConfiguredForDynamicRepaint)
			return;

		InventoryPanelView[] panels = GetComponentsInChildren<InventoryPanelView>(true);
		for (int i = 0; i < panels.Length; i++)
		{
			if (panels[i] != null && IsMissionPrepInventoryPanel(panels[i]))
			{
				m_PresetInventoryPanel = panels[i];
				return;
			}
		}

		if (m_PresetInventoryPanel == null && panels.Length > 0)
			m_PresetInventoryPanel = panels[0];
	}

	private void TryResolveAvailableEquipmentPanelReference()
	{
		if (m_AvailableEquipmentPanel != null && m_AvailableEquipmentPanel.IsConfiguredForDynamicRepaint)
			return;

		InventoryPanelView[] panels = GetComponentsInChildren<InventoryPanelView>(true);
		for (int i = 0; i < panels.Length; i++)
		{
			if (panels[i] != null && IsMissionPrepAvailableEquipmentPanel(panels[i]))
			{
				m_AvailableEquipmentPanel = panels[i];
				return;
			}
		}
	}

	private void TryResolveAvailableEquipmentCatalogReference()
	{
		if (m_AvailableEquipmentCatalog != null)
			return;

		m_AvailableEquipmentCatalog = GetComponentInChildren<MissionPrepAvailableEquipmentCatalog>(true);
		if (m_AvailableEquipmentCatalog == null && m_PresetCatalog != null)
			m_AvailableEquipmentCatalog = m_PresetCatalog.gameObject.AddComponent<MissionPrepAvailableEquipmentCatalog>();
	}

	private static bool IsMissionPrepInventoryPanel(InventoryPanelView _panel)
	{
		return IsMissionPrepPanelNamed(_panel, "PresetEquipmentPanel", "Units (1)");
	}

	private static bool IsMissionPrepAvailableEquipmentPanel(InventoryPanelView _panel)
	{
		return IsMissionPrepPanelNamed(_panel, "AvailableEquipmentPanel", "Units (3)");
	}

	private static bool IsMissionPrepPanelNamed(InventoryPanelView _panel, string _primaryName, string _fallbackParentName)
	{
		if (_panel == null || !_panel.IsConfiguredForDynamicRepaint)
			return false;

		Transform t = _panel.transform;
		while (t != null)
		{
			if (t.name == "UnitInventory" || t.name == "InventoryRoot" || t.name == "Ground")
				return false;

			if (t.name == _primaryName || t.name == _fallbackParentName)
				return true;

			t = t.parent;
		}

		return false;
	}
	#endregion
}
