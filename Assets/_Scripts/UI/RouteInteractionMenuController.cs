using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Контекстное ПКМ-меню на сегменте маршрута с поддержкой подменю.
/// </summary>
[DefaultExecutionOrder(-849)]
[DisallowMultipleComponent]
public sealed class RouteInteractionMenuController : MonoBehaviour
{
	#region Constants
	private const int c_MenuSortingOrder = 31600;
	private const float c_ItemHeight = 26f;
	private const float c_MenuMinWidth = 260f;
	private const float c_SubmenuGap = 4f;
	private const float c_ScreenEdgePadding = 8f;
	private static readonly Vector2 s_CursorOffset = new Vector2(6f, -6f);
	#endregion

	#region Events
	public event Action<RouteInteractionMenuAction, RtsUnitMember, int, Vector3, object> ActionClicked;
	#endregion

	#region Static Access
	private static RouteInteractionMenuController s_Instance;

	public static RouteInteractionMenuController Instance => EnsureInstance();
	#endregion

	#region Private Fields
	private RectTransform m_Root;
	private RectTransform m_SubmenuRoot;
	private CanvasGroup m_CanvasGroup;
	private Canvas m_OverrideCanvas;
	private GraphicRaycaster m_Raycaster;
	private MenuVisualStyle m_Style;
	private RtsUnitMember m_TargetUnit;
	private int m_SegmentIndex = -1;
	private Vector3 m_WorldPoint;
	private bool m_IsVisible;
	private bool m_UiBuilt;
	private RouteMenuItemDefinition m_HoveredSubmenuParent;
	private readonly List<RouteMenuItemDefinition> m_MainItems = new List<RouteMenuItemDefinition>(8);
	#endregion

	#region Public Properties
	public bool IsVisible => m_IsVisible;
	public RtsUnitMember TargetUnit => m_TargetUnit;
	#endregion

