using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RuntimeModificationSlotView))]
public sealed class RuntimeModificationSlotDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	#region Private Fields
	private static RuntimeModificationSlotDrag s_ActiveDrag;

	private RuntimeModificationSlotView m_SlotView;
	private RuntimeInventoryModificationCoordinator m_Coordinator;
	private InventorySlotView m_DragSlot;
	private RectTransform m_DragRect;
	private RectTransform m_SourceRect;
	private Canvas m_RootCanvas;
	private CanvasGroup m_DragCanvasGroup;
	private Vector2 m_DragOffsetLocal;
	private bool m_Dragging;
	private bool m_DropAccepted;
	#endregion

	#region Public Properties
	public ItemModificationSlotDescriptor SlotDescriptor => m_SlotView != null ? m_SlotView.Descriptor : default;
	public bool WeaponIsMainHand => m_SlotView != null && m_SlotView.WeaponIsMainHand;
	public int WeaponBagIndex => m_SlotView != null ? m_SlotView.WeaponBagIndex : -1;
	public bool WeaponIsOnGroundPanel => m_SlotView != null && m_SlotView.WeaponIsOnGroundPanel;
	public int WeaponGroundSlotIndex => m_SlotView != null ? m_SlotView.WeaponGroundSlotIndex : -1;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_SlotView = GetComponent<RuntimeModificationSlotView>();
		m_SourceRect = transform as RectTransform;
	}

	private void OnEnable()
	{
		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;
	}

	private void OnDisable()
	{
		if (m_Dragging)
			FinishDragVisualCleanup();
	}
	#endregion

	#region Public Methods
	public static void CleanupActiveDragVisual()
	{
		if (s_ActiveDrag == null)
			return;

		s_ActiveDrag.FinishDragVisualCleanup();
	}

	public void NotifyDropAccepted()
	{
		m_DropAccepted = true;
	}
	#endregion

	#region Drag Handlers
	public void OnBeginDrag(PointerEventData eventData)
	{
		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (m_Coordinator == null || m_SlotView == null || m_SourceRect == null || !m_SlotView.HasInstalledItem)
			return;

		if (!m_SlotView.TryGetInstalledItem(out InventorySlotRuntimeData installedItem))
			return;

		InventoryPanelView characterPanel = m_Coordinator.CharacterPanel;
		if (characterPanel == null)
			return;

		m_RootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
		if (m_RootCanvas == null)
			return;

		m_DragSlot = characterPanel.CreateDetachedDragVisual(
			MissionPrepInventoryCopyUtility.CloneSlot(installedItem),
			m_RootCanvas.transform);
		if (m_DragSlot == null)
			return;

		m_DragRect = m_DragSlot.transform as RectTransform;
		if (m_DragRect == null)
		{
			DestroyDragSlot();
			return;
		}

		RuntimeInventoryModificationDragContext.BeginModificationSlot(
			m_SlotView.Descriptor,
			MissionPrepInventoryCopyUtility.CloneSlot(installedItem),
			m_SlotView.WeaponIsMainHand,
			m_SlotView.WeaponBagIndex);

		m_DragCanvasGroup = m_DragSlot.GetComponent<CanvasGroup>();
		if (m_DragCanvasGroup == null)
			m_DragCanvasGroup = m_DragSlot.gameObject.AddComponent<CanvasGroup>();
		m_DragCanvasGroup.blocksRaycasts = false;

		m_DragRect.SetAsLastSibling();
		m_DragRect.position = m_SourceRect.position;

		m_Dragging = true;
		m_DropAccepted = false;
		s_ActiveDrag = this;

		Camera cam = GetDragCamera(eventData);
		RectTransform canvasRt = m_RootCanvas.transform as RectTransform;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, eventData.pressPosition, cam, out Vector2 pressLocal))
			m_DragOffsetLocal = (Vector2)m_DragRect.localPosition - pressLocal;
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

		if (!m_DropAccepted && m_Coordinator != null && !RuntimeInventoryModificationDragContext.WasDropConsumed)
		{
			Camera cam = GetDragCamera(eventData);
			if (m_Coordinator.IsScreenPointOverCharacterPanel(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryEjectModificationSlotToCharacterBag(this);
			else if (m_Coordinator.IsScreenPointOverGroundPanel(eventData.position, cam))
				m_DropAccepted = m_Coordinator.TryEjectModificationSlotToGround(this);
		}

		FinishDragVisualCleanup();
		m_DropAccepted = false;
	}
	#endregion

	#region Private Methods
	private void UpdateDragPosition(PointerEventData eventData)
	{
		if (m_DragRect == null || m_RootCanvas == null)
			return;

		Camera cam = GetDragCamera(eventData);
		RectTransform canvasRt = m_RootCanvas.transform as RectTransform;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, eventData.position, cam, out Vector2 pointerLocal))
		{
			m_DragRect.localPosition = new Vector3(
				pointerLocal.x + m_DragOffsetLocal.x,
				pointerLocal.y + m_DragOffsetLocal.y,
				m_DragRect.localPosition.z);
		}
	}

	private Camera GetDragCamera(PointerEventData eventData)
	{
		if (m_RootCanvas == null || m_RootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;

		return eventData.pressEventCamera != null ? eventData.pressEventCamera : m_RootCanvas.worldCamera;
	}

	private void FinishDragVisualCleanup()
	{
		m_Dragging = false;

		if (s_ActiveDrag == this)
			s_ActiveDrag = null;

		DestroyDragSlot();
		RuntimeInventoryModificationDragContext.ResetAfterDrag();
	}

	private void DestroyDragSlot()
	{
		if (!Application.isPlaying || m_DragSlot == null)
			return;

		Transform panelRoot = m_Coordinator != null && m_Coordinator.CharacterPanel != null
			? m_Coordinator.CharacterPanel.transform
			: transform;

		EditorSelectionGuard.DestroyRuntimeSpawnedSlot(m_DragSlot.gameObject, panelRoot);
		m_DragSlot = null;
		m_DragRect = null;
		m_DragCanvasGroup = null;
	}
	#endregion
}
