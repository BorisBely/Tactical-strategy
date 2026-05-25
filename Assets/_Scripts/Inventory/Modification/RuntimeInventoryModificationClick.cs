using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class RuntimeInventoryModificationClick : MonoBehaviour, IPointerClickHandler
{
	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	[SerializeField] private RuntimeInventoryModificationCoordinator m_Coordinator;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;
	}

	private void OnEnable()
	{
		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;
	}
	#endregion

	#region Public Methods
	public void Bind(RuntimeInventoryModificationCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region Event Handlers
	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		if (eventData.clickCount >= 2)
		{
			m_Coordinator.TryCollapseEmptyModificationSlotsForSlot(m_Slot);
			return;
		}

		m_Coordinator.TryToggleModificationPanel(m_Slot);
	}
	#endregion
}
