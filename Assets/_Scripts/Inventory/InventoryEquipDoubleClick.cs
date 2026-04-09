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
	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
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
		if (eventData.clickCount < 2)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		if (selectionManager == null || m_Slot == null)
			return;

		selectionManager.TryEquipFromCharacterBagDoubleClick(m_Slot);
	}
	#endregion
}
