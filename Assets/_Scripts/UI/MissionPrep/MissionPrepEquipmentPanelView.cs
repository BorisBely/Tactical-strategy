using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Пресеты и броня: опции из <see cref="MissionPrepEquipmentPresetCatalog"/>, выбор на <see cref="MissionPrepUnitPresetState"/>.
/// Смена брони сразу переключает визуал на <see cref="MissionPrepUnitArmorVisualController"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class MissionPrepEquipmentPanelView : MonoBehaviour
{
	#region Events
	public event Action<MissionPrepUnitPresetState, int> PresetSelected;
	public event Action<MissionPrepUnitPresetState, int> ArmorVisualSelected;
	public event Action<MissionPrepUnitPresetState, int> CamouflageVisualSelected;
	public event Action CreateNewPresetRequested;
	public event Action PresetListChanged;
	#endregion

	#region Serialized Fields
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[SerializeField] private MissionPrepLoadoutCoordinator m_LoadoutCoordinator;
	[SerializeField] private TMP_Dropdown m_PresetDropdown;
	[SerializeField] private TMP_Dropdown m_ArmorDropdown;
	[SerializeField] private TMP_Dropdown m_CamouflageDropdown;
	[SerializeField] private bool m_AppendCreateNewPresetEntry = true;

	[Tooltip("Если Armor Dropdown пуст — ищем TMP_Dropdown в UnitPreset (1).")]
	[SerializeField] private bool m_AutoResolveArmorDropdownInUi = true;

	[Tooltip("Если каталог не задан — число строк-заглушек.")]
	[SerializeField, Min(0)] private int m_FallbackStubPresetRows = 1;
	#endregion

	#region Private Fields
	private MissionPrepUnitPresetState m_BoundPresetState;
	private int m_LastCreateNewPresetIndex = -1;
	private int m_PresetSlotCount;
	private int m_ArmorOptionCount;
	private int m_CamouflageOptionCount;
	private bool m_SuppressPresetDropdownEvent;
	private bool m_SuppressArmorDropdownEvent;
	private bool m_SuppressCamouflageDropdownEvent;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsurePresetDropdownType();
		TryResolveArmorDropdownReference();
		TryResolveCamouflageDropdownReference();
		PrepareDropdownCaption(m_PresetDropdown);
		PrepareDropdownCaption(m_ArmorDropdown);
		PrepareDropdownCaption(m_CamouflageDropdown);
		EnsurePresetCaptionUi();
		EnsureArmorDropdownDescriptionHover();
		EnsureCamouflageDropdownDescriptionHover();
		SyncPresetDropdownReferences();
		LayoutEquipmentChrome();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!Application.isPlaying)
		{
			ApplyDropdownTextOnlyMode(m_PresetDropdown);
			ApplyDropdownTextOnlyMode(m_ArmorDropdown);
			ApplyDropdownTextOnlyMode(m_CamouflageDropdown);
		}
	}
