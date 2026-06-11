using UnityEngine;

/// <summary>
/// Отладочные команды экипировки шлема из сумки.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitHeadEquipmentDebug : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private CharacterInventory m_Inventory;
	[SerializeField] private UnitHeadEquipment m_HeadEquipment;
	[SerializeField] private UnitIndividualTraits m_Traits;
	[SerializeField] private UnitCharacterAppearance m_Appearance;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_HeadEquipment == null)
			m_HeadEquipment = GetComponent<UnitHeadEquipment>();
		if (m_Traits == null)
			m_Traits = GetComponent<UnitIndividualTraits>();
		if (m_Appearance == null)
			m_Appearance = GetComponent<UnitCharacterAppearance>();
	}
	#endregion

	#region Public Methods
	[ContextMenu("Equip First Helmet From Bag")]
	public void EquipFirstHelmetFromBag()
	{
		if (m_Inventory == null || m_HeadEquipment == null)
			return;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			if (!m_Inventory.TryGetInventorySlot(false, i, out InventorySlotRuntimeData slot))
				continue;

			if (!HelmetEquipUtility.CanEquipToHead(slot))
				continue;

			m_Inventory.TryMoveBagItemToHead(i, m_HeadEquipment, m_Traits, m_Appearance);
			return;
		}
	}

	[ContextMenu("Unequip Head To Bag")]
	public void UnequipHeadToBag()
	{
		m_Inventory?.TryUnequipHeadToBag();
	}
	#endregion
}
