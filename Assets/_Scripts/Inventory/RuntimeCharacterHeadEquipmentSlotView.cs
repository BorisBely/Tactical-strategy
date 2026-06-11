using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Слот экипированного шлема runtime-инвентаря: подсветка при drag и приём сброса шлема.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class RuntimeCharacterHeadEquipmentSlotView : MonoBehaviour, IDropHandler, IInventoryEquipmentSlotDropHandler
{
	#region Private Fields
	private RuntimeInventoryModificationCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	#endregion

	#region Public Methods
	public void Bind(RuntimeInventoryModificationCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventoryPanelView panel = m_Slot.GetComponentInParent<InventoryPanelView>();
		if (panel != null)
			InventorySlotUiUtility.ConfigureHeadEquipmentSlot(m_Slot, panel.EquipmentSlotAppearance);

		InventorySlotUiUtility.EnsureEquipmentSlotDropReceiver(this);
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventorySlotUiUtility.ApplyHeadEquipmentSlotHighlight(m_Slot, InventorySlotUiUtility.IsHelmetEquipDragActive());
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		HandleEquipmentSlotDrop(eventData);
	}

	public void HandleEquipmentSlotDrop(PointerEventData eventData)
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return;

		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		if (!m_Coordinator.TryEquipHelmetDragToHead() || eventData?.pointerDrag == null)
			return;

		if (eventData.pointerDrag.TryGetComponent(out InventoryGroundToCharacterDrag groundDrag))
			groundDrag.NotifyDropAccepted();
		else if (eventData.pointerDrag.TryGetComponent(out InventoryCharacterToGroundDrag characterDrag))
			characterDrag.NotifyDropAccepted();
	}
	#endregion
}
