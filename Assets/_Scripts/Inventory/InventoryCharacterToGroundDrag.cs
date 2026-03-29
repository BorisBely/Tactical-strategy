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
	private int m_CapturedBagIndex;
	#endregion

	#region Public Properties
	public InventorySlotView SlotView => m_Slot;
	public bool WasDraggingThisFrame => m_Dragging;
	/// <summary>Слот основного оружия (первая ячейка снаряжения на панели).</summary>
	public bool CapturedFromMainHandEquipmentSlot => m_CapturedFromMainHandEquipmentSlot;
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
		PlayerInventoryCoordinator coordinator = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.Coordinator
			: null;
		CharacterInventory inv = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.ActiveCharacterInventory
			: null;

		if (coordinator == null || inv == null || m_Slot == null || !m_Slot.HasItem || m_Rect == null)
			return;

		m_CharacterPanel = GetComponentInParent<InventoryPanelView>();
		if (m_CharacterPanel == null || m_CharacterPanel != coordinator.CharacterInventoryPanel)
			return;

		if (!coordinator.TryResolveCharacterInventorySlot(m_Slot, inv, out m_CapturedFromMainHandEquipmentSlot,
			    out m_CapturedBagIndex))
			return;

		m_RootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
		if (m_RootCanvas == null)
			return;

		m_CharacterContentParent = transform.parent;
		m_CharacterSiblingIndex = transform.GetSiblingIndex();
		m_CharacterPanel.DetachSlotForDrag(m_Slot);

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

		if (!m_DropAccepted && m_CharacterContentParent != null)
		{
			transform.SetParent(m_CharacterContentParent, false);
			int max = m_CharacterContentParent.childCount - 1;
			transform.SetSiblingIndex(Mathf.Clamp(m_CharacterSiblingIndex, 0, Mathf.Max(0, max)));
			if (m_CharacterPanel != null)
				m_CharacterPanel.RefreshSlotsFromHierarchy();
		}
		else if (m_DropAccepted && m_CharacterPanel != null)
			m_CharacterPanel.RebuildContentLayout();

		m_DropAccepted = false;
		m_CharacterContentParent = null;
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
