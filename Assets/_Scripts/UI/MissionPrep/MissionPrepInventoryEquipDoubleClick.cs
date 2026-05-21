using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Двойной клик по ячейке инвентаря пресета на экране предмиссии (без RTS SelectionManager).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepInventoryEquipDoubleClick : MonoBehaviour, IPointerClickHandler
{
	#region Constants
	private const float c_DoubleClickMaxDelaySeconds = 0.35f;
	#endregion

	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	#endregion

	#region Private Fields
	private float m_LastLeftClickUnscaledTime = -1f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void OnEnable()
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}
	#endregion

	#region IPointerClickHandler
	public void OnPointerClick(PointerEventData eventData)
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (eventData.button != PointerEventData.InputButton.Left || m_Slot == null || m_Coordinator == null)
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		if (!m_Coordinator.TryResolveInventorySlot(m_Slot, out bool isMainHand, out int bagIndex))
			return;

		CharacterInventory inventory = m_Coordinator.BoundInventory;
		if (inventory == null)
			return;

		UnitEquipment equipment = inventory.GetComponentInChildren<UnitEquipment>(true);

		if (isMainHand)
		{
			if (inventory.TryUnequipMainHandToBag())
				m_Coordinator.NotifyInventoryMutated();
			return;
		}

		if (bagIndex < 0)
			return;

		InventorySlotRuntimeData data = inventory.BagItems[bagIndex];
		if (data.Definition == null || !data.Definition.IsEquipment)
			return;

		if (equipment != null && inventory.TryMoveBagItemToMainHand(bagIndex, equipment))
			m_Coordinator.NotifyInventoryMutated();
	}
	#endregion
}
