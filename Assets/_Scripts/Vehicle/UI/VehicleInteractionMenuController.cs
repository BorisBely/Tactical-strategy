using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>ПКМ-меню по машине: посадка / посадка с одной стороны / погрузка раненого.</summary>
[DefaultExecutionOrder(-840)]
[DisallowMultipleComponent]
public sealed class VehicleInteractionMenuController : MonoBehaviour
{
	#region Constants
	private const int c_MenuSortingOrder = 31510;
	private const float c_ItemHeight = 32f;
	private const float c_MenuMinWidth = 240f;
	private const float c_ScreenEdgePadding = 8f;
	private static readonly Vector2 s_CursorOffset = new Vector2(6f, -6f);
	#endregion

	#region Nested
	public enum MenuAction : byte
	{
		Board,
		BoardOneSide,
		BoardGunner,
		LoadWounded,
		Exchange,
		NoSpace
	}

	private struct MenuVisualStyle
	{
		public Color PanelBackground;
		public Color ItemNormal;
		public Color ItemHover;
		public Color ItemPressed;
		public Color Text;
		public TMP_FontAsset Font;
		public float FontSize;

		public static MenuVisualStyle CreateDefault()
		{
			return new MenuVisualStyle
			{
				PanelBackground = InventoryUiTheme.PanelBackground,
				ItemNormal = InventoryUiTheme.CellBackground,
				ItemHover = InventoryUiTheme.MenuItemHover,
				ItemPressed = InventoryUiTheme.MenuItemPressed,
				Text = InventoryUiTheme.PrimaryText,
				FontSize = 16f
			};
		}
	}
	#endregion

	#region Events
	public event Action<MenuAction, VehicleController> ActionClicked;
	#endregion

	#region Static
	private static VehicleInteractionMenuController s_Instance;
	public static VehicleInteractionMenuController Instance => EnsureInstance();
	#endregion

	#region Private Fields
	private static int s_ConsumedLeftClickFrame = -1;
	private RectTransform m_Root;
	private CanvasGroup m_CanvasGroup;
	private Canvas m_OverrideCanvas;
	private GraphicRaycaster m_Raycaster;
	private VehicleController m_Target;
	private bool m_IsVisible;
	private bool m_UiBuilt;
	private MenuVisualStyle m_Style;
	private readonly List<(RectTransform Rect, MenuAction Action, bool Disabled)> m_Items =
		new List<(RectTransform, MenuAction, bool)>(8);
	#endregion

