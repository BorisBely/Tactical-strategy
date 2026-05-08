using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class PauseMenuController : MonoBehaviour
{
	#region Constants
	private static readonly CommandHint[] s_CommandHints =
	{
		new CommandHint("pause.help.lmb"),
		new CommandHint("pause.help.ctrl_lmb"),
		new CommandHint("pause.help.rmb"),
		new CommandHint("pause.help.shift_rmb"),
		new CommandHint("pause.help.double_rmb"),
		new CommandHint("pause.help.e"),
		new CommandHint("pause.help.z"),
		new CommandHint("pause.help.c"),
		new CommandHint("pause.help.f"),
		new CommandHint("pause.help.r"),
		new CommandHint("pause.help.t"),
		new CommandHint("pause.help.v"),
		new CommandHint("pause.help.i"),
		new CommandHint("pause.help.h"),
		new CommandHint("pause.help.ctrl_click_inventory"),
		new CommandHint("pause.help.l"),
		new CommandHint("pause.help.esc"),
	};
	#endregion

	#region Private Fields
	private static PauseMenuController s_Instance;

	[SerializeField] private Canvas m_Canvas;
	[SerializeField] private GameObject m_RootPanel;
	[SerializeField] private TextMeshProUGUI m_TitleText;
	[SerializeField] private TextMeshProUGUI m_LeftColumnText;
	[SerializeField] private TextMeshProUGUI m_RightColumnText;
	#endregion

	#region Public Properties
	public static bool IsPaused { get; private set; }
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (s_Instance != null)
			return;

		GameObject root = new GameObject(nameof(PauseMenuController));
		s_Instance = root.AddComponent<PauseMenuController>();
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
		SetPaused(false, false);
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
		if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
			return;

		SetPaused(!IsPaused, true);
	}

	private void OnDestroy()
	{
		LocalizationManager.LanguageChanged -= RefreshTexts;
		if (s_Instance == this)
			s_Instance = null;

		if (IsPaused)
			Time.timeScale = 1f;

		IsPaused = false;
	}
	#endregion

	#region Private Methods
	private void SetPaused(bool _paused, bool _refreshTexts)
	{
		IsPaused = _paused;
		Time.timeScale = _paused ? 0f : 1f;

		if (m_RootPanel != null)
			m_RootPanel.SetActive(_paused);

		if (_paused && _refreshTexts)
			RefreshTexts();
	}

	private void BuildUiIfNeeded()
	{
		if (m_Canvas != null && m_RootPanel != null && m_TitleText != null && m_LeftColumnText != null && m_RightColumnText != null)
			return;

		m_Canvas = gameObject.GetComponent<Canvas>();
		if (m_Canvas == null)
			m_Canvas = gameObject.AddComponent<Canvas>();

		m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		m_Canvas.sortingOrder = 1000;

		CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
		if (scaler == null)
			scaler = gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		if (gameObject.GetComponent<GraphicRaycaster>() == null)
			gameObject.AddComponent<GraphicRaycaster>();

		m_RootPanel = CreatePanel("PausePanel", transform);
		RectTransform panelRect = m_RootPanel.transform as RectTransform;
		panelRect.anchorMin = Vector2.zero;
		panelRect.anchorMax = Vector2.one;
		panelRect.offsetMin = Vector2.zero;
		panelRect.offsetMax = Vector2.zero;

		Image background = m_RootPanel.GetComponent<Image>();
		background.color = new Color(0f, 0f, 0f, 0.72f);

		GameObject content = CreateRectObject("Content", m_RootPanel.transform);
		RectTransform contentRect = content.transform as RectTransform;
		contentRect.anchorMin = new Vector2(0.5f, 0.5f);
		contentRect.anchorMax = new Vector2(0.5f, 0.5f);
		contentRect.pivot = new Vector2(0.5f, 0.5f);
		contentRect.sizeDelta = new Vector2(980f, 720f);

		VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlHeight = false;
		layout.childControlWidth = true;
		layout.childForceExpandHeight = false;
		layout.childForceExpandWidth = true;
		layout.spacing = 18f;
		layout.padding = new RectOffset(40, 40, 40, 40);

		ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		m_TitleText = CreateText("Title", content.transform, 54f, FontStyles.Bold, TextAlignmentOptions.Center);

		LayoutElement titleLayout = m_TitleText.gameObject.AddComponent<LayoutElement>();
		titleLayout.preferredHeight = 82f;

		GameObject columnsRoot = CreateRectObject("Columns", content.transform);
		HorizontalLayoutGroup columnsLayout = columnsRoot.AddComponent<HorizontalLayoutGroup>();
		columnsLayout.childAlignment = TextAnchor.UpperCenter;
		columnsLayout.childControlWidth = true;
		columnsLayout.childControlHeight = true;
		columnsLayout.childForceExpandWidth = true;
		columnsLayout.childForceExpandHeight = true;
		columnsLayout.spacing = 28f;

		LayoutElement columnsElement = columnsRoot.AddComponent<LayoutElement>();
		columnsElement.preferredHeight = 560f;

		m_LeftColumnText = CreateText("LeftColumn", columnsRoot.transform, 24f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
		m_RightColumnText = CreateText("RightColumn", columnsRoot.transform, 24f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
		m_LeftColumnText.textWrappingMode = TextWrappingModes.Normal;
		m_RightColumnText.textWrappingMode = TextWrappingModes.Normal;

		LayoutElement leftLayout = m_LeftColumnText.gameObject.AddComponent<LayoutElement>();
		leftLayout.minWidth = 420f;
		LayoutElement rightLayout = m_RightColumnText.gameObject.AddComponent<LayoutElement>();
		rightLayout.minWidth = 420f;

		RefreshTexts();
	}

	private void RefreshTexts()
	{
		if (m_TitleText == null || m_LeftColumnText == null || m_RightColumnText == null)
			return;

		m_TitleText.text = $"{LocalizationManager.Get("pause.title")}\n<size=28>{LocalizationManager.Get("pause.subtitle")}</size>";

		StringBuilder leftBuilder = new StringBuilder(320);
		StringBuilder rightBuilder = new StringBuilder(320);
		int splitIndex = (s_CommandHints.Length + 1) / 2;

		for (int i = 0; i < s_CommandHints.Length; i++)
		{
			StringBuilder targetBuilder = i < splitIndex ? leftBuilder : rightBuilder;
			targetBuilder.AppendLine(LocalizationManager.Get(s_CommandHints[i].LocalizationKey));
		}

		m_LeftColumnText.text = leftBuilder.ToString().TrimEnd();
		m_RightColumnText.text = rightBuilder.ToString().TrimEnd();
	}

	private static GameObject CreatePanel(string _name, Transform _parent)
	{
		GameObject root = CreateRectObject(_name, _parent);
		root.AddComponent<CanvasRenderer>();
		root.AddComponent<Image>();
		return root;
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
		text.color = Color.white;
		text.raycastTarget = false;

		RectTransform rect = text.rectTransform;
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(1f, 1f);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		return text;
	}
	#endregion

	#region Nested Types
	private readonly struct CommandHint
	{
		public readonly string LocalizationKey;

		public CommandHint(string _localizationKey)
		{
			LocalizationKey = _localizationKey;
		}
	}
	#endregion
}
