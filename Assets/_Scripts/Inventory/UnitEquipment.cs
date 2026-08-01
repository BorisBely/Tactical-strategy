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
	private Transform m_LeftHandIkTargetNotReady;
	private Transform m_RightHandIkTarget;
	private Transform m_RightHandIkTargetNotReady;
	private EquippedWeapon m_EquippedWeapon;
	private bool m_WeaponParentedToLeftHandForBoltCycle;
	private Transform m_BoltCycleOriginalWeaponParent;
	private Vector3 m_BoltCycleOriginalLocalPosition;
	private Quaternion m_BoltCycleOriginalLocalRotation = Quaternion.identity;
	private Vector3 m_BoltCycleOriginalLocalScale = Vector3.one;
	private Transform m_BoltCycleWeaponHoldAnchor;
	private EquippedWeapon m_TurretWeaponOverride;
	private ItemDefinition m_TurretWeaponDefinitionOverride;
	private bool m_PersonalWeaponHiddenForTurret;
	#endregion

	#region Public Properties
	/// <summary>Текущее экипированное оружие (тип). Null если слот пуст.</summary>
	public ItemDefinition EquippedDefinition =>
		m_TurretWeaponDefinitionOverride != null ? m_TurretWeaponDefinitionOverride : m_EquippedDefinition;

	/// <summary>Скрипт на инстансе экипированного оружия (ствол, позже патроны и т.д.). Null если на префабе нет компонента.</summary>
	public EquippedWeapon EquippedWeapon =>
		m_TurretWeaponOverride != null ? m_TurretWeaponOverride : m_EquippedWeapon;

	public bool IsOperatingVehicleTurret => m_TurretWeaponOverride != null;

	/// <summary>Left-hand IK target (high ready) on the weapon instance or foregrip.</summary>
	public Transform LeftHandIkTargetTransform => m_LeftHandIkTarget;

	/// <summary>Left-hand IK target (low ready / not ready) on the weapon instance or foregrip.</summary>
	public Transform LeftHandIkTargetNotReadyTransform => m_LeftHandIkTargetNotReady;

	/// <summary>Right‑hand IK target transform (high ready) on the weapon instance. Null otherwise.</summary>
	public Transform RightHandIkTargetTransform => m_RightHandIkTarget;

	/// <summary>Right‑hand IK target transform (low ready) on the weapon instance. Null otherwise.</summary>
	public Transform RightHandIkTargetNotReadyTransform => m_RightHandIkTargetNotReady;

	/// <summary>Корень инстанса визуала в руке. Null если нет префаба или слот пуст.</summary>
	public Transform MainWeaponRoot => m_MainWeaponInstance != null ? m_MainWeaponInstance.transform : null;

	public Transform EffectiveWeaponRoot
	{
		get
		{
			if (m_TurretWeaponOverride != null)
				return m_TurretWeaponOverride.transform;
			return m_MainWeaponInstance != null ? m_MainWeaponInstance.transform : null;
		}
	}

	/// <summary>Оружие отцеплено от руки, но остаётся экипированным в инвентаре.</summary>
	public bool HasDetachedWeaponVisual => m_DetachedWeaponInstance != null;

	/// <summary>Якорь правой руки (родитель визуала оружия).</summary>
	public Transform RightHandAnchor => m_RightHand;

	/// <summary>True, пока оружие временно удерживается стабильным якорем для болтового передёргивания.</summary>
	public bool IsWeaponHeldForBoltCycle => m_WeaponParentedToLeftHandForBoltCycle;

	/// <summary>Legacy alias for systems that only need to know that bolt-cycle hold owns weapon pose.</summary>
	public bool IsWeaponParentedToLeftHandForBoltCycle => IsWeaponHeldForBoltCycle;

	/// <summary>
	/// Отвязать оружие от правой кисти на стабильный якорь (world pose сохраняется), чтобы правая могла крутить затвор.
	/// </summary>
	public bool TryBeginBoltCycleLeftHandGrip()
	{
		if (m_MainWeaponInstance == null || m_RightHand == null)
			return false;

		if (m_WeaponParentedToLeftHandForBoltCycle && m_BoltCycleWeaponHoldAnchor != null)
			return true;

		Transform weaponTransform = m_MainWeaponInstance.transform;
		m_BoltCycleOriginalWeaponParent = weaponTransform.parent;
		m_BoltCycleOriginalLocalPosition = weaponTransform.localPosition;
		m_BoltCycleOriginalLocalRotation = weaponTransform.localRotation;
		m_BoltCycleOriginalLocalScale = weaponTransform.localScale;

		Vector3 worldPosition = weaponTransform.position;
		Quaternion worldRotation = weaponTransform.rotation;
		Vector3 worldScale = weaponTransform.lossyScale;

		m_BoltCycleWeaponHoldAnchor = CreateBoltCycleWeaponHoldAnchor(worldPosition, worldRotation);
		if (m_BoltCycleWeaponHoldAnchor == null)
			return false;

		weaponTransform.SetParent(m_BoltCycleWeaponHoldAnchor, true);
		weaponTransform.SetPositionAndRotation(worldPosition, worldRotation);
		PreserveWorldScaleAsLocal(weaponTransform, worldScale);

		m_WeaponParentedToLeftHandForBoltCycle = true;
		return true;
	}

	private Transform CreateBoltCycleWeaponHoldAnchor(Vector3 _worldPosition, Quaternion _worldRotation)
	{
		GameObject anchorObject = new GameObject("BoltCycleWeaponHoldAnchor");
		Transform anchorTransform = anchorObject.transform;
		anchorTransform.SetPositionAndRotation(_worldPosition, _worldRotation);
		anchorTransform.SetParent(transform, true);
		return anchorTransform;
	}

	private static void PreserveWorldScaleAsLocal(Transform _transform, Vector3 _worldScale)
	{
		if (_transform == null)
			return;

		Transform parent = _transform.parent;
		if (parent == null)
		{
			_transform.localScale = _worldScale;
			return;
		}

		Vector3 parentLossy = parent.lossyScale;
		_transform.localScale = new Vector3(
			ApproximatelyZero(parentLossy.x) ? _worldScale.x : _worldScale.x / parentLossy.x,
			ApproximatelyZero(parentLossy.y) ? _worldScale.y : _worldScale.y / parentLossy.y,
			ApproximatelyZero(parentLossy.z) ? _worldScale.z : _worldScale.z / parentLossy.z);
	}

	private static bool ApproximatelyZero(float _value)
	{
		return Mathf.Abs(_value) < 1e-8f;
	}

	/// <summary>Вернуть оружие к исходному родителю и локальной позе до болтового цикла.</summary>
	public void EndBoltCycleLeftHandGrip()
	{
		if (!m_WeaponParentedToLeftHandForBoltCycle)
			return;

		m_WeaponParentedToLeftHandForBoltCycle = false;
		if (m_MainWeaponInstance == null)
		{
			ClearBoltCycleHoldState();
			return;
		}

		Transform weaponTransform = m_MainWeaponInstance.transform;
		Transform restoreParent = m_BoltCycleOriginalWeaponParent != null ? m_BoltCycleOriginalWeaponParent : m_RightHand;
		if (restoreParent == null)
		{
			ClearBoltCycleHoldState();
			return;
		}

		weaponTransform.SetParent(restoreParent, false);
		weaponTransform.localPosition = m_BoltCycleOriginalLocalPosition;
		weaponTransform.localRotation = m_BoltCycleOriginalLocalRotation;
		weaponTransform.localScale = m_BoltCycleOriginalLocalScale;

		ClearBoltCycleHoldState();
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Временно подменить источник огня/IK на орудие турели без изменения инвентаря юнита.
	/// </summary>
	public void SetTurretWeaponOverride(EquippedWeapon _weapon, ItemDefinition _definition)
	{
		m_TurretWeaponOverride = _weapon;
		m_TurretWeaponDefinitionOverride = _definition;
		if (m_MainWeaponInstance != null && m_MainWeaponInstance.activeSelf)
		{
			m_MainWeaponInstance.SetActive(false);
			m_PersonalWeaponHiddenForTurret = true;
		}

		RefreshHandIkTargetsFromOverride();
		NotifyEquipmentChanged();
	}

	public void ClearTurretWeaponOverride()
	{
		m_TurretWeaponOverride = null;
		m_TurretWeaponDefinitionOverride = null;
		if (m_PersonalWeaponHiddenForTurret && m_MainWeaponInstance != null)
			m_MainWeaponInstance.SetActive(true);
		m_PersonalWeaponHiddenForTurret = false;
		RefreshHandIkTargets();
		NotifyEquipmentChanged();
	}

	private void RefreshHandIkTargetsFromOverride()
	{
		if (m_TurretWeaponOverride == null)
		{
			RefreshHandIkTargets();
			return;
		}

		ItemDefinition def = m_TurretWeaponDefinitionOverride;
		string leftReady = GetChildNameOr(def, def != null ? def.LeftHandIkTargetChildName : null, "LeftHandIkTarget");
		string leftNotReady = GetChildNameOr(def, def != null ? def.LeftHandIkTargetNotReadyChildName : null, "LeftHandIkTarget_NotReady");
		string rightReady = GetChildNameOr(def, def != null ? def.RightHandIkTargetChildName : null, "RightHandIkTarget");
		string rightNotReady = GetChildNameOr(def, def != null ? def.RightHandIkTargetNotReadyChildName : null, "RightHandIkTarget_NotReady");

		m_LeftHandIkTarget = m_TurretWeaponOverride.ResolveLeftHandIkTargetTransform(leftReady);
		m_LeftHandIkTargetNotReady = m_TurretWeaponOverride.ResolveLeftHandIkTargetTransform(leftNotReady);
		m_RightHandIkTarget = m_TurretWeaponOverride.ResolveRightHandIkTargetTransform(rightReady);
		m_RightHandIkTargetNotReady = m_TurretWeaponOverride.ResolveRightHandIkTargetTransform(rightNotReady);
	}

	private static string GetChildNameOr(ItemDefinition def, string fromDef, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(fromDef))
			return fromDef;
		return fallback;
	}

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
		SyncForeGripIkTargetsFromAsset();

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
		if (m_TurretWeaponOverride != null)
		{
			RefreshHandIkTargetsFromOverride();
			return;
		}

		if (m_MainWeaponInstance == null)
		{
			m_LeftHandIkTarget = null;
			m_LeftHandIkTargetNotReady = null;
			return;
		}

		string readyName = m_EquippedDefinition != null ? m_EquippedDefinition.LeftHandIkTargetChildName : null;
		if (string.IsNullOrWhiteSpace(readyName))
			m_LeftHandIkTarget = null;
		else if (m_EquippedWeapon != null)
			m_LeftHandIkTarget = m_EquippedWeapon.ResolveLeftHandIkTargetTransform(readyName);
		else
			m_LeftHandIkTarget = FindChildRecursive(m_MainWeaponInstance.transform, readyName);

		string notReadyName = m_EquippedDefinition != null ? m_EquippedDefinition.LeftHandIkTargetNotReadyChildName : null;
		if (string.IsNullOrWhiteSpace(notReadyName))
			m_LeftHandIkTargetNotReady = null;
		else if (m_EquippedWeapon != null)
			m_LeftHandIkTargetNotReady = m_EquippedWeapon.ResolveLeftHandIkTargetTransform(notReadyName);
		else
			m_LeftHandIkTargetNotReady = FindChildRecursive(m_MainWeaponInstance.transform, notReadyName);
	}

	/// <summary>Refresh right‑hand IK targets (high ready / low ready).</summary>
	public void RefreshRightHandIkTarget()
	{
		if (m_TurretWeaponOverride != null)
		{
			RefreshHandIkTargetsFromOverride();
			return;
		}

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
		m_LeftHandIkTargetNotReady = null;
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
		m_WeaponParentedToLeftHandForBoltCycle = false;
		ClearBoltCycleHoldState();

		if (m_DetachedWeaponInstance != null)
		{
			Destroy(m_DetachedWeaponInstance);
			m_DetachedWeaponInstance = null;
		}

		m_EquippedDefinition = null;
		m_LeftHandIkTarget = null;
		m_LeftHandIkTargetNotReady = null;
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

	private void ClearBoltCycleHoldState()
	{
		m_BoltCycleOriginalWeaponParent = null;
		m_BoltCycleOriginalLocalPosition = Vector3.zero;
		m_BoltCycleOriginalLocalRotation = Quaternion.identity;
		m_BoltCycleOriginalLocalScale = Vector3.one;

		if (m_BoltCycleWeaponHoldAnchor != null)
		{
			Destroy(m_BoltCycleWeaponHoldAnchor.gameObject);
			m_BoltCycleWeaponHoldAnchor = null;
		}
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

	public void SyncForeGripIkTargetsFromAsset()
	{
		if (m_EquippedWeapon == null || m_EquippedDefinition == null)
			return;

		Transform fgRoot = m_EquippedWeapon.UnderBarrelForegripVisualRoot;
		if (fgRoot == null)
			return;

		int fgIndex = 0;
		string name = fgRoot.name;
		for (int i = 5; i >= 1; i--)
		{
			if (name.Contains("ForeGrip" + i))
			{
				fgIndex = i;
				break;
			}
		}

		if (fgIndex < 1 || !m_EquippedDefinition.HasForeGripIkConfigured(fgIndex))
			return;

		if (m_LeftHandIkTarget != null && m_LeftHandIkTarget.IsChildOf(fgRoot))
		{
			m_LeftHandIkTarget.localPosition = m_EquippedDefinition.GetForeGripLeftHandIkReadyLocalPosition(fgIndex);
			m_LeftHandIkTarget.localRotation = m_EquippedDefinition.GetForeGripLeftHandIkReadyLocalRotation(fgIndex);
		}

		if (m_LeftHandIkTargetNotReady != null && m_LeftHandIkTargetNotReady.IsChildOf(fgRoot))
		{
			m_LeftHandIkTargetNotReady.localPosition = m_EquippedDefinition.GetForeGripLeftHandIkNotReadyLocalPosition(fgIndex);
			m_LeftHandIkTargetNotReady.localRotation = m_EquippedDefinition.GetForeGripLeftHandIkNotReadyLocalRotation(fgIndex);
		}
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
