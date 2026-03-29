using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Визуальное снаряжение на юните. Оружие — дочерний объект правой руки; цель IK левой руки берётся с префаба оружия.
/// </summary>
[DisallowMultipleComponent]
public class UnitEquipment : MonoBehaviour
{
	#region Serialized Fields
	[Header("Правая рука (оружие)")]
	[Tooltip("Кость или пустой объект в правой кисти — родитель для Equipped Visual Prefab.")]
	[FormerlySerializedAs("m_MainHand")]
	[SerializeField] private Transform m_RightHand;
	#endregion

	#region Private Fields
	private GameObject m_MainWeaponInstance;
	private ItemDefinition m_EquippedDefinition;
	private Transform m_LeftHandIkTarget;
	#endregion

	#region Public Properties
	/// <summary>Текущее экипированное оружие (тип). Null если слот пуст.</summary>
	public ItemDefinition EquippedDefinition => m_EquippedDefinition;

	/// <summary>Трансформ цели IK на инстансе оружия (дочерний по имени из ItemDefinition). Иначе null.</summary>
	public Transform LeftHandIkTargetTransform => m_LeftHandIkTarget;

	/// <summary>Корень инстанса визуала в руке. Null если нет префаба или слот пуст.</summary>
	public Transform MainWeaponRoot => m_MainWeaponInstance != null ? m_MainWeaponInstance.transform : null;

	/// <summary>Якорь правой руки (родитель визуала оружия).</summary>
	public Transform RightHandAnchor => m_RightHand;
	#endregion

	#region Public Methods
	public void ClearMainWeapon()
	{
		m_EquippedDefinition = null;
		m_LeftHandIkTarget = null;
		if (m_MainWeaponInstance != null)
		{
			Destroy(m_MainWeaponInstance);
			m_MainWeaponInstance = null;
		}
	}

	/// <summary>Экипировать предмет. Для General возвращает false. Предмет уже должен быть снят из списка инвентаря вызывающим кодом.</summary>
	public bool TryEquip(ItemDefinition _item)
	{
		if (_item == null || !_item.IsEquipment)
			return false;

		if (m_RightHand == null)
		{
			Debug.LogWarning($"{nameof(UnitEquipment)}: не задан якорь правой руки.", this);
			return false;
		}

		ClearMainWeapon();
		m_EquippedDefinition = _item;

		GameObject prefab = _item.EquippedVisualPrefab;
		if (prefab == null)
			return true;

		m_MainWeaponInstance = Instantiate(prefab, m_RightHand);
		m_MainWeaponInstance.transform.localPosition = _item.RightHandLocalPosition;
		m_MainWeaponInstance.transform.localRotation = _item.RightHandLocalRotation;
		DisablePhysicsOnEquippedVisual(m_MainWeaponInstance);

		string ikName = _item.LeftHandIkTargetChildName;
		if (!string.IsNullOrWhiteSpace(ikName))
			m_LeftHandIkTarget = FindChildRecursive(m_MainWeaponInstance.transform, ikName);

		return true;
	}

	public void ClearAllEquipment()
	{
		ClearMainWeapon();
	}
	#endregion

	#region Private Methods
	private static void DisablePhysicsOnEquippedVisual(GameObject _root)
	{
		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			pickups[i].enabled = false;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}
	#endregion
}
