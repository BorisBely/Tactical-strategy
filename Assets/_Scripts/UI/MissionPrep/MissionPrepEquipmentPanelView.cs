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
public sealed class MissionPrepEquipmentPanelView : MonoBehaviour
{
	#region Events
	public event Action<MissionPrepUnitPresetState, int> PresetSelected;
	public event Action<MissionPrepUnitPresetState, int> ArmorVisualSelected;
	public event Action CreateNewPresetRequested;
	#endregion

	#region Serialized Fields
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[SerializeField] private MissionPrepLoadoutCoordinator m_LoadoutCoordinator;
	[SerializeField] private TMP_Dropdown m_PresetDropdown;
	[SerializeField] private TMP_Dropdown m_ArmorDropdown;
	[SerializeField] private bool m_AppendCreateNewPresetEntry = true;

	[Tooltip("Если Armor Dropdown пуст — ищем другой TMP_Dropdown в соседних секциях UI (UnitPreset и т.д.).")]
	[SerializeField] private bool m_AutoResolveArmorDropdownInUi = true;

	[Tooltip("Если каталог не задан — число строк-заглушек.")]
	[SerializeField, Min(0)] private int m_FallbackStubPresetRows = 1;
	#endregion

	#region Private Fields
	private MissionPrepUnitPresetState m_BoundPresetState;
	private int m_LastCreateNewPresetIndex = -1;
	private int m_PresetSlotCount;
	private int m_ArmorOptionCount;
	private bool m_SuppressPresetDropdownEvent;
	private bool m_SuppressArmorDropdownEvent;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		TryResolveArmorDropdownReference();
		ApplyDropdownTextOnlyMode(m_PresetDropdown);
		ApplyDropdownTextOnlyMode(m_ArmorDropdown);
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!Application.isPlaying)
		{
			ApplyDropdownTextOnlyMode(m_PresetDropdown);
			ApplyDropdownTextOnlyMode(m_ArmorDropdown);
		}
	}
#endif

	private void OnEnable()
	{
		TryResolveArmorDropdownReference();
		ApplyDropdownTextOnlyMode(m_PresetDropdown);
		ApplyDropdownTextOnlyMode(m_ArmorDropdown);

		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		if (m_PresetDropdown != null)
			m_PresetDropdown.onValueChanged.AddListener(HandlePresetDropdownValueChanged);
		if (m_ArmorDropdown != null)
			m_ArmorDropdown.onValueChanged.AddListener(HandleArmorDropdownValueChanged);

		ResolveLoadoutCoordinatorReference();
		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.BeginEditingPresets();

		RefreshPresetEditingUi();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		if (m_PresetDropdown != null)
			m_PresetDropdown.onValueChanged.RemoveListener(HandlePresetDropdownValueChanged);
		if (m_ArmorDropdown != null)
			m_ArmorDropdown.onValueChanged.RemoveListener(HandleArmorDropdownValueChanged);
	}
	#endregion

	#region Public Methods
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

	public void ClearUnitBinding()
	{
		if (m_LoadoutCoordinator != null)
			m_LoadoutCoordinator.ClearUnitBinding();

		m_BoundPresetState = null;
	}

	public MissionPrepUnitPresetState BoundPresetState => m_BoundPresetState;

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

		int presetIndex = m_LoadoutCoordinator != null
			? m_LoadoutCoordinator.EditingPresetCatalogIndex
			: m_BoundPresetState != null
				? m_BoundPresetState.PresetCatalogIndex
				: 0;

		if (m_PresetCatalog != null)
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

		if (m_LoadoutCoordinator == null)
			ApplyArmorVisualForBoundUnit();
	}

	public void RefreshForBoundUnit() => RefreshPresetEditingUi();
	#endregion

	#region Private Methods
	private void TryResolveArmorDropdownReference()
	{
		if (m_ArmorDropdown != null || !m_AutoResolveArmorDropdownInUi || m_PresetDropdown == null)
			return;

		Transform layoutContent = transform.parent != null ? transform.parent.parent : null;
		if (layoutContent == null)
			return;

		for (int i = 0; i < layoutContent.childCount; i++)
		{
			TMP_Dropdown[] dropdowns = layoutContent.GetChild(i).GetComponentsInChildren<TMP_Dropdown>(true);
			for (int d = 0; d < dropdowns.Length; d++)
			{
				if (dropdowns[d] == null || dropdowns[d] == m_PresetDropdown)
					continue;

				m_ArmorDropdown = dropdowns[d];
				return;
			}
		}
	}

	private void RebuildPresetDropdownOptions()
	{
		if (m_PresetDropdown == null)
			return;

		m_SuppressPresetDropdownEvent = true;
		m_PresetDropdown.ClearOptions();

		List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();

		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
		{
			m_PresetSlotCount = m_PresetCatalog.PresetCount;
			for (int i = 0; i < m_PresetCatalog.PresetCount; i++)
				options.Add(new TMP_Dropdown.OptionData(m_PresetCatalog.GetPresetLabel(i)));
		}
		else
		{
			m_PresetSlotCount = m_FallbackStubPresetRows;
			for (int i = 0; i < m_FallbackStubPresetRows; i++)
				options.Add(new TMP_Dropdown.OptionData($"Preset stub {i + 1}"));
		}

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
	#endregion
}
