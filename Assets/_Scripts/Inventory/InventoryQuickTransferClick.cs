using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Ctrl + ЛКМ по занятой ячейке: быстрый перенос через <see cref="RtsUnitSelectionManager.TryQuickTransferCtrlClick"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public class InventoryQuickTransferClick : MonoBehaviour, IPointerClickHandler
{
	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
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

		if (m_Slot == null || !m_Slot.HasItem)
			return;

		if (Time.unscaledTime < m_NextAllowedUnscaledTime)
			return;

		RtsUnitSelectionManager selectionManager = InventoryScreenBindings.Instance != null
			? InventoryScreenBindings.Instance.SelectionManager
			: null;
		if (selectionManager == null || !selectionManager.TryQuickTransferCtrlClick(m_Slot))
			return;

		m_NextAllowedUnscaledTime = Time.unscaledTime + m_ClickCooldown;
	}
	#endregion
}
