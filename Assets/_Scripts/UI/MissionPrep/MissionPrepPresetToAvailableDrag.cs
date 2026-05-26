using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Перетаскивание ячейки из инвентаря пресета. При сбросе на панель доступного снаряжения предмет удаляется из снимка пресета;
/// каталог доступных предметов не меняется.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepPresetToAvailableDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	#endregion

	#region Private Fields
	private RectTransform m_Rect;
	private Canvas m_RootCanvas;
	private CanvasGroup m_CanvasGroup;
	private InventoryPanelView m_PresetPanel;
	private bool m_Dragging;
	private bool m_DropAccepted;
	private bool m_HasResolvedSlot;
	private bool m_IsMainHandSlot;
	private int m_BagIndex;
	private Vector2 m_DragOffsetLocal;
	#endregion

	#region Public Properties
	public InventorySlotView SlotView => m_Slot;
	public InventoryPanelView SourcePresetPanel => m_PresetPanel;
	public bool HasResolvedSlot => m_HasResolvedSlot;
	public bool IsMainHandSlot => m_IsMainHandSlot;
	public int BagIndex => m_BagIndex;
	public bool IsDraggingFromPreset => m_Dragging;
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

		MissionPrepModificationDragContext.ResetAfterDrag();
		m_HasResolvedSlot = false;
		m_DropAccepted = false;
		m_PresetPanel = null;
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
		m_HasResolvedSlot = false;
		m_DropAccepted = false;
		m_IsMainHandSlot = false;
		m_BagIndex = -1;
		m_PresetPanel = null;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null || m_Slot == null || !m_Slot.HasItem || m_Rect == null)
			return;

		m_PresetPanel = m_Coordinator.PresetInventoryPanel;
		if (m_PresetPanel == null)
			return;

		if (!m_Coordinator.TryResolveInventorySlot(m_Slot, out m_IsMainHandSlot, out m_BagIndex))
			return;

		m_HasResolvedSlot = true;
		MissionPrepModificationDragContext.BeginPreset(m_Slot.Data, m_IsMainHandSlot, m_BagIndex);
		if (m_Coordinator.PresetInventoryPanel != null)
			InventorySlotUiUtility.RefreshMainHandEquipHighlight(m_Coordinator.PresetInventoryPanel);
		if (ItemModificationUtility.IsModifiableWeapon(m_Slot.Data.Definition))
			MissionPrepInlineModificationBuilder.ClearRowsFollowingInventorySlot(m_PresetPanel, m_Slot);
		m_PresetPanel.DetachSlotForDrag(m_Slot);

		m_RootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
		if (m_RootCanvas == null)
			return;

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

		m_Dragging = false;

		if (!m_DropAccepted && m_Coordinator != null && m_HasResolvedSlot &&
		    !MissionPrepModificationDragContext.WasDropConsumed)
		{
			Camera cam = GetDragCamera(eventData);
			if (m_Coordinator.IsScreenPointOverAvailableEquipmentPanel(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryRemovePresetInventorySlot(m_IsMainHandSlot, m_BagIndex);
			else if (!m_IsMainHandSlot &&
			         m_Coordinator.IsScreenPointOverPresetMainHandSlot(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryMovePresetBagItemToMainHand(m_BagIndex);
			else if (m_IsMainHandSlot &&
			         m_Coordinator.IsScreenPointOverPresetInventoryPanel(eventData.position, cam) &&
			         !m_Coordinator.IsScreenPointOverPresetMainHandSlot(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryUnequipPresetMainHandToBag();
		}

		m_CanvasGroup.blocksRaycasts = true;

		if (!m_DropAccepted && m_Coordinator != null && !MissionPrepModificationDragContext.WasDropConsumed)
			m_Coordinator.RepaintInventoryPanel();

		DestroyDraggedSlotVisual();
		MissionPrepModificationDragContext.ResetAfterDrag();
		m_HasResolvedSlot = false;
		m_DropAccepted = false;
		m_PresetPanel = null;
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

	private void DestroyDraggedSlotVisual()
	{
		if (!Application.isPlaying)
			return;

		if (m_PresetPanel != null && m_Slot != null && m_Slot.IsRuntimeSpawned)
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(gameObject, m_PresetPanel.transform);
		else
			Destroy(gameObject);
	}
	#endregion
}
