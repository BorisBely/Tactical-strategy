using System.Diagnostics;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HealthStatusTooltip : MonoBehaviour
{
	#region Constants
	private const int c_TooltipSortingOrder = 32000;
	private const float c_MaxWidth = 300f;
	private const float c_MinHeight = 56f;
	private const float c_Padding = 10f;
	private const float c_ScreenEdgePadding = 8f;
	private static readonly Vector2 s_CursorOffset = new Vector2(16f, -16f);
	private static readonly Color s_BackgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.96f);
	private static readonly Color s_TitleColor = Color.white;
	private static readonly Color s_DescriptionColor = new Color(0.86f, 0.86f, 0.86f, 1f);
	private static readonly Color s_DebuffsColor = new Color(0.78f, 0.82f, 0.95f, 1f);
	#endregion

	#region Static Access
	private static HealthStatusTooltip s_Instance;

	public static HealthStatusTooltip Instance => EnsureInstance();
	#endregion

	#region Private Fields
	private RectTransform m_Root;
	private CanvasGroup m_CanvasGroup;
	private Canvas m_OverrideCanvas;
	private TMP_Text m_TitleText;
	private TMP_Text m_DescriptionText;
	private TMP_Text m_DebuffsText;
	private Canvas m_RootCanvas;
	private RectTransform m_RootCanvasRect;
	private HealthStatusSlotView m_ActiveSource;
	private bool m_IsVisible;
	private Vector2 m_LastScreenPosition;
	#endregion

	#region Public Properties
	public bool IsVisibleForSource(HealthStatusSlotView _source) => m_IsVisible && m_ActiveSource == _source;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (s_Instance != null && s_Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		s_Instance = this;
		EnsureUi();
		HideImmediate();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		HideImmediate();
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}

	private void LateUpdate()
	{
		if (!m_IsVisible)
			return;

		if (!IsInventoryUiOpen())
		{
			HideImmediate();
			return;
		}

		if (m_ActiveSource == null || !m_ActiveSource.isActiveAndEnabled || !m_ActiveSource.HasEntry)
		{
			HideImmediate();
			return;
		}

		UpdatePosition(m_LastScreenPosition);
	}
	#endregion

	#region Public Methods
	public void ShowForSlot(HealthStatusSlotView _source, Vector2 _screenPosition)
	{
		if (_source == null || !_source.HasEntry || !_source.HasTooltipContent)
		{
			HideIfSource(_source);
			return;
		}

		HealthStatusEntryData data = _source.EntryData;
		string description = data.GetLocalizedDescriptionText();
		string debuffsText = data.GetLocalizedDebuffsText();
		if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(debuffsText))
		{
			HideIfSource(_source);
			return;
		}

		Canvas hostCanvas = _source.GetComponentInParent<Canvas>();
		if (hostCanvas == null)
		{
			HideIfSource(_source);
			return;
		}

		EnsureUi();
		BindToRootCanvas(hostCanvas);
		TryApplyFontFromSlot(_source);

		m_ActiveSource = _source;
		m_LastScreenPosition = _screenPosition;
		m_TitleText.text = data.GetLocalizedStatusText();
		m_DescriptionText.text = description ?? string.Empty;
		m_DescriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(description));
		m_DebuffsText.text = debuffsText ?? string.Empty;
		m_DebuffsText.gameObject.SetActive(!string.IsNullOrWhiteSpace(debuffsText));
		m_IsVisible = true;
		m_CanvasGroup.alpha = 1f;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;
		gameObject.SetActive(true);

		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_Root);
		EnsureMinimumSize();
		UpdatePosition(_screenPosition);
		transform.SetAsLastSibling();
	}

	public void UpdateScreenPosition(Vector2 _screenPosition)
	{
		if (!m_IsVisible)
			return;

		m_LastScreenPosition = _screenPosition;
		UpdatePosition(_screenPosition);
	}

	public void HideIfSource(HealthStatusSlotView _source)
	{
		if (!m_IsVisible || m_ActiveSource != _source)
			return;

		HideImmediate();
	}

	public void HideImmediate()
	{
		m_IsVisible = false;
		m_ActiveSource = null;
		if (m_CanvasGroup != null)
			m_CanvasGroup.alpha = 0f;
	}
	#endregion

	#region Private Methods
	private static HealthStatusTooltip EnsureInstance()
	{
		if (s_Instance != null)
			return s_Instance;

		var rootObject = new GameObject(nameof(HealthStatusTooltip));
		s_Instance = rootObject.AddComponent<HealthStatusTooltip>();
		return s_Instance;
	}

	private void EnsureUi()
	{
		if (m_Root != null)
			return;

		m_Root = gameObject.AddComponent<RectTransform>();
		m_Root.anchorMin = Vector2.zero;
		m_Root.anchorMax = Vector2.zero;
		m_Root.pivot = new Vector2(0f, 1f);
		m_Root.sizeDelta = new Vector2(c_MaxWidth, 80f);

		Image background = gameObject.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(background);
		background.color = s_BackgroundColor;
		background.raycastTarget = false;

		m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
		m_CanvasGroup.alpha = 0f;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;

		VerticalLayoutGroup layout = gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(
			Mathf.RoundToInt(c_Padding),
			Mathf.RoundToInt(c_Padding),
			Mathf.RoundToInt(c_Padding),
			Mathf.RoundToInt(c_Padding));
		layout.spacing = 4f;
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		ContentSizeFitter fitter = gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		LayoutElement rootLayout = gameObject.AddComponent<LayoutElement>();
		rootLayout.preferredWidth = c_MaxWidth;
		rootLayout.minWidth = 160f;
		rootLayout.minHeight = c_MinHeight;

		m_TitleText = CreateTextChild("Title", 15f, FontStyles.Bold, s_TitleColor);
		m_DescriptionText = CreateTextChild("Description", 13f, FontStyles.Normal, s_DescriptionColor);
		m_DebuffsText = CreateTextChild("Debuffs", 13f, FontStyles.Normal, s_DebuffsColor);
		TryApplyDefaultTmpFont();
	}

	private TMP_Text CreateTextChild(string _name, float _fontSize, FontStyles _fontStyle, Color _color)
	{
		var textObject = new GameObject(_name, typeof(RectTransform));
		textObject.transform.SetParent(transform, false);

		TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
		text.fontSize = _fontSize;
		text.fontStyle = _fontStyle;
		text.color = _color;
		text.alignment = TextAlignmentOptions.TopLeft;
		text.textWrappingMode = TextWrappingModes.Normal;
		text.overflowMode = TextOverflowModes.Overflow;
		text.raycastTarget = false;

		LayoutElement layout = textObject.AddComponent<LayoutElement>();
		layout.preferredWidth = c_MaxWidth - c_Padding * 2f;
		layout.flexibleWidth = 0f;

		return text;
	}

	private void TryApplyDefaultTmpFont()
	{
		TMP_FontAsset font = TMP_Settings.defaultFontAsset;
		if (font == null)
			return;

		if (m_TitleText != null)
			m_TitleText.font = font;
		if (m_DescriptionText != null)
			m_DescriptionText.font = font;
		if (m_DebuffsText != null)
			m_DebuffsText.font = font;
	}

	private void TryApplyFontFromSlot(HealthStatusSlotView _source)
	{
		if (_source == null)
			return;

		TMP_Text slotText = _source.GetComponentInChildren<TMP_Text>(true);
		if (slotText == null || slotText.font == null)
		{
			TryApplyDefaultTmpFont();
			return;
		}

		if (m_TitleText != null)
			m_TitleText.font = slotText.font;
		if (m_DescriptionText != null)
			m_DescriptionText.font = slotText.font;
		if (m_DebuffsText != null)
			m_DebuffsText.font = slotText.font;
	}

	private void BindToRootCanvas(Canvas _hostCanvas)
	{
		if (_hostCanvas == null)
			return;

		m_RootCanvas = _hostCanvas.rootCanvas != null ? _hostCanvas.rootCanvas : _hostCanvas;
		m_RootCanvasRect = m_RootCanvas.transform as RectTransform;
		transform.SetParent(m_RootCanvas.transform, false);
		transform.localScale = Vector3.one;
		transform.localRotation = Quaternion.identity;

		if (!TryGetComponent(out m_OverrideCanvas))
			m_OverrideCanvas = gameObject.AddComponent<Canvas>();

		m_OverrideCanvas.overrideSorting = true;
		m_OverrideCanvas.sortingOrder = c_TooltipSortingOrder;

		if (TryGetComponent(out GraphicRaycaster raycaster))
			Destroy(raycaster);
	}

	private void EnsureMinimumSize()
	{
		Vector2 size = m_Root.rect.size;
		if (size.x >= 120f && size.y >= c_MinHeight)
			return;

		m_Root.sizeDelta = new Vector2(Mathf.Max(size.x, 220f), Mathf.Max(size.y, c_MinHeight));
	}

	private void UpdatePosition(Vector2 _screenPosition)
	{
		if (m_Root == null || m_RootCanvasRect == null)
			return;

		if (m_RootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
		{
			m_Root.position = new Vector3(
				_screenPosition.x + s_CursorOffset.x,
				_screenPosition.y + s_CursorOffset.y,
				0f);
			ClampToScreenOverlay();
			return;
		}

		Camera eventCamera = m_RootCanvas.worldCamera;
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    m_RootCanvasRect,
			    _screenPosition,
			    eventCamera,
			    out Vector2 localPoint))
			return;

		m_Root.anchoredPosition = localPoint + s_CursorOffset;
		ClampToScreenCamera(eventCamera);
	}

	private void ClampToScreenOverlay()
	{
		Vector3[] corners = new Vector3[4];
		m_Root.GetWorldCorners(corners);

		Vector2 screenMin = corners[0];
		Vector2 screenMax = corners[0];
		for (int i = 1; i < 4; i++)
		{
			screenMin = Vector2.Min(screenMin, corners[i]);
			screenMax = Vector2.Max(screenMax, corners[i]);
		}

		Vector3 position = m_Root.position;
		if (screenMin.x < c_ScreenEdgePadding)
			position.x += c_ScreenEdgePadding - screenMin.x;
		if (screenMax.x > Screen.width - c_ScreenEdgePadding)
			position.x -= screenMax.x - (Screen.width - c_ScreenEdgePadding);
		if (screenMin.y < c_ScreenEdgePadding)
			position.y += c_ScreenEdgePadding - screenMin.y;
		if (screenMax.y > Screen.height - c_ScreenEdgePadding)
			position.y -= screenMax.y - (Screen.height - c_ScreenEdgePadding);

		m_Root.position = position;
	}

	private void ClampToScreenCamera(Camera _eventCamera)
	{
		Vector3[] corners = new Vector3[4];
		m_Root.GetWorldCorners(corners);

		Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(_eventCamera, corners[0]);
		Vector2 screenMax = screenMin;
		for (int i = 1; i < 4; i++)
		{
			Vector2 corner = RectTransformUtility.WorldToScreenPoint(_eventCamera, corners[i]);
			screenMin = Vector2.Min(screenMin, corner);
			screenMax = Vector2.Max(screenMax, corner);
		}

		Vector2 shift = Vector2.zero;
		if (screenMin.x < c_ScreenEdgePadding)
			shift.x += c_ScreenEdgePadding - screenMin.x;
		if (screenMax.x > Screen.width - c_ScreenEdgePadding)
			shift.x -= screenMax.x - (Screen.width - c_ScreenEdgePadding);
		if (screenMin.y < c_ScreenEdgePadding)
			shift.y += c_ScreenEdgePadding - screenMin.y;
		if (screenMax.y > Screen.height - c_ScreenEdgePadding)
			shift.y -= screenMax.y - (Screen.height - c_ScreenEdgePadding);

		if (shift == Vector2.zero)
			return;

		Vector2 shiftedScreen = RectTransformUtility.WorldToScreenPoint(_eventCamera, m_Root.position) + shift;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    m_RootCanvasRect,
			    shiftedScreen,
			    _eventCamera,
			    out Vector2 shiftedLocal))
		{
			m_Root.anchoredPosition = shiftedLocal;
		}
	}

	private void HandleLanguageChanged()
	{
		if (!m_IsVisible || m_ActiveSource == null)
			return;

		ShowForSlot(m_ActiveSource, m_LastScreenPosition);
	}

	private static bool IsInventoryUiOpen()
	{
		return InventoryScreenBindings.Instance != null && InventoryScreenBindings.Instance.IsInventoryOpen;
	}

	[Conditional("HEALTH_TOOLTIP_DEBUG")]
	private static void Log(string _message)
	{
		UnityEngine.Debug.Log($"[HealthTooltip] {_message}");
	}
	#endregion
}
