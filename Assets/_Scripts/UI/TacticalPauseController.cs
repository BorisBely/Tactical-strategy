using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Тактическая пауза на Space: замораживает симуляцию, но не блокирует RTS-команды и камеру.
/// </summary>
[DefaultExecutionOrder(-880)]
[DisallowMultipleComponent]
public sealed class TacticalPauseController : MonoBehaviour
{
	#region Private Fields
	private static TacticalPauseController s_Instance;

	[SerializeField] private Canvas m_Canvas;
	[SerializeField] private GameObject m_IndicatorRoot;
	[SerializeField] private TextMeshProUGUI m_TitleText;
	[SerializeField] private TextMeshProUGUI m_HintText;
	#endregion

	#region Public Properties
	public static bool IsTacticalPaused => GamePauseState.IsTacticalPaused;
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (s_Instance != null)
			return;

		GameObject root = new GameObject(nameof(TacticalPauseController));
		s_Instance = root.AddComponent<TacticalPauseController>();
	}
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
		DontDestroyOnLoad(gameObject);

		BuildUiIfNeeded();
		SetTacticalPaused(false);
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += RefreshTexts;
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= RefreshTexts;
	}

	private void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame)
			return;

		if (PauseMenuController.IsPaused)
			return;

		SetTacticalPaused(!IsTacticalPaused);
	}

	private void OnDestroy()
	{
		LocalizationManager.LanguageChanged -= RefreshTexts;
		if (s_Instance == this)
			s_Instance = null;

		if (IsTacticalPaused)
			GamePauseState.SetTacticalPaused(false);
	}
	#endregion

	#region Private Methods
	private void SetTacticalPaused(bool _paused)
	{
		GamePauseState.SetTacticalPaused(_paused);
		RefreshIndicatorVisibility();
		if (_paused)
			RefreshTexts();
	}

	private void RefreshIndicatorVisibility()
	{
		if (m_IndicatorRoot == null)
			return;

		m_IndicatorRoot.SetActive(IsTacticalPaused && !PauseMenuController.IsPaused);
	}

	private void BuildUiIfNeeded()
	{
		if (m_Canvas != null && m_IndicatorRoot != null && m_TitleText != null && m_HintText != null)
			return;

		m_Canvas = gameObject.GetComponent<Canvas>();
		if (m_Canvas == null)
			m_Canvas = gameObject.AddComponent<Canvas>();

		m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		m_Canvas.sortingOrder = 950;

		CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
		if (scaler == null)
			scaler = gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		m_IndicatorRoot = CreateRectObject("TacticalPauseIndicator", transform);
		RectTransform rootRect = m_IndicatorRoot.transform as RectTransform;
		rootRect.anchorMin = new Vector2(0.5f, 1f);
		rootRect.anchorMax = new Vector2(0.5f, 1f);
		rootRect.pivot = new Vector2(0.5f, 1f);
		rootRect.anchoredPosition = new Vector2(0f, -24f);
		rootRect.sizeDelta = new Vector2(520f, 72f);

		Image background = m_IndicatorRoot.AddComponent<Image>();
		background.color = new Color(0.08f, 0.12f, 0.18f, 0.82f);
		background.raycastTarget = false;

		VerticalLayoutGroup layout = m_IndicatorRoot.AddComponent<VerticalLayoutGroup>();
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlHeight = true;
		layout.childControlWidth = true;
		layout.childForceExpandHeight = false;
		layout.childForceExpandWidth = true;
		layout.spacing = 4f;
		layout.padding = new RectOffset(16, 16, 10, 10);

		m_TitleText = CreateText("Title", m_IndicatorRoot.transform, 28f, FontStyles.Bold, TextAlignmentOptions.Center);
		m_HintText = CreateText("Hint", m_IndicatorRoot.transform, 18f, FontStyles.Normal, TextAlignmentOptions.Center);

		LayoutElement titleLayout = m_TitleText.gameObject.AddComponent<LayoutElement>();
		titleLayout.preferredHeight = 34f;
		LayoutElement hintLayout = m_HintText.gameObject.AddComponent<LayoutElement>();
		hintLayout.preferredHeight = 24f;

		RefreshTexts();
		m_IndicatorRoot.SetActive(false);
	}

	private void RefreshTexts()
	{
		if (m_TitleText == null || m_HintText == null)
			return;

		m_TitleText.text = LocalizationManager.Get("tactical_pause.title", "ТАКТИЧЕСКАЯ ПАУЗА");
		m_HintText.text = LocalizationManager.Get("tactical_pause.hint", "Space — продолжить");
	}

	private static GameObject CreateRectObject(string _name, Transform _parent)
	{
		GameObject go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		return go;
	}

	private static TextMeshProUGUI CreateText(string _name, Transform _parent, float _fontSize, FontStyles _fontStyle,
		TextAlignmentOptions _alignment)
	{
		GameObject go = CreateRectObject(_name, _parent);
		TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
		text.fontSize = _fontSize;
		text.fontStyle = _fontStyle;
		text.alignment = _alignment;
		text.color = new Color(0.95f, 0.88f, 0.45f, 1f);
		text.raycastTarget = false;

		RectTransform rect = text.rectTransform;
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		return text;
	}
	#endregion

	#region Internal Methods
	internal static void NotifyMenuPauseChanged(bool _menuPaused)
	{
		if (s_Instance == null)
			return;

		s_Instance.RefreshIndicatorVisibility();
	}
	#endregion
}
