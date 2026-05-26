using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Двойной клик по оружию на панели доступного снаряжения — экипировка в слот основной руки пресета.
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

		if (!MissionPrepWeaponEquipUtility.CanEquipToMainHand(m_Slot.Data))
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		m_Coordinator.TryEquipAvailableSlotToMainHand(m_Slot);
	}
	#endregion
}
