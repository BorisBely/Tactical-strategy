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
	private int m_CapturedGroundSlotIndex = -1;
	private bool m_Dragging;
	private bool m_DropAccepted;
	private Vector2 m_DragOffsetLocal;
	private RuntimeInlineModificationDragHelper.DragAttachment m_ModDragAttachment;
	#endregion

	#region Public Properties
	public InventorySlotView SlotView => m_Slot;
	/// <summary>Для координатора: drag начался и ещё не завершён EndDrag.</summary>
	public bool WasDraggingThisFrame => m_Dragging;
	/// <summary>Индекс ячейки на панели «земля» / «Найдено» до DetachSlotForDrag.</summary>
	public int CapturedGroundSlotIndex => m_CapturedGroundSlotIndex;
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
		if (m_Slot != null)
			InventoryItemTooltip.Instance.HideIfSource(m_Slot);

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

		m_CapturedGroundSlotIndex = m_GroundPanel.GetInventorySlotListIndex(m_Slot);

		if (ItemModificationUtility.IsModificationItem(m_Slot.Data))
			RuntimeInventoryModificationCoordinator.Instance?.TryBeginModificationDragFromGroundSlot(m_Slot);
		else if (WeaponEquipUtility.CanEquipToMainHand(m_Slot.Data))
		{
			RuntimeInventoryModificationDragContext.BeginGround(
				m_Slot.Data,
				m_GroundPanel.GetInventorySlotListIndex(m_Slot),
				m_Slot);
			if (selectionManager.CharacterInventoryPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(selectionManager.CharacterInventoryPanel);
		}
		else if (HelmetEquipUtility.CanEquipToHead(m_Slot.Data))
		{
			RuntimeInventoryModificationDragContext.BeginGround(
				m_Slot.Data,
				m_GroundPanel.GetInventorySlotListIndex(m_Slot),
				m_Slot);
			if (selectionManager.CharacterInventoryPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(selectionManager.CharacterInventoryPanel);
		}
		else if (BackpackEquipUtility.CanEquipToBack(m_Slot.Data))
		{
			RuntimeInventoryModificationDragContext.BeginGround(
				m_Slot.Data,
				m_GroundPanel.GetInventorySlotListIndex(m_Slot),
				m_Slot);
			if (selectionManager.CharacterInventoryPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(selectionManager.CharacterInventoryPanel);
		}

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

		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;
		if (coordinator?.CharacterPanel != null)
			RuntimeInlineModificationBuilder.RefreshEquipmentSlotHighlights(coordinator.CharacterPanel);

		if (InventoryExchangeController.Instance.IsActive && coordinator?.GroundPanel != null)
			RuntimeInlineModificationBuilder.RefreshEquipmentSlotHighlights(coordinator.GroundPanel);
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!m_Dragging)
			return;

		bool wasDragging = m_Dragging;
		m_Dragging = false;

		bool wasModificationDropConsumed = RuntimeInventoryModificationDragContext.WasDropConsumed;
		RtsUnitSelectionManager selectionManager = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.SelectionManager
			: null;
		Camera eventCamera = GetDragCamera(eventData);
		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (!wasModificationDropConsumed && !m_DropAccepted && wasDragging)
		{
			bool exchangeActive = InventoryExchangeController.Instance.IsActive;

			if (coordinator != null &&
			    coordinator.IsScreenPointOverCharacterMainHandSlot(eventData.position, eventCamera))
			{
				m_DropAccepted = coordinator.TryEquipWeaponDragToMainHand();
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (coordinator != null &&
			         coordinator.IsScreenPointOverCharacterHeadSlot(eventData.position, eventCamera))
			{
				m_DropAccepted = coordinator.TryEquipHelmetDragToHead();
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (coordinator != null &&
			         coordinator.IsScreenPointOverCharacterBackSlot(eventData.position, eventCamera))
			{
				m_DropAccepted = coordinator.TryEquipBackpackDragToBack();
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (exchangeActive && coordinator != null &&
			         coordinator.IsScreenPointOverPartnerMainHandSlot(eventData.position, eventCamera))
			{
				m_DropAccepted = coordinator.TryEquipWeaponDragToPartnerMainHand();
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (exchangeActive && coordinator != null &&
			         coordinator.IsScreenPointOverPartnerHeadSlot(eventData.position, eventCamera))
			{
				m_DropAccepted = coordinator.TryEquipHelmetDragToPartnerHead();
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (exchangeActive && coordinator != null &&
			         coordinator.IsScreenPointOverPartnerBackSlot(eventData.position, eventCamera))
			{
				m_DropAccepted = coordinator.TryEquipBackpackDragToPartnerBack();
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (!m_DropAccepted && selectionManager != null && coordinator != null &&
			         coordinator.IsScreenPointOverCharacterPanel(eventData.position, eventCamera))
			{
				m_DropAccepted = selectionManager.TryRouteGroundDragOnCharacterPanel(
					this, eventData.position, eventCamera, _requireActiveDrag: false);
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (!m_DropAccepted && exchangeActive && selectionManager != null && coordinator != null &&
			         coordinator.IsScreenPointOverGroundPanel(eventData.position, eventCamera))
			{
				m_DropAccepted = selectionManager.TryRouteGroundDragOnPartnerPanel(
					this, eventData.position, eventCamera, _requireActiveDrag: false);
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
		}

		m_CanvasGroup.blocksRaycasts = true;

		if (wasModificationDropConsumed || RuntimeInventoryModificationDragContext.WasDropConsumed)
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
		m_CapturedGroundSlotIndex = -1;
		RuntimeInventoryModificationDragContext.ResetAfterDrag();
	}
	#endregion

	#region Private Methods
	private void UpdateDragPosition(PointerEventData eventData)
	{
		RuntimeInlineModificationDragHelper.UpdateDragPosition(
			m_ModDragAttachment, eventData, m_RootCanvas, m_DragOffsetLocal);
	}

	private Camera GetDragCamera(PointerEventData eventData)
	{
		if (m_RootCanvas == null)
			return null;

		if (m_RootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;

		return eventData.pressEventCamera != null ? eventData.pressEventCamera : m_RootCanvas.worldCamera;
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
