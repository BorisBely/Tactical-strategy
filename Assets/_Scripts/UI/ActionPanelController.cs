using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(-800)]
[DisallowMultipleComponent]
public sealed class ActionPanelController : MonoBehaviour
{
	#region Constants
	private const int c_PanelSortingOrder = 31000;
	private const float c_PanelHeight = 56f;
	private const float c_ButtonWidth = 88f;
	private const float c_ButtonSpacing = 4f;
	private const float c_FadeDuration = 0.2f;
	private const float c_LabelFontSize = 12f;
	private const float c_KeyFontSize = 14f;
	private const float c_ScreenBottomMargin = 4f;
	#endregion

	#region Entry Definition
	[Serializable]
	public struct Entry
	{
		public string Label;
		public string KeyDisplay;
		public Action OnClick;
	}
	#endregion

	#region Static Access
	private static ActionPanelController s_Instance;

	public static ActionPanelController Instance => s_Instance;
	#endregion

	#region Private Fields
	private Canvas m_Canvas;
	private CanvasGroup m_CanvasGroup;
	private RectTransform m_PanelRect;
	private Coroutine m_FadeCoroutine;
	private float m_TargetAlpha;
	private bool m_IsHovered;
	private bool m_UiBuilt;
	private TMP_FontAsset m_ResolvedFont;
	private GameObject[] m_ButtonObjects;
	private bool m_SubscribedToSelection;
	#endregion

