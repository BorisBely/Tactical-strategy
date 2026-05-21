using TMPro;
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

	[SerializeField] private bool m_HideInventoryUntilUnitSelected = true;
	[SerializeField] private bool m_InventoryStartHidden = true;

	[Header("Заголовок инвентаря (опционально)")]
	[SerializeField] private TMP_Text m_InventoryTitleText;
	[SerializeField] private string m_InventoryTitleLocalizationKey = "mission_prep.equipment.inventory_title";
	#endregion

	#region Private Fields
	private MissionPrepUnitCellView m_CurrentUnitCell;
	#endregion

	#region Public Properties
	public InventoryPanelView PresetInventoryPanel => m_PresetInventoryPanel;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureLoadoutCoordinator();
		WireLoadoutCoordinator();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;

		if (m_UnitList != null)
			m_UnitList.UnitCellSelected += HandleUnitSelected;

		if (m_EquipmentPanel != null)
		{
			m_EquipmentPanel.PresetSelected += HandlePresetSelected;
			m_EquipmentPanel.ArmorVisualSelected += HandleArmorVisualSelected;
			m_EquipmentPanel.CreateNewPresetRequested += HandleCreateNewPresetRequested;
		}

		if (m_HideEquipmentUntilSelection && m_EquipmentPanel != null)
			m_EquipmentPanel.SetVisible(false);

		ApplyStaticInventoryTitle();

		if (m_InventoryStartHidden)
			SetInventoryVisible(false);
		else if (m_HideInventoryUntilUnitSelected)
			OnInventoryUnitBindingChanged(false);
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;

		if (m_UnitList != null)
			m_UnitList.UnitCellSelected -= HandleUnitSelected;

		if (m_EquipmentPanel != null)
		{
			m_EquipmentPanel.PresetSelected -= HandlePresetSelected;
			m_EquipmentPanel.ArmorVisualSelected -= HandleArmorVisualSelected;
			m_EquipmentPanel.CreateNewPresetRequested -= HandleCreateNewPresetRequested;
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

		if (m_PresetCatalog == null)
			return string.Empty;

		return m_PresetCatalog.GetPresetLabel(m_PresetCatalog.ClampPresetIndex(state.PresetCatalogIndex));
	}

	public void SetInventoryVisible(bool _visible)
	{
		if (m_PresetInventoryPanel != null)
			m_PresetInventoryPanel.gameObject.SetActive(_visible);
	}

	public void RefreshInventoryPanel()
	{
		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.RepaintInventoryPanel();
		else if (m_PresetInventoryPanel != null)
			m_PresetInventoryPanel.ClearAllSlots();
	}
	#endregion

	#region Private Methods
	private void EnsureLoadoutCoordinator()
	{
		if (m_LoadoutCoordinator == null)
			TryGetComponent(out m_LoadoutCoordinator);

		if (m_LoadoutCoordinator == null)
			m_LoadoutCoordinator = gameObject.AddComponent<MissionPrepLoadoutCoordinator>();
	}

	private void WireLoadoutCoordinator()
	{
		if (m_LoadoutCoordinator == null)
			return;

		TryResolvePresetInventoryPanelReference();

		m_LoadoutCoordinator.Configure(m_PresetCatalog, m_PresetInventoryPanel);

		if (m_EquipmentPanel != null)
			m_EquipmentPanel.SetLoadoutCoordinator(m_LoadoutCoordinator);
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
		RefreshUnitCellPresetLabel(m_CurrentUnitCell);
		RefreshInventoryPanel();
	}

	private void HandleArmorVisualSelected(MissionPrepUnitPresetState _state, int _armorIndex)
	{
		RefreshInventoryPanel();
	}

	private void OnInventoryUnitBindingChanged(bool _hasBoundUnit)
	{
		if (m_HideInventoryUntilUnitSelected)
			SetInventoryVisible(_hasBoundUnit);
		else if (_hasBoundUnit)
			SetInventoryVisible(true);

		if (_hasBoundUnit)
			RefreshInventoryPanel();
		else
			ClearInventoryPanel();
	}

	private void ClearInventoryPanel()
	{
		if (m_PresetInventoryPanel != null)
			m_PresetInventoryPanel.ClearAllSlots();
	}

	private void HandleLanguageChanged()
	{
		ApplyStaticInventoryTitle();
	}

	private void ApplyStaticInventoryTitle()
	{
		if (m_InventoryTitleText == null)
			return;

		m_InventoryTitleText.text = LocalizationManager.Get(m_InventoryTitleLocalizationKey, "Inventory");
	}

	private void RefreshUnitCellPresetLabel(MissionPrepUnitCellView _cell)
	{
		if (_cell == null)
			return;

		string label = m_EquipmentPanel != null
			? m_EquipmentPanel.GetBoundPresetLabel()
			: GetPresetLabelForUnit(_cell.BoundUnitRoot);

		_cell.SetPresetDisplayName(label);
	}

	private void HandleCreateNewPresetRequested()
	{
		// Открыть поток создания пресета.
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

	private static bool IsMissionPrepInventoryPanel(InventoryPanelView _panel)
	{
		if (_panel == null || !_panel.IsConfiguredForDynamicRepaint)
			return false;

		Transform t = _panel.transform;
		while (t != null)
		{
			if (t.name == "UnitInventory" || t.name == "InventoryRoot")
				return false;

			if (t.name == "Units (2)")
				return true;

			t = t.parent;
		}

		return false;
	}
	#endregion
}
