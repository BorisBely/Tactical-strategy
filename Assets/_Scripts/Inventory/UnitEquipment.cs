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
	private Transform m_GripLeftHandTarget;
	private bool m_UsesWeaponGripRig;
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

	/// <summary>Cached left grip: ForeGrip LeftHandIK if attached, else WeaponGripRig.LeftHandIK.</summary>
	public Transform GripLeftHandTarget => m_GripLeftHandTarget;

	public bool UsesWeaponGripRig => m_UsesWeaponGripRig;

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

		const string leftReady = "LeftHandIkTarget";
		const string leftNotReady = "LeftHandIkTarget_NotReady";
		const string rightReady = "RightHandIkTarget";
		const string rightNotReady = "RightHandIkTarget_NotReady";

		Transform pitch = m_TurretWeaponOverride.transform;
		m_LeftHandIkTarget = ResolveTurretHandIkTarget(pitch, m_TurretWeaponOverride, leftReady, true);
		m_LeftHandIkTargetNotReady = ResolveTurretHandIkTarget(pitch, m_TurretWeaponOverride, leftNotReady, true);
		m_RightHandIkTarget = ResolveTurretHandIkTarget(pitch, m_TurretWeaponOverride, rightReady, false);
		m_RightHandIkTargetNotReady = ResolveTurretHandIkTarget(pitch, m_TurretWeaponOverride, rightNotReady, false);
	}

	private static Transform ResolveTurretHandIkTarget(
		Transform _pitch,
		EquippedWeapon _weapon,
		string _childName,
		bool _leftHand)
	{
		if (_pitch == null || string.IsNullOrWhiteSpace(_childName))
			return null;

		Transform fromWeapon = _leftHand
			? _weapon.ResolveLeftHandIkTargetTransform(_childName)
			: _weapon.ResolveRightHandIkTargetTransform(_childName);
		if (fromWeapon != null)
			return fromWeapon;

		Transform direct = _pitch.Find(_childName);
		if (direct != null)
			return direct;

		Transform[] all = _pitch.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _childName)
				return all[i];
		}

		return null;
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
		ResolveGripTargets();
		WeaponGripResolver resolver = GetComponent<WeaponGripResolver>();
		if (resolver != null)
			resolver.RebuildCache();
	}

	/// <summary>
	/// Cache GripRig / ForeGrip hand targets. Call on equip and after attachment visual refresh.
	/// Creates GripRig at runtime from ItemDefinition Ready IK if prefab was not migrated yet.
	/// </summary>
	public void ResolveGripTargets()
	{
		m_GripLeftHandTarget = null;
		m_UsesWeaponGripRig = false;

		if (m_TurretWeaponOverride != null)
			return;

		if (m_MainWeaponInstance == null)
			return;

		WeaponGripRig gripRig = m_MainWeaponInstance.GetComponentInChildren<WeaponGripRig>(true);
		if (gripRig == null || !gripRig.HasValidGrips)
			gripRig = EnsureGripRigRuntime(m_MainWeaponInstance.transform, m_EquippedDefinition);

		if (gripRig == null)
			return;

		gripRig.BuildCache();
		m_UsesWeaponGripRig = gripRig.HasRightHandIkTargets;

		Transform foregripRoot = m_EquippedWeapon != null ? m_EquippedWeapon.UnderBarrelForegripVisualRoot : null;
		if (foregripRoot != null)
		{
			WeaponForeGrip foreGrip = EnsureForeGripRuntime(foregripRoot, m_EquippedDefinition);
			if (foreGrip != null && foreGrip.LeftHandGrip != null)
			{
				m_GripLeftHandTarget = foreGrip.LeftHandGrip;
				return;
			}
		}

		m_GripLeftHandTarget = gripRig.LeftHandIk;
	}

	private static WeaponGripRig EnsureGripRigRuntime(Transform _weaponRoot, ItemDefinition _)
	{
		if (_weaponRoot == null)
			return null;

		WeaponGripRig gripRig = _weaponRoot.GetComponent<WeaponGripRig>();
		if (gripRig == null)
			gripRig = _weaponRoot.gameObject.AddComponent<WeaponGripRig>();

		Transform gripRoot = _weaponRoot.Find(WeaponGripRig.GripRigChildName);
		if (gripRoot == null)
		{
			var go = new GameObject(WeaponGripRig.GripRigChildName);
			gripRoot = go.transform;
			gripRoot.SetParent(_weaponRoot, false);
		}

		Transform left = EnsureNamedChild(gripRoot, WeaponGripRig.LeftHandIkName);
		if (left == null)
			left = EnsureNamedChild(gripRoot, WeaponGripRig.LeftHandGripName);
		gripRig.SetLeftHandIk(left);
		EnsureRightHandIkTreeRuntime(gripRoot, gripRig);

		return gripRig;
	}

	private static void EnsureRightHandIkTreeRuntime(Transform _gripRoot, WeaponGripRig _gripRig)
	{
		if (_gripRoot == null || _gripRig == null)
			return;

		Transform rightRoot = _gripRoot.Find(WeaponGripRig.RightHandIkRootName)
		                    ?? _gripRoot.Find(WeaponGripRig.RightHandRootName);
		if (rightRoot == null)
		{
			var go = new GameObject(WeaponGripRig.RightHandIkRootName);
			rightRoot = go.transform;
			rightRoot.SetParent(_gripRoot, false);
		}

		Transform standing = EnsureNamedChild(rightRoot, WeaponGripRig.StandingName);
		Transform crouch = EnsureNamedChild(rightRoot, WeaponGripRig.CrouchName);
		Transform vehicle = EnsureNamedChild(rightRoot, WeaponGripRig.VehicleName);

		_gripRig.SetRightHandPoseTargets(
			EnsureNamedChild(standing, WeaponGripRig.ReadyName),
			EnsureNamedChild(standing, WeaponGripRig.NotReadyName),
			EnsureNamedChild(crouch, WeaponGripRig.ReadyName),
			EnsureNamedChild(crouch, WeaponGripRig.NotReadyName),
			EnsureNamedChild(vehicle, WeaponGripRig.ReadyName),
			EnsureNamedChild(vehicle, WeaponGripRig.NotReadyName));
	}

	private static WeaponForeGrip EnsureForeGripRuntime(Transform _foregripRoot, ItemDefinition _)
	{
		if (_foregripRoot == null)
			return null;

		WeaponForeGrip foreGrip = _foregripRoot.GetComponent<WeaponForeGrip>();
		if (foreGrip == null)
			foreGrip = _foregripRoot.GetComponentInChildren<WeaponForeGrip>(true);
		if (foreGrip == null)
			foreGrip = _foregripRoot.gameObject.AddComponent<WeaponForeGrip>();

		Transform left = foreGrip.LeftHandGrip;
		if (left == null)
		{
			left = EnsureNamedChild(_foregripRoot, WeaponForeGrip.LeftHandGripName);
			foreGrip.SetLeftHandGrip(left);
		}

		return foreGrip;
	}

	private static Transform EnsureNamedChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;

		var go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return t;
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

		const string readyName = "LeftHandIkTarget";
		const string notReadyName = "LeftHandIkTarget_NotReady";
		if (m_EquippedWeapon != null)
			m_LeftHandIkTarget = m_EquippedWeapon.ResolveLeftHandIkTargetTransform(readyName);
		else
			m_LeftHandIkTarget = FindChildRecursive(m_MainWeaponInstance.transform, readyName);

		if (m_EquippedWeapon != null)
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

		const string readyName = "RightHandIkTarget";
		const string notReadyName = "RightHandIkTarget_NotReady";
		if (m_EquippedWeapon != null)
			m_RightHandIkTarget = m_EquippedWeapon.ResolveRightHandIkTargetTransform(readyName);
		else
			m_RightHandIkTarget = FindChildRecursive(m_MainWeaponInstance.transform, readyName);

		if (m_EquippedWeapon != null)
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
		m_GripLeftHandTarget = null;
		m_UsesWeaponGripRig = false;
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
		// ForeGrip IK coords no longer live on ItemDefinition; GripRig / WeaponForeGrip own the targets.
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
