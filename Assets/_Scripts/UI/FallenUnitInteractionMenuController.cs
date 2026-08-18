using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Контекстное меню взаимодействия со сражённым (бессознательным) юнитом.
/// «Обмен» открывает инвентарь в режиме обмена с целевым юнитом.
/// </summary>
[DefaultExecutionOrder(-850)]
[DisallowMultipleComponent]
public sealed class FallenUnitInteractionMenuController : MonoBehaviour
{
	#region Constants
	private const int c_MenuSortingOrder = 31500;
	private const float c_ItemHeight = 32f;
	private const float c_MenuMinWidth = 188f;
	private const float c_ScreenEdgePadding = 8f;
	private static readonly Vector2 s_CursorOffset = new Vector2(6f, -6f);
	private static readonly (string Label, FallenUnitInteractionMenuAction Action)[] s_FallenMenuItems =
	{
		("Обмен", FallenUnitInteractionMenuAction.Exchange),
		("Стабилизировать", FallenUnitInteractionMenuAction.Stabilize),
		("Поднять", FallenUnitInteractionMenuAction.Lift)
	};
	#endregion

	#region Events
	public event Action<FallenUnitInteractionMenuAction, RtsUnitMember> ActionClicked;
	#endregion

	#region Static Access
	private static FallenUnitInteractionMenuController s_Instance;

	public static FallenUnitInteractionMenuController Instance => EnsureInstance();
	#endregion

	#region Private Fields
	private RectTransform m_Root;
	private CanvasGroup m_CanvasGroup;
	private Canvas m_OverrideCanvas;
	private GraphicRaycaster m_Raycaster;
	private MenuVisualStyle m_Style;
	private RtsUnitMember m_TargetUnit;
	private bool m_IsVisible;
	private bool m_UiBuilt;
	#endregion

