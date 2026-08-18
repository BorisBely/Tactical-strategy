using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class RuntimePartnerMainHandEquipmentSlotView : MonoBehaviour, IDropHandler, IInventoryEquipmentSlotDropHandler
{
	private RuntimeInventoryModificationCoordinator m_Coordinator;
	private InventorySlotView m_Slot;

	public void Bind(RuntimeInventoryModificationCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventoryPanelView panel = m_Slot.GetComponentInParent<InventoryPanelView>();
		if (panel != null)
		{
			InventorySlotUiUtility.ConfigureMainHandEquipmentSlot(
				m_Slot, panel.EquipmentSlotAppearance, panel.LeadingEquipmentUsesVehicleLabels);
		}

		InventorySlotUiUtility.EnsureEquipmentSlotDropReceiver(this);
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventorySlotUiUtility.ApplyMainHandEquipmentSlotHighlight(m_Slot, InventorySlotUiUtility.IsWeaponEquipDragActive());
	}

	public void OnDrop(PointerEventData eventData) => HandleEquipmentSlotDrop(eventData);

	public void HandleEquipmentSlotDrop(PointerEventData eventData)
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return;

		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		if (!m_Coordinator.TryEquipWeaponDragToPartnerMainHand() || eventData?.pointerDrag == null)
			return;

		NotifyDropAccepted(eventData.pointerDrag);
	}

	private static void NotifyDropAccepted(GameObject _pointerDrag)
	{
		if (_pointerDrag.TryGetComponent(out InventoryGroundToCharacterDrag groundDrag))
			groundDrag.NotifyDropAccepted();
		else if (_pointerDrag.TryGetComponent(out InventoryCharacterToGroundDrag characterDrag))
			characterDrag.NotifyDropAccepted();
	}
}

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class RuntimePartnerHeadEquipmentSlotView : MonoBehaviour, IDropHandler, IInventoryEquipmentSlotDropHandler
{
	private RuntimeInventoryModificationCoordinator m_Coordinator;
	private InventorySlotView m_Slot;

	public void Bind(RuntimeInventoryModificationCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventoryPanelView panel = m_Slot.GetComponentInParent<InventoryPanelView>();
		if (panel != null)
		{
			InventorySlotUiUtility.ConfigureHeadEquipmentSlot(
				m_Slot, panel.EquipmentSlotAppearance, panel.LeadingEquipmentUsesVehicleLabels);
		}

		InventorySlotUiUtility.EnsureEquipmentSlotDropReceiver(this);
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventorySlotUiUtility.ApplyHeadEquipmentSlotHighlight(m_Slot, InventorySlotUiUtility.IsHelmetEquipDragActive());
	}

	public void OnDrop(PointerEventData eventData) => HandleEquipmentSlotDrop(eventData);

	public void HandleEquipmentSlotDrop(PointerEventData eventData)
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return;

		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		if (!m_Coordinator.TryEquipHelmetDragToPartnerHead() || eventData?.pointerDrag == null)
			return;

		NotifyDropAccepted(eventData.pointerDrag);
	}

	private static void NotifyDropAccepted(GameObject _pointerDrag)
	{
		if (_pointerDrag.TryGetComponent(out InventoryGroundToCharacterDrag groundDrag))
			groundDrag.NotifyDropAccepted();
		else if (_pointerDrag.TryGetComponent(out InventoryCharacterToGroundDrag characterDrag))
			characterDrag.NotifyDropAccepted();
	}
}

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class RuntimePartnerBackEquipmentSlotView : MonoBehaviour, IDropHandler, IInventoryEquipmentSlotDropHandler
{
	private RuntimeInventoryModificationCoordinator m_Coordinator;
	private InventorySlotView m_Slot;

	public void Bind(RuntimeInventoryModificationCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventoryPanelView panel = m_Slot.GetComponentInParent<InventoryPanelView>();
		if (panel != null)
		{
			InventorySlotUiUtility.ConfigureBackEquipmentSlot(
				m_Slot, panel.EquipmentSlotAppearance, panel.LeadingEquipmentUsesVehicleLabels);
		}

		InventorySlotUiUtility.EnsureEquipmentSlotDropReceiver(this);
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		InventorySlotUiUtility.ApplyBackEquipmentSlotHighlight(m_Slot, InventorySlotUiUtility.IsBackpackEquipDragActive());
	}

	public void OnDrop(PointerEventData eventData) => HandleEquipmentSlotDrop(eventData);

	public void HandleEquipmentSlotDrop(PointerEventData eventData)
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return;

		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		if (!m_Coordinator.TryEquipBackpackDragToPartnerBack() || eventData?.pointerDrag == null)
			return;

		NotifyDropAccepted(eventData.pointerDrag);
	}

	private static void NotifyDropAccepted(GameObject _pointerDrag)
	{
		if (_pointerDrag.TryGetComponent(out InventoryGroundToCharacterDrag groundDrag))
			groundDrag.NotifyDropAccepted();
		else if (_pointerDrag.TryGetComponent(out InventoryCharacterToGroundDrag characterDrag))
			characterDrag.NotifyDropAccepted();
	}
}
