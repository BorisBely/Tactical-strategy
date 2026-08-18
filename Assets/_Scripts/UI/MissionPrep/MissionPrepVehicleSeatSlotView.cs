using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Место машины в списке Mission Prep: заголовок слота + компактный drop / полная UnitCell при занятии.
/// В свёрнутой машине занятое место показывается строкой с именем юнита (как установленный мод).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepVehicleSeatSlotView : MonoBehaviour, IDropHandler, IPointerClickHandler
{
	#region Constants
	private const float c_DoubleClickMaxDelaySeconds = 0.35f;
	#endregion

	#region Private Fields
	private InventoryPanelSectionHeader m_SeatHeader;
	private RectTransform m_ContentHost;
	private LayoutElement m_RootLayout;
	private LayoutElement m_ContentHostLayout;
	private Image m_EmptyBackground;
	private TMP_Text m_EmptyLabel;
	private GameObject m_EmptyRoot;
	private GameObject m_CompactRoot;
	private Image m_CompactBackground;
	private TMP_Text m_CompactLabel;
	private MissionPrepUnitCellDrag m_CompactDrag;
	private MissionPrepUnitCellView m_UnitCellPrefab;
	private MissionPrepUnitCellView m_OccupiedCell;
	private VehicleController m_Vehicle;
	private VehicleSeatId m_SeatId;
	private MissionPrepVehicleAssignmentStore m_Assignments;
	private Action<MissionPrepUnitCellView> m_OnOccupiedCellClicked;
	private float m_LastLeftClickUnscaledTime = -1f;
	private readonly Color m_NormalEmptyColor = InventoryUiTheme.TitleBar;
	private readonly Color m_NormalCompactColor = MissionPrepInventoryUiColors.CellBackground;
	private readonly Color m_DropHighlightColor = InventoryUiTheme.UnitCellSelected;
	private bool m_DropHighlighted;
	private bool m_RosterExpanded;
	private Image m_HeaderBackground;
	private Color m_HeaderNormalColor;
	#endregion

	#region Public Properties
	public VehicleController Vehicle => m_Vehicle;
	public VehicleSeatId SeatId => m_SeatId;
	public MissionPrepUnitCellView OccupiedCell => m_OccupiedCell;
	public bool IsOccupied
	{
		get
		{
			return m_Assignments != null &&
			       m_Assignments.TryGetAssignedUnit(m_Vehicle, m_SeatId, out GameObject unitRoot) &&
			       unitRoot != null;
		}
	}
	#endregion

	#region Public Methods
	public void Configure(
		VehicleController _vehicle,
		VehicleSeatId _seatId,
		MissionPrepVehicleAssignmentStore _assignments,
		MissionPrepUnitCellView _unitCellPrefab,
		Action<MissionPrepUnitCellView> _onOccupiedCellClicked)
	{
		m_Vehicle = _vehicle;
		m_SeatId = _seatId;
		m_Assignments = _assignments;
		m_UnitCellPrefab = _unitCellPrefab;
		m_OnOccupiedCellClicked = _onOccupiedCellClicked;
		EnsureUi();
		RefreshSeatHeader();
		Refresh();
	}

	public void SetRosterExpanded(bool _expanded)
	{
		m_RosterExpanded = _expanded;
		Refresh();
	}

	public void Refresh()
	{
		EnsureUi();

		GameObject unitRoot = null;
		bool hasUnit = m_Assignments != null &&
		               m_Assignments.TryGetAssignedUnit(m_Vehicle, m_SeatId, out unitRoot) &&
		               unitRoot != null;

		if (!m_RosterExpanded)
		{
			if (hasUnit)
				ShowCollapsedOccupied(unitRoot);
			else
				ShowCollapsedEmpty();
			return;
		}

		gameObject.SetActive(true);
		SetCompactRowVisible(false);
		SetExpandedChromeVisible(true);

		if (hasUnit)
			ShowOccupied(unitRoot);
		else
			ShowEmpty();

		ApplyDropHighlightColors();
	}

	public bool TryAcceptUnit(GameObject _unitRoot)
	{
		if (_unitRoot == null || m_Assignments == null || m_Vehicle == null)
			return false;

		m_Assignments.Assign(m_Vehicle, m_SeatId, _unitRoot);
		return true;
	}

	/// <summary>Green drop-target highlight while a unit cell is dragged over this seat / its title.</summary>
	public void SetDropHighlight(bool _highlighted)
	{
		EnsureUi();
		m_DropHighlighted = _highlighted;
		ApplyDropHighlightColors();
	}

	public static void ClearAllDropHighlights()
	{
		MissionPrepVehicleSeatSlotView[] seats =
			FindObjectsByType<MissionPrepVehicleSeatSlotView>(FindObjectsInactive.Include);
		for (int i = 0; i < seats.Length; i++)
		{
			if (seats[i] != null && seats[i].m_DropHighlighted)
				seats[i].SetDropHighlight(false);
		}
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		GameObject unitRoot = ResolveUnitRoot(eventData);
		if (unitRoot == null)
			return;

		if (TryAcceptUnit(unitRoot))
		{
			MissionPrepUnitCellDrag drag = ResolveDrag(eventData);
			drag?.NotifyDropAccepted();
		}
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		TryHandleClearDoubleClick(eventData);
	}
	#endregion

	#region Private Methods
	private static MissionPrepUnitCellDrag ResolveDrag(PointerEventData eventData)
	{
		if (eventData?.pointerDrag == null)
			return null;

		MissionPrepUnitCellDrag drag = eventData.pointerDrag.GetComponent<MissionPrepUnitCellDrag>();
		if (drag == null)
			drag = eventData.pointerDrag.GetComponentInParent<MissionPrepUnitCellDrag>();
		if (drag == null)
			drag = eventData.pointerDrag.GetComponentInChildren<MissionPrepUnitCellDrag>(true);
		return drag;
	}

	private static GameObject ResolveUnitRoot(PointerEventData eventData)
	{
		MissionPrepUnitCellDrag drag = ResolveDrag(eventData);
		if (drag == null || drag.Cell == null || drag.Cell.IsVehicleCell)
			return null;

		return drag.UnitRoot;
	}

	private void EnsureUi()
	{
		if (m_ContentHost != null)
		{
			EnsureCompactRow();
			return;
		}

		MissionPrepVehicleSeatSlotUiBuilder.BuildBlock(
			gameObject,
			out m_SeatHeader,
			out m_ContentHost,
			out m_EmptyBackground,
			out m_EmptyLabel);

		m_EmptyRoot = m_EmptyBackground != null ? m_EmptyBackground.gameObject : null;
		m_RootLayout = GetComponent<LayoutElement>();
		m_ContentHostLayout = m_ContentHost != null ? m_ContentHost.GetComponent<LayoutElement>() : null;

		if (m_ContentHost != null && m_ContentHost.GetComponent<SeatDropRelay>() == null)
			m_ContentHost.gameObject.AddComponent<SeatDropRelay>().Initialize(this);

		if (m_EmptyRoot != null && m_EmptyRoot.GetComponent<SeatDropRelay>() == null)
			m_EmptyRoot.AddComponent<SeatDropRelay>().Initialize(this);

		if (m_SeatHeader != null)
		{
			m_SeatHeader.SetRaycastTarget(true);
			if (m_SeatHeader.GetComponent<SeatDropRelay>() == null)
				m_SeatHeader.gameObject.AddComponent<SeatDropRelay>().Initialize(this);

			m_HeaderBackground = m_SeatHeader.GetComponent<Image>();
			if (m_HeaderBackground != null)
				m_HeaderNormalColor = m_HeaderBackground.color;
		}

		EnsureContentHostRaycast();
		EnsureCompactRow();
	}

	private void EnsureCompactRow()
	{
		if (m_CompactRoot != null)
			return;

		m_CompactRoot = new GameObject("AssignedUnitRow", typeof(RectTransform));
		m_CompactRoot.transform.SetParent(transform, false);

		InventoryModificationSlotUiBuilder.BuildRow(
			m_CompactRoot,
			m_NormalCompactColor,
			out m_CompactBackground,
			out _,
			out _,
			out m_CompactLabel);

		if (m_CompactRoot.GetComponent<SeatDropRelay>() == null)
			m_CompactRoot.AddComponent<SeatDropRelay>().Initialize(this);

		if (m_CompactRoot.GetComponent<CompactRowClickRelay>() == null)
			m_CompactRoot.AddComponent<CompactRowClickRelay>().Initialize(this);

		m_CompactDrag = m_CompactRoot.GetComponent<MissionPrepUnitCellDrag>();
		if (m_CompactDrag == null)
			m_CompactDrag = m_CompactRoot.AddComponent<MissionPrepUnitCellDrag>();
		m_CompactDrag.BindVisualSource(m_CompactRoot);

		m_CompactRoot.SetActive(false);
	}

	private void EnsureContentHostRaycast()
	{
		if (m_ContentHost == null)
			return;

		Image hostImage = m_ContentHost.GetComponent<Image>();
		if (hostImage == null)
			hostImage = m_ContentHost.gameObject.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(hostImage);
		hostImage.color = new Color(1f, 1f, 1f, 0f);
		hostImage.raycastTarget = true;
	}

	private void RefreshSeatHeader()
	{
		if (m_SeatHeader == null)
			return;

		m_SeatHeader.Configure(
			MissionPrepVehicleSeatLabels.GetSeatLocalizationKey(m_SeatId),
			MissionPrepVehicleSeatLabels.GetSeatFallbackLabel(m_SeatId));
		m_SeatHeader.SetRaycastTarget(true);
		if (m_SeatHeader.GetComponent<SeatDropRelay>() == null)
			m_SeatHeader.gameObject.AddComponent<SeatDropRelay>().Initialize(this);

		m_HeaderBackground = m_SeatHeader.GetComponent<Image>();
		if (m_HeaderBackground != null)
		{
			m_HeaderNormalColor = InventoryUiTheme.TitleBar;
			if (!m_DropHighlighted)
				m_HeaderBackground.color = m_HeaderNormalColor;
		}
	}

	private void ShowCollapsedEmpty()
	{
		if (IsOccupiedCellDragSource())
			return;

		SetCompactRowVisible(false);
		gameObject.SetActive(false);
	}

	private void ShowCollapsedOccupied(GameObject _unitRoot)
	{
		gameObject.SetActive(true);
		ShowOccupied(_unitRoot);
		SetExpandedChromeVisible(false);
		SetCompactRowVisible(true);
		ApplyCollapsedOccupiedLayout();
		RefreshCompactLabel(_unitRoot);
		BindCompactDrag();
		ApplyDropHighlightColors();
		LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
	}

	private void ShowEmpty()
	{
		if (IsOccupiedCellDragSource())
		{
			if (m_OccupiedCell != null)
				m_OccupiedCell.gameObject.SetActive(false);
			if (m_EmptyRoot != null)
				m_EmptyRoot.SetActive(true);
			if (m_EmptyLabel != null)
				m_EmptyLabel.text = MissionPrepVehicleSeatLabels.GetEmptyLabel();
			SetContentHeight(MissionPrepVehicleSeatSlotUiBuilder.EmptyRowHeight);
			return;
		}

		DestroyOccupiedCell();
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(true);
		if (m_EmptyLabel != null)
			m_EmptyLabel.text = MissionPrepVehicleSeatLabels.GetEmptyLabel();

		SetContentHeight(MissionPrepVehicleSeatSlotUiBuilder.EmptyRowHeight);
	}

	private void ShowOccupied(GameObject _unitRoot)
	{
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(false);

		SetContentHeight(MissionPrepVehicleSeatSlotUiBuilder.CellHeight);

		if (m_OccupiedCell != null && m_OccupiedCell.BoundUnitRoot == _unitRoot)
		{
			m_OccupiedCell.gameObject.SetActive(true);
			UnitCellDisplayBinder.Apply(m_OccupiedCell, _unitRoot);
			m_OccupiedCell.SetInteractionEnabled(true);
			MissionPrepVehicleSeatSlotUiBuilder.LayoutOccupiedUnitCell(m_OccupiedCell.transform as RectTransform);
			return;
		}

		if (m_OccupiedCell != null && !IsOccupiedCellDragSource())
			DestroyOccupiedCell();

		if (m_OccupiedCell == null)
		{
			if (m_UnitCellPrefab == null || m_ContentHost == null)
				return;

			m_OccupiedCell = Instantiate(m_UnitCellPrefab, m_ContentHost);
			m_OccupiedCell.gameObject.name = $"SeatUnitCell_{m_SeatId}";
			MissionPrepVehicleSeatSlotUiBuilder.LayoutOccupiedUnitCell(m_OccupiedCell.transform as RectTransform);
			m_OccupiedCell.Clicked += HandleOccupiedCellClicked;

			Button clickButton = m_OccupiedCell.GetComponentInChildren<Button>(true);
			GameObject relayHost = clickButton != null ? clickButton.gameObject : m_OccupiedCell.gameObject;
			SeatClearClickRelay relay = relayHost.GetComponent<SeatClearClickRelay>();
			if (relay == null)
				relay = relayHost.AddComponent<SeatClearClickRelay>();
			relay.Initialize(this);

			if (relayHost.GetComponent<SeatDropRelay>() == null)
				relayHost.AddComponent<SeatDropRelay>().Initialize(this);
		}
		else
		{
			m_OccupiedCell.gameObject.SetActive(true);
			MissionPrepVehicleSeatSlotUiBuilder.LayoutOccupiedUnitCell(m_OccupiedCell.transform as RectTransform);
		}

		UnitCellDisplayBinder.Apply(m_OccupiedCell, _unitRoot);
		m_OccupiedCell.SetInteractionEnabled(true);
	}

	private void SetExpandedChromeVisible(bool _visible)
	{
		if (m_SeatHeader != null)
			m_SeatHeader.gameObject.SetActive(_visible);
		if (m_ContentHost != null)
			m_ContentHost.gameObject.SetActive(_visible);
	}

	private void SetCompactRowVisible(bool _visible)
	{
		if (m_CompactRoot != null && m_CompactRoot.activeSelf != _visible)
			m_CompactRoot.SetActive(_visible);
	}

	private void ApplyCollapsedOccupiedLayout()
	{
		float height = InventoryModificationSlotUiBuilder.RowHeight;
		if (m_RootLayout != null)
		{
			m_RootLayout.minHeight = height;
			m_RootLayout.preferredHeight = height;
			m_RootLayout.flexibleHeight = 0f;
		}

		RectTransform rootRt = transform as RectTransform;
		if (rootRt != null)
			rootRt.sizeDelta = new Vector2(MissionPrepVehicleSeatSlotUiBuilder.CellWidth, height);
	}

	private void RefreshCompactLabel(GameObject _unitRoot)
	{
		if (m_CompactLabel == null)
			return;

		string seat = MissionPrepVehicleSeatLabels.GetSeatLabel(m_SeatId);
		string unitName = UnitCellDisplayBinder.ResolveUnitName(_unitRoot);
		m_CompactLabel.text = string.IsNullOrWhiteSpace(unitName)
			? seat
			: $"{seat} {unitName}";
	}

	private void BindCompactDrag()
	{
		if (m_CompactDrag == null || m_OccupiedCell == null)
			return;

		m_CompactDrag.BindCell(m_OccupiedCell);
		m_CompactDrag.BindVisualSource(m_CompactRoot);
		m_CompactDrag.enabled = m_OccupiedCell.BoundUnitRoot != null;
	}

	private void ApplyDropHighlightColors()
	{
		Color compactColor = m_DropHighlighted ? m_DropHighlightColor : m_NormalCompactColor;
		if (m_CompactBackground != null)
			m_CompactBackground.color = compactColor;

		if (m_EmptyBackground != null)
			m_EmptyBackground.color = m_DropHighlighted ? m_DropHighlightColor : m_NormalEmptyColor;

		if (m_HeaderBackground != null)
			m_HeaderBackground.color = m_DropHighlighted ? m_DropHighlightColor : m_HeaderNormalColor;

		if (m_OccupiedCell != null)
			m_OccupiedCell.SetDropTargetHighlight(m_DropHighlighted && m_RosterExpanded);
	}

	private bool IsOccupiedCellDragSource()
	{
		if (m_OccupiedCell == null)
			return false;

		MissionPrepUnitCellDrag occupiedDrag = m_OccupiedCell.GetComponentInChildren<MissionPrepUnitCellDrag>(true);
		if (occupiedDrag != null && occupiedDrag.IsDragging)
			return true;

		return m_CompactDrag != null && m_CompactDrag.IsDragging;
	}

	private void SetContentHeight(float _contentHeight)
	{
		MissionPrepVehicleSeatSlotUiBuilder.ApplyContentHeight(m_ContentHostLayout, _contentHeight);
		MissionPrepVehicleSeatSlotUiBuilder.ApplyRootHeight(m_RootLayout, _contentHeight);

		RectTransform rootRt = transform as RectTransform;
		if (rootRt != null)
			rootRt.sizeDelta = new Vector2(
				MissionPrepVehicleSeatSlotUiBuilder.CellWidth,
				InventoryUiTheme.SectionHeaderHeight + _contentHeight);
	}

	private void DestroyOccupiedCell()
	{
		if (m_OccupiedCell == null)
			return;

		m_OccupiedCell.Clicked -= HandleOccupiedCellClicked;
		Destroy(m_OccupiedCell.gameObject);
		m_OccupiedCell = null;
	}

	private void HandleOccupiedCellClicked(MissionPrepUnitCellView _cell)
	{
		m_OnOccupiedCellClicked?.Invoke(_cell);
	}

	internal void TryHandleClearDoubleClick(PointerEventData eventData)
	{
		if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		if (m_Assignments == null || m_Vehicle == null)
			return;

		m_Assignments.ClearSeat(m_Vehicle, m_SeatId);
	}

	internal void TryHandleCompactRowClick(PointerEventData eventData)
	{
		if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
			return;

		if (m_CompactDrag != null && m_CompactDrag.ConsumeSuppressedClick())
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (unityReportsDouble || timedDouble)
		{
			if (m_Assignments != null && m_Vehicle != null)
				m_Assignments.ClearSeat(m_Vehicle, m_SeatId);
			return;
		}

		if (m_OccupiedCell != null)
			HandleOccupiedCellClicked(m_OccupiedCell);
	}

	private void OnDestroy()
	{
		DestroyOccupiedCell();
	}
	#endregion

	private sealed class SeatClearClickRelay : MonoBehaviour, IPointerClickHandler
	{
		private MissionPrepVehicleSeatSlotView m_Owner;

		public void Initialize(MissionPrepVehicleSeatSlotView _owner)
		{
			m_Owner = _owner;
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			m_Owner?.TryHandleClearDoubleClick(eventData);
		}
	}

	private sealed class CompactRowClickRelay : MonoBehaviour, IPointerClickHandler
	{
		private MissionPrepVehicleSeatSlotView m_Owner;

		public void Initialize(MissionPrepVehicleSeatSlotView _owner)
		{
			m_Owner = _owner;
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			m_Owner?.TryHandleCompactRowClick(eventData);
		}
	}

	private sealed class SeatDropRelay : MonoBehaviour, IDropHandler
	{
		private MissionPrepVehicleSeatSlotView m_Owner;

		public void Initialize(MissionPrepVehicleSeatSlotView _owner)
		{
			m_Owner = _owner;
		}

		public void OnDrop(PointerEventData eventData)
		{
			m_Owner?.OnDrop(eventData);
		}
	}
}
