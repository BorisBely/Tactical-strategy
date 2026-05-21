using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Двойной ЛКМ: слот основного оружия — снять в сумку; строка сумки — экипировать.
/// Если в руках уже тот же тип предмета, повторный двойной клик по строке в сумке снова убирает оружие в сумку.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public class InventoryEquipDoubleClick : MonoBehaviour, IPointerClickHandler
{
	#region Constants
	private const float c_DoubleClickMaxDelaySeconds = 0.35f;
	#endregion

	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	#endregion

	#region Private Fields
	private float m_LastLeftClickUnscaledTime = -1f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region IPointerClickHandler
	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		if (IsMissionPrepPresetInventorySlot())
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		if (bindings == null || selectionManager == null || m_Slot == null)
			return;

		selectionManager.TryEquipFromCharacterBagDoubleClick(m_Slot);
	}

	private bool IsMissionPrepPresetInventorySlot()
	{
		MissionPrepLoadoutCoordinator coordinator = MissionPrepLoadoutCoordinator.Instance;
		if (coordinator == null || coordinator.PresetInventoryPanel == null || m_Slot == null)
			return false;

		return m_Slot.GetComponentInParent<InventoryPanelView>() == coordinator.PresetInventoryPanel;
	}
	#endregion
}
