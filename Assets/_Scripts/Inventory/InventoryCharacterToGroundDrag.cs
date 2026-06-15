using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Та же ячейка следует за курсором; при сбросе на <see cref="InventoryGroundDropZone"/> переносится на панель «земля».
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public class InventoryCharacterToGroundDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	#region Serialized Fields
	[Tooltip("Если пусто — ищется на этом объекте.")]
	[SerializeField] private InventorySlotView m_Slot;
	#endregion

	#region Private Fields
	private RectTransform m_Rect;
	private Canvas m_RootCanvas;
	private CanvasGroup m_CanvasGroup;
	private InventoryPanelView m_CharacterPanel;
	private Transform m_CharacterContentParent;
	private int m_CharacterSiblingIndex;
	private bool m_Dragging;
	private bool m_DropAccepted;
	private Vector2 m_DragOffsetLocal;
	private bool m_CapturedFromMainHandEquipmentSlot;
	private bool m_CapturedFromHeadEquipmentSlot;
	private bool m_CapturedFromBackEquipmentSlot;
	private int m_CapturedBagIndex;
	private RuntimeInlineModificationDragHelper.DragAttachment m_ModDragAttachment;
	#endregion

	#region Public Properties
	public InventorySlotView SlotView => m_Slot;
	public bool WasDraggingThisFrame => m_Dragging;
	/// <summary>Слот основного оружия (первая ячейка снаряжения на панели).</summary>
	public bool CapturedFromMainHandEquipmentSlot => m_CapturedFromMainHandEquipmentSlot;
	/// <summary>Слот головы (вторая ячейка снаряжения на панели).</summary>
	public bool CapturedFromHeadEquipmentSlot => m_CapturedFromHeadEquipmentSlot;
	/// <summary>Слот спины (третья ячейка снаряжения на панели).</summary>
	public bool CapturedFromBackEquipmentSlot => m_CapturedFromBackEquipmentSlot;
	/// <summary>Индекс в <see cref="CharacterInventory.BagItems"/> (если не слот оружия).</summary>
	public int CapturedBagIndex => m_CapturedBagIndex;
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

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		CharacterInventory inv = bindings != null ? bindings.GetActiveCharacterInventoryForUi() : null;

		if (selectionManager == null || inv == null || m_Slot == null || !m_Slot.HasItem || m_Rect == null)
			return;

		m_CharacterPanel = GetComponentInParent<InventoryPanelView>();
		if (m_CharacterPanel == null || m_CharacterPanel != selectionManager.CharacterInventoryPanel)
			return;

		if (!selectionManager.TryResolveCharacterInventorySlot(
			    m_Slot,
			    inv,
			    out m_CapturedFromMainHandEquipmentSlot,
			    out m_CapturedFromHeadEquipmentSlot,
			    out m_CapturedFromBackEquipmentSlot,
			    out m_CapturedBagIndex))
			return;

		m_RootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
		if (m_RootCanvas == null)
			return;

		if (ItemModificationUtility.IsModificationItem(m_Slot.Data))
			RuntimeInventoryModificationCoordinator.Instance?.TryBeginModificationDragFromCharacterSlot(m_Slot);
		else if (WeaponEquipUtility.CanEquipToMainHand(m_Slot.Data) && !m_CapturedFromMainHandEquipmentSlot)
		{
			RuntimeInventoryModificationDragContext.BeginCharacter(m_Slot.Data, false, m_CapturedBagIndex, m_Slot);
			if (m_CharacterPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(m_CharacterPanel);
		}
		else if (HelmetEquipUtility.CanEquipToHead(m_Slot.Data) && !m_CapturedFromHeadEquipmentSlot)
		{
			RuntimeInventoryModificationDragContext.BeginCharacter(m_Slot.Data, false, m_CapturedBagIndex, m_Slot);
			if (m_CharacterPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(m_CharacterPanel);
		}
		else if (BackpackEquipUtility.CanEquipToBack(m_Slot.Data) && !m_CapturedFromBackEquipmentSlot)
		{
			RuntimeInventoryModificationDragContext.BeginCharacter(m_Slot.Data, false, m_CapturedBagIndex, m_Slot);
			if (m_CharacterPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(m_CharacterPanel);
		}
		else if (m_CapturedFromHeadEquipmentSlot)
		{
			RuntimeInventoryModificationDragContext.BeginCharacter(
				m_Slot.Data,
				_isMainHand: false,
				_bagIndex: -1,
				_sourceSlot: m_Slot,
				_isHead: true);
			if (InventoryExchangeController.Instance.IsActive && selectionManager.GroundPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(selectionManager.GroundPanel);
		}
		else if (m_CapturedFromBackEquipmentSlot)
		{
			RuntimeInventoryModificationDragContext.BeginCharacter(
				m_Slot.Data,
				_isMainHand: false,
				_bagIndex: -1,
				_sourceSlot: m_Slot,
				_isHead: false,
				_isBack: true);
			if (InventoryExchangeController.Instance.IsActive && selectionManager.GroundPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(selectionManager.GroundPanel);
		}
		else if (m_CapturedFromMainHandEquipmentSlot)
		{
			RuntimeInventoryModificationDragContext.BeginCharacter(
				m_Slot.Data,
				_isMainHand: true,
				_bagIndex: -1,
				_sourceSlot: m_Slot);
			if (InventoryExchangeController.Instance.IsActive && selectionManager.GroundPanel != null)
				InventorySlotUiUtility.RefreshEquipmentSlotHighlights(selectionManager.GroundPanel);
		}

		m_CharacterContentParent = transform.parent;
		m_CharacterSiblingIndex = transform.GetSiblingIndex();
		m_CharacterPanel.DetachSlotForDrag(m_Slot);

		m_Dragging = true;
		m_DropAccepted = false;
		m_CanvasGroup.blocksRaycasts = false;

		m_ModDragAttachment = RuntimeInlineModificationDragHelper.Attach(
			m_Slot, m_Rect, m_RootCanvas, m_CharacterPanel);
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

			if (exchangeActive && coordinator != null &&
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
			else if (selectionManager != null && coordinator != null &&
			         coordinator.IsScreenPointOverCharacterPanel(eventData.position, eventCamera))
			{
				m_DropAccepted = selectionManager.TryRouteCharacterDragOnCharacterPanel(
					this, eventData.position, eventCamera, _requireActiveDrag: false);
				if (m_DropAccepted)
					DestroyDraggedSlotVisual();
			}
			else if (exchangeActive && selectionManager != null && coordinator != null &&
			         coordinator.IsScreenPointOverGroundPanel(eventData.position, eventCamera))
			{
				m_DropAccepted = selectionManager.TryRouteCharacterDragOnPartnerPanel(
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
		else if (!m_DropAccepted && m_CharacterContentParent != null)
		{
			RuntimeInlineModificationDragHelper.RestoreToContent(m_ModDragAttachment, m_CharacterPanel);
		}
		else if (m_DropAccepted)
		{
			RuntimeInlineModificationDragHelper.CleanupAfterDrop(m_ModDragAttachment);
			if (m_CharacterPanel != null)
				m_CharacterPanel.RebuildContentLayout();
		}
		else
		{
			RuntimeInlineModificationDragHelper.CleanupAfterDrop(m_ModDragAttachment);
		}

		m_ModDragAttachment = null;
		m_DropAccepted = false;
		m_CharacterContentParent = null;
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

		if (m_CharacterPanel != null && m_Slot.IsRuntimeSpawned)
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(gameObject, m_CharacterPanel.transform);
		else
			Destroy(gameObject);
	}
	#endregion
}
