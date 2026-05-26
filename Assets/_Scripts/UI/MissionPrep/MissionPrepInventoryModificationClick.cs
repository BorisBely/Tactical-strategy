using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepInventoryModificationClick : MonoBehaviour, IPointerClickHandler
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

	#region Public Methods
	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
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

		if (m_Slot == null || !m_Slot.HasItem ||
		    !ItemModificationUtility.IsModifiableWeapon(m_Slot.Data.Definition))
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (unityReportsDouble || timedDouble)
		{
			m_Coordinator?.TryCollapseModificationPanelForDoubleClick(m_Slot);
			return;
		}

		m_Coordinator?.TryToggleModificationPanel(m_Slot);
	}
	#endregion
}
