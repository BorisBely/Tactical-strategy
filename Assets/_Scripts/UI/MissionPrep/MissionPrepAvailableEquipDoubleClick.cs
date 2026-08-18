using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Двойной клик по доступному снаряжению: оружие — в слот основной руки пресета; шлем — в сумку пресета.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepAvailableEquipDoubleClick : MonoBehaviour, IPointerClickHandler
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
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Slot == null || !m_Slot.HasItem || m_Coordinator == null)
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		ItemDefinition definition = m_Slot.Data.Definition;
		if (definition != null &&
		    definition.IsEquipment &&
		    definition.EquipmentKind == EquipmentKind.Helmet)
		{
			m_Coordinator.TryEquipAvailableSlotToHead(m_Slot);
			return;
		}

		if (MissionPrepBackpackEquipUtility.CanEquipToBack(m_Slot.Data))
		{
			m_Coordinator.TryEquipAvailableSlotToBack(m_Slot);
			return;
		}

		if (!MissionPrepWeaponEquipUtility.CanEquipToMainHand(m_Slot.Data))
			return;

		m_Coordinator.TryEquipAvailableSlotToMainHand(m_Slot);
	}
	#endregion
}
