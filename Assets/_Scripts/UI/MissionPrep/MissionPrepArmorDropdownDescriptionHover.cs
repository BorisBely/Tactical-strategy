using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Подсказки для дропдауна брони в окне подготовки пресета: кнопка выбора и пункты списка.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepArmorDropdownDescriptionHover : MonoBehaviour
{
	#region Constants
	private const string c_DropdownListChildName = "Dropdown List";
	private const string c_DropdownContentPath = "Viewport/Content";
	#endregion

	#region Serialized Fields
	[SerializeField] private TMP_Dropdown m_ArmorDropdown;
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	#endregion

	#region Private Fields
	private UiDescriptionHover m_CaptionHover;
	private readonly List<UiDescriptionHover> m_ItemHovers = new List<UiDescriptionHover>();
	private int m_LastCaptionArmorIndex = -1;
	private int m_LastItemHoverCount = -1;
	#endregion

	#region Public Methods
	public void Bind(TMP_Dropdown _armorDropdown, MissionPrepEquipmentPresetCatalog _presetCatalog)
	{
		m_ArmorDropdown = _armorDropdown;
		m_PresetCatalog = _presetCatalog;
		RefreshAll();
	}
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		EnsureArmorDropdownReference();
		RefreshAll();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}

	private void LateUpdate()
	{
		EnsureArmorDropdownReference();
		RefreshCaptionHover();
		RefreshItemHovers();
	}
	#endregion

	#region Private Methods
	private void HandleLanguageChanged()
	{
		m_LastCaptionArmorIndex = -1;
		m_LastItemHoverCount = -1;
		RefreshAll();
	}

	private void RefreshAll()
	{
		RefreshCaptionHover();
		RefreshItemHovers();
	}

	private void EnsureArmorDropdownReference()
	{
		if (m_ArmorDropdown != null)
			return;

		m_ArmorDropdown = GetComponent<TMP_Dropdown>();
	}

	private void RefreshCaptionHover()
	{
		if (m_ArmorDropdown == null)
			return;

		int armorIndex = Mathf.Clamp(m_ArmorDropdown.value, 0, ResolveArmorOptionCount() - 1);
		if (m_CaptionHover == null)
		{
			m_CaptionHover = m_ArmorDropdown.gameObject.GetComponent<UiDescriptionHover>();
			if (m_CaptionHover == null)
				m_CaptionHover = m_ArmorDropdown.gameObject.AddComponent<UiDescriptionHover>();
		}

		if (m_LastCaptionArmorIndex == armorIndex && m_CaptionHover != null)
			return;

		m_LastCaptionArmorIndex = armorIndex;
		ResolveArmorTooltip(armorIndex, out string title, out string description);
		m_CaptionHover.Configure(title, description, m_ArmorDropdown.transform as RectTransform);
	}

	private void RefreshItemHovers()
	{
		if (m_ArmorDropdown == null)
			return;

		Transform dropdownList = m_ArmorDropdown.transform.Find(c_DropdownListChildName);
		if (dropdownList == null || !dropdownList.gameObject.activeInHierarchy)
			return;

		Transform content = dropdownList.Find(c_DropdownContentPath);
		if (content == null)
			return;

		int optionCount = ResolveArmorOptionCount();
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

			int armorIndex = Mathf.Clamp(i, 0, optionCount - 1);
			ResolveArmorTooltip(armorIndex, out string title, out string description);
			hover.Configure(title, description, hoverRect);
			m_ItemHovers.Add(hover);
		}
	}

	private int ResolveArmorOptionCount()
	{
		if (m_PresetCatalog != null && m_PresetCatalog.ArmorOptionCount > 0)
			return m_PresetCatalog.ArmorOptionCount;

		return MissionPrepUnitArmorVisualController.ArmorVariantCount;
	}

	private void ResolveArmorTooltip(int _armorIndex, out string _title, out string _description)
	{
		if (m_PresetCatalog != null && _armorIndex >= 0 && _armorIndex < m_PresetCatalog.ArmorOptionCount)
		{
			_title = m_PresetCatalog.GetArmorLabel(_armorIndex);
			_description = ResolveArmorDescription(_armorIndex);
			return;
		}

		if (_armorIndex == MissionPrepUnitArmorVisualController.HeavyArmorIndex)
		{
			_title = LocalizationManager.Get("mission_prep.equipment.armor.heavy", "Тяжёлая броня");
			_description = LocalizationManager.Get(
				"mission_prep.equipment.armor.heavy.description",
				"Защищает от пуль и имеет дополнительную защиту от осколков.");
			return;
		}

		_title = LocalizationManager.Get("mission_prep.equipment.armor.light", "Лёгкая броня");
		_description = LocalizationManager.Get(
			"mission_prep.equipment.armor.light.description",
			"Защищает от пуль.");
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

	private static string ResolveArmorDescription(int _armorIndex)
	{
		if (_armorIndex == MissionPrepUnitArmorVisualController.HeavyArmorIndex)
		{
			return LocalizationManager.Get(
				"mission_prep.equipment.armor.heavy.description",
				"Защищает от пуль и имеет дополнительную защиту от осколков.");
		}

		return LocalizationManager.Get(
			"mission_prep.equipment.armor.light.description",
			"Защищает от пуль.");
	}
	#endregion
}
