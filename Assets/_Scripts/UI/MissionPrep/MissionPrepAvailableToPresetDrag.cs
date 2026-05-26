using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Перетаскивание ячейки с панели доступного снаряжения. При успешном сбросе предмет копируется в снимок пресета;
/// в списке доступных ячейка остаётся.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepAvailableToPresetDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	#endregion

	#region Private Fields
	private RectTransform m_Rect;
	private Canvas m_RootCanvas;
	private CanvasGroup m_CanvasGroup;
	private InventoryPanelView m_AvailablePanel;
	private Transform m_AvailableContentParent;
	private int m_AvailableSiblingIndex;
	private InventorySlotRuntimeData m_SourceData;
	private bool m_Dragging;
	private bool m_DropAccepted;
	private Vector2 m_DragOffsetLocal;
	#endregion

	#region Public Properties
	public InventorySlotView SlotView => m_Slot;

	/// <summary>Панель, с которой начали drag (ячейка во время переноса на canvas).</summary>
	public InventoryPanelView SourceAvailablePanel => m_AvailablePanel;

	/// <summary>Активный drag с панели доступного снаряжения (не хвост прошлого переноса).</summary>
	public bool IsDraggingFromAvailable => m_Dragging;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Rect = transform as RectTransform;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		m_CanvasGroup = GetComponent<CanvasGroup>();
		if (m_CanvasGroup == null)
			m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
	}

	private void OnEnable()
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void OnDisable()
	{
		if (!m_Dragging)
			return;

		m_Dragging = false;
		if (m_CanvasGroup != null)
			m_CanvasGroup.blocksRaycasts = true;

		if (m_AvailableContentParent != null)
		{
			transform.SetParent(m_AvailableContentParent, false);
			int max = m_AvailableContentParent.childCount - 1;
			transform.SetSiblingIndex(Mathf.Clamp(m_AvailableSiblingIndex, 0, Mathf.Max(0, max)));
		}

		if (!m_SourceData.IsEmpty && m_Slot != null)
			m_Slot.SetItem(m_SourceData);

		MissionPrepModificationDragContext.ResetAfterDrag();
		m_AvailablePanel = null;
		m_AvailableContentParent = null;
		m_DropAccepted = false;
	}
	#endregion

	#region Public Methods
	public void NotifyDropAccepted()
	{
		m_DropAccepted = true;
	}
	#endregion

	#region Drag Handlers
	public void OnBeginDrag(PointerEventData eventData)
	{
		m_AvailablePanel = null;
		m_AvailableContentParent = null;
		m_Dragging = false;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null || m_Slot == null || !m_Slot.HasItem || m_Rect == null)
			return;

		m_AvailablePanel = m_Coordinator.AvailableEquipmentPanel;
		if (m_AvailablePanel == null || !IsSlotOnPanel(m_Slot, m_AvailablePanel))
			return;

		IReadOnlyList<InventorySlotView> slots = m_AvailablePanel.Slots;
		bool found = false;
		for (int i = 0; i < slots.Count; i++)
		{
			if (slots[i] == m_Slot)
			{
				found = true;
				break;
			}
		}

		if (!found)
			return;

		m_RootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
		if (m_RootCanvas == null)
			return;

		m_SourceData = m_Slot.Data;
		MissionPrepModificationDragContext.BeginAvailable(m_SourceData);
		if (m_Coordinator.PresetInventoryPanel != null)
			InventorySlotUiUtility.RefreshMainHandEquipHighlight(m_Coordinator.PresetInventoryPanel);
		m_AvailableContentParent = transform.parent;
		m_AvailableSiblingIndex = transform.GetSiblingIndex();
		m_AvailablePanel.DetachSlotForDrag(m_Slot);

		m_Dragging = true;
		m_DropAccepted = false;
		m_CanvasGroup.blocksRaycasts = false;

		transform.SetParent(m_RootCanvas.transform, true);
		transform.SetAsLastSibling();

		Camera cam = GetDragCamera(eventData);
		RectTransform canvasRt = m_RootCanvas.transform as RectTransform;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, eventData.pressPosition, cam, out Vector2 pressLocal))
			m_DragOffsetLocal = (Vector2)m_Rect.localPosition - pressLocal;
		else
			m_DragOffsetLocal = Vector2.zero;

		UpdateDragPosition(eventData);
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!m_Dragging)
			return;

		UpdateDragPosition(eventData);

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator?.PresetInventoryPanel != null)
			MissionPrepInlineModificationBuilder.RefreshMainHandSlotHighlights(m_Coordinator.PresetInventoryPanel);
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!m_Dragging)
			return;

		bool wasDragging = m_Dragging;
		m_Dragging = false;

		if (!m_DropAccepted && m_Coordinator != null &&
		    !MissionPrepModificationDragContext.WasDropConsumed && wasDragging)
		{
			Camera cam = GetDragCamera(eventData);
			if (m_Coordinator.IsScreenPointOverPresetMainHandSlot(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryEquipAvailableSlotToMainHand(m_Slot);
			else if (m_Coordinator.IsScreenPointOverPresetInventoryPanel(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryTransferAvailableSlotToPreset(m_Slot);
		}

		m_CanvasGroup.blocksRaycasts = true;

		if (m_AvailableContentParent != null)
		{
			transform.SetParent(m_AvailableContentParent, false);
			int max = m_AvailableContentParent.childCount - 1;
			transform.SetSiblingIndex(Mathf.Clamp(m_AvailableSiblingIndex, 0, Mathf.Max(0, max)));
		}

		if (!m_SourceData.IsEmpty)
			m_Slot.SetItem(m_SourceData);

		if (m_AvailablePanel != null)
		{
			m_AvailablePanel.RefreshSlotsFromHierarchy();
			m_AvailablePanel.RebuildContentLayout();
		}

		MissionPrepModificationDragContext.ResetAfterDrag();
		m_AvailablePanel = null;
		m_AvailableContentParent = null;
		m_DropAccepted = false;
	}
	#endregion

	#region Private Methods
	private void UpdateDragPosition(PointerEventData eventData)
	{
		Camera cam = GetDragCamera(eventData);
		RectTransform canvasRt = m_RootCanvas.transform as RectTransform;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, eventData.position, cam, out Vector2 pointerLocal))
			m_Rect.localPosition = new Vector3(
				pointerLocal.x + m_DragOffsetLocal.x,
				pointerLocal.y + m_DragOffsetLocal.y,
				m_Rect.localPosition.z);
	}

	private Camera GetDragCamera(PointerEventData eventData)
	{
		if (m_RootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;

		return eventData.pressEventCamera != null ? eventData.pressEventCamera : m_RootCanvas.worldCamera;
	}

	private static bool IsSlotOnPanel(InventorySlotView _slot, InventoryPanelView _panel)
	{
		if (_slot == null || _panel == null)
			return false;

		return _slot.GetComponentInParent<InventoryPanelView>() == _panel;
	}
	#endregion
}