	#region Entries
	private Entry[] m_Entries;
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		EnsureInstance();
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
		BuildUi();
		HideImmediate();
	}

	private void Start()
	{
		RefreshConditionalButtons();
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
		{
			if (m_SubscribedToSelection)
			{
				RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
				if (manager != null)
					manager.SelectionChanged -= OnSelectionChanged;
				m_SubscribedToSelection = false;
			}

			s_Instance = null;
		}
	}

	private void Update()
	{
		if (!m_UiBuilt)
			return;

		if (PauseMenuController.IsPaused)
		{
			if (m_TargetAlpha > 0f)
			{
				m_TargetAlpha = 0f;
				StartFade(0f);
			}

			return;
		}

		m_IsHovered = IsMouseOverPanel();

		float desired = m_IsHovered ? 1f : 0f;
		if (!Mathf.Approximately(m_TargetAlpha, desired))
		{
			m_TargetAlpha = desired;
			StartFade(desired);
		}
	}

	private bool IsMouseOverPanel()
	{
		if (m_PanelRect == null)
			return false;

		Vector2 mousePosition = Mouse.current?.position.ReadValue() ?? Vector2.zero;
		return RectTransformUtility.RectangleContainsScreenPoint(m_PanelRect, mousePosition, null);
	}
	#endregion

	#region Public Methods
	public static void EnsureInstance()
	{
		if (s_Instance != null)
			return;

		GameObject go = new GameObject(nameof(ActionPanelController));
		go.AddComponent<ActionPanelController>();
		DontDestroyOnLoad(go);
	}
	#endregion

	#region Private Methods - UI Building
	private void BuildUi()
	{
		if (m_UiBuilt)
			return;

		BuildEntries();
		EnsureCanvas();
		EnsureCanvasGroup();
		ResolveFont();
		BuildPanel();
		BuildButtons();
		PositionPanel();
		m_UiBuilt = true;
	}

	private void ResolveFont()
	{
		if (m_ResolvedFont != null)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings != null)
		{
			TextMeshProUGUI tmpText = bindings.GetComponentInChildren<TextMeshProUGUI>(true);
			if (tmpText != null && tmpText.font != null)
			{
				m_ResolvedFont = tmpText.font;
				return;
			}
		}

		m_ResolvedFont = TMP_Settings.defaultFontAsset;
	}

	private void EnsureCanvas()
	{
		m_Canvas = gameObject.AddComponent<Canvas>();
		m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		m_Canvas.sortingOrder = c_PanelSortingOrder;

		CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(2560f, 1440f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0f;

		gameObject.AddComponent<GraphicRaycaster>();
	}

	private void EnsureCanvasGroup()
	{
		m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
		m_CanvasGroup.alpha = 0f;
		m_CanvasGroup.interactable = true;
		m_CanvasGroup.blocksRaycasts = true;
	}

	private void BuildPanel()
	{
		GameObject panelGo = new GameObject("Panel", typeof(RectTransform));
		panelGo.transform.SetParent(transform, false);

		m_PanelRect = panelGo.transform as RectTransform;
		m_PanelRect.anchorMin = new Vector2(0.5f, 0f);
		m_PanelRect.anchorMax = new Vector2(0.5f, 0f);
		m_PanelRect.pivot = new Vector2(0.5f, 0f);
		m_PanelRect.sizeDelta = new Vector2(0f, c_PanelHeight);

		Image panelBg = panelGo.AddComponent<Image>();
		panelBg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
		panelBg.raycastTarget = true;

		HorizontalLayoutGroup layout = panelGo.AddComponent<HorizontalLayoutGroup>();
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlWidth = false;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = true;
		layout.spacing = c_ButtonSpacing;
		layout.padding = new RectOffset(8, 8, 4, 4);
	}

	private void BuildButtons()
	{
		m_ButtonObjects = new GameObject[m_Entries.Length];
		for (int i = 0; i < m_Entries.Length; i++)
		{
			Entry entry = m_Entries[i];
			m_ButtonObjects[i] = BuildButton(entry, i);
		}
	}

	private GameObject BuildButton(Entry _entry, int _index)
	{
		GameObject btnGo = new GameObject($"Btn_{_entry.Label}", typeof(RectTransform));
		btnGo.transform.SetParent(m_PanelRect, false);

		RectTransform btnRect = btnGo.transform as RectTransform;
		btnRect.sizeDelta = new Vector2(c_ButtonWidth, c_PanelHeight - 8f);

		Image btnBg = btnGo.AddComponent<Image>();
		btnBg.color = new Color(0.18f, 0.18f, 0.22f, 1f);
		btnBg.raycastTarget = true;

		Button button = btnGo.AddComponent<Button>();
		button.targetGraphic = btnBg;
		ColorBlock colors = button.colors;
		colors.normalColor = new Color(0.18f, 0.18f, 0.22f, 1f);
		colors.highlightedColor = new Color(0.28f, 0.28f, 0.35f, 1f);
		colors.pressedColor = new Color(0.12f, 0.12f, 0.16f, 1f);
		colors.selectedColor = new Color(0.22f, 0.22f, 0.28f, 1f);
		colors.colorMultiplier = 1f;
		button.colors = colors;

		int index = _index;
		button.onClick.AddListener(() => HandleButtonClick(index));

		GameObject labelGo = new GameObject("Label", typeof(RectTransform));
		labelGo.transform.SetParent(btnRect, false);
		RectTransform labelRect = labelGo.transform as RectTransform;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = new Vector2(1f, 0.55f);
		labelRect.offsetMin = new Vector2(2f, 2f);
		labelRect.offsetMax = new Vector2(-2f, 0f);

		TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
		label.text = _entry.Label;
		label.fontSize = c_LabelFontSize;
		label.alignment = TextAlignmentOptions.Center;
		label.color = new Color(0.82f, 0.82f, 0.82f, 1f);
		label.raycastTarget = false;
		if (m_ResolvedFont != null)
			label.font = m_ResolvedFont;

		GameObject keyGo = new GameObject("Key", typeof(RectTransform));
		keyGo.transform.SetParent(btnRect, false);
		RectTransform keyRect = keyGo.transform as RectTransform;
		keyRect.anchorMin = new Vector2(0f, 0.55f);
		keyRect.anchorMax = new Vector2(1f, 1f);
		keyRect.offsetMin = new Vector2(2f, 0f);
		keyRect.offsetMax = new Vector2(-2f, -2f);

		TextMeshProUGUI keyText = keyGo.AddComponent<TextMeshProUGUI>();
		keyText.text = _entry.KeyDisplay;
		keyText.fontSize = c_KeyFontSize;
		keyText.alignment = TextAlignmentOptions.Center;
		keyText.color = new Color(0.5f, 0.6f, 0.75f, 1f);
		keyText.raycastTarget = false;
		if (m_ResolvedFont != null)
			keyText.font = m_ResolvedFont;

		return btnGo;
	}

	private void BuildEntries()
	{
		m_Entries = new Entry[]
		{
			new Entry { Label = "Граната",    KeyDisplay = "G", OnClick = OnClickGrenade },
			new Entry { Label = "Готовность", KeyDisplay = "E", OnClick = OnClickReady },
			new Entry { Label = "Присед",     KeyDisplay = "C", OnClick = OnClickCrouch },
			new Entry { Label = "Зарядка",    KeyDisplay = "T", OnClick = OnClickMagazineLoad },
			new Entry { Label = "Перезарядка",KeyDisplay = "R", OnClick = OnClickReload },
			new Entry { Label = "Реж.прицел", KeyDisplay = "B", OnClick = OnClickAimMode },
			new Entry { Label = "Реж.огня",   KeyDisplay = "V", OnClick = OnClickFireMode },
			new Entry { Label = "Наведение",  KeyDisplay = "Q", OnClick = OnClickRotate },
			new Entry { Label = "Построение", KeyDisplay = "X", OnClick = OnClickFormation },
			new Entry { Label = "Инвентарь",  KeyDisplay = "I", OnClick = OnClickInventory },
		};
	}

	private void PositionPanel()
	{
		float totalWidth = m_Entries.Length * c_ButtonWidth + (m_Entries.Length - 1) * c_ButtonSpacing + 16f;
		m_PanelRect.sizeDelta = new Vector2(totalWidth, c_PanelHeight);
		m_PanelRect.anchoredPosition = new Vector2(0f, c_ScreenBottomMargin);
	}
	#endregion

	#region Private Methods - Fade
	private void StartFade(float _targetAlpha)
	{
		if (m_FadeCoroutine != null)
			StopCoroutine(m_FadeCoroutine);
		m_FadeCoroutine = StartCoroutine(FadeRoutine(_targetAlpha));
	}

	private IEnumerator FadeRoutine(float _targetAlpha)
	{
		float startAlpha = m_CanvasGroup.alpha;
		float elapsed = 0f;
		while (elapsed < c_FadeDuration)
		{
			elapsed += Time.unscaledDeltaTime;
			float t = Mathf.Clamp01(elapsed / c_FadeDuration);
			m_CanvasGroup.alpha = Mathf.Lerp(startAlpha, _targetAlpha, t);
			yield return null;
		}

		m_CanvasGroup.alpha = _targetAlpha;
		m_CanvasGroup.blocksRaycasts = _targetAlpha > 0.01f;
		m_FadeCoroutine = null;
	}

	private void HideImmediate()
	{
		if (m_FadeCoroutine != null)
		{
			StopCoroutine(m_FadeCoroutine);
			m_FadeCoroutine = null;
		}

		if (m_CanvasGroup != null)
		{
			m_CanvasGroup.alpha = 0f;
			m_CanvasGroup.blocksRaycasts = false;
		}

		m_TargetAlpha = 0f;
		m_IsHovered = false;
	}
	#endregion

	#region Private Methods - Conditional Buttons
	private void OnSelectionChanged()
	{
		RefreshConditionalButtons();
	}

	private void RefreshConditionalButtons()
	{
		if (!m_UiBuilt || m_ButtonObjects == null)
			return;

		if (!m_SubscribedToSelection)
		{
			RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
			if (manager != null)
			{
				manager.SelectionChanged += OnSelectionChanged;
				m_SubscribedToSelection = true;
			}
		}

		RtsUnitSelectionManager mgr = RtsUnitSelectionManager.Instance;
		int count = mgr != null ? mgr.SelectedUnitCount : 0;

		if (m_ButtonObjects.Length > 8 && m_ButtonObjects[8] != null)
			m_ButtonObjects[8].SetActive(count > 1);

		if (m_ButtonObjects.Length > 9 && m_ButtonObjects[9] != null)
			m_ButtonObjects[9].SetActive(count == 1);

		UpdatePanelWidth();
	}

	private void UpdatePanelWidth()
	{
		if (m_PanelRect == null || m_ButtonObjects == null)
			return;

		int visibleCount = 0;
		for (int i = 0; i < m_ButtonObjects.Length; i++)
		{
			if (m_ButtonObjects[i] != null && m_ButtonObjects[i].activeSelf)
				visibleCount++;
		}

		float totalWidth = visibleCount > 0
			? visibleCount * c_ButtonWidth + (visibleCount - 1) * c_ButtonSpacing + 16f
			: m_Entries.Length * c_ButtonWidth + (m_Entries.Length - 1) * c_ButtonSpacing + 16f;

		m_PanelRect.sizeDelta = new Vector2(totalWidth, c_PanelHeight);
	}
	#endregion

	#region Private Methods - Action Handlers
	private void HandleButtonClick(int _index)
	{
		if (_index < 0 || _index >= m_Entries.Length)
			return;

		if (PauseMenuController.IsPaused)
			return;

		if (GameInputGate.ShouldBlockGameplayInput())
			return;

		m_Entries[_index].OnClick?.Invoke();
	}

	private static void OnClickGrenade()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CycleGrenadeThrowTypePublic();
	}

	private static void OnClickReady()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.ToggleSelectedReady();
	}

	private static void OnClickCrouch()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CommandSelectedCrouchToggle();
	}

	private static void OnClickMagazineLoad()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CommandSelectedManualMagazineLoading();
	}

	private static void OnClickReload()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CommandSelectedWeaponReload();
	}

	private static void OnClickAimMode()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CommandSelectedCycleWeaponAimMode();
	}

	private static void OnClickFireMode()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CommandSelectedCycleWeaponFireMode();
	}

	private static void OnClickFormation()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.CycleSelectedFormation();
	}

	private static void OnClickRotate()
	{
		RtsUnitSelectionManager manager = RtsUnitSelectionManager.Instance;
		if (manager == null)
			return;
		manager.ToggleRotateToPointMode();
	}

	private static void OnClickInventory()
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
			return;
		bindings.ToggleInventoryWindow();
	}
	#endregion
}
