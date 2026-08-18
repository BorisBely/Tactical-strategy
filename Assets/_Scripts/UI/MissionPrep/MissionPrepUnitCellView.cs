using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One row in the pre-mission unit list. Data binding is intentionally left for later — use inspector placeholders.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitCellView : MonoBehaviour, IScrollHandler
{
	#region Events
	public event Action<MissionPrepUnitCellView> Clicked;
	#endregion

	#region Private Fields
	[SerializeField] private Button m_ClickArea;
	[SerializeField] private TextMeshProUGUI m_UnitRankText;
	[SerializeField] private TextMeshProUGUI m_UnitNameText;
	[SerializeField] private TextMeshProUGUI m_UnitPresetText;
	[SerializeField] private TextMeshProUGUI m_HealthStatusText;
	[SerializeField] private TextMeshProUGUI m_ArmorStatusText;
	[SerializeField] private TextMeshProUGUI m_VehicleAssignmentText;

	[Header("Выделение строки")]
	[SerializeField] private Graphic m_SelectionBackground;
	[SerializeField] private Color m_NormalBackgroundColor = new Color(0.16470589f, 0.16470589f, 0.16470589f, 1f);
	[SerializeField] private Color m_HoverBackgroundColor = new Color(0.20392157f, 0.20392157f, 0.20392157f, 1f);
	[SerializeField] private Color m_SelectedBackgroundColor = new Color(0.27f, 0.36f, 0.31f, 1f);
	[SerializeField] private bool m_UseInventoryUiTheme = true;

	private bool m_IsHovered;
	private bool m_InteractionEnabled = true;
	private bool m_DragSourcePlaceholder;
	private string m_VehicleAssignmentCaption = string.Empty;
	private bool m_RightStatusPaddingApplied;
	/// <summary>null = обычная ячейка (техника / место); true = в машине; false = вне машины.</summary>
	private bool? m_SquadBoarded;
	private bool m_DropTargetHighlighted;
	#endregion

	#region Public Properties
	public TextMeshProUGUI UnitRankText => m_UnitRankText;
	public TextMeshProUGUI UnitNameText => m_UnitNameText;
	public TextMeshProUGUI UnitPresetText => m_UnitPresetText;
	public TextMeshProUGUI HealthStatusText => m_HealthStatusText;
	public TextMeshProUGUI ArmorStatusText => m_ArmorStatusText;
	public GameObject BoundUnitRoot { get; private set; }
	public VehicleController BoundVehicle { get; private set; }
	public bool IsVehicleCell => BoundVehicle != null;
	public bool IsInsideSeatSlot => GetComponentInParent<MissionPrepVehicleSeatSlotView>() != null;
	public bool IsSelected { get; private set; }
	public bool InteractionEnabled => m_InteractionEnabled;
	public string VehicleAssignmentCaption => m_VehicleAssignmentCaption;
	#endregion

	#region Public Methods
	public void BindToUnit(GameObject _unitRoot, string _displayName)
	{
		BoundVehicle = null;
		BoundUnitRoot = _unitRoot;

		if (m_UnitNameText != null)
			m_UnitNameText.text = _displayName ?? string.Empty;

		EnsureUnitDrag();
	}

	public void BindToVehicle(VehicleController _vehicle, string _displayName)
	{
		BoundUnitRoot = null;
		BoundVehicle = _vehicle;

		if (m_UnitNameText != null)
			m_UnitNameText.text = _displayName ?? string.Empty;

		EnsureUnitDrag();
	}

	public void SetPresetDisplayName(string _presetName)
	{
		if (m_UnitPresetText != null)
			m_UnitPresetText.text = _presetName ?? string.Empty;
	}

	public void SetRankDisplayName(string _rankName)
	{
		if (m_UnitRankText != null)
			m_UnitRankText.text = _rankName ?? string.Empty;
	}

	public void SetHealthStatusText(string _healthStatusText)
	{
		if (m_HealthStatusText != null)
			m_HealthStatusText.text = _healthStatusText ?? string.Empty;
	}

	public void SetArmorStatusText(string _armorStatusText)
	{
		if (m_ArmorStatusText == null)
			return;

		string status = _armorStatusText ?? string.Empty;
		m_ArmorStatusText.text = status;
		m_ArmorStatusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(status));
	}

	public void SetVehicleAssignmentCaption(string _caption)
	{
		m_VehicleAssignmentCaption = _caption ?? string.Empty;
		bool hasCaption = !string.IsNullOrWhiteSpace(m_VehicleAssignmentCaption);
		m_SquadBoarded = hasCaption;
		EnsureVehicleAssignmentText();
		if (m_VehicleAssignmentText != null)
		{
			m_VehicleAssignmentText.text = hasCaption ? m_VehicleAssignmentCaption : string.Empty;
			m_VehicleAssignmentText.gameObject.SetActive(hasCaption);

			// Keep armor on its own line above the assignment caption.
			if (m_ArmorStatusText != null)
				m_ArmorStatusText.verticalAlignment = hasCaption
					? VerticalAlignmentOptions.Top
					: VerticalAlignmentOptions.Middle;
		}

		ApplyBackgroundVisual();
	}

	public void SetSelected(bool _selected)
	{
		IsSelected = _selected;
		ApplyBackgroundVisual();
	}

	/// <summary>Temporary green tint while this occupied seat cell is a drag drop target.</summary>
	public void SetDropTargetHighlight(bool _highlighted)
	{
		if (m_DropTargetHighlighted == _highlighted)
			return;

		m_DropTargetHighlighted = _highlighted;
		ApplyBackgroundVisual();
	}

	/// <summary>Dark slot placeholder left behind while this cell is being dragged.</summary>
	public void SetDragSourcePlaceholder(bool _active)
	{
		m_DragSourcePlaceholder = _active;
		ApplyBackgroundVisual();

		// Dim text/icons so the hole reads as an empty dark slot, not a white/ghost cell.
		CanvasGroup contentGroup = GetComponent<CanvasGroup>();
		if (contentGroup != null && _active)
			contentGroup.alpha = 1f;

		SetContentVisible(!_active);
	}

	private void SetContentVisible(bool _visible)
	{
		if (m_UnitRankText != null)
			m_UnitRankText.enabled = _visible;
		if (m_UnitNameText != null)
			m_UnitNameText.enabled = _visible;
		if (m_UnitPresetText != null)
			m_UnitPresetText.enabled = _visible;
		if (m_HealthStatusText != null)
			m_HealthStatusText.enabled = _visible;
		if (m_ArmorStatusText != null)
			m_ArmorStatusText.enabled = _visible;
		if (m_VehicleAssignmentText != null)
			m_VehicleAssignmentText.enabled = _visible;
	}

	public void SetInteractionEnabled(bool _enabled)
	{
		m_InteractionEnabled = _enabled;
		if (m_ClickArea != null)
			m_ClickArea.interactable = _enabled;

		if (!_enabled)
			SetHovered(false);
		else
			ApplyBackgroundVisual();
	}

	public void ClearBinding()
	{
		SetSelected(false);
		SetHovered(false);
		m_DropTargetHighlighted = false;
		BoundUnitRoot = null;
		BoundVehicle = null;
		m_VehicleAssignmentCaption = string.Empty;
		m_SquadBoarded = null;
		if (m_UnitNameText != null)
			m_UnitNameText.text = string.Empty;
		SetPresetDisplayName(string.Empty);
		SetRankDisplayName(string.Empty);
		SetHealthStatusText(string.Empty);
		if (m_ArmorStatusText != null)
		{
			m_ArmorStatusText.text = string.Empty;
			m_ArmorStatusText.gameObject.SetActive(false);
		}
		if (m_VehicleAssignmentText != null)
		{
			m_VehicleAssignmentText.text = string.Empty;
			m_VehicleAssignmentText.gameObject.SetActive(false);
		}

		ApplyBackgroundVisual();
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_SelectionBackground == null)
		{
			Transform bg = transform.Find("Image (2)");
			if (bg != null)
				bg.TryGetComponent(out m_SelectionBackground);
		}

		if (m_SelectionBackground == null && m_ClickArea != null)
		{
			// Prefer Image (2); Button's Graphic is a disabled white Image — avoid using it.
			Graphic[] graphics = m_ClickArea.GetComponents<Graphic>();
			for (int i = 0; i < graphics.Length; i++)
			{
				if (graphics[i] != null && graphics[i].enabled)
				{
					m_SelectionBackground = graphics[i];
					break;
				}
			}
		}

		if (m_SelectionBackground is Image selectionImage)
			InventorySlotUiUtility.EnsureImageCanRenderSolidColor(selectionImage);

		ConfigureClickAreaRaycast();

		if (m_HealthStatusText == null)
		{
			Transform healthTextTransform = transform.Find("Button/HealthText");
			if (healthTextTransform != null)
				healthTextTransform.TryGetComponent(out m_HealthStatusText);
		}

		if (m_ArmorStatusText == null)
		{
			Transform armorTextTransform = transform.Find("Button/ArmorText");
			if (armorTextTransform != null)
				armorTextTransform.TryGetComponent(out m_ArmorStatusText);
		}

		EnsureVehicleAssignmentText();
		ApplyRightStatusPadding();
		DisableNestedScrollTraps();
		EnsureListLayout();
		ApplyThemeColors();
		ApplyBackgroundVisual();
		EnsureHoverRelay();
	}

	private void OnEnable()
	{
		if (m_ClickArea != null)
			m_ClickArea.onClick.AddListener(HandleClicked);
	}

	private void OnDisable()
	{
		if (m_ClickArea != null)
			m_ClickArea.onClick.RemoveListener(HandleClicked);
	}
	#endregion

	#region Private Methods
	private void HandleClicked()
	{
		if (!m_InteractionEnabled)
			return;

		if (IsVehicleCell && !IsInsideSeatSlot)
		{
			MissionPrepUnitCellDrag drag = GetComponentInChildren<MissionPrepUnitCellDrag>(true);
			if (drag != null && drag.ConsumeSuppressedClick())
				return;

			GetComponent<MissionPrepVehicleRosterBlock>()?.ToggleExpanded();
			return;
		}

		Clicked?.Invoke(this);
	}

	internal void SetHovered(bool _hovered)
	{
		if (m_IsHovered == _hovered)
			return;

		m_IsHovered = _hovered;
		ApplyBackgroundVisual();
	}

	public void OnScroll(PointerEventData _eventData)
	{
		if (_eventData == null)
			return;

		Transform parent = transform.parent;
		ScrollRect parentScroll = parent != null
			? parent.GetComponentInParent<ScrollRect>()
			: GetComponentInParent<ScrollRect>();
		if (parentScroll != null && parentScroll.enabled)
			parentScroll.OnScroll(_eventData);
	}

	private void ApplyThemeColors()
	{
		if (!m_UseInventoryUiTheme)
			return;

		m_NormalBackgroundColor = InventoryUiTheme.UnitCellNormal;
		m_HoverBackgroundColor = InventoryUiTheme.UnitCellHover;
		m_SelectedBackgroundColor = InventoryUiTheme.UnitCellSelected;

		ApplyBottomDivider();
	}

	private void DisableNestedScrollTraps()
	{
		// Leftover inventory ScrollRect inside UnitCell.prefab eats the mouse wheel
		// before PrepUnitListScroll can handle it.
		Transform leftover = transform.Find("Button/Scroll View");
		if (leftover != null)
			leftover.gameObject.SetActive(false);

		ScrollRect[] nested = GetComponentsInChildren<ScrollRect>(true);
		for (int i = 0; i < nested.Length; i++)
		{
			if (nested[i] != null)
				nested[i].enabled = false;
		}
	}

	private void EnsureListLayout()
	{
		LayoutElement layout = GetComponent<LayoutElement>();
		if (layout == null)
			layout = gameObject.AddComponent<LayoutElement>();
		layout.minHeight = InventoryUiTheme.UnitCellHeight;
		layout.preferredHeight = InventoryUiTheme.UnitCellHeight;
		layout.flexibleHeight = 0f;
		layout.minWidth = InventoryUiTheme.UnitCellWidth;
		layout.preferredWidth = InventoryUiTheme.UnitCellWidth;
		layout.flexibleWidth = 0f;
		layout.ignoreLayout = false;

		// Prefab geometry: center pivot 400×80. Do not retarget anchors — Button and Image (2)
		// are authored around the center. Nested layout groups also must not own this cell.
		RectTransform rt = transform as RectTransform;
		if (rt == null)
			return;

		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.zero;
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.sizeDelta = new Vector2(InventoryUiTheme.UnitCellWidth, InventoryUiTheme.UnitCellHeight);
		rt.localScale = Vector3.one;
	}

	/// <summary>
	/// Right-side status text sits closer to center by the vertical scrollbar width.
	/// </summary>
	private void ApplyRightStatusPadding()
	{
		if (m_RightStatusPaddingApplied)
			return;

		float pad = InventoryUiScrollbarUtility.ScrollbarWidth + InventoryUiScrollbarUtility.ScrollbarSpacing;
		InsetRight(m_HealthStatusText, pad);
		InsetRight(m_ArmorStatusText, pad);
		InsetRight(m_VehicleAssignmentText, pad);
		m_RightStatusPaddingApplied = true;
	}

	private static void InsetRight(TextMeshProUGUI _text, float _pixels)
	{
		if (_text == null || _pixels <= 0f)
			return;

		RectTransform rt = _text.rectTransform;
		Vector2 offsetMax = rt.offsetMax;
		offsetMax.x -= _pixels;
		rt.offsetMax = offsetMax;
	}

	private void EnsureVehicleAssignmentText()
	{
		if (m_VehicleAssignmentText != null)
			return;

		Transform existing = transform.Find("Button/AssignmentText");
		if (existing != null && existing.TryGetComponent(out m_VehicleAssignmentText))
		{
			m_VehicleAssignmentText.gameObject.SetActive(false);
			return;
		}

		if (m_ArmorStatusText == null)
			return;

		Transform button = m_ArmorStatusText.transform.parent;
		if (button == null)
			return;

		GameObject go = new GameObject("AssignmentText", typeof(RectTransform));
		go.transform.SetParent(button, false);
		go.transform.SetSiblingIndex(m_ArmorStatusText.transform.GetSiblingIndex() + 1);

		RectTransform rt = go.transform as RectTransform;
		RectTransform armorRt = m_ArmorStatusText.rectTransform;
		rt.anchorMin = armorRt.anchorMin;
		rt.anchorMax = armorRt.anchorMax;
		rt.pivot = armorRt.pivot;
		// One line below armor in the right status column.
		rt.anchoredPosition = new Vector2(armorRt.anchoredPosition.x, armorRt.anchoredPosition.y - 14f);
		rt.sizeDelta = new Vector2(armorRt.sizeDelta.x, Mathf.Min(armorRt.sizeDelta.y, -52f));

		TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
		tmp.font = m_ArmorStatusText.font;
		tmp.fontSharedMaterial = m_ArmorStatusText.fontSharedMaterial;
		tmp.fontSize = Mathf.Max(11f, m_ArmorStatusText.fontSize - 1f);
		tmp.alignment = TextAlignmentOptions.MidlineRight;
		tmp.color = InventoryUiTheme.SectionHeaderText;
		tmp.raycastTarget = false;
		tmp.textWrappingMode = TextWrappingModes.NoWrap;
		tmp.overflowMode = TextOverflowModes.Ellipsis;
		tmp.text = string.Empty;
		go.SetActive(false);
		m_VehicleAssignmentText = tmp;

		if (m_RightStatusPaddingApplied)
		{
			InsetRight(
				m_VehicleAssignmentText,
				InventoryUiScrollbarUtility.ScrollbarWidth + InventoryUiScrollbarUtility.ScrollbarSpacing);
		}
	}

	private void ApplyBottomDivider()
	{
		// Prefab path is Button/Image (not a direct child of UnitCell root).
		Transform divider = transform.Find("Button/Image");
		if (divider == null)
			divider = transform.Find("Image");
		if (divider == null || !divider.TryGetComponent(out Image dividerImage))
			return;

		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(dividerImage);
		dividerImage.type = Image.Type.Simple;
		dividerImage.color = InventoryUiTheme.Divider;
		dividerImage.raycastTarget = false;

		RectTransform dividerRt = divider as RectTransform;
		if (dividerRt == null)
			return;

		// Prefab authored height is 3px (inventory slots use theme 1px).
		const float dividerHeight = 3f;
		dividerRt.anchorMin = new Vector2(0f, 0f);
		dividerRt.anchorMax = new Vector2(1f, 0f);
		dividerRt.pivot = new Vector2(0.5f, 0.5f);
		dividerRt.anchoredPosition = new Vector2(0f, dividerHeight * 0.5f);
		dividerRt.sizeDelta = new Vector2(0f, dividerHeight);
		dividerRt.localScale = Vector3.one;
	}

	private void ConfigureClickAreaRaycast()
	{
		if (m_ClickArea == null)
			return;

		// Keep an invisible Image on the Button for hover/click raycasts.
		// Disabling it broke cell highlight; ColorTint on a white sprite caused the white flash.
		Image hitImage = m_ClickArea.GetComponent<Image>();
		if (hitImage == null)
			hitImage = m_ClickArea.gameObject.AddComponent<Image>();

		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(hitImage);
		hitImage.color = new Color(1f, 1f, 1f, 0f);
		hitImage.raycastTarget = true;
		hitImage.enabled = true;

		m_ClickArea.targetGraphic = hitImage;
		m_ClickArea.transition = Selectable.Transition.None;

		if (m_SelectionBackground != null)
			m_SelectionBackground.raycastTarget = false;
	}

	private void ApplyBackgroundVisual()
	{
		if (m_SelectionBackground == null)
			return;

		if (m_DragSourcePlaceholder)
			m_SelectionBackground.color = InventoryUiTheme.TitleBar;
		else if (m_DropTargetHighlighted)
			m_SelectionBackground.color = InventoryUiTheme.UnitCellSelected;
		else if (IsSelected && !IsVehicleCell)
			m_SelectionBackground.color = m_SelectedBackgroundColor;
		else if (IsSelected)
			m_SelectionBackground.color = m_HoverBackgroundColor;
		else if (m_SquadBoarded == true)
			m_SelectionBackground.color = m_IsHovered && m_InteractionEnabled
				? InventoryUiTheme.UnitCellAssignedHover
				: InventoryUiTheme.UnitCellAssigned;
		else if (m_SquadBoarded == false)
			m_SelectionBackground.color = m_IsHovered && m_InteractionEnabled
				? InventoryUiTheme.UnitCellUnassignedHover
				: InventoryUiTheme.UnitCellUnassigned;
		else if (m_IsHovered && m_InteractionEnabled)
			m_SelectionBackground.color = m_HoverBackgroundColor;
		else
			m_SelectionBackground.color = m_NormalBackgroundColor;
	}

	private void EnsureHoverRelay()
	{
		if (m_ClickArea == null)
			return;

		if (m_ClickArea.GetComponent<UnitCellHoverRelay>() != null)
			return;

		m_ClickArea.gameObject.AddComponent<UnitCellHoverRelay>().Initialize(this);
	}

	private void EnsureUnitDrag()
	{
		CanvasGroup cellGroup = GetComponent<CanvasGroup>();
		if (cellGroup != null)
			cellGroup.blocksRaycasts = true;

		GameObject host = m_ClickArea != null ? m_ClickArea.gameObject : gameObject;
		MissionPrepUnitCellDrag drag = host.GetComponent<MissionPrepUnitCellDrag>();
		if (drag == null)
			drag = host.AddComponent<MissionPrepUnitCellDrag>();
		drag.BindCell(this);
		drag.enabled = BoundUnitRoot != null || BoundVehicle != null;
	}
	#endregion

	private sealed class UnitCellHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		#region Private Fields
		private MissionPrepUnitCellView m_Owner;
		#endregion

		#region Public Methods
		public void Initialize(MissionPrepUnitCellView _owner)
		{
			m_Owner = _owner;
		}
		#endregion

		#region Event Handlers
		public void OnPointerEnter(PointerEventData _eventData)
		{
			if (_eventData == null || _eventData.dragging)
				return;

			m_Owner?.SetHovered(true);
		}

		public void OnPointerExit(PointerEventData _eventData)
		{
			m_Owner?.SetHovered(false);
		}
		#endregion
	}
}