	#region Public Properties
	public bool IsVisible => m_IsVisible;
	public VehicleController Target => m_Target;
	public static bool DidConsumeLeftClickThisFrame => s_ConsumedLeftClickFrame == Time.frameCount;
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap() => EnsureInstance();
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
		HideImmediate();
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}

	private void Update()
	{
		if (!m_IsVisible)
			return;
		if (PauseMenuController.IsPaused ||
		    (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
		{
			HideImmediate();
			return;
		}

		// Прямой клик — EventSystem + Input System часто не видит меню.
		if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
			TryClickItemUnderCursor();
	}
	#endregion

	#region Public Methods
	public void ShowForVehicle(
		VehicleController _vehicle,
		Vector2 _screenPosition,
		bool _canBoard,
		bool _canLoadWounded,
		bool _hasSpaceForLiving,
		bool _hasSpaceForWounded,
		bool _hasFreeGunnerSeat)
	{
		if (_vehicle == null)
		{
			HideImmediate();
			return;
		}

		EnsureUi();
		ClearItems();

		if (_canBoard)
		{
			if (_hasSpaceForLiving)
			{
				AddItem("Сесть", MenuAction.Board);
				AddItem("Посадка с одной стороны", MenuAction.BoardOneSide);
				if (_hasFreeGunnerSeat)
					AddItem("Сесть на место стрелка", MenuAction.BoardGunner);
			}
			else
				AddItem("Нет места", MenuAction.NoSpace, _disabled: true);
		}

		if (_canLoadWounded)
		{
			if (_hasSpaceForWounded)
				AddItem("Погрузить раненого", MenuAction.LoadWounded);
			else
				AddItem("Нет места для раненого", MenuAction.NoSpace, _disabled: true);
		}

		if (_canBoard)
			AddItem("Обмен", MenuAction.Exchange);

		if (m_Items.Count == 0)
		{
			HideImmediate();
			return;
		}

		m_Target = _vehicle;
		m_IsVisible = true;
		m_CanvasGroup.alpha = 1f;
		m_CanvasGroup.blocksRaycasts = true;
		m_CanvasGroup.interactable = true;
		m_Root.gameObject.SetActive(true);
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_Root);
		UpdatePosition(_screenPosition);
	}

	public void HideImmediate()
	{
		m_IsVisible = false;
		m_Target = null;
		if (m_CanvasGroup != null)
		{
			m_CanvasGroup.alpha = 0f;
			m_CanvasGroup.blocksRaycasts = false;
			m_CanvasGroup.interactable = false;
		}

		if (m_Root != null)
			m_Root.gameObject.SetActive(false);
	}

	public bool IsScreenPointOverMenu(Vector2 _screenPosition)
	{
		return m_IsVisible && m_Root != null && m_Root.gameObject.activeInHierarchy &&
		       RectTransformUtility.RectangleContainsScreenPoint(m_Root, _screenPosition, null);
	}
	#endregion

	#region Private Methods
	private static VehicleInteractionMenuController EnsureInstance()
	{
		if (s_Instance != null)
			return s_Instance;
		if (!PlayModeSingleton.CanSpawn)
			return null;
		var go = new GameObject(nameof(VehicleInteractionMenuController));
		s_Instance = go.AddComponent<VehicleInteractionMenuController>();
		return s_Instance;
	}

	private void EnsureUi()
	{
		ResolveStyleFromGameUi();
		EnsureHostCanvasBinding();

		if (m_UiBuilt)
			return;

		GameObject rootObject = CreateRectObject("MenuRoot", transform);
		m_Root = rootObject.transform as RectTransform;
		m_Root.anchorMin = Vector2.zero;
		m_Root.anchorMax = Vector2.zero;
		m_Root.pivot = new Vector2(0f, 1f);

		m_CanvasGroup = rootObject.AddComponent<CanvasGroup>();
		m_CanvasGroup.alpha = 0f;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;

		Image background = rootObject.AddComponent<Image>();
		ApplyPanelImageStyle(background, m_Style.PanelBackground);

		VerticalLayoutGroup layout = rootObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(6, 6, 6, 6);
		layout.spacing = 3f;
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		ContentSizeFitter fitter = rootObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		m_UiBuilt = true;
		rootObject.SetActive(false);
		EnsureHostCanvasBinding();
	}

	private void ResolveStyleFromGameUi()
	{
		m_Style = MenuVisualStyle.CreateDefault();
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
			return;

		InventoryPanelView panel = bindings.CharacterInventoryPanel;
		if (panel != null)
		{
			if (panel.TryGetComponent(out Image panelImage))
				m_Style.PanelBackground = panelImage.color;

			InventoryEquipmentSlotAppearance appearance = panel.EquipmentSlotAppearance;
			if (appearance != null)
			{
				m_Style.ItemNormal = appearance.NormalBackgroundColor;
				m_Style.ItemHover = appearance.HighlightBackgroundColor;
			}
		}

		TextMeshProUGUI titleText = bindings.GetComponentInChildren<TextMeshProUGUI>(true);
		if (titleText != null)
		{
			if (titleText.font != null)
				m_Style.Font = titleText.font;
			if (titleText.fontSize > 0f)
				m_Style.FontSize = Mathf.Clamp(titleText.fontSize, 14f, 22f);
			m_Style.Text = titleText.color;
		}
	}

	private void EnsureHostCanvasBinding()
	{
		Canvas hostCanvas = ResolveHostCanvas();
		if (hostCanvas == null)
		{
			EnsureFallbackOverlayCanvas();
			return;
		}

		Transform hostTransform = hostCanvas.transform;
		if (m_Root == null)
			transform.SetParent(hostTransform, false);
		else
			m_Root.SetParent(hostTransform, false);

		transform.localScale = Vector3.one;
		transform.localRotation = Quaternion.identity;

		RectTransform host = m_Root != null ? m_Root : transform as RectTransform;
		if (host == null)
			return;

		if (!host.TryGetComponent(out m_OverrideCanvas))
			m_OverrideCanvas = host.gameObject.AddComponent<Canvas>();
		m_OverrideCanvas.overrideSorting = true;
		m_OverrideCanvas.sortingOrder = c_MenuSortingOrder;

		if (!host.TryGetComponent(out m_Raycaster))
			m_Raycaster = host.gameObject.AddComponent<GraphicRaycaster>();
	}

	private void EnsureFallbackOverlayCanvas()
	{
		Canvas canvas = gameObject.GetComponent<Canvas>();
		if (canvas == null)
			canvas = gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = c_MenuSortingOrder;

		CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
		if (scaler == null)
			scaler = gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(2560f, 1440f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0f;

		if (gameObject.GetComponent<GraphicRaycaster>() == null)
			gameObject.AddComponent<GraphicRaycaster>();
	}

	private static Canvas ResolveHostCanvas()
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
			return null;

		if (bindings.CharacterInventoryPanel != null)
		{
			Canvas panelCanvas = bindings.CharacterInventoryPanel.GetComponentInParent<Canvas>();
			if (panelCanvas != null)
				return panelCanvas.rootCanvas;
		}

		Canvas bindingsCanvas = bindings.GetComponentInParent<Canvas>();
		return bindingsCanvas != null ? bindingsCanvas.rootCanvas : null;
	}

	private void ClearItems()
	{
		m_Items.Clear();
		if (m_Root == null)
			return;
		for (int i = m_Root.childCount - 1; i >= 0; i--)
			Destroy(m_Root.GetChild(i).gameObject);
	}

	private void AddItem(string _label, MenuAction _action, bool _disabled = false)
	{
		GameObject item = CreateRectObject(_label, m_Root);
		RectTransform rect = item.transform as RectTransform;
		rect.sizeDelta = new Vector2(c_MenuMinWidth, c_ItemHeight);

		Image img = item.AddComponent<Image>();
		ApplyPanelImageStyle(img, m_Style.ItemNormal);
		img.raycastTarget = true;

		LayoutElement layoutElement = item.AddComponent<LayoutElement>();
		layoutElement.preferredHeight = c_ItemHeight;
		layoutElement.minWidth = c_MenuMinWidth;

		GameObject textGo = CreateRectObject("Label", item.transform);
		RectTransform textRect = textGo.transform as RectTransform;
		textRect.anchorMin = Vector2.zero;
		textRect.anchorMax = Vector2.one;
		textRect.offsetMin = new Vector2(12f, 0f);
		textRect.offsetMax = new Vector2(-8f, 0f);

		TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
		tmp.text = _label;
		tmp.fontSize = m_Style.FontSize;
		tmp.color = _disabled ? new Color(0.55f, 0.55f, 0.55f) : m_Style.Text;
		tmp.alignment = TextAlignmentOptions.MidlineLeft;
		tmp.raycastTarget = false;
		if (m_Style.Font != null)
			tmp.font = m_Style.Font;

		m_Items.Add((rect, _action, _disabled));
	}

	private void TryClickItemUnderCursor()
	{
		if (Mouse.current == null || m_Items.Count == 0)
			return;

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		for (int i = 0; i < m_Items.Count; i++)
		{
			(RectTransform rect, MenuAction action, bool disabled) = m_Items[i];
			if (rect == null || disabled)
				continue;
			if (!RectTransformUtility.RectangleContainsScreenPoint(rect, mousePosition, null))
				continue;

			VehicleController target = m_Target;
			HideImmediate();
			s_ConsumedLeftClickFrame = Time.frameCount;
			if (target != null)
				ActionClicked?.Invoke(action, target);
			return;
		}

		if (!IsScreenPointOverMenu(mousePosition))
			HideImmediate();
	}

	private void UpdatePosition(Vector2 _screenPosition)
	{
		if (m_Root == null)
			return;

		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_Root);

		m_Root.position = new Vector3(
			_screenPosition.x + s_CursorOffset.x,
			_screenPosition.y + s_CursorOffset.y,
			0f);
		ClampToScreen();
	}

	private void ClampToScreen()
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

	private static void ApplyPanelImageStyle(Image _image, Color _color)
	{
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_image);
		_image.color = _color;
		_image.type = Image.Type.Sliced;
		_image.raycastTarget = true;
	}

	private static GameObject CreateRectObject(string _name, Transform _parent)
	{
		var go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		return go;
	}
	#endregion
}
