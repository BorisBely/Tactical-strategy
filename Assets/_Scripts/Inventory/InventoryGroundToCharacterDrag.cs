using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Та же ячейка следует за курсором; при сбросе на <see cref="InventoryCharacterBagDropZone"/> переносится в рюкзак.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public class InventoryGroundToCharacterDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	#region Serialized Fields
	[Tooltip("Если пусто — ищется на этом объекте.")]
	[SerializeField] private InventorySlotView m_Slot;
	#endregion

	#region Private Fields
	private RectTransform m_Rect;
	private Canvas m_RootCanvas;
	private CanvasGroup m_CanvasGroup;
	private InventoryPanelView m_GroundPanel;
	private Transform m_GroundContentParent;
	private int m_GroundSiblingIndex;
	private bool m_Dragging;
	private bool m_DropAccepted;
	private Vector2 m_DragOffsetLocal;
	#endregion

	#region Public Properties
	public InventorySlotView SlotView => m_Slot;
	/// <summary>Для координатора: drag начался и ещё не завершён EndDrag.</summary>
	public bool WasDraggingThisFrame => m_Dragging;
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
		PlayerInventoryCoordinator coordinator = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.Coordinator
			: null;
		if (coordinator == null || m_Slot == null || !m_Slot.HasItem || m_Rect == null)
			return;

		m_GroundPanel = GetComponentInParent<InventoryPanelView>();
		if (m_GroundPanel == null || m_GroundPanel != coordinator.GroundPanel)
			return;

		IReadOnlyList<InventorySlotView> slots = m_GroundPanel.Slots;
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

		m_GroundContentParent = transform.parent;
		m_GroundSiblingIndex = transform.GetSiblingIndex();
		m_GroundPanel.DetachSlotForDrag(m_Slot);

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
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!m_Dragging)
			return;

		m_Dragging = false;
		m_CanvasGroup.blocksRaycasts = true;

		if (!m_DropAccepted && m_GroundContentParent != null)
		{
			transform.SetParent(m_GroundContentParent, false);
			int max = m_GroundContentParent.childCount - 1;
			transform.SetSiblingIndex(Mathf.Clamp(m_GroundSiblingIndex, 0, Mathf.Max(0, max)));
			if (m_GroundPanel != null)
				m_GroundPanel.RefreshSlotsFromHierarchy();
		}
		else if (m_DropAccepted && m_GroundPanel != null)
			m_GroundPanel.RebuildContentLayout();

		m_DropAccepted = false;
		m_GroundContentParent = null;
	}
	#endregion

	#region Private Methods
	private void UpdateDragPosition(PointerEventData eventData)
	{
		Camera cam = GetDragCamera(eventData);
		RectTransform canvasRt = m_RootCanvas.transform as RectTransform;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, eventData.position, cam, out Vector2 pointerLocal))
			m_Rect.localPosition = new Vector3(pointerLocal.x + m_DragOffsetLocal.x, pointerLocal.y + m_DragOffsetLocal.y, m_Rect.localPosition.z);
	}

	private Camera GetDragCamera(PointerEventData eventData)
	{
		if (m_RootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;
		return eventData.pressEventCamera != null ? eventData.pressEventCamera : m_RootCanvas.worldCamera;
	}
	#endregion
}
