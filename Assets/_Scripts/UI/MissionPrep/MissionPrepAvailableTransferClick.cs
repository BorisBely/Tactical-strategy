using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Ctrl + ЛКМ по ячейке доступного снаряжения — копия предмета в инвентарь пресета.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class MissionPrepAvailableTransferClick : MonoBehaviour, IPointerClickHandler
{
	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	[SerializeField, Min(0.05f)] private float m_ClickCooldown = 0.2f;
	#endregion

	#region Private Fields
	private float m_NextAllowedUnscaledTime;
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

		Keyboard kb = Keyboard.current;
		if (kb == null || !(kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Slot == null || !m_Slot.HasItem || m_Coordinator == null)
			return;

		if (Time.unscaledTime < m_NextAllowedUnscaledTime)
			return;

		if (!m_Coordinator.TryTransferAvailableSlotToPreset(m_Slot))
			return;

		m_NextAllowedUnscaledTime = Time.unscaledTime + m_ClickCooldown;
	}
	#endregion
}
