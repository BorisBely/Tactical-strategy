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
	private RuntimeInlineModificationDragHelper.DragAttachment m_ModDragAttachment;
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
		RtsUnitSelectionManager selectionManager = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.SelectionManager
			: null;
		if (selectionManager == null || m_Slot == null || !m_Slot.HasItem || m_Rect == null)
			return;

		m_GroundPanel = GetComponentInParent<InventoryPanelView>();
		if (m_GroundPanel == null || m_GroundPanel != selectionManager.GroundPanel)
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

		if (ItemModificationUtility.IsModificationItem(m_Slot.Data))
			RuntimeInventoryModificationCoordinator.Instance?.TryBeginModificationDragFromGroundSlot(m_Slot);

		m_GroundContentParent = transform.parent;
		m_GroundSiblingIndex = transform.GetSiblingIndex();
		m_GroundPanel.DetachSlotForDrag(m_Slot);

		m_Dragging = true;
		m_DropAccepted = false;
		m_CanvasGroup.blocksRaycasts = false;

		m_ModDragAttachment = RuntimeInlineModificationDragHelper.Attach(
			m_Slot, m_Rect, m_RootCanvas, m_GroundPanel);
		m_DragOffsetLocal = RuntimeInlineModificationDragHelper.ComputeDragOffsetLocal(
			m_ModDragAttachment, eventData, m_RootCanvas);

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

		bool wasModificationDropConsumed = RuntimeInventoryModificationDragContext.WasDropConsumed;

		if (wasModificationDropConsumed)
		{
			DestroyDraggedSlotVisual();
		}
		else if (!m_DropAccepted && m_GroundContentParent != null)
		{
			RuntimeInlineModificationDragHelper.RestoreToContent(m_ModDragAttachment, m_GroundPanel);
		}
		else if (m_DropAccepted)
		{
			RuntimeInlineModificationDragHelper.CleanupAfterDrop(m_ModDragAttachment);
			if (m_GroundPanel != null)
				m_GroundPanel.RebuildContentLayout();
		}
		else
		{
			RuntimeInlineModificationDragHelper.CleanupAfterDrop(m_ModDragAttachment);
		}

		m_ModDragAttachment = null;
		m_DropAccepted = false;
		m_GroundContentParent = null;
		RuntimeInventoryModificationDragContext.ResetAfterDrag();
	}
	#endregion

	#region Private Methods
	private void UpdateDragPosition(PointerEventData eventData)
	{
		RuntimeInlineModificationDragHelper.UpdateDragPosition(
			m_ModDragAttachment, eventData, m_RootCanvas, m_DragOffsetLocal);
	}

	private void DestroyDraggedSlotVisual()
	{
		if (!Application.isPlaying || m_Slot == null)
			return;

		RuntimeInlineModificationDragHelper.CleanupAfterDrop(m_ModDragAttachment);
		m_ModDragAttachment = null;

		if (m_GroundPanel != null && m_Slot.IsRuntimeSpawned)
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(gameObject, m_GroundPanel.transform);
		else
			Destroy(gameObject);
	}
	#endregion
}
