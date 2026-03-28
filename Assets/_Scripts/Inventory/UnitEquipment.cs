using UnityEngine;

/// <summary>
/// Модель основного оружия на юните. Остальное снаряжение — отдельным шагом, без заготовок здесь.
/// </summary>
[DisallowMultipleComponent]
public class UnitEquipment : MonoBehaviour
{
	#region Serialized Fields
	[Header("Основное оружие")]
	[Tooltip("Кость / пустой объект в основной руке.")]
	[SerializeField] private Transform m_MainHand;
	#endregion

	#region Private Fields
	private GameObject m_MainWeaponInstance;
	#endregion

	#region Public Methods
	public void ClearMainWeapon()
	{
		if (m_MainWeaponInstance != null)
		{
			Destroy(m_MainWeaponInstance);
			m_MainWeaponInstance = null;
		}
	}

	/// <summary>Экипировать предмет. Для General возвращает false.</summary>
	public bool TryEquip(ItemDefinition _item)
	{
		if (_item == null || !_item.IsEquipment)
			return false;

		if (m_MainHand == null)
		{
			Debug.LogWarning($"{nameof(UnitEquipment)}: не задан якорь Main Hand.", this);
			return false;
		}

		ClearMainWeapon();

		GameObject prefab = _item.EquippedVisualPrefab;
		if (prefab == null)
			return true;

		m_MainWeaponInstance = Instantiate(prefab, m_MainHand.position, m_MainHand.rotation, m_MainHand);
		return true;
	}

	public void ClearAllEquipment()
	{
		ClearMainWeapon();
	}
	#endregion
}
