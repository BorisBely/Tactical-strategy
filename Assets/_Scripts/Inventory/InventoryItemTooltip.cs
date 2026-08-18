using System.Collections;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Общая подсказка с описанием предмета. Следует за курсором поверх Canvas инвентаря.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryItemTooltip : MonoBehaviour
{
	#region Constants
	private const int c_TooltipSortingOrder = 32000;
	private const float c_MaxWidth = 300f;
	private const float c_MinHeight = 56f;
	private const float c_Padding = 10f;
	private const float c_ScreenEdgePadding = 8f;
	private const float c_FadeDuration = 0.08f;
	private static readonly Vector2 s_CursorOffset = new Vector2(16f, -16f);
	private static readonly Color s_BackgroundColor = InventoryUiTheme.TooltipBackground;
	private static readonly Color s_TitleColor = Color.white;
	private static readonly Color s_DescriptionColor = new Color(0.86f, 0.86f, 0.86f, 1f);
	#endregion

	#region Static Access
	private static InventoryItemTooltip s_Instance;

	public static InventoryItemTooltip Instance => EnsureInstance();
	#endregion

	#region Private Fields
	private RectTransform m_Root;
	private CanvasGroup m_CanvasGroup;
	private Canvas m_OverrideCanvas;
	private TMP_Text m_TitleText;
	private TMP_Text m_DescriptionText;
	private Canvas m_RootCanvas;
	private RectTransform m_RootCanvasRect;
	private InventorySlotView m_ActiveSource;
	private bool m_IsVisible;
	private Vector2 m_LastScreenPosition;
	private Coroutine m_FadeCoroutine;
	#endregion

	#region Public Properties
	public bool IsVisibleForSource(InventorySlotView _source) => m_IsVisible && m_ActiveSource == _source;
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
		Log("Awake: tooltip service ready.");
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

		if (!IsAnyInventoryUiOpen())
		{
			Log("LateUpdate hide: inventory UI is closed.");
			HideImmediate();
			return;
		}

		if (m_ActiveSource == null || !m_ActiveSource.isActiveAndEnabled || !m_ActiveSource.HasItem)
		{
			Log("LateUpdate hide: active source slot is invalid.");
			HideImmediate();
			return;
		}

		UpdatePosition(m_LastScreenPosition);
	}
	#endregion

	#region Public Methods
	public void ShowForSlot(InventorySlotView _source, Vector2 _screenPosition)
	{
		if (_source == null)
		{
			Log("ShowForSlot skipped: source is null.");
			return;
		}

		if (!_source.HasItem)
		{
			Log($"ShowForSlot skipped: slot '{_source.name}' has no item.");
			HideIfSource(_source);
			return;
		}

		if (IsDragVisualSlot(_source))
		{
			Log($"ShowForSlot skipped: slot '{_source.name}' is drag visual.");
			HideIfSource(_source);
			return;
		}

		string description = ResolveDescription(_source.Data);
		if (string.IsNullOrWhiteSpace(description))
		{
			Log($"ShowForSlot skipped: empty description for '{ResolveTitle(_source.Data)}'.");
			HideIfSource(_source);
			return;
		}

		Canvas hostCanvas = _source.GetComponentInParent<Canvas>();
		if (hostCanvas == null)
		{
			Log($"ShowForSlot skipped: no parent Canvas for slot '{_source.name}'.");
			HideIfSource(_source);
			return;
		}

		EnsureUi();
		BindToRootCanvas(hostCanvas);
		TryApplyFontFromSlot(_source);

		m_ActiveSource = _source;
		m_LastScreenPosition = _screenPosition;
		m_TitleText.text = ResolveTitle(_source.Data);
		m_DescriptionText.text = description;
		m_IsVisible = true;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;
		gameObject.SetActive(true);

		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_Root);
		EnsureMinimumSize();
		UpdatePosition(_screenPosition);
		transform.SetAsLastSibling();
		StartFade(1f);
		LogTooltipState("ShowForSlot success");
	}

	public void UpdateScreenPosition(Vector2 _screenPosition)
	{
		if (!m_IsVisible)
			return;

		m_LastScreenPosition = _screenPosition;
		UpdatePosition(_screenPosition);
	}

	public void HideIfSource(InventorySlotView _source)
	{
		if (!m_IsVisible || m_ActiveSource != _source)
			return;

		Log($"HideIfSource: '{(_source != null ? _source.name : "null")}'.");
		HideImmediate();
	}

	public void HideImmediate()
	{
		m_IsVisible = false;
		m_ActiveSource = null;
		if (m_FadeCoroutine != null)
		{
			StopCoroutine(m_FadeCoroutine);
			m_FadeCoroutine = null;
		}

		if (m_CanvasGroup != null)
			m_CanvasGroup.alpha = 0f;
	}
	#endregion

	#region Fade
	private void StartFade(float _targetAlpha)
	{
		if (m_CanvasGroup == null)
			return;

		if (m_FadeCoroutine != null)
			StopCoroutine(m_FadeCoroutine);

		m_FadeCoroutine = StartCoroutine(CoFade(_targetAlpha));
	}

	private IEnumerator CoFade(float _targetAlpha)
	{
		float start = m_CanvasGroup.alpha;
		float t = 0f;
		while (t < c_FadeDuration)
		{
			t += Time.unscaledDeltaTime;
			m_CanvasGroup.alpha = Mathf.Lerp(start, _targetAlpha, Mathf.Clamp01(t / c_FadeDuration));
			yield return null;
		}

		m_CanvasGroup.alpha = _targetAlpha;
		m_FadeCoroutine = null;
	}
	#endregion

	#region Private Methods
	private static InventoryItemTooltip EnsureInstance()
	{
		if (s_Instance != null)
			return s_Instance;

		var rootObject = new GameObject(nameof(InventoryItemTooltip));
		s_Instance = rootObject.AddComponent<InventoryItemTooltip>();
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
		{
			Log("TryApplyDefaultTmpFont: TMP default font is not assigned.");
			return;
		}

		if (m_TitleText != null)
			m_TitleText.font = font;
		if (m_DescriptionText != null)
			m_DescriptionText.font = font;
	}

	private void TryApplyFontFromSlot(InventorySlotView _source)
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
		{
			Log("UpdatePosition failed: ScreenPointToLocalPointInRectangle returned false.");
			return;
		}

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

	[Conditional("ITEM_TOOLTIP_DEBUG")]
	private void LogTooltipState(string _prefix)
	{
		if (m_Root == null)
			return;

		Vector3[] corners = new Vector3[4];
		m_Root.GetWorldCorners(corners);
		Log(
			$"{_prefix}: title='{m_TitleText.text}', canvas='{(m_RootCanvas != null ? m_RootCanvas.name : "null")}', " +
			$"sorting={c_TooltipSortingOrder}, alpha={m_CanvasGroup.alpha:0.00}, size={m_Root.rect.size}, " +
			$"screenPos={m_Root.position}, corners=({corners[0]} .. {corners[2]})");
	}

	private void HandleLanguageChanged()
	{
		if (!m_IsVisible || m_ActiveSource == null)
			return;

		ShowForSlot(m_ActiveSource, m_LastScreenPosition);
	}

	private static bool IsAnyInventoryUiOpen()
	{
		bool inventoryOpen = InventoryScreenBindings.Instance != null && InventoryScreenBindings.Instance.IsInventoryOpen;
		bool missionPrepOpen = MissionPrepScreenBindings.Instance != null && MissionPrepScreenBindings.Instance.IsMissionPrepOpen;
		return inventoryOpen || missionPrepOpen;
	}

	private static bool IsDragVisualSlot(InventorySlotView _slot)
	{
		return _slot != null && _slot.gameObject.name.Contains("_DragVisual");
	}

	private static string ResolveTitle(InventorySlotRuntimeData _data)
	{
		if (_data.Definition != null)
			return _data.Definition.GetLocalizedDisplayName();

		if (!string.IsNullOrWhiteSpace(_data.LocalizationKey))
			return LocalizationManager.Get(_data.LocalizationKey, _data.DisplayName);

		return _data.DisplayName;
	}

	private static string ResolveDescription(InventorySlotRuntimeData _data)
	{
		if (_data.Definition != null)
			return _data.Definition.GetLocalizedDescription();

		return string.Empty;
	}

	[Conditional("ITEM_TOOLTIP_DEBUG")]
	private static void Log(string _message)
	{
		UnityEngine.Debug.Log($"[ItemTooltip] {_message}");
	}
	#endregion
}