	#region Public Properties
	public bool IsVisible => m_IsVisible;
	public RtsUnitMember TargetUnit => m_TargetUnit;
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
			HideImmediate();
	}
	#endregion

	#region Public Methods
	public void ShowForUnit(RtsUnitMember _targetUnit, Vector2 _screenPosition, bool _canStabilize = true)
	{
		if (_canStabilize)
		{
			ShowMenu(_targetUnit, _screenPosition, s_FallenMenuItems);
			return;
		}

		ShowMenu(
			_targetUnit,
			_screenPosition,
			new[]
			{
				("Обмен", FallenUnitInteractionMenuAction.Exchange),
				("Поднять", FallenUnitInteractionMenuAction.Lift)
			});
	}

	public void ShowReleaseForCarryingUnit(RtsUnitMember _targetUnit, Vector2 _screenPosition)
	{
		string label = LocalizationManager.Get("unit.menu.release", "Отпустить");
		ShowMenu(
			_targetUnit,
			_screenPosition,
			new[] { (label, FallenUnitInteractionMenuAction.ReleaseCarry) });
	}

	public void ShowFirstAidForUnit(RtsUnitMember _targetUnit, Vector2 _screenPosition)
	{
		string label = LocalizationManager.Get("unit.menu.first_aid", "Первая помощь");
		ShowMenu(
			_targetUnit,
			_screenPosition,
			new[] { (label, FallenUnitInteractionMenuAction.FirstAid) });
	}

	private void ShowMenu(
		RtsUnitMember _targetUnit,
		Vector2 _screenPosition,
		IReadOnlyList<(string Label, FallenUnitInteractionMenuAction Action)> _items)
	{
		if (_targetUnit == null || _items == null || _items.Count == 0)
		{
			HideImmediate();
			return;
		}

		EnsureUi();
		RebuildMenuItems(_items);
		m_TargetUnit = _targetUnit;
		m_IsVisible = true;
		m_CanvasGroup.alpha = 1f;
		m_CanvasGroup.blocksRaycasts = true;
		m_CanvasGroup.interactable = true;
		m_Root.gameObject.SetActive(true);
		UpdatePosition(_screenPosition);
		m_Root.SetAsLastSibling();
	}

	public void HideImmediate()
	{
		m_IsVisible = false;
		m_TargetUnit = null;

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
		if (!m_IsVisible || m_Root == null || !m_Root.gameObject.activeInHierarchy)
			return false;

		return RectTransformUtility.RectangleContainsScreenPoint(m_Root, _screenPosition, null);
	}
	#endregion

	#region Private Methods
	private static FallenUnitInteractionMenuController EnsureInstance()
	{
		if (s_Instance != null)
			return s_Instance;
		if (!PlayModeSingleton.CanSpawn)
			return null;

		var rootObject = new GameObject(nameof(FallenUnitInteractionMenuController));
		s_Instance = rootObject.AddComponent<FallenUnitInteractionMenuController>();
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
	}

	private void RebuildMenuItems(IReadOnlyList<(string Label, FallenUnitInteractionMenuAction Action)> _items)
	{
		if (m_Root == null)
			return;

		for (int i = m_Root.childCount - 1; i >= 0; i--)
			Destroy(m_Root.GetChild(i).gameObject);

		for (int i = 0; i < _items.Count; i++)
			CreateMenuItem(m_Root, _items[i].Label, _items[i].Action);

		m_Root.sizeDelta = new Vector2(c_MenuMinWidth, _items.Count * c_ItemHeight + 12f);
		LayoutRebuilder.ForceRebuildLayoutImmediate(m_Root);
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
				m_Style.FontSize = titleText.fontSize;
			m_Style.Text = titleText.color;
		}
	}

	private void CreateMenuItem(Transform _parent, string _label, FallenUnitInteractionMenuAction _action)
	{
		GameObject itemObject = CreateRectObject(_label, _parent);
		RectTransform itemRect = itemObject.transform as RectTransform;
		itemRect.sizeDelta = new Vector2(c_MenuMinWidth, c_ItemHeight);

		Image itemBackground = itemObject.AddComponent<Image>();
		ApplyPanelImageStyle(itemBackground, Color.white);

		Button button = itemObject.AddComponent<Button>();
		UiInteractionAudioUtility.EnsureHoverSoundOn(itemObject);
		button.targetGraphic = itemBackground;
		ColorBlock colors = button.colors;
		colors.normalColor = m_Style.ItemNormal;
		colors.highlightedColor = m_Style.ItemHover;
		colors.pressedColor = m_Style.ItemPressed;
		colors.selectedColor = m_Style.ItemHover;
		colors.colorMultiplier = 1f;
		colors.fadeDuration = 0.08f;
		button.colors = colors;
		button.onClick.AddListener(() => HandleItemClicked(_action));

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
		labelText.text = _label;
		labelText.fontSize = m_Style.FontSize;
		if (m_Style.Font != null)
			labelText.font = m_Style.Font;
		labelText.alignment = TextAlignmentOptions.MidlineLeft;
		labelText.color = m_Style.Text;
		labelText.raycastTarget = false;
	}

	private static void ApplyPanelImageStyle(Image _image, Color _color)
	{
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_image);
		_image.color = _color;
		_image.type = Image.Type.Sliced;
		_image.raycastTarget = true;
	}

	private void HandleItemClicked(FallenUnitInteractionMenuAction _action)
	{
		RtsUnitMember targetUnit = m_TargetUnit;
		HideImmediate();
		ActionClicked?.Invoke(_action, targetUnit);
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

	private static GameObject CreateRectObject(string _name, Transform _parent)
	{
		var go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		return go;
	}
	#endregion

	#region Nested Types
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
}

public enum FallenUnitInteractionMenuAction
{
	Exchange = 0,
	Stabilize = 1,
	DragAway = 2,
	Lift = 3,
	FirstAid = 4,
	ReleaseDrag = 5,
	ReleaseCarry = 6
}
