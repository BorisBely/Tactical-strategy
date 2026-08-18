using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Заголовок секции в Content панели («Снаряжение» / «Сумка»).
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryPanelSectionHeader : MonoBehaviour
{
	#region Constants
	public const string EquipmentObjectName = "SectionEquipment";
	public const string BagObjectName = "SectionBag";
	public const string EquipmentLocalizationKey = "inventory.section.equipment";
	public const string BagLocalizationKey = "inventory.section.bag";
	#endregion

	#region Serialized Fields
	[SerializeField] private TMP_Text m_Label;
	[SerializeField] private string m_LocalizationKey;
	#endregion

	#region Public Properties
	public string LocalizationKey => m_LocalizationKey;
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		RefreshLabel();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}
	#endregion

	#region Public Methods
	public void Configure(string _localizationKey, string _fallback)
	{
		m_LocalizationKey = _localizationKey ?? string.Empty;
		EnsureChrome();
		EnsureLabel();
		RefreshLabel(_fallback);
	}

	public void SetRaycastTarget(bool _enabled)
	{
		Image bg = gameObject.GetComponent<Image>();
		if (bg == null)
			bg = gameObject.AddComponent<Image>();
		bg.raycastTarget = _enabled;
	}

	public static InventoryPanelSectionHeader Ensure(
		Transform _container,
		string _objectName,
		string _localizationKey,
		string _fallback)
	{
		if (_container == null)
			return null;

		Transform existing = _container.Find(_objectName);
		InventoryPanelSectionHeader header = existing != null
			? existing.GetComponent<InventoryPanelSectionHeader>()
			: null;

		if (header == null)
		{
			GameObject go = new GameObject(_objectName, typeof(RectTransform));
			go.transform.SetParent(_container, false);
			header = go.AddComponent<InventoryPanelSectionHeader>();
		}

		header.Configure(_localizationKey, _fallback);
		header.gameObject.SetActive(true);
		return header;
	}
	#endregion

	#region Private Methods
	private void EnsureChrome()
	{
		RectTransform rt = transform as RectTransform;
		// Top-left + явная ширина: stretch+sizeDelta(0) в VLG схлопывал ширину → вертикальный текст.
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = Vector2.zero;
		rt.sizeDelta = new Vector2(400f, InventoryUiTheme.SectionHeaderHeight);
		rt.localScale = Vector3.one;

		LayoutElement layout = gameObject.GetComponent<LayoutElement>();
		if (layout == null)
			layout = gameObject.AddComponent<LayoutElement>();
		layout.minWidth = 200f;
		layout.preferredWidth = 400f;
		layout.flexibleWidth = 1f;
		layout.minHeight = InventoryUiTheme.SectionHeaderHeight;
		layout.preferredHeight = InventoryUiTheme.SectionHeaderHeight;
		layout.flexibleHeight = 0f;

		Image bg = gameObject.GetComponent<Image>();
		if (bg == null)
			bg = gameObject.AddComponent<Image>();
		InventoryUiTheme.ApplyImageColor(bg, InventoryUiTheme.TitleBar);
		// Inventory section titles stay non-blocking; seat headers re-enable via SetRaycastTarget.
		bg.raycastTarget = false;
	}

	private void EnsureLabel()
	{
		if (m_Label == null)
		{
			Transform labelTransform = transform.Find("Label");
			if (labelTransform != null)
				m_Label = labelTransform.GetComponent<TMP_Text>();
		}

		if (m_Label == null)
		{
			GameObject labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(transform, false);
			m_Label = labelGo.AddComponent<TextMeshProUGUI>();
		}

		RectTransform labelRt = m_Label.rectTransform;
		labelRt.anchorMin = Vector2.zero;
		labelRt.anchorMax = Vector2.one;
		labelRt.offsetMin = new Vector2(10f, 0f);
		labelRt.offsetMax = new Vector2(-8f, 0f);
		labelRt.localScale = Vector3.one;

		m_Label.fontSize = 14f;
		m_Label.fontStyle = FontStyles.Bold;
		m_Label.color = InventoryUiTheme.SectionHeaderText;
		m_Label.alignment = TextAlignmentOptions.MidlineLeft;
		m_Label.textWrappingMode = TextWrappingModes.NoWrap;
		m_Label.overflowMode = TextOverflowModes.Ellipsis;
		m_Label.raycastTarget = false;
	}

	private void RefreshLabel(string _fallback = null)
	{
		if (m_Label == null)
			EnsureLabel();
		if (m_Label == null)
			return;

		string fallback = string.IsNullOrEmpty(_fallback) ? m_Label.text : _fallback;
		if (string.IsNullOrWhiteSpace(m_LocalizationKey))
		{
			m_Label.text = fallback;
			return;
		}

		m_Label.text = LocalizationManager.HasInstance
			? LocalizationManager.Get(m_LocalizationKey, fallback)
			: fallback;
	}

	private void HandleLanguageChanged()
	{
		RefreshLabel();
	}
	#endregion
}
