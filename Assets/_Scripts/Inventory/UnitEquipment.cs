using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Визуальное снаряжение на юните. Оружие — дочерний объект правой руки; цель IK левой руки берётся с префаба оружия.
/// </summary>
[DisallowMultipleComponent]
public class UnitEquipment : MonoBehaviour
{
	#region Events
	public event Action EquipmentChanged;
	#endregion

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
	private EquippedWeapon m_EquippedWeapon;
	#endregion

	#region Public Properties
	/// <summary>Текущее экипированное оружие (тип). Null если слот пуст.</summary>
	public ItemDefinition EquippedDefinition => m_EquippedDefinition;

	/// <summary>Скрипт на инстансе экипированного оружия (ствол, позже патроны и т.д.). Null если на префабе нет компонента.</summary>
	public EquippedWeapon EquippedWeapon => m_EquippedWeapon;

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
		ClearMainWeaponInternal(true);
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

		ClearMainWeaponInternal(false);
		m_EquippedDefinition = _item;

		GameObject prefab = _item.EquippedVisualPrefab;
		if (prefab == null)
		{
			NotifyEquipmentChanged();
			return true;
		}

		m_MainWeaponInstance = Instantiate(prefab, m_RightHand);
		m_MainWeaponInstance.transform.localPosition = _item.RightHandLocalPosition;
		m_MainWeaponInstance.transform.localRotation = _item.RightHandLocalRotation;
		DisablePhysicsOnEquippedVisual(m_MainWeaponInstance);

		m_EquippedWeapon = m_MainWeaponInstance.GetComponentInChildren<EquippedWeapon>(true);

		RefreshLeftHandIkTarget();

		NotifyEquipmentChanged();
		return true;
	}

	public void ClearAllEquipment()
	{
		ClearMainWeapon();
	}

	public void SetMainWeaponVisualActive(bool _active)
	{
		if (m_MainWeaponInstance == null)
			return;

		m_MainWeaponInstance.SetActive(_active);
	}

	/// <summary>
	/// Пересчитать цель IK левой руки (например после установки/снятия рукоятки).
	/// </summary>
	public void RefreshLeftHandIkTarget()
	{
		string ikName = m_EquippedDefinition != null ? m_EquippedDefinition.LeftHandIkTargetChildName : null;
		if (string.IsNullOrWhiteSpace(ikName) || m_MainWeaponInstance == null)
		{
			m_LeftHandIkTarget = null;
			return;
		}

		if (m_EquippedWeapon != null)
			m_LeftHandIkTarget = m_EquippedWeapon.ResolveLeftHandIkTargetTransform(ikName);
		else
			m_LeftHandIkTarget = FindChildRecursive(m_MainWeaponInstance.transform, ikName);
	}
	#endregion

	#region Private Methods
	private void ClearMainWeaponInternal(bool _notifyChanged)
	{
		m_EquippedDefinition = null;
		m_LeftHandIkTarget = null;
		m_EquippedWeapon = null;
		if (m_MainWeaponInstance != null)
		{
			Destroy(m_MainWeaponInstance);
			m_MainWeaponInstance = null;
		}

		if (_notifyChanged)
			NotifyEquipmentChanged();
	}

	private void NotifyEquipmentChanged()
	{
		EquipmentChanged?.Invoke();
	}

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
