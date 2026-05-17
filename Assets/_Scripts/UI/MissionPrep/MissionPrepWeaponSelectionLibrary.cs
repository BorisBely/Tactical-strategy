using UnityEngine;

/// <summary>
/// Список оружий, доступных на экране предмиссии. Каждый элемент — <see cref="ItemDefinition"/> категории Equipment,
/// вид оружия: <see cref="ItemDefinition.EquippedVisualPrefab"/> + пресеты модулей на нём через <see cref="EquippedWeapon"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepWeaponSelectionLibrary : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Экипируемые предметы-оружие (есть WeaponDefinition и префаб в руке для визуала).")]
	[SerializeField] private ItemDefinition[] m_AvailableWeapons = System.Array.Empty<ItemDefinition>();
	#endregion

	#region Public Properties
	public int WeaponCount => m_AvailableWeapons != null ? m_AvailableWeapons.Length : 0;
	#endregion

	#region Public Methods
	public ItemDefinition GetWeapon(int _index)
	{
		if (m_AvailableWeapons == null || _index < 0 || _index >= m_AvailableWeapons.Length)
			return null;

		return m_AvailableWeapons[_index];
	}

	public ItemDefinition[] GetWeaponsSnapshot()
	{
		return m_AvailableWeapons != null ? (ItemDefinition[])m_AvailableWeapons.Clone() : System.Array.Empty<ItemDefinition>();
	}
	#endregion

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (m_AvailableWeapons == null)
			return;

		for (int i = 0; i < m_AvailableWeapons.Length; i++)
			ValidateEntry(m_AvailableWeapons[i], i);
	}

	private static void ValidateEntry(ItemDefinition _item, int _indexInArray)
	{
		if (_item == null)
		{
			Debug.LogWarning($"MissionPrepWeaponSelectionLibrary: элемент [{_indexInArray}] пуст.");
			return;
		}

		if (!_item.IsEquipment || _item.EquipmentKind != EquipmentKind.Weapon || _item.WeaponDefinition == null || _item.EquippedVisualPrefab == null)
			Debug.LogWarning(
				$"MissionPrepWeaponSelectionLibrary: элемент [{_indexInArray}] «{_item.name}» должен быть снаряжением Kind=Weapon, с WeaponDefinition и Equipped Visual Prefab.",
				_item);
	}
#endif
}
