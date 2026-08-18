using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Сворачиваемая колонка Mission Prep. Разметка задаётся в сцене; в runtime только toggle ширины/контента.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class MissionPrepCollapsibleColumn : MonoBehaviour
{
	#region Constants
	public const float DefaultExpandedWidth = 400f;
	public const float DefaultCollapsedWidth = 40f;
	public const float DefaultColumnHeight = 1320f;
	public const float DefaultStatsColumnHeight = 300f;
	public const float ToggleBarHeight = 36f;
	#endregion

	#region Serialized Fields
	[SerializeField] private RectTransform m_ContentRoot;
	[SerializeField] private Button m_ToggleButton;
	[SerializeField] private TMP_Text m_ToggleLabel;
	[SerializeField] private string m_LocalizationKey;
	[SerializeField] private string m_FallbackLabel = "Column";
	[SerializeField] private float m_ExpandedWidth = DefaultExpandedWidth;
	[SerializeField] private float m_CollapsedWidth = DefaultCollapsedWidth;
	[SerializeField] private float m_ExpandedHeight = DefaultColumnHeight;
	[SerializeField] private bool m_StartExpanded = true;
	#endregion

	#region Private Fields
	private LayoutElement m_LayoutElement;
	private bool m_Expanded = true;
	private bool m_Wired;
	#endregion

	#region Public Properties
	public bool IsExpanded => m_Expanded;
	public string LocalizationKey => m_LocalizationKey;
	public RectTransform ContentRoot => m_ContentRoot;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureLayoutElement();
		WireToggle();
		SetExpanded(m_StartExpanded, _force: true);
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		RefreshToggleLabel();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}
	#endregion

	#region Public Methods
	public void Configure(
		RectTransform _contentRoot,
		Button _toggleButton,
		TMP_Text _toggleLabel,
		string _localizationKey,
		string _fallbackLabel,
		float _expandedWidth = DefaultExpandedWidth,
		float _collapsedWidth = DefaultCollapsedWidth,
		float _expandedHeight = DefaultColumnHeight,
		bool _startExpanded = true)
	{
		m_ContentRoot = _contentRoot;
		m_ToggleButton = _toggleButton;
		m_ToggleLabel = _toggleLabel;
		m_LocalizationKey = _localizationKey ?? string.Empty;
		m_FallbackLabel = _fallbackLabel ?? "Column";
		m_ExpandedWidth = _expandedWidth;
		m_CollapsedWidth = _collapsedWidth;
		m_ExpandedHeight = _expandedHeight;
		m_StartExpanded = _startExpanded;
		m_Wired = false;
		EnsureLayoutElement();
		WireToggle();
		SetExpanded(m_StartExpanded, _force: true);
	}

	public void SetExpanded(bool _expanded)
	{
		SetExpanded(_expanded, _force: false);
	}

	public void Toggle()
	{
		SetExpanded(!m_Expanded);
	}
	#endregion

	#region Private Methods
	private void SetExpanded(bool _expanded, bool _force)
	{
		if (!_force && m_Expanded == _expanded)
			return;

		m_Expanded = _expanded;
		EnsureLayoutElement();

		float width = m_Expanded ? m_ExpandedWidth : m_CollapsedWidth;
		float height = Mathf.Max(ToggleBarHeight, m_ExpandedHeight);
		m_LayoutElement.minWidth = width;
		m_LayoutElement.preferredWidth = width;
		m_LayoutElement.flexibleWidth = 0f;
		m_LayoutElement.minHeight = height;
		m_LayoutElement.preferredHeight = height;
		m_LayoutElement.flexibleHeight = 0f;

		RectTransform rt = transform as RectTransform;
		if (rt != null)
			rt.sizeDelta = new Vector2(width, height);

		if (m_ContentRoot != null)
			m_ContentRoot.gameObject.SetActive(m_Expanded);

		ApplyToggleChrome();
		RefreshToggleLabel();
	}

	private void ApplyToggleChrome()
	{
		if (m_ToggleButton == null)
			return;

		RectTransform toggleRt = m_ToggleButton.transform as RectTransform;
		if (toggleRt == null)
			return;

		Image toggleImage = m_ToggleButton.targetGraphic as Image;
		if (toggleImage == null)
			toggleImage = m_ToggleButton.GetComponent<Image>();
		if (toggleImage != null)
			InventoryUiTheme.ApplyImageColor(toggleImage, InventoryUiTheme.TitleBar);

		if (m_Expanded)
		{
			toggleRt.anchorMin = new Vector2(0f, 1f);
			toggleRt.anchorMax = new Vector2(1f, 1f);
			toggleRt.pivot = new Vector2(0.5f, 1f);
			toggleRt.anchoredPosition = Vector2.zero;
			toggleRt.sizeDelta = new Vector2(0f, ToggleBarHeight);
		}
		else
		{
			// Full-height hit area, but keep chrome as a top strip feel via label placement.
			toggleRt.anchorMin = Vector2.zero;
			toggleRt.anchorMax = Vector2.one;
			toggleRt.offsetMin = Vector2.zero;
			toggleRt.offsetMax = Vector2.zero;
			toggleRt.pivot = new Vector2(0.5f, 0.5f);
		}

		if (m_ToggleLabel == null)
			return;

		RectTransform labelRt = m_ToggleLabel.rectTransform;
		m_ToggleLabel.textWrappingMode = TextWrappingModes.Normal;
		m_ToggleLabel.overflowMode = TextOverflowModes.Ellipsis;

		if (m_Expanded)
		{
			labelRt.anchorMin = Vector2.zero;
			labelRt.anchorMax = Vector2.one;
			labelRt.offsetMin = new Vector2(6f, 2f);
			labelRt.offsetMax = new Vector2(-6f, -2f);
			labelRt.pivot = new Vector2(0.5f, 0.5f);
			m_ToggleLabel.alignment = TextAlignmentOptions.MidlineLeft;
		}
		else
		{
			// Title pinned to the top of the collapsed strip.
			labelRt.anchorMin = new Vector2(0f, 1f);
			labelRt.anchorMax = new Vector2(1f, 1f);
			labelRt.pivot = new Vector2(0.5f, 1f);
			labelRt.anchoredPosition = Vector2.zero;
			labelRt.sizeDelta = new Vector2(-8f, ToggleBarHeight);
			m_ToggleLabel.alignment = TextAlignmentOptions.MidlineLeft;
		}
	}

	private void EnsureLayoutElement()
	{
		if (m_LayoutElement == null)
			m_LayoutElement = GetComponent<LayoutElement>();
		if (m_LayoutElement == null)
			m_LayoutElement = gameObject.AddComponent<LayoutElement>();
	}

	private void WireToggle()
	{
		if (m_Wired || m_ToggleButton == null)
			return;

		m_ToggleButton.onClick.RemoveListener(Toggle);
		m_ToggleButton.onClick.AddListener(Toggle);
		m_Wired = true;
	}

	private void RefreshToggleLabel()
	{
		if (m_ToggleLabel == null)
			return;

		string name = string.IsNullOrWhiteSpace(m_LocalizationKey)
			? m_FallbackLabel
			: LocalizationManager.Get(m_LocalizationKey, m_FallbackLabel);

		// ASCII only — LiberationSans SDF has no ◀ (U+25C0).
		m_ToggleLabel.text = m_Expanded
			? $"< {name}"
			: name;
	}

	private void HandleLanguageChanged()
	{
		RefreshToggleLabel();
	}
	#endregion
}
