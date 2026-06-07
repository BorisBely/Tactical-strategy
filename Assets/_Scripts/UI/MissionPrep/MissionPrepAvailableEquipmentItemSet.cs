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
	[SerializeField] private AmmoDefinition m_MagazineAmmo556;
	[SerializeField] private AmmoDefinition m_MagazineAmmo762;
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
			ItemDefinition item = m_Items[i];
			AmmoDefinition ammo = ResolveMagazineAmmo(item);
			MissionPrepAvailableEquipmentCatalog.AppendDefinition(
				_outSlots,
				_seen,
				item,
				ammo,
				m_RoundsPerMagazine);
		}
	}

	private AmmoDefinition ResolveMagazineAmmo(ItemDefinition _item)
	{
		if (_item == null || _item.MagazineDefinition == null)
			return null;

		return _item.MagazineDefinition.SupportedCaliber switch
		{
			CaliberType.Five56By45 => m_MagazineAmmo556 != null ? m_MagazineAmmo556 : m_MagazineAmmo,
			CaliberType.Seven62By39 => m_MagazineAmmo762 != null ? m_MagazineAmmo762 : m_MagazineAmmo,
			CaliberType.Five45By39 => m_MagazineAmmo762 != null ? m_MagazineAmmo762 : m_MagazineAmmo,
			_ => m_MagazineAmmo
		};
	}
	#endregion
}
