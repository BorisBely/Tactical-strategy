using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Подсказки для дропдауна камуфляжа в окне подготовки пресета.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepCamouflageDropdownDescriptionHover : MonoBehaviour
{
	#region Constants
	private const string c_DropdownListChildName = "Dropdown List";
	private const string c_DropdownContentPath = "Viewport/Content";
	#endregion

	#region Serialized Fields
	[SerializeField] private TMP_Dropdown m_CamouflageDropdown;
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	#endregion

	#region Private Fields
	private UiDescriptionHover m_CaptionHover;
	private readonly List<UiDescriptionHover> m_ItemHovers = new List<UiDescriptionHover>();
	private int m_LastCaptionCamouflageIndex = -1;
	private int m_LastItemHoverCount = -1;
	#endregion

	#region Public Methods
	public void Bind(TMP_Dropdown _camouflageDropdown, MissionPrepEquipmentPresetCatalog _presetCatalog)
	{
		m_CamouflageDropdown = _camouflageDropdown;
		m_PresetCatalog = _presetCatalog;
		RefreshAll();
	}
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		EnsureCamouflageDropdownReference();
		RefreshAll();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}

	private void LateUpdate()
	{
		EnsureCamouflageDropdownReference();
		RefreshCaptionHover();
		RefreshItemHovers();
	}
	#endregion

	#region Private Methods
	private void HandleLanguageChanged()
	{
		m_LastCaptionCamouflageIndex = -1;
		m_LastItemHoverCount = -1;
		RefreshAll();
	}

	private void RefreshAll()
	{
		RefreshCaptionHover();
		RefreshItemHovers();
	}

	private void EnsureCamouflageDropdownReference()
	{
		if (m_CamouflageDropdown != null)
			return;

		m_CamouflageDropdown = GetComponent<TMP_Dropdown>();
	}

	private void RefreshCaptionHover()
	{
		if (m_CamouflageDropdown == null)
			return;

		int camouflageIndex = Mathf.Clamp(m_CamouflageDropdown.value, 0, ResolveCamouflageOptionCount() - 1);
		if (m_CaptionHover == null)
		{
			m_CaptionHover = m_CamouflageDropdown.gameObject.GetComponent<UiDescriptionHover>();
			if (m_CaptionHover == null)
				m_CaptionHover = m_CamouflageDropdown.gameObject.AddComponent<UiDescriptionHover>();
		}

		if (m_LastCaptionCamouflageIndex == camouflageIndex && m_CaptionHover != null)
			return;

		m_LastCaptionCamouflageIndex = camouflageIndex;
		ResolveCamouflageTooltip(camouflageIndex, out string title, out string description);
		m_CaptionHover.Configure(title, description, m_CamouflageDropdown.transform as RectTransform);
	}

	private void RefreshItemHovers()
	{
		if (m_CamouflageDropdown == null)
			return;

		Transform dropdownList = m_CamouflageDropdown.transform.Find(c_DropdownListChildName);
		if (dropdownList == null || !dropdownList.gameObject.activeInHierarchy)
			return;

		Transform content = dropdownList.Find(c_DropdownContentPath);
		if (content == null)
			return;

		int optionCount = ResolveCamouflageOptionCount();
		int childCount = content.childCount;
		if (childCount <= 0)
			return;

		if (m_LastItemHoverCount == childCount && m_ItemHovers.Count == childCount)
			return;

		m_ItemHovers.Clear();
		m_LastItemHoverCount = childCount;

		for (int i = 0; i < childCount; i++)
		{
			Transform itemTransform = content.GetChild(i);
			ResolveDropdownItemHoverTarget(itemTransform, out GameObject hoverHost, out RectTransform hoverRect);

			UiDescriptionHover hover = hoverHost.GetComponent<UiDescriptionHover>();
			if (hover == null)
				hover = hoverHost.AddComponent<UiDescriptionHover>();

			int camouflageIndex = Mathf.Clamp(i, 0, optionCount - 1);
			ResolveCamouflageTooltip(camouflageIndex, out string title, out string description);
			hover.Configure(title, description, hoverRect);
			m_ItemHovers.Add(hover);
		}
	}

	private int ResolveCamouflageOptionCount()
	{
		if (m_PresetCatalog != null && m_PresetCatalog.CamouflageOptionCount > 0)
			return m_PresetCatalog.CamouflageOptionCount;

		return UnitCamouflagePatternUtility.PatternCount;
	}

	private void ResolveCamouflageTooltip(int _camouflageIndex, out string _title, out string _description)
	{
		if (m_PresetCatalog != null && _camouflageIndex >= 0 && _camouflageIndex < m_PresetCatalog.CamouflageOptionCount)
		{
			_title = m_PresetCatalog.GetCamouflageLabel(_camouflageIndex);
			_description = ResolveCamouflageDescription(_camouflageIndex);
			return;
		}

		UnitCamouflagePattern pattern = UnitCamouflagePatternUtility.FromIndex(_camouflageIndex);
		_title = UnitCamouflagePatternUtility.GetLocalizedLabel(pattern);
		_description = UnitCamouflagePatternUtility.GetLocalizedDescription(pattern);
	}

	private static string ResolveCamouflageDescription(int _camouflageIndex)
	{
		return UnitCamouflagePatternUtility.GetLocalizedDescription(UnitCamouflagePatternUtility.FromIndex(_camouflageIndex));
	}

	private static void ResolveDropdownItemHoverTarget(
		Transform _itemTransform,
		out GameObject _hoverHost,
		out RectTransform _hoverRect)
	{
		_hoverHost = _itemTransform.gameObject;
		_hoverRect = _itemTransform as RectTransform;

		if (_itemTransform.TryGetComponent(out Toggle toggle) && toggle.targetGraphic != null)
		{
			_hoverHost = toggle.targetGraphic.gameObject;
			_hoverRect = toggle.targetGraphic.rectTransform;
			return;
		}

		Graphic graphic = _itemTransform.GetComponentInChildren<Graphic>(true);
		if (graphic != null && graphic.raycastTarget)
		{
			_hoverHost = graphic.gameObject;
			_hoverRect = graphic.rectTransform;
		}
	}
	#endregion
}
