using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Набор предметов для панели «доступное снаряжение» (mission prep).
/// </summary>
[CreateAssetMenu(
	fileName = "AvailableEquipmentItemSet",
	menuName = "Polygone/Mission Prep/Available Equipment Item Set",
	order = 20)]
public sealed class MissionPrepAvailableEquipmentItemSet : ScriptableObject
{
	#region Serialized Fields
	[SerializeField] private ItemDefinition[] m_Items = System.Array.Empty<ItemDefinition>();
	[SerializeField] private AmmoDefinition m_MagazineAmmo;
	[Tooltip("-1 = заполнить магазин по вместимости MagazineDefinition.")]
	[SerializeField] private int m_RoundsPerMagazine = -1;
	#endregion

	#region Public Methods
	public void AppendUnique(List<InventorySlotRuntimeData> _outSlots, HashSet<ItemDefinition> _seen)
	{
		if (_outSlots == null || _seen == null || m_Items == null)
			return;

		for (int i = 0; i < m_Items.Length; i++)
		{
			MissionPrepAvailableEquipmentCatalog.AppendDefinition(
				_outSlots,
				_seen,
				m_Items[i],
				m_MagazineAmmo,
				m_RoundsPerMagazine);
		}
	}
	#endregion
}
