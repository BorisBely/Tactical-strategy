using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Визуальное снаряжение на юните. Оружие — дочерний объект правой руки; цели IK рук берутся с префаба оружия.
/// Локальная поза оружия (relaxed/ready) — <see cref="UnitEquippedWeaponPose"/>.
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
	private GameObject m_DetachedWeaponInstance;
	private ItemDefinition m_EquippedDefinition;
	private Transform m_LeftHandIkTarget;
	private Transform m_RightHandIkTarget;
	private Transform m_RightHandIkTargetNotReady;
	private EquippedWeapon m_EquippedWeapon;
	#endregion

	#region Public Properties
	/// <summary>Текущее экипированное оружие (тип). Null если слот пуст.</summary>
	public ItemDefinition EquippedDefinition => m_EquippedDefinition;

	/// <summary>Скрипт на инстансе экипированного оружия (ствол, позже патроны и т.д.). Null если на префабе нет компонента.</summary>
	public EquippedWeapon EquippedWeapon => m_EquippedWeapon;

	/// <summary>Трансформ цели IK левой руки на инстансе оружия. Иначе null.</summary>
	public Transform LeftHandIkTargetTransform => m_LeftHandIkTarget;

	/// <summary>Трансформ цели IK правой руки (готов) на инстансе оружия. Иначе null.</summary>
	public Transform RightHandIkTargetTransform => m_RightHandIkTarget;

	/// <summary>Трансформ цели IK правой руки (не готов) на инстансе оружия. Иначе null.</summary>
	public Transform RightHandIkTargetNotReadyTransform => m_RightHandIkTargetNotReady;

	/// <summary>Корень инстанса визуала в руке. Null если нет префаба или слот пуст.</summary>
	public Transform MainWeaponRoot => m_MainWeaponInstance != null ? m_MainWeaponInstance.transform : null;

	/// <summary>Оружие отцеплено от руки, но остаётся экипированным в инвентаре.</summary>
	public bool HasDetachedWeaponVisual => m_DetachedWeaponInstance != null;

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
		m_MainWeaponInstance.transform.localPosition = Vector3.zero;
		m_MainWeaponInstance.transform.localRotation = Quaternion.identity;
		DisablePhysicsOnEquippedVisual(m_MainWeaponInstance);

		m_EquippedWeapon = m_MainWeaponInstance.GetComponentInChildren<EquippedWeapon>(true);

		RefreshHandIkTargets();

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
	/// Пересчитать цели IK обеих рук (например после установки/снятия рукоятки).
	/// </summary>
	public void RefreshHandIkTargets()
	{
		RefreshLeftHandIkTarget();
		RefreshRightHandIkTarget();
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

	/// <summary>Пересчитать цели IK правой руки (готов / не готов).</summary>
	public void RefreshRightHandIkTarget()
	{
		if (m_MainWeaponInstance == null)
		{
			m_RightHandIkTarget = null;
			m_RightHandIkTargetNotReady = null;
			return;
		}

		string readyName = m_EquippedDefinition != null ? m_EquippedDefinition.RightHandIkTargetChildName : null;
		if (string.IsNullOrWhiteSpace(readyName))
			m_RightHandIkTarget = null;
		else if (m_EquippedWeapon != null)
			m_RightHandIkTarget = m_EquippedWeapon.ResolveRightHandIkTargetTransform(readyName);
		else
			m_RightHandIkTarget = FindChildRecursive(m_MainWeaponInstance.transform, readyName);

		string notReadyName = m_EquippedDefinition != null ? m_EquippedDefinition.RightHandIkTargetNotReadyChildName : null;
		if (string.IsNullOrWhiteSpace(notReadyName))
			m_RightHandIkTargetNotReady = null;
		else if (m_EquippedWeapon != null)
			m_RightHandIkTargetNotReady = m_EquippedWeapon.ResolveRightHandIkTargetTransform(notReadyName);
		else
			m_RightHandIkTargetNotReady = FindChildRecursive(m_MainWeaponInstance.transform, notReadyName);
	}

	/// <summary>
	/// Отцепляет визуал оружия от руки и оставляет его в мире с физикой.
	/// Слот инвентаря и <see cref="EquippedDefinition"/> не меняются.
	/// </summary>
	public bool TryDetachMainWeaponToWorld(Vector3 _worldPosition, Quaternion _worldRotation, Vector3 _impulse)
	{
		if (m_MainWeaponInstance == null || m_EquippedDefinition == null || m_DetachedWeaponInstance != null)
			return false;

		m_DetachedWeaponInstance = m_MainWeaponInstance;
		m_MainWeaponInstance = null;
		m_EquippedWeapon = m_DetachedWeaponInstance.GetComponentInChildren<EquippedWeapon>(true);
		m_LeftHandIkTarget = null;
		m_RightHandIkTarget = null;
		m_RightHandIkTargetNotReady = null;

		Transform detachedTransform = m_DetachedWeaponInstance.transform;
		detachedTransform.SetParent(null, true);
		detachedTransform.SetPositionAndRotation(_worldPosition, _worldRotation);

		EnablePhysicsOnDetachedWeapon(m_DetachedWeaponInstance, _impulse);
		return true;
	}

	/// <summary>Возвращает отцеплённый визуал обратно в правую руку.</summary>
	public void RestoreDetachedMainWeaponToHand()
	{
		if (m_DetachedWeaponInstance != null)
		{
			Destroy(m_DetachedWeaponInstance);
			m_DetachedWeaponInstance = null;
			m_EquippedWeapon = null;
		}

		if (m_EquippedDefinition == null || m_RightHand == null || m_MainWeaponInstance != null)
			return;

		GameObject prefab = m_EquippedDefinition.EquippedVisualPrefab;
		if (prefab == null)
			return;

		m_MainWeaponInstance = Instantiate(prefab, m_RightHand);
		m_MainWeaponInstance.transform.localPosition = Vector3.zero;
		m_MainWeaponInstance.transform.localRotation = Quaternion.identity;
		DisablePhysicsOnEquippedVisual(m_MainWeaponInstance);
		m_EquippedWeapon = m_MainWeaponInstance.GetComponentInChildren<EquippedWeapon>(true);
		RefreshHandIkTargets();
		NotifyEquipmentChanged();
	}
	#endregion

	#region Private Methods
	private void ClearMainWeaponInternal(bool _notifyChanged)
	{
		if (m_DetachedWeaponInstance != null)
		{
			Destroy(m_DetachedWeaponInstance);
			m_DetachedWeaponInstance = null;
		}

		m_EquippedDefinition = null;
		m_LeftHandIkTarget = null;
		m_RightHandIkTarget = null;
		m_RightHandIkTargetNotReady = null;
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

	private static void EnablePhysicsOnDetachedWeapon(GameObject _root, Vector3 _impulse)
	{
		if (_root == null)
			return;

		if (!_root.TryGetComponent(out Rigidbody body))
		{
			body = _root.AddComponent<Rigidbody>();
			body.mass = 3f;
		}

		EnsureDetachedWeaponCollider(_root);
		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null)
				colliders[i].enabled = true;
		}

		body.isKinematic = false;
		body.useGravity = true;
		body.detectCollisions = true;
		body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		body.linearVelocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;

		if (_impulse.sqrMagnitude > 0.0001f)
			body.AddForce(_impulse, ForceMode.Impulse);
	}

	private static void EnsureDetachedWeaponCollider(GameObject _root)
	{
		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null && colliders[i].enabled)
				return;
		}

		Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
		if (renderers.Length == 0)
			return;

		Bounds bounds = renderers[0].bounds;
		for (int i = 1; i < renderers.Length; i++)
			bounds.Encapsulate(renderers[i].bounds);

		BoxCollider box = _root.AddComponent<BoxCollider>();
		box.center = _root.transform.InverseTransformPoint(bounds.center);
		Vector3 size = bounds.size;
		box.size = new Vector3(Mathf.Max(size.x, 0.05f), Mathf.Max(size.y, 0.05f), Mathf.Max(size.z, 0.05f));
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