#endif

	private void OnEnable()
	{
		TryResolveArmorDropdownReference();
		TryResolveCamouflageDropdownReference();
		PrepareDropdownCaption(m_PresetDropdown);
		PrepareDropdownCaption(m_ArmorDropdown);
		PrepareDropdownCaption(m_CamouflageDropdown);
		EnsureArmorDropdownDescriptionHover();
		EnsureCamouflageDropdownDescriptionHover();

		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		if (m_PresetDropdown != null)
			m_PresetDropdown.onValueChanged.AddListener(HandlePresetDropdownValueChanged);
		if (m_ArmorDropdown != null)
			m_ArmorDropdown.onValueChanged.AddListener(HandleArmorDropdownValueChanged);
		if (m_CamouflageDropdown != null)
			m_CamouflageDropdown.onValueChanged.AddListener(HandleCamouflageDropdownValueChanged);

		ResolveLoadoutCoordinatorReference();
		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.BeginEditingPresets();

		LayoutEquipmentChrome();
		RefreshPresetEditingUi();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		if (m_PresetDropdown != null)
			m_PresetDropdown.onValueChanged.RemoveListener(HandlePresetDropdownValueChanged);
		if (m_ArmorDropdown != null)
			m_ArmorDropdown.onValueChanged.RemoveListener(HandleArmorDropdownValueChanged);
		if (m_CamouflageDropdown != null)
			m_CamouflageDropdown.onValueChanged.RemoveListener(HandleCamouflageDropdownValueChanged);
	}
	#endregion

	#region Public Properties
	public MissionPrepUnitPresetState BoundPresetState => m_BoundPresetState;
	public int CreateNewPresetRowIndex => m_LastCreateNewPresetIndex;
	#endregion

	#region Public Methods
	public bool TryGetLoadoutCoordinator(out MissionPrepLoadoutCoordinator _coordinator)
	{
		ResolveLoadoutCoordinatorReference();
		_coordinator = m_LoadoutCoordinator;
		return _coordinator != null;
	}

	public void SetPresetDropdown(TMP_Dropdown _dropdown)
	{
		m_PresetDropdown = _dropdown;
		PrepareDropdownCaption(m_PresetDropdown);
	}
	public void SetVisible(bool _visible)
	{
		gameObject.SetActive(_visible);
	}

	public void SetLoadoutCoordinator(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_LoadoutCoordinator = _coordinator;
	}

	public void BindToUnit(GameObject _unitRoot)
	{
		ResolveLoadoutCoordinatorReference();
		SetUnitEditingChromeVisible(true);

		m_BoundPresetState = _unitRoot != null
			? MissionPrepUnitPresetState.GetOrCreate(_unitRoot, 0)
			: null;

		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.BindUnit(_unitRoot);
		else if (_unitRoot == null)
			ClearUnitBinding();
		else
			ApplyActivePresetForBoundUnitWithoutCoordinator();

		RefreshPresetEditingUi();
	}

	public void BindToVehicle(VehicleController _vehicle)
	{
		ResolveLoadoutCoordinatorReference();
		m_BoundPresetState = null;
		SetUnitEditingChromeVisible(false);

		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.BindVehicle(_vehicle);
	}

	private void SetUnitEditingChromeVisible(bool _visible)
	{
		if (m_PresetDropdown != null)
			m_PresetDropdown.gameObject.SetActive(_visible);
		if (m_ArmorDropdown != null)
			m_ArmorDropdown.gameObject.SetActive(_visible);
		if (m_CamouflageDropdown != null)
			m_CamouflageDropdown.gameObject.SetActive(_visible);

		// Wrapper roots used by renamed Prep* dropdowns.
		SetSiblingActiveIfPresent("PrepPresetDropdown", _visible);
		SetSiblingActiveIfPresent("PrepArmorDropdown", _visible);
		SetSiblingActiveIfPresent("PrepCamouflageDropdown", _visible);
		SetSiblingActiveIfPresent("UnitPreset", _visible);
		SetSiblingActiveIfPresent("UnitPreset (1)", _visible);
		SetSiblingActiveIfPresent("UnitCamouflage", _visible);
	}

	private void SetSiblingActiveIfPresent(string _name, bool _visible)
	{
		Transform t = transform.Find(_name);
		if (t == null && transform.parent != null)
			t = transform.parent.Find(_name);
		if (t != null)
			t.gameObject.SetActive(_visible);
	}

	public void ClearUnitBinding()
	{
		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.ClearUnitBinding();

		m_BoundPresetState = null;
	}

	public string GetBoundPresetLabel()
	{
		if (m_LoadoutCoordinator != null && m_BoundPresetState != null &&
		    m_LoadoutCoordinator.TryGetPresetLabelForUnit(m_BoundPresetState, out string label))
			return label;

		if (m_PresetCatalog == null || m_BoundPresetState == null)
			return string.Empty;

		return m_PresetCatalog.GetPresetLabel(m_PresetCatalog.ClampPresetIndex(m_BoundPresetState.PresetCatalogIndex));
	}

	public void RefreshPresetEditingUi()
	{
		RebuildPresetDropdownOptions();
		RebuildArmorDropdownOptions();
		RebuildCamouflageDropdownOptions();

		int presetIndex = m_LoadoutCoordinator != null
			? m_LoadoutCoordinator.EditingPresetCatalogIndex
			: m_BoundPresetState != null
				? m_BoundPresetState.PresetCatalogIndex
				: 0;

		ResolveLoadoutCoordinatorReference();
		if (m_LoadoutCoordinator != null)
			presetIndex = m_LoadoutCoordinator.ClampPresetCatalogIndex(presetIndex);
		else if (m_PresetCatalog != null)
			presetIndex = m_PresetCatalog.ClampPresetIndex(presetIndex);
		else
			presetIndex = Mathf.Clamp(presetIndex, 0, Mathf.Max(0, m_PresetSlotCount - 1));

		if (m_PresetDropdown != null)
		{
			m_SuppressPresetDropdownEvent = true;
			m_PresetDropdown.SetValueWithoutNotify(presetIndex);
			m_PresetDropdown.RefreshShownValue();
			m_SuppressPresetDropdownEvent = false;
		}

		if (m_ArmorDropdown != null)
		{
			int armorIndex = m_LoadoutCoordinator != null
				? m_LoadoutCoordinator.GetActivePresetArmorIndex()
				: m_BoundPresetState != null
					? m_BoundPresetState.GetArmorForPreset(presetIndex)
					: 0;

			if (m_PresetCatalog != null)
				armorIndex = m_PresetCatalog.ClampArmorIndex(armorIndex);
			else
				armorIndex = Mathf.Clamp(armorIndex, 0, Mathf.Max(0, m_ArmorOptionCount - 1));

			m_SuppressArmorDropdownEvent = true;
			m_ArmorDropdown.SetValueWithoutNotify(armorIndex);
			m_ArmorDropdown.RefreshShownValue();
			m_SuppressArmorDropdownEvent = false;
		}

		if (m_CamouflageDropdown != null)
		{
			int camouflageIndex = m_LoadoutCoordinator != null
				? m_LoadoutCoordinator.GetActivePresetCamouflageIndex()
				: m_BoundPresetState != null
					? m_BoundPresetState.GetCamouflageForPreset(presetIndex)
					: 0;

			if (m_PresetCatalog != null)
				camouflageIndex = m_PresetCatalog.ClampCamouflageIndex(camouflageIndex);
			else
				camouflageIndex = Mathf.Clamp(camouflageIndex, 0, Mathf.Max(0, m_CamouflageOptionCount - 1));

			m_SuppressCamouflageDropdownEvent = true;
			m_CamouflageDropdown.SetValueWithoutNotify(camouflageIndex);
			m_CamouflageDropdown.RefreshShownValue();
			m_SuppressCamouflageDropdownEvent = false;
		}

		if (m_LoadoutCoordinator == null)
		{
			ApplyArmorVisualForBoundUnit();
			ApplyCamouflageVisualForBoundUnit();
		}

		PresetListChanged?.Invoke();
	}

	public void RefreshForBoundUnit() => RefreshPresetEditingUi();

	public void NotifyPresetCreated()
	{
		RefreshPresetEditingUi();

		MissionPrepPresetCaptionUi captionUi = GetComponent<MissionPrepPresetCaptionUi>();
		captionUi?.BeginRenameCaption();
	}
	#endregion

	#region Private Methods
	private void TryResolveArmorDropdownReference()
	{
		if (m_ArmorDropdown != null || !m_AutoResolveArmorDropdownInUi || m_PresetDropdown == null)
			return;

		m_ArmorDropdown = FindSiblingDropdownByRootNames("PrepArmorDropdown", "UnitPreset (1)");
		if (m_ArmorDropdown != null)
			PrepareDropdownCaption(m_ArmorDropdown);
	}

	private void TryResolveCamouflageDropdownReference()
	{
		if (m_CamouflageDropdown != null)
			return;

		m_CamouflageDropdown = FindSiblingDropdownByRootNames("PrepCamouflageDropdown", "UnitCamouflage");
		if (m_CamouflageDropdown != null)
			PrepareDropdownCaption(m_CamouflageDropdown);
	}

	private TMP_Dropdown FindSiblingDropdownByRootNames(params string[] _rootNames)
	{
		if (_rootNames == null || _rootNames.Length == 0)
			return null;

		// Hierarchy: Dropdown(this) → Prep*Dropdown wrapper → PrepPresetEquipmentPanel
		Transform panel = transform.parent != null ? transform.parent.parent : null;
		if (panel == null)
			panel = transform.parent;
		if (panel == null)
			return null;

		for (int i = 0; i < panel.childCount; i++)
		{
			Transform section = panel.GetChild(i);
			if (section == null)
				continue;

			bool nameMatch = false;
			for (int n = 0; n < _rootNames.Length; n++)
			{
				if (section.name == _rootNames[n])
				{
					nameMatch = true;
					break;
				}
			}

			if (!nameMatch)
				continue;

			TMP_Dropdown dropdown = section.GetComponentInChildren<TMP_Dropdown>(true);
			if (dropdown != null && dropdown != m_PresetDropdown)
				return dropdown;
		}

		return null;
	}

	private void RebuildPresetDropdownOptions()
	{
		if (m_PresetDropdown == null)
			return;

		ResolveLoadoutCoordinatorReference();

		m_SuppressPresetDropdownEvent = true;
		m_PresetDropdown.ClearOptions();

		List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
		int presetCount = ResolvePresetSlotCount();

		for (int i = 0; i < presetCount; i++)
		{
			string label = ResolvePresetLabel(i);
			options.Add(new TMP_Dropdown.OptionData(label));
		}

		m_PresetSlotCount = presetCount;

		if (m_AppendCreateNewPresetEntry)
		{
			string createLabel = LocalizationManager.Get("mission_prep.equipment.create_new_preset");
			options.Add(new TMP_Dropdown.OptionData(createLabel));
			m_LastCreateNewPresetIndex = options.Count - 1;
		}
		else
			m_LastCreateNewPresetIndex = -1;

		if (options.Count > 0)
			m_PresetDropdown.AddOptions(options);

		m_SuppressPresetDropdownEvent = false;
	}

	private int ResolvePresetSlotCount()
	{
		if (m_LoadoutCoordinator != null)
			return m_LoadoutCoordinator.GetPresetSlotCount();

		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.PresetCount;

		return m_FallbackStubPresetRows;
	}

	private string ResolvePresetLabel(int _presetIndex)
	{
		if (m_LoadoutCoordinator != null && m_LoadoutCoordinator.TryGetPresetLabel(_presetIndex, out string label))
			return label;

		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.GetPresetLabel(_presetIndex);

		return $"Preset stub {_presetIndex + 1}";
	}

	private void RebuildArmorDropdownOptions()
	{
		if (m_ArmorDropdown == null)
			return;

		m_SuppressArmorDropdownEvent = true;
		m_ArmorDropdown.ClearOptions();

		List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();

		if (m_PresetCatalog != null && m_PresetCatalog.ArmorOptionCount > 0)
		{
			m_ArmorOptionCount = m_PresetCatalog.ArmorOptionCount;
			for (int i = 0; i < m_PresetCatalog.ArmorOptionCount; i++)
				options.Add(new TMP_Dropdown.OptionData(m_PresetCatalog.GetArmorLabel(i)));
		}
		else
		{
			m_ArmorOptionCount = MissionPrepUnitArmorVisualController.ArmorVariantCount;
			options.Add(new TMP_Dropdown.OptionData(
				LocalizationManager.Get("mission_prep.equipment.armor.light", "Light armor")));
			options.Add(new TMP_Dropdown.OptionData(
				LocalizationManager.Get("mission_prep.equipment.armor.heavy", "Heavy armor")));
		}

		if (options.Count > 0)
			m_ArmorDropdown.AddOptions(options);

		m_SuppressArmorDropdownEvent = false;
	}

	private void RebuildCamouflageDropdownOptions()
	{
		if (m_CamouflageDropdown == null)
			return;

		m_SuppressCamouflageDropdownEvent = true;
		m_CamouflageDropdown.ClearOptions();

		List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();

		if (m_PresetCatalog != null && m_PresetCatalog.CamouflageOptionCount > 0)
		{
			m_CamouflageOptionCount = m_PresetCatalog.CamouflageOptionCount;
			for (int i = 0; i < m_PresetCatalog.CamouflageOptionCount; i++)
				options.Add(new TMP_Dropdown.OptionData(m_PresetCatalog.GetCamouflageLabel(i)));
		}
		else
		{
			m_CamouflageOptionCount = UnitCamouflagePatternUtility.PatternCount;
			for (int i = 0; i < m_CamouflageOptionCount; i++)
			{
				UnitCamouflagePattern pattern = UnitCamouflagePatternUtility.FromIndex(i);
				options.Add(new TMP_Dropdown.OptionData(UnitCamouflagePatternUtility.GetLocalizedLabel(pattern)));
			}
		}

		if (options.Count > 0)
			m_CamouflageDropdown.AddOptions(options);

		m_SuppressCamouflageDropdownEvent = false;
	}

	private void HandleLanguageChanged()
	{
		RefreshPresetEditingUi();
	}

	private void HandlePresetDropdownValueChanged(int _index)
	{
		if (m_SuppressPresetDropdownEvent)
			return;

		if (m_LastCreateNewPresetIndex >= 0 && _index == m_LastCreateNewPresetIndex)
		{
			RestorePresetDropdownSelection();
			CreateNewPresetRequested?.Invoke();
			return;
		}

		if (_index < 0 || _index >= m_PresetSlotCount)
			return;

		ResolveLoadoutCoordinatorReference();

		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.SwitchToPreset(_index);
		else
			ApplyPresetForBoundUnitWithoutCoordinator(_index);

		RefreshPresetEditingUi();
		PresetSelected?.Invoke(m_BoundPresetState, _index);
	}

	private void HandleArmorDropdownValueChanged(int _index)
	{
		if (m_SuppressArmorDropdownEvent)
			return;

		if (_index < 0 || _index >= m_ArmorOptionCount)
			return;

		ResolveLoadoutCoordinatorReference();

		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.SetActivePresetArmor(_index);
		else if (m_BoundPresetState != null)
		{
			m_BoundPresetState.SetArmorForActivePreset(_index);
			MissionPrepLoadoutCoordinator.Instance?.PropagatePresetToAllUnitsWithCatalogIndex(
				m_BoundPresetState.PresetCatalogIndex);
			ApplyArmorVisualForBoundUnit();
		}

		RefreshPresetEditingUi();
		ArmorVisualSelected?.Invoke(m_BoundPresetState, _index);
	}

	private void HandleCamouflageDropdownValueChanged(int _index)
	{
		if (m_SuppressCamouflageDropdownEvent)
			return;

		if (_index < 0 || _index >= m_CamouflageOptionCount)
			return;

		ResolveLoadoutCoordinatorReference();

		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.SetActivePresetCamouflage(_index);
		else if (m_BoundPresetState != null)
		{
			m_BoundPresetState.SetCamouflageForActivePreset(_index);
			MissionPrepLoadoutCoordinator.Instance?.PropagatePresetToAllUnitsWithCatalogIndex(
				m_BoundPresetState.PresetCatalogIndex);
			ApplyCamouflageVisualForBoundUnit();
		}

		RefreshPresetEditingUi();
		CamouflageVisualSelected?.Invoke(m_BoundPresetState, _index);
	}

	private void ResolveLoadoutCoordinatorReference()
	{
		if (m_LoadoutCoordinator != null)
			return;

		m_LoadoutCoordinator = GetComponentInParent<MissionPrepLoadoutCoordinator>();
		if (m_LoadoutCoordinator == null)
			m_LoadoutCoordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void ApplyPresetForBoundUnitWithoutCoordinator(int _presetIndex)
	{
		if (m_BoundPresetState == null)
			return;

		CharacterInventory inventory = m_BoundPresetState.GetComponentInChildren<CharacterInventory>(true);
		int presetCount = m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0
			? m_PresetCatalog.PresetCount
			: 2;

		if (inventory != null)
		{
			m_BoundPresetState.ChangeActivePresetIndex(_presetIndex, inventory, presetCount);
			m_BoundPresetState.EnsureSnapshotDefaultsFromCatalog(_presetIndex, m_PresetCatalog);
			m_BoundPresetState.ApplyActivePresetToRuntime(inventory);

			if (m_LoadoutCoordinator != null)
				m_LoadoutCoordinator.RepaintInventoryPanel();
		}
		else
			m_BoundPresetState.SetActivePresetIndex(_presetIndex, presetCount);

		ApplyArmorVisualForBoundUnit();
		ApplyCamouflageVisualForBoundUnit();
	}

	private void ApplyActivePresetForBoundUnitWithoutCoordinator()
	{
		if (m_BoundPresetState == null)
			return;

		CharacterInventory inventory = m_BoundPresetState.GetComponentInChildren<CharacterInventory>(true);
		if (inventory == null)
			return;

		m_BoundPresetState.ApplyActivePresetToRuntime(inventory);
		ApplyArmorVisualForBoundUnit();
		ApplyCamouflageVisualForBoundUnit();
	}

	private void ApplyArmorVisualForBoundUnit()
	{
		if (m_BoundPresetState == null)
			return;

		GameObject unitRoot = m_BoundPresetState.gameObject;
		int armorIndex = m_PresetCatalog != null
			? m_PresetCatalog.ClampArmorIndex(m_BoundPresetState.GetArmorForPreset(m_BoundPresetState.PresetCatalogIndex))
			: Mathf.Clamp(
				m_BoundPresetState.GetArmorForPreset(m_BoundPresetState.PresetCatalogIndex),
				0,
				MissionPrepUnitArmorVisualController.ArmorVariantCount - 1);

		MissionPrepUnitArmorVisualController visual = MissionPrepUnitArmorVisualController.GetOrCreate(unitRoot, armorIndex);
		visual.ApplyArmorVisual(armorIndex);

		UnitArmor armor = unitRoot.GetComponent<UnitArmor>() ?? unitRoot.AddComponent<UnitArmor>();
		armor.SetArmorFromPresetIndex(armorIndex);
	}

	private void ApplyCamouflageVisualForBoundUnit()
	{
		if (m_BoundPresetState == null)
			return;

		GameObject unitRoot = m_BoundPresetState.gameObject;
		int presetIndex = m_BoundPresetState.PresetCatalogIndex;
		int camouflageIndex = m_PresetCatalog != null
			? m_PresetCatalog.ClampCamouflageIndex(m_BoundPresetState.GetCamouflageForPreset(presetIndex))
			: UnitCamouflagePatternUtility.ClampIndex(m_BoundPresetState.GetCamouflageForPreset(presetIndex));

		UnitCharacterMaterialAppearance materialAppearance = UnitCharacterMaterialAppearance.GetOrCreate(unitRoot);
		if (materialAppearance != null)
			materialAppearance.SetCamouflageIndex(camouflageIndex);
	}

	private static void PrepareDropdownCaption(TMP_Dropdown _dropdown)
	{
		if (_dropdown == null)
			return;

		ReleaseCaptionFromStaticLocalization(_dropdown.captionText);
		ApplyDropdownTextOnlyMode(_dropdown);
	}

	private static void ReleaseCaptionFromStaticLocalization(TMP_Text _captionText)
	{
		if (_captionText == null)
			return;

		LocalizedTextMeshProUGUI localized = _captionText.GetComponent<LocalizedTextMeshProUGUI>();
		if (localized == null)
			return;

		if (Application.isPlaying)
			Destroy(localized);
		else
			DestroyImmediate(localized);
	}

	private static void ApplyDropdownTextOnlyMode(TMP_Dropdown _dropdown)
	{
		if (_dropdown == null)
			return;

		HideDropdownImageSlot(_dropdown.captionImage);
		HideDropdownImageSlot(_dropdown.itemImage);
	}

	private static void HideDropdownImageSlot(Image _image)
	{
		if (_image == null)
			return;

		_image.sprite = null;
		_image.enabled = false;
	}

	private void RestorePresetDropdownSelection()
	{
		if (m_PresetDropdown == null)
			return;

		int presetIndex = m_LoadoutCoordinator != null
			? m_LoadoutCoordinator.EditingPresetCatalogIndex
			: m_BoundPresetState != null
				? m_BoundPresetState.PresetCatalogIndex
				: 0;

		if (m_LoadoutCoordinator != null)
			presetIndex = m_LoadoutCoordinator.ClampPresetCatalogIndex(presetIndex);
		else if (m_PresetCatalog != null)
			presetIndex = m_PresetCatalog.ClampPresetIndex(presetIndex);
		else
			presetIndex = Mathf.Clamp(presetIndex, 0, Mathf.Max(0, m_PresetSlotCount - 1));

		m_SuppressPresetDropdownEvent = true;
		m_PresetDropdown.SetValueWithoutNotify(presetIndex);
		m_PresetDropdown.RefreshShownValue();
		m_SuppressPresetDropdownEvent = false;
	}

	private void EnsurePresetCaptionUi()
	{
		if (m_PresetDropdown == null)
			return;

		if (GetComponent<MissionPrepPresetCaptionUi>() == null)
			gameObject.AddComponent<MissionPrepPresetCaptionUi>();
	}

	private void EnsureArmorDropdownDescriptionHover()
	{
		if (m_ArmorDropdown == null)
			return;

		MissionPrepArmorDropdownDescriptionHover hover =
			m_ArmorDropdown.GetComponent<MissionPrepArmorDropdownDescriptionHover>();
		if (hover == null)
			hover = m_ArmorDropdown.gameObject.AddComponent<MissionPrepArmorDropdownDescriptionHover>();

		hover.Bind(m_ArmorDropdown, m_PresetCatalog);
	}

	private void EnsureCamouflageDropdownDescriptionHover()
	{
		if (m_CamouflageDropdown == null)
			return;

		MissionPrepCamouflageDropdownDescriptionHover hover =
			m_CamouflageDropdown.GetComponent<MissionPrepCamouflageDropdownDescriptionHover>();
		if (hover == null)
			hover = m_CamouflageDropdown.gameObject.AddComponent<MissionPrepCamouflageDropdownDescriptionHover>();

		hover.Bind(m_CamouflageDropdown, m_PresetCatalog);
	}

	private void EnsurePresetDropdownType()
	{
		MissionPrepPresetDropdownUtility.EnsureOn(gameObject, ref m_PresetDropdown);
	}

	/// <summary>
	/// After ColumnContent reparent, dropdown wrappers kept old positive-Y offsets and floated away.
	/// Pin preset/armor/camo + scroll under the collapse strip.
	/// </summary>
	private void LayoutEquipmentChrome()
	{
		const float dropdownHeight = 40f;
		const float gap = 4f;
		float top = gap;

		RectTransform presetWrap = ResolveDropdownWrapper(m_PresetDropdown, "PrepPresetDropdown");
		if (presetWrap != null)
		{
			PinTopStrip(presetWrap, top, dropdownHeight);
			top += dropdownHeight + gap;
		}

		RectTransform armorWrap = ResolveDropdownWrapper(m_ArmorDropdown, "PrepArmorDropdown");
		if (armorWrap != null)
		{
			PinTopStrip(armorWrap, top, dropdownHeight);
			top += dropdownHeight + gap;
		}

		RectTransform camoWrap = ResolveDropdownWrapper(m_CamouflageDropdown, "PrepCamouflageDropdown");
		if (camoWrap != null)
		{
			PinTopStrip(camoWrap, top, dropdownHeight);
			top += dropdownHeight + gap;
		}

		Transform content = transform.Find("ColumnContent");
		if (content == null)
			content = transform;

		// Duplicate of CollapseToggle title — hide floating Text (TMP) inside content.
		for (int i = 0; i < content.childCount; i++)
		{
			Transform child = content.GetChild(i);
			if (child != null && child.name == "Text (TMP)")
				child.gameObject.SetActive(false);
		}

		RectTransform scroll = content.Find("PrepPresetEquipmentPanelScroll") as RectTransform;
		if (scroll == null)
		{
			Transform deep = FindDeepChild(content, "PrepPresetEquipmentPanelScroll");
			scroll = deep as RectTransform;
		}

		if (scroll != null)
		{
			scroll.anchorMin = Vector2.zero;
			scroll.anchorMax = Vector2.one;
			scroll.pivot = new Vector2(0.5f, 0.5f);
			scroll.anchoredPosition = new Vector2(0f, -top * 0.5f);
			scroll.sizeDelta = new Vector2(0f, -top);
		}
	}

	private RectTransform ResolveDropdownWrapper(TMP_Dropdown _dropdown, string _wrapperName)
	{
		Transform content = transform.Find("ColumnContent") ?? transform;
		Transform named = content.Find(_wrapperName) ?? FindDeepChild(content, _wrapperName);
		if (named != null)
			return named as RectTransform;

		if (_dropdown == null)
			return null;

		Transform wrap = _dropdown.transform.parent;
		if (wrap != null && wrap.name == _wrapperName)
			return wrap as RectTransform;

		return _dropdown.transform as RectTransform;
	}

	private static void PinTopStrip(RectTransform _rt, float _topInset, float _height)
	{
		if (_rt == null)
			return;

		_rt.anchorMin = new Vector2(0f, 1f);
		_rt.anchorMax = new Vector2(1f, 1f);
		_rt.pivot = new Vector2(0.5f, 1f);
		_rt.anchoredPosition = new Vector2(0f, -_topInset);
		_rt.sizeDelta = new Vector2(0f, _height);
		_rt.localScale = Vector3.one;

		// Inner TMP_Dropdown should fill the wrapper.
		if (_rt.childCount > 0)
		{
			RectTransform inner = _rt.GetChild(0) as RectTransform;
			if (inner != null && inner.GetComponent<TMP_Dropdown>() != null)
			{
				inner.anchorMin = Vector2.zero;
				inner.anchorMax = Vector2.one;
				inner.offsetMin = Vector2.zero;
				inner.offsetMax = Vector2.zero;
				inner.pivot = new Vector2(0.5f, 0.5f);
			}
		}
	}

	private static Transform FindDeepChild(Transform _parent, string _name)
	{
		if (_parent == null || string.IsNullOrEmpty(_name))
			return null;

		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform child = _parent.GetChild(i);
			if (child.name == _name)
				return child;
			Transform nested = FindDeepChild(child, _name);
			if (nested != null)
				return nested;
		}

		return null;
	}

	private void SyncPresetDropdownReferences()
	{
		MissionPrepPresetCaptionUi captionUi = GetComponent<MissionPrepPresetCaptionUi>();
		if (captionUi != null && m_PresetDropdown != null)
			captionUi.SetPresetDropdown(m_PresetDropdown);
	}
	#endregion
}