	#region Nested Types
	public sealed class RouteMenuItemDefinition
	{
		public string Label;
		public RouteInteractionMenuAction Action;
		public object Payload;
		public bool HasSubmenu;
		public List<RouteMenuItemDefinition> Children;
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
				PanelBackground = new Color(0.31132078f, 0.31132078f, 0.31132078f, 0.9411765f),
				ItemNormal = new Color(0.3372549f, 0.3529412f, 0.37254903f, 1f),
				ItemHover = new Color(0.2f, 0.68f, 0.32f, 0.72f),
				ItemPressed = new Color(0.254717f, 0.254717f, 0.254717f, 1f),
				Text = Color.white,
				FontSize = 16f
			};
		}
	}
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
		m_Style = MenuVisualStyle.CreateDefault();
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

		if (PauseMenuController.IsPaused)
		{
			HideImmediate();
			return;
		}

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
		{
			HideImmediate();
			return;
		}

		Mouse mouse = Mouse.current;
		if (mouse != null && mouse.leftButton.wasPressedThisFrame)
		{
			Vector2 screenPos = mouse.position.ReadValue();
			if (!IsScreenPointOverMenu(screenPos))
				HideImmediate();
		}
	}
	#endregion

	#region Public Methods
	public void ShowForRoute(
		RtsUnitMember _unit,
		int _segmentIndex,
		Vector3 _worldPoint,
		Vector2 _screenPos,
		IReadOnlyList<RouteMenuItemDefinition> _items)
	{
		if (_unit == null || _items == null || _items.Count == 0)
		{
			HideImmediate();
			return;
		}

		EnsureUi();
		m_TargetUnit = _unit;
		m_SegmentIndex = _segmentIndex;
		m_WorldPoint = _worldPoint;
		m_MainItems.Clear();
		for (int i = 0; i < _items.Count; i++)
			m_MainItems.Add(_items[i]);

		RebuildMainMenu();
		HideSubmenu();

		m_IsVisible = true;
		m_CanvasGroup.alpha = 1f;
		m_CanvasGroup.blocksRaycasts = true;
		m_CanvasGroup.interactable = true;
		m_Root.gameObject.SetActive(true);
		UpdatePosition(_screenPos);
		m_Root.SetAsLastSibling();
	}

	public void HideImmediate()
	{
		m_IsVisible = false;
		m_TargetUnit = null;
		m_SegmentIndex = -1;
		m_HoveredSubmenuParent = null;
		m_MainItems.Clear();
		HideSubmenu();

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
		if (!m_IsVisible)
			return false;

		if (m_Root != null &&
		    m_Root.gameObject.activeInHierarchy &&
		    RectTransformUtility.RectangleContainsScreenPoint(m_Root, _screenPosition, null))
			return true;

		if (m_SubmenuRoot != null &&
		    m_SubmenuRoot.gameObject.activeInHierarchy &&
		    RectTransformUtility.RectangleContainsScreenPoint(m_SubmenuRoot, _screenPosition, null))
			return true;

		return false;
	}
	#endregion

	#region Private Methods
	private static RouteInteractionMenuController EnsureInstance()
	{
		if (s_Instance != null)
			return s_Instance;

		var rootObject = new GameObject(nameof(RouteInteractionMenuController));
		s_Instance = rootObject.AddComponent<RouteInteractionMenuController>();
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
		ConfigureMenuPanel(m_Root);

		GameObject submenuObject = CreateRectObject("SubmenuRoot", transform);
		m_SubmenuRoot = submenuObject.transform as RectTransform;
		ConfigureMenuPanel(m_SubmenuRoot);
		m_SubmenuRoot.gameObject.SetActive(false);

		m_CanvasGroup = rootObject.AddComponent<CanvasGroup>();
		m_CanvasGroup.alpha = 0f;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;

		m_UiBuilt = true;
		rootObject.SetActive(false);
	}

	private void ConfigureMenuPanel(RectTransform _panel)
	{
		_panel.anchorMin = Vector2.zero;
		_panel.anchorMax = Vector2.zero;
		_panel.pivot = new Vector2(0f, 1f);

		Image background = _panel.gameObject.GetComponent<Image>();
		if (background == null)
			background = _panel.gameObject.AddComponent<Image>();
		ApplyPanelImageStyle(background, m_Style.PanelBackground);

		VerticalLayoutGroup layout = _panel.gameObject.GetComponent<VerticalLayoutGroup>();
		if (layout == null)
			layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(6, 6, 6, 6);
		layout.spacing = 2f;
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		ContentSizeFitter fitter = _panel.gameObject.GetComponent<ContentSizeFitter>();
		if (fitter == null)
			fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	private void RebuildMainMenu()
	{
		ClearChildren(m_Root);
		for (int i = 0; i < m_MainItems.Count; i++)
			CreateMenuItem(m_Root, m_MainItems[i], _isSubmenuItem: false);

		m_Root.sizeDelta = new Vector2(c_MenuMinWidth, m_MainItems.Count * c_ItemHeight + 12f);
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_Root);
	}

	private void ShowSubmenu(RouteMenuItemDefinition _parent, RectTransform _parentItemRect)
	{
		if (_parent == null || !_parent.HasSubmenu || _parent.Children == null || _parent.Children.Count == 0)
		{
			HideSubmenu();
			return;
		}

		m_HoveredSubmenuParent = _parent;
		ClearChildren(m_SubmenuRoot);
		for (int i = 0; i < _parent.Children.Count; i++)
			CreateMenuItem(m_SubmenuRoot, _parent.Children[i], _isSubmenuItem: true);

		m_SubmenuRoot.sizeDelta = new Vector2(c_MenuMinWidth, _parent.Children.Count * c_ItemHeight + 12f);
		m_SubmenuRoot.gameObject.SetActive(true);
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_SubmenuRoot);

		Vector3[] corners = new Vector3[4];
		_parentItemRect.GetWorldCorners(corners);
		float x = corners[2].x + c_SubmenuGap;
		float y = corners[2].y;
		if (x + c_MenuMinWidth > Screen.width - c_ScreenEdgePadding)
			x = corners[0].x - c_MenuMinWidth - c_SubmenuGap;

		m_SubmenuRoot.position = new Vector3(x, y, 0f);
		ClampRectToScreen(m_SubmenuRoot);
	}

	private void HideSubmenu()
	{
		m_HoveredSubmenuParent = null;
		if (m_SubmenuRoot != null)
			m_SubmenuRoot.gameObject.SetActive(false);
	}

	private void CreateMenuItem(RectTransform _parent, RouteMenuItemDefinition _item, bool _isSubmenuItem)
	{
		string label = _item.HasSubmenu && !_isSubmenuItem ? _item.Label + " ▸" : _item.Label;
		GameObject itemObject = CreateRectObject(label, _parent);
		RectTransform itemRect = itemObject.transform as RectTransform;
		itemRect.sizeDelta = new Vector2(c_MenuMinWidth, c_ItemHeight);

		Image itemBackground = itemObject.AddComponent<Image>();
		ApplyPanelImageStyle(itemBackground, m_Style.ItemNormal);

		Button button = itemObject.AddComponent<Button>();
		UiInteractionAudioUtility.EnsureHoverSoundOn(itemObject);
		button.targetGraphic = itemBackground;
		ColorBlock colors = button.colors;
		colors.normalColor = m_Style.ItemNormal;
		colors.highlightedColor = m_Style.ItemHover;
		colors.pressedColor = m_Style.ItemPressed;
		colors.selectedColor = m_Style.ItemHover;
		colors.fadeDuration = 0.08f;
		button.colors = colors;

		LayoutElement layoutElement = itemObject.AddComponent<LayoutElement>();
		layoutElement.preferredHeight = c_ItemHeight;
		layoutElement.minWidth = c_MenuMinWidth;

		GameObject labelObject = CreateRectObject("Label", itemObject.transform);
		RectTransform labelRect = labelObject.transform as RectTransform;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = new Vector2(12f, 0f);
		labelRect.offsetMax = new Vector2(-8f, 0f);

		TextMeshProUGUI labelText = labelObject.AddComponent<TextMeshProUGUI>();
		labelText.text = label;
		labelText.fontSize = m_Style.FontSize;
		if (m_Style.Font != null)
			labelText.font = m_Style.Font;
		labelText.alignment = TextAlignmentOptions.MidlineLeft;
		labelText.color = m_Style.Text;
		labelText.raycastTarget = false;

		RouteMenuItemHoverRelay hoverRelay = itemObject.AddComponent<RouteMenuItemHoverRelay>();
		hoverRelay.Initialize(this, _item, itemRect, _isSubmenuItem);

		if (!_item.HasSubmenu || _isSubmenuItem)
			button.onClick.AddListener(() => HandleItemClicked(_item));
	}

	internal void HandleItemHovered(RouteMenuItemDefinition _item, RectTransform _itemRect, bool _isSubmenuItem)
	{
		if (_isSubmenuItem)
			return;

		if (_item != null && _item.HasSubmenu)
			ShowSubmenu(_item, _itemRect);
		else
			HideSubmenu();
	}

	private void HandleItemClicked(RouteMenuItemDefinition _item)
	{
		if (_item == null)
			return;

		RtsUnitMember unit = m_TargetUnit;
		int segmentIndex = m_SegmentIndex;
		Vector3 worldPoint = m_WorldPoint;
		RouteInteractionMenuAction action = _item.Action;
		object payload = _item.Payload;
		HideImmediate();
		ActionClicked?.Invoke(action, unit, segmentIndex, worldPoint, payload);
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
		{
			m_Root.SetParent(hostTransform, false);
			if (m_SubmenuRoot != null)
				m_SubmenuRoot.SetParent(hostTransform, false);
		}

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

		Transform inventoryRoot = bindings.transform;
		Canvas bindingsCanvas = inventoryRoot.GetComponentInParent<Canvas>();
		return bindingsCanvas != null ? bindingsCanvas.rootCanvas : null;
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
				m_Style.FontSize = Mathf.Max(14f, titleText.fontSize - 2f);
			m_Style.Text = titleText.color;
		}
	}

	private static void ApplyPanelImageStyle(Image _image, Color _color)
	{
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_image);
		_image.color = _color;
		_image.type = Image.Type.Sliced;
		_image.raycastTarget = true;
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
		ClampRectToScreen(m_Root);
	}

	private static void ClampRectToScreen(RectTransform _rect)
	{
		if (_rect == null)
			return;

		Vector3[] corners = new Vector3[4];
		_rect.GetWorldCorners(corners);

		Vector2 screenMin = corners[0];
		Vector2 screenMax = corners[0];
		for (int i = 1; i < 4; i++)
		{
			screenMin = Vector2.Min(screenMin, corners[i]);
			screenMax = Vector2.Max(screenMax, corners[i]);
		}

		Vector3 position = _rect.position;
		if (screenMin.x < c_ScreenEdgePadding)
			position.x += c_ScreenEdgePadding - screenMin.x;
		if (screenMax.x > Screen.width - c_ScreenEdgePadding)
			position.x -= screenMax.x - (Screen.width - c_ScreenEdgePadding);
		if (screenMin.y < c_ScreenEdgePadding)
			position.y += c_ScreenEdgePadding - screenMin.y;
		if (screenMax.y > Screen.height - c_ScreenEdgePadding)
			position.y -= screenMax.y - (Screen.height - c_ScreenEdgePadding);

		_rect.position = position;
	}

	private static void ClearChildren(Transform _parent)
	{
		if (_parent == null)
			return;

		for (int i = _parent.childCount - 1; i >= 0; i--)
			Destroy(_parent.GetChild(i).gameObject);
	}

	private static GameObject CreateRectObject(string _name, Transform _parent)
	{
		var go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		return go;
	}
	#endregion

	#region Hover Relay
	private sealed class RouteMenuItemHoverRelay : MonoBehaviour, IPointerEnterHandler
	{
		private RouteInteractionMenuController m_Owner;
		private RouteMenuItemDefinition m_Item;
		private RectTransform m_ItemRect;
		private bool m_IsSubmenuItem;

		public void Initialize(
			RouteInteractionMenuController _owner,
			RouteMenuItemDefinition _item,
			RectTransform _itemRect,
			bool _isSubmenuItem)
		{
			m_Owner = _owner;
			m_Item = _item;
			m_ItemRect = _itemRect;
			m_IsSubmenuItem = _isSubmenuItem;
		}

		public void OnPointerEnter(PointerEventData _eventData)
		{
			m_Owner?.HandleItemHovered(m_Item, m_ItemRect, m_IsSubmenuItem);
		}
	}
	#endregion
}
