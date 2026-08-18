using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Drag юнита или машины Mission Prep: колонка ↔ колонка, юнит ↔ место машины.</summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitCellDrag : MonoBehaviour,
	IBeginDragHandler,
	IDragHandler,
	IEndDragHandler,
	IInitializePotentialDragHandler,
	IPointerUpHandler
{
	#region Private Fields
	private const int c_DragVisualSortingOrder = 32000;

	[SerializeField] private MissionPrepUnitCellView m_Cell;
	private RectTransform m_DragVisual;
	private Canvas m_Canvas;
	private ScrollRect m_ParentScroll;
	private bool m_ScrollWasEnabled = true;
	private bool m_Dragging;
	private bool m_DropHandled;
	private bool m_SuppressNextClick;
	private bool m_ScrollDisabledForGesture;
	private Vector2 m_PotentialDragPosition;
	private GameObject m_VisualSource;
	private CanvasGroup m_SourceCanvasGroup;
	private float m_SourceAlpha = 1f;
	private bool m_SourceBlocksRaycasts = true;
	private CanvasGroup m_VisualSourceCanvasGroup;
	private float m_VisualSourceAlpha = 1f;
	private bool m_VisualSourceBlocksRaycasts = true;
	private MissionPrepVehicleSeatSlotView m_HighlightedSeat;
	private MissionPrepVehicleRosterBlock m_HighlightedVehicle;
	private MissionPrepRosterColumnDropZone m_HighlightedColumn;
	private static readonly List<RaycastResult> s_RaycastBuffer = new List<RaycastResult>(32);
	#endregion

	#region Public Properties
	public GameObject UnitRoot => m_Cell != null ? m_Cell.BoundUnitRoot : null;
	public MissionPrepUnitCellView Cell => m_Cell;
	public bool IsDragging => m_Dragging;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Cell == null)
			m_Cell = GetComponent<MissionPrepUnitCellView>() ?? GetComponentInParent<MissionPrepUnitCellView>();
	}

	private void OnDisable()
	{
		// If the seat cell is destroyed mid-drag, still restore scroll.
		if (m_Dragging)
			CleanupDrag(null);
	}
	#endregion

	#region Public Methods
	public void BindCell(MissionPrepUnitCellView _cell)
	{
		m_Cell = _cell;
		RestoreCellRaycasts();
	}

	public void BindVisualSource(GameObject _visualSource)
	{
		m_VisualSource = _visualSource;
	}

	public void NotifyDropAccepted()
	{
		m_DropHandled = true;
	}

	public bool ConsumeSuppressedClick()
	{
		bool suppress = m_SuppressNextClick;
		m_SuppressNextClick = false;
		return suppress;
	}
	#endregion

	#region Event Handlers
	public void OnInitializePotentialDrag(PointerEventData eventData)
	{
		if (eventData == null)
			return;

		// Keep the default threshold so a click can expand a vehicle without starting a drag.
		eventData.useDragThreshold = true;
		m_SuppressNextClick = false;
		m_PotentialDragPosition = eventData.position;
		DisableParentScroll();
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		m_Dragging = false;
		m_DropHandled = false;

		if (m_Cell == null)
			m_Cell = GetComponent<MissionPrepUnitCellView>() ?? GetComponentInParent<MissionPrepUnitCellView>();

		bool canDragUnit = m_Cell != null && m_Cell.BoundUnitRoot != null && m_Cell.BoundVehicle == null;
		bool canDragVehicle = m_Cell != null && m_Cell.IsVehicleCell && !m_Cell.IsInsideSeatSlot;
		if (!canDragUnit && !canDragVehicle)
		{
			RestoreParentScroll();
			return;
		}

		m_Canvas = GetComponentInParent<Canvas>();
		if (m_Canvas != null && m_Canvas.rootCanvas != null)
			m_Canvas = m_Canvas.rootCanvas;
		if (m_Canvas == null)
		{
			RestoreParentScroll();
			return;
		}

		DisableParentScroll();
		DestroyDragVisual();
		m_DragVisual = CreateFullCellDragVisual();
		if (m_DragVisual == null)
		{
			RestoreParentScroll();
			return;
		}

		HideSourceCellPlaceholder();

		m_Dragging = true;
		m_SuppressNextClick = true;
		eventData.pointerDrag = gameObject;
		UpdateDragVisual(eventData);
		UpdateDropHighlights(eventData);
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!m_Dragging)
			return;
		UpdateDragVisual(eventData);
		UpdateDropHighlights(eventData);
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		CleanupDrag(eventData);
	}

	public void OnPointerUp(PointerEventData eventData)
	{
		if (m_Dragging)
			return;

		RestoreParentScroll();
	}
	#endregion

	#region Private Methods
	private void CleanupDrag(PointerEventData eventData)
	{
		if (m_Dragging && !m_DropHandled && eventData != null)
			TryResolveDropByRaycast(eventData);

		ClearSeatDropHighlight();
		ClearVehicleDropHighlight();
		ClearColumnDropHighlight();
		MissionPrepVehicleSeatSlotView.ClearAllDropHighlights();
		MissionPrepRosterColumnDropZone.ClearAllHighlights();
		MissionPrepUnitUnassignDropZone.ClearAllHighlights();
		RestoreSourceCellPlaceholder();
		DestroyDragVisual();
		RestoreParentScroll();
		m_Dragging = false;
		m_DropHandled = false;

		if (m_SuppressNextClick && eventData != null &&
		    (eventData.position - m_PotentialDragPosition).sqrMagnitude < 100f)
			m_SuppressNextClick = false;
	}

	private void HideSourceCellPlaceholder()
	{
		if (m_Cell == null)
			return;

		GameObject placeholder = m_VisualSource != null ? m_VisualSource : m_Cell.gameObject;
		m_VisualSourceCanvasGroup = GetOrAddCanvasGroup(placeholder);
		m_SourceCanvasGroup = GetOrAddCanvasGroup(m_Cell.gameObject);

		m_VisualSourceAlpha = m_VisualSourceCanvasGroup.alpha;
		m_VisualSourceBlocksRaycasts = m_VisualSourceCanvasGroup.blocksRaycasts;
		m_SourceAlpha = m_SourceCanvasGroup.alpha;
		m_SourceBlocksRaycasts = m_SourceCanvasGroup.blocksRaycasts;

		m_VisualSourceCanvasGroup.blocksRaycasts = false;
		m_SourceCanvasGroup.blocksRaycasts = false;
		m_SourceCanvasGroup.alpha = 1f;

		Button[] buttons = m_Cell.GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			if (buttons[i] == null)
				continue;
			buttons[i].transition = Selectable.Transition.None;
			buttons[i].interactable = false;
		}

		m_Cell.SetHovered(false);
		m_Cell.SetDragSourcePlaceholder(true);
		if (placeholder != m_Cell.gameObject)
			m_VisualSourceCanvasGroup.alpha = 0.45f;
	}

	private void RestoreSourceCellPlaceholder()
	{
		if (m_VisualSourceCanvasGroup != null)
		{
			m_VisualSourceCanvasGroup.alpha = m_VisualSourceAlpha;
			m_VisualSourceCanvasGroup.blocksRaycasts = m_VisualSourceBlocksRaycasts;
			m_VisualSourceCanvasGroup = null;
		}

		if (m_SourceCanvasGroup != null)
		{
			m_SourceCanvasGroup.alpha = m_SourceAlpha;
			m_SourceCanvasGroup.blocksRaycasts = m_SourceBlocksRaycasts;
			m_SourceCanvasGroup = null;
		}

		RestoreCellRaycasts();

		if (m_Cell == null)
			return;

		m_Cell.SetDragSourcePlaceholder(false);

		Button[] buttons = m_Cell.GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			if (buttons[i] == null)
				continue;
			buttons[i].transition = Selectable.Transition.None;
			buttons[i].interactable = m_Cell.InteractionEnabled;
		}
	}

	private void RestoreCellRaycasts()
	{
		if (m_Cell == null)
			return;

		CanvasGroup cellGroup = m_Cell.GetComponent<CanvasGroup>();
		if (cellGroup != null)
			cellGroup.blocksRaycasts = true;

		if (m_VisualSource == null || m_VisualSource == m_Cell.gameObject)
			return;

		CanvasGroup visualGroup = m_VisualSource.GetComponent<CanvasGroup>();
		if (visualGroup != null)
			visualGroup.blocksRaycasts = true;
	}

	private static CanvasGroup GetOrAddCanvasGroup(GameObject _host)
	{
		CanvasGroup group = _host.GetComponent<CanvasGroup>();
		if (group == null)
			group = _host.AddComponent<CanvasGroup>();
		return group;
	}

	private void UpdateDropHighlights(PointerEventData eventData)
	{
		if (m_Cell != null && m_Cell.BoundUnitRoot != null && !m_Cell.IsVehicleCell)
		{
			UpdateSeatDropHighlight(eventData);
			UpdateVehicleDropHighlight(eventData);
		}
		else
		{
			ClearSeatDropHighlight();
			ClearVehicleDropHighlight();
		}

		UpdateColumnDropHighlight(eventData);
	}

	private void UpdateSeatDropHighlight(PointerEventData eventData)
	{
		MissionPrepVehicleSeatSlotView seat = ResolveSeatUnderPointer(eventData);
		if (seat == m_HighlightedSeat)
			return;

		if (m_HighlightedSeat != null)
			m_HighlightedSeat.SetDropHighlight(false);

		m_HighlightedSeat = seat;
		if (m_HighlightedSeat != null)
			m_HighlightedSeat.SetDropHighlight(true);
	}

	private void ClearSeatDropHighlight()
	{
		if (m_HighlightedSeat != null)
		{
			m_HighlightedSeat.SetDropHighlight(false);
			m_HighlightedSeat = null;
		}
	}

	private void UpdateVehicleDropHighlight(PointerEventData eventData)
	{
		MissionPrepVehicleRosterBlock vehicle = m_HighlightedSeat == null
			? ResolveVehicleUnderPointer(eventData)
			: null;
		if (vehicle != null && !vehicle.HasEmptySeat())
			vehicle = null;

		if (vehicle == m_HighlightedVehicle)
			return;

		if (m_HighlightedVehicle != null)
			m_HighlightedVehicle.SetDropHighlight(false);

		m_HighlightedVehicle = vehicle;
		if (m_HighlightedVehicle != null)
			m_HighlightedVehicle.SetDropHighlight(true);
	}

	private void ClearVehicleDropHighlight()
	{
		if (m_HighlightedVehicle != null)
		{
			m_HighlightedVehicle.SetDropHighlight(false);
			m_HighlightedVehicle = null;
		}
	}

	private void UpdateColumnDropHighlight(PointerEventData eventData)
	{
		MissionPrepRosterColumnDropZone column = ResolveColumnUnderPointer(eventData);
		if (column == m_HighlightedColumn)
			return;

		if (m_HighlightedColumn != null)
			m_HighlightedColumn.OnPointerExit(eventData);

		m_HighlightedColumn = column;
		if (m_HighlightedColumn != null)
			m_HighlightedColumn.OnPointerEnter(eventData);
	}

	private void ClearColumnDropHighlight()
	{
		if (m_HighlightedColumn != null)
		{
			m_HighlightedColumn.OnPointerExit(null);
			m_HighlightedColumn = null;
		}
	}

	private MissionPrepRosterColumnDropZone ResolveColumnUnderPointer(PointerEventData eventData)
	{
		if (eventData == null || EventSystem.current == null || m_Cell == null || m_Cell.IsInsideSeatSlot)
			return null;

		if (m_Cell.BoundUnitRoot != null && ResolveSeatUnderPointer(eventData) != null)
			return null;

		if (m_Cell.BoundUnitRoot != null && !m_Cell.IsVehicleCell)
		{
			MissionPrepVehicleRosterBlock vehicle = ResolveVehicleUnderPointer(eventData);
			if (vehicle != null && vehicle.HasEmptySeat())
				return null;
		}

		s_RaycastBuffer.Clear();
		EventSystem.current.RaycastAll(eventData, s_RaycastBuffer);

		for (int i = 0; i < s_RaycastBuffer.Count; i++)
		{
			GameObject hit = s_RaycastBuffer[i].gameObject;
			if (hit == null)
				continue;
			if (m_DragVisual != null && hit.transform.IsChildOf(m_DragVisual))
				continue;
			if (m_Cell != null && hit.transform.IsChildOf(m_Cell.transform))
				continue;

			MissionPrepRosterColumnDropZone column = hit.GetComponentInParent<MissionPrepRosterColumnDropZone>();
			if (column == null || column.OwnsCell(m_Cell))
				continue;

			return column;
		}

		return null;
	}

	private MissionPrepVehicleSeatSlotView ResolveSeatUnderPointer(PointerEventData eventData)
	{
		if (eventData == null || EventSystem.current == null)
			return null;

		s_RaycastBuffer.Clear();
		EventSystem.current.RaycastAll(eventData, s_RaycastBuffer);

		for (int i = 0; i < s_RaycastBuffer.Count; i++)
		{
			GameObject hit = s_RaycastBuffer[i].gameObject;
			if (hit == null)
				continue;
			if (m_DragVisual != null && hit.transform.IsChildOf(m_DragVisual))
				continue;
			if (m_Cell != null && hit.transform.IsChildOf(m_Cell.transform))
				continue;

			MissionPrepVehicleSeatSlotView seat = hit.GetComponentInParent<MissionPrepVehicleSeatSlotView>();
			if (seat == null || seat.OccupiedCell == m_Cell)
				continue;

			return seat;
		}

		return null;
	}

	private MissionPrepVehicleRosterBlock ResolveVehicleUnderPointer(PointerEventData eventData)
	{
		if (eventData == null || EventSystem.current == null)
			return null;

		s_RaycastBuffer.Clear();
		EventSystem.current.RaycastAll(eventData, s_RaycastBuffer);

		for (int i = 0; i < s_RaycastBuffer.Count; i++)
		{
			GameObject hit = s_RaycastBuffer[i].gameObject;
			if (hit == null)
				continue;
			if (m_DragVisual != null && hit.transform.IsChildOf(m_DragVisual))
				continue;
			if (m_Cell != null && hit.transform.IsChildOf(m_Cell.transform))
				continue;

			if (hit.GetComponentInParent<MissionPrepVehicleSeatSlotView>() != null)
				continue;

			MissionPrepVehicleRosterBlock vehicle = hit.GetComponentInParent<MissionPrepVehicleRosterBlock>();
			if (vehicle != null)
				return vehicle;
		}

		return null;
	}

	private void TryResolveDropByRaycast(PointerEventData eventData)
	{
		if (m_Cell == null || EventSystem.current == null)
			return;

		s_RaycastBuffer.Clear();
		EventSystem.current.RaycastAll(eventData, s_RaycastBuffer);

		bool isOccupiedSeatCopy = m_Cell.IsInsideSeatSlot;
		GameObject unitRoot = UnitRoot;

		for (int i = 0; i < s_RaycastBuffer.Count; i++)
		{
			GameObject hit = s_RaycastBuffer[i].gameObject;
			if (hit == null)
				continue;

			if (m_DragVisual != null && hit.transform.IsChildOf(m_DragVisual))
				continue;

			if (!isOccupiedSeatCopy && unitRoot != null && !m_Cell.IsVehicleCell)
			{
				MissionPrepVehicleSeatSlotView seat = hit.GetComponentInParent<MissionPrepVehicleSeatSlotView>();
				if (seat != null && seat.OccupiedCell != m_Cell && seat.TryAcceptUnit(unitRoot))
				{
					m_DropHandled = true;
					return;
				}

				MissionPrepVehicleRosterBlock vehicle = hit.GetComponentInParent<MissionPrepVehicleRosterBlock>();
				if (vehicle != null && vehicle.TryAcceptUnit(unitRoot))
				{
					m_DropHandled = true;
					return;
				}
			}

			if (isOccupiedSeatCopy && unitRoot != null)
			{
				MissionPrepVehicleSeatSlotView seat = hit.GetComponentInParent<MissionPrepVehicleSeatSlotView>();
				if (seat != null && seat.OccupiedCell != m_Cell && seat.TryAcceptUnit(unitRoot))
				{
					m_DropHandled = true;
					return;
				}

				MissionPrepVehicleRosterBlock vehicle = hit.GetComponentInParent<MissionPrepVehicleRosterBlock>();
				if (vehicle != null && vehicle.TryAcceptUnit(unitRoot))
				{
					m_DropHandled = true;
					return;
				}

				if (seat == null && vehicle == null)
				{
					MissionPrepUnitUnassignDropZone unassign = hit.GetComponentInParent<MissionPrepUnitUnassignDropZone>();
					if (unassign != null && unassign.TryAcceptFromDrag(m_Cell))
					{
						m_DropHandled = true;
						return;
					}
				}

				continue;
			}

			MissionPrepRosterColumnDropZone column = hit.GetComponentInParent<MissionPrepRosterColumnDropZone>();
			if (column != null && column.TryAccept(m_Cell))
			{
				m_DropHandled = true;
				return;
			}
		}
	}

	private RectTransform CreateFullCellDragVisual()
	{
		RectTransform sourceRt = m_Cell.transform as RectTransform;
		if (sourceRt == null)
			return null;

		GameObject cloneGo = Instantiate(m_Cell.gameObject, m_Canvas.transform, false);
		cloneGo.name = "UnitCellDragVisual";
		cloneGo.SetActive(true);
		StripCloneBehaviours(cloneGo);

		CanvasGroup group = cloneGo.GetComponent<CanvasGroup>();
		if (group == null)
			group = cloneGo.AddComponent<CanvasGroup>();
		group.blocksRaycasts = false;
		group.interactable = false;
		group.alpha = 0.92f;

		if (!cloneGo.TryGetComponent(out Canvas overlay))
			overlay = cloneGo.AddComponent<Canvas>();
		overlay.renderMode = m_Canvas.renderMode;
		overlay.worldCamera = m_Canvas.worldCamera;
		overlay.overrideSorting = true;
		overlay.sortingOrder = c_DragVisualSortingOrder;
		if (cloneGo.TryGetComponent(out GraphicRaycaster raycaster))
			Destroy(raycaster);

		RectTransform visualRt = cloneGo.transform as RectTransform;
		visualRt.SetAsLastSibling();
		visualRt.anchorMin = new Vector2(0.5f, 0.5f);
		visualRt.anchorMax = new Vector2(0.5f, 0.5f);
		visualRt.pivot = new Vector2(0.5f, 0.5f);
		visualRt.sizeDelta = sourceRt.rect.size;
		if (visualRt.sizeDelta.x < 8f || visualRt.sizeDelta.y < 8f)
			visualRt.sizeDelta = new Vector2(InventoryUiTheme.UnitCellWidth, InventoryUiTheme.UnitCellHeight);
		visualRt.localScale = Vector3.one;
		visualRt.localRotation = Quaternion.identity;

		LayoutElement layout = cloneGo.GetComponent<LayoutElement>();
		if (layout == null)
			layout = cloneGo.AddComponent<LayoutElement>();
		layout.ignoreLayout = true;

		LayoutRebuilder.ForceRebuildLayoutImmediate(visualRt);
		return visualRt;
	}

	private static void StripCloneBehaviours(GameObject _clone)
	{
		MissionPrepVehicleRosterBlock[] blocks = _clone.GetComponentsInChildren<MissionPrepVehicleRosterBlock>(true);
		for (int i = 0; i < blocks.Length; i++)
		{
			if (blocks[i] == null)
				continue;
			blocks[i].enabled = false;
			Destroy(blocks[i]);
		}

		MissionPrepUnitCellDrag[] drags = _clone.GetComponentsInChildren<MissionPrepUnitCellDrag>(true);
		for (int i = 0; i < drags.Length; i++)
		{
			if (drags[i] == null)
				continue;
			drags[i].enabled = false;
			Destroy(drags[i]);
		}

		Button[] buttons = _clone.GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			if (buttons[i] == null)
				continue;
			buttons[i].enabled = false;
			buttons[i].interactable = false;
		}

		Graphic[] graphics = _clone.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < graphics.Length; i++)
		{
			if (graphics[i] != null)
				graphics[i].raycastTarget = false;
		}
	}

	private void DestroyDragVisual()
	{
		if (m_DragVisual == null)
			return;

		Destroy(m_DragVisual.gameObject);
		m_DragVisual = null;
	}

	private void DisableParentScroll()
	{
		if (m_ScrollDisabledForGesture)
			return;

		if (m_ParentScroll == null)
			m_ParentScroll = GetComponentInParent<ScrollRect>();

		if (m_ParentScroll == null)
			return;

		m_ScrollWasEnabled = m_ParentScroll.enabled;
		m_ParentScroll.enabled = false;
		m_ScrollDisabledForGesture = true;
	}

	private void RestoreParentScroll()
	{
		if (m_ParentScroll != null)
		{
			m_ParentScroll.enabled = m_ScrollWasEnabled;
			m_ParentScroll = null;
		}

		m_ScrollDisabledForGesture = false;
	}

	private void UpdateDragVisual(PointerEventData eventData)
	{
		if (m_DragVisual == null || eventData == null || m_Canvas == null)
			return;

		m_DragVisual.SetAsLastSibling();

		if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
		{
			m_DragVisual.position = eventData.position;
			return;
		}

		Camera cam = eventData.pressEventCamera != null ? eventData.pressEventCamera : m_Canvas.worldCamera;
		RectTransform canvasRt = m_Canvas.transform as RectTransform;
		if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
			    canvasRt, eventData.position, cam, out Vector3 world))
			m_DragVisual.position = world;
	}
	#endregion
}
