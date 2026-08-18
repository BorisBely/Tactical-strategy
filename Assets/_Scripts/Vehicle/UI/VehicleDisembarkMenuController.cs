using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>ПКМ по кнопке высадки: без водителя / все / по имени.</summary>
[DefaultExecutionOrder(-835)]
[DisallowMultipleComponent]
public sealed class VehicleDisembarkMenuController : MonoBehaviour
{
	#region Constants
	private const int c_MenuSortingOrder = 31520;
	private const float c_ItemHeight = 32f;
	private const float c_MenuMinWidth = 240f;
	private const float c_ScreenEdgePadding = 8f;
	private static readonly Vector2 s_CursorOffset = new Vector2(6f, -6f);
	#endregion

	#region Nested
	public enum MenuAction : byte
	{
		ExceptDriver,
		Everyone,
		Specific
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
				FontSize = 18f
			};
		}
	}
	#endregion

	#region Events
	public event Action<MenuAction, VehicleController, RtsUnitMember> ActionClicked;
	#endregion

	#region Static
	private static VehicleDisembarkMenuController s_Instance;
	public static VehicleDisembarkMenuController Instance => EnsureInstance();
	#endregion

	#region Private Fields
	private RectTransform m_Root;
	private CanvasGroup m_CanvasGroup;
	private Canvas m_OverrideCanvas;
	private GraphicRaycaster m_Raycaster;
	private VehicleController m_Target;
	private bool m_IsVisible;
	private bool m_UiBuilt;
	private MenuVisualStyle m_Style;
	private readonly List<(RectTransform Rect, MenuAction Action, RtsUnitMember Unit)> m_Items =
		new List<(RectTransform, MenuAction, RtsUnitMember)>(8);
	#endregion

	#region Public Properties
	public bool IsVisible => m_IsVisible;
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

		if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
			TryClickItemUnderCursor();
	}
	#endregion

	#region Public Methods
	public void ShowForVehicle(VehicleController _vehicle, Vector2 _screenPosition)
	{
		if (_vehicle == null || _vehicle.Seats == null)
		{
			HideImmediate();
			return;
		}

		EnsureUi();
		ClearItems();

		var ordered = new List<(VehicleSeatId Seat, RtsUnitMember Unit)>(8);
		_vehicle.Seats.CollectOccupantsOrdered(ordered);
		if (ordered.Count == 0)
		{
			AddLabel("Пассажиров нет");
		}
		else
		{
			AddItem("Без водителя", MenuAction.ExceptDriver, null);
			AddItem("Высадить всех", MenuAction.Everyone, null);

			for (int i = 0; i < ordered.Count; i++)
			{
				RtsUnitMember unit = ordered[i].Unit;
				if (unit == null)
					continue;
				string name = ResolveName(unit);
				string role = SeatLabel(ordered[i].Seat);
				AddItem($"{role}: {name}", MenuAction.Specific, unit);
			}
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

	public bool IsScreenPointOverMenu(Vector2 _pos) =>
		m_IsVisible && m_Root != null && m_Root.gameObject.activeInHierarchy &&
		RectTransformUtility.RectangleContainsScreenPoint(m_Root, _pos, null);
	#endregion

	#region Private Methods
	private static VehicleDisembarkMenuController EnsureInstance()
	{
		if (s_Instance != null)
			return s_Instance;
		if (!PlayModeSingleton.CanSpawn)
			return null;
		var go = new GameObject(nameof(VehicleDisembarkMenuController));
		s_Instance = go.AddComponent<VehicleDisembarkMenuController>();
		return s_Instance;
	}

	private void EnsureUi()
	{
		ResolveStyleFromGameUi();
		EnsureHostCanvasBinding();

		if (m_UiBuilt)
			return;

		GameObject rootGo = CreateRectObject("MenuRoot", transform);
		m_Root = rootGo.transform as RectTransform;
		m_Root.anchorMin = Vector2.zero;
		m_Root.anchorMax = Vector2.zero;
		m_Root.pivot = new Vector2(0f, 1f);
		m_CanvasGroup = rootGo.AddComponent<CanvasGroup>();

		Image bg = rootGo.AddComponent<Image>();
		ApplyPanelImageStyle(bg, m_Style.PanelBackground);

		VerticalLayoutGroup layout = rootGo.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(6, 6, 6, 6);
		layout.spacing = 3f;
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		ContentSizeFitter fitter = rootGo.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		m_UiBuilt = true;
		rootGo.SetActive(false);
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

	private void AddLabel(string _text)
	{
		GameObject item = CreateRectObject("Label", m_Root);
		RectTransform rect = item.transform as RectTransform;
		rect.sizeDelta = new Vector2(c_MenuMinWidth, c_ItemHeight);

		LayoutElement layoutElement = item.AddComponent<LayoutElement>();
		layoutElement.preferredHeight = c_ItemHeight;
		layoutElement.minWidth = c_MenuMinWidth;

		GameObject textGo = CreateRectObject("LabelText", item.transform);
		RectTransform textRect = textGo.transform as RectTransform;
		textRect.anchorMin = Vector2.zero;
		textRect.anchorMax = Vector2.one;
		textRect.offsetMin = new Vector2(12f, 0f);
		textRect.offsetMax = new Vector2(-8f, 0f);

		TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
		tmp.text = _text;
		tmp.fontSize = m_Style.FontSize;
		tmp.color = new Color(0.7f, 0.7f, 0.7f);
		tmp.alignment = TextAlignmentOptions.MidlineLeft;
		tmp.raycastTarget = false;
		if (m_Style.Font != null)
			tmp.font = m_Style.Font;
	}

	private void AddItem(string _label, MenuAction _action, RtsUnitMember _unit)
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
		tmp.color = m_Style.Text;
		tmp.alignment = TextAlignmentOptions.MidlineLeft;
		tmp.raycastTarget = false;
		if (m_Style.Font != null)
			tmp.font = m_Style.Font;

		m_Items.Add((rect, _action, _unit));
	}

	private void TryClickItemUnderCursor()
	{
		if (Mouse.current == null || m_Items.Count == 0)
			return;

		Vector2 mousePosition = Mouse.current.position.ReadValue();
		for (int i = 0; i < m_Items.Count; i++)
		{
			(RectTransform rect, MenuAction action, RtsUnitMember unit) = m_Items[i];
			if (rect == null)
				continue;
			if (!RectTransformUtility.RectangleContainsScreenPoint(rect, mousePosition, null))
				continue;

			VehicleController target = m_Target;
			HideImmediate();
			if (target != null)
				ActionClicked?.Invoke(action, target, unit);
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

	private static string ResolveName(RtsUnitMember _unit)
	{
		if (_unit.TryGetComponent(out UnitRosterDisplayState roster))
			return roster.DisplayName;
		return _unit.name;
	}

	private static string SeatLabel(VehicleSeatId _seat) => _seat switch
	{
		VehicleSeatId.Driver => "Водитель",
		VehicleSeatId.Commander => "Командир",
		VehicleSeatId.Gunner => "Стрелок",
		VehicleSeatId.RearLeft => "Задний Л",
		VehicleSeatId.RearCenter => "Задний Ц",
		VehicleSeatId.RearRight => "Задний П",
		VehicleSeatId.Litter1 => "Носилки 1",
		VehicleSeatId.Litter2 => "Носилки 2",
		_ => "Место"
	};
	#endregion
}
