using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Подсветка слота экипировки при наведении на шлем или оружие в сумке / доступном снаряжении.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class InventoryEquipmentEquipPreviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	#region Private Fields
	private InventorySlotView m_Slot;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData eventData)
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		if (m_Slot == null || !m_Slot.HasItem)
			return;

		InventorySlotRuntimeData data = m_Slot.Data;
		if (HelmetEquipUtility.CanEquipToHead(data))
			InventoryEquipmentEquipHoverContext.SetHoveredHelmet(data);
		else if (WeaponEquipUtility.CanEquipToMainHand(data))
			InventoryEquipmentEquipHoverContext.SetHoveredWeapon(data);
		else if (BackpackEquipUtility.CanEquipToBack(data))
			InventoryEquipmentEquipHoverContext.SetHoveredBackpack(data);
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		if (m_Slot == null)
			return;

		InventorySlotRuntimeData data = m_Slot.Data;
		InventoryEquipmentEquipHoverContext.ClearHoveredHelmet(data);
		InventoryEquipmentEquipHoverContext.ClearHoveredWeapon(data);
		InventoryEquipmentEquipHoverContext.ClearHoveredBackpack(data);
	}
	#endregion
}
