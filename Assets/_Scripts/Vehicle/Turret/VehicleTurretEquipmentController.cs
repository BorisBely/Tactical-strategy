using UnityEngine;

/// <summary>
/// Синхронизация слотов <see cref="VehicleInventory"/> с визуалом турели и полным коробом без reload.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45)]
public sealed class VehicleTurretEquipmentController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleInventory m_Inventory;
	[SerializeField] private VehicleTurretVisualMount m_VisualMount;
	[SerializeField] private VehicleTurretAimController m_Aim;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;

	[Header("Default full boxes (no reload)")]
	[SerializeField] private ItemDefinition m_DefaultM2MagazineItem;
	[SerializeField] private AmmoDefinition m_DefaultM2Ammo;
	[SerializeField] private ItemDefinition m_DefaultMk19MagazineItem;
	[SerializeField] private AmmoDefinition m_DefaultMk19Ammo;
	#endregion

	#region Private Fields
	private EquippedWeapon m_ActiveEquippedWeapon;
	#endregion

	#region Public Properties
	public EquippedWeapon ActiveEquippedWeapon => m_ActiveEquippedWeapon;
	public WeaponRuntimeState ActiveWeaponRuntimeState
	{
		get
		{
			if (m_Inventory == null || !m_Inventory.HasTurretWeapon)
				return null;
			return m_Inventory.TurretWeapon.InstanceState != null
				? m_Inventory.TurretWeapon.InstanceState.WeaponState
				: null;
		}
	}

	public ItemDefinition ActiveWeaponItem =>
		m_Inventory != null && m_Inventory.HasTurretWeapon ? m_Inventory.TurretWeapon.Definition : null;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveRefs();
	}

	private void OnEnable()
	{
		if (m_Inventory != null)
			m_Inventory.InventoryChanged += HandleInventoryChanged;
		RefreshFromInventory();
	}

	private void OnDisable()
	{
		if (m_Inventory != null)
			m_Inventory.InventoryChanged -= HandleInventoryChanged;
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle)
	{
		m_Vehicle = _vehicle;
		ResolveRefs();
		RefreshFromInventory();
	}

	public void RefreshFromInventory()
	{
		if (m_Inventory == null || m_VisualMount == null)
			return;

		TurretWeaponVariant variant = TurretWeaponVariant.None;
		if (m_Inventory.HasTurretWeapon && m_Inventory.TurretWeapon.Definition != null)
		{
			variant = m_Inventory.TurretWeapon.Definition.TurretWeaponVariant;
			if (variant == TurretWeaponVariant.None)
				variant = TurretWeaponVariant.Browning127;
			EnsureFullLoadedBox(m_Inventory.TurretWeapon);
		}

		m_VisualMount.ShowWeaponVariant(variant);
		m_VisualMount.SetFrontalShieldVisible(m_Inventory.HasFrontalShield);
		m_VisualMount.SetSurroundShieldVisible(m_Inventory.HasSurroundShield);
		m_VisualMount.SetGunnerHatchVisible(m_Inventory.HasAnyEquipmentSlotOccupied);

		if (m_Aim != null)
		{
			m_Aim.SetActiveVariant(variant);

			bool hasShield = m_Inventory.HasFrontalShield || m_Inventory.HasSurroundShield;
			m_Aim.SetDriveType(hasShield ? TurretDriveType.Electric : TurretDriveType.Mechanical);
		}

		EnsureEquippedWeaponComponent(variant);
	}
	#endregion

	#region Private Methods
	private void ResolveRefs()
	{
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
		if (m_Inventory == null)
			TryGetComponent(out m_Inventory);
		if (m_VisualMount == null)
			TryGetComponent(out m_VisualMount);
		if (m_Aim == null)
			TryGetComponent(out m_Aim);
		if (m_Hierarchy == null)
			TryGetComponent(out m_Hierarchy);

		if (m_DefaultM2MagazineItem == null)
			m_DefaultM2MagazineItem = TurretContentCatalog.Get()?.M2MagazineBox;
		if (m_DefaultMk19MagazineItem == null)
			m_DefaultMk19MagazineItem = TurretContentCatalog.Get()?.Mk19MagazineBox;
		if (m_DefaultM2Ammo == null)
			m_DefaultM2Ammo = TurretContentCatalog.Get()?.Ammo127;
		if (m_DefaultMk19Ammo == null)
			m_DefaultMk19Ammo = TurretContentCatalog.Get()?.Ammo40;
	}

	private void HandleInventoryChanged(VehicleInventory _)
	{
		RefreshFromInventory();
	}

	private void EnsureFullLoadedBox(InventorySlotRuntimeData _weaponSlot)
	{
		if (_weaponSlot.IsEmpty || _weaponSlot.Definition == null)
			return;

		WeaponRuntimeState weaponState = _weaponSlot.InstanceState != null
			? _weaponSlot.InstanceState.WeaponState
			: null;
		if (weaponState == null)
			return;

		if (weaponState.HasMagazine && weaponState.HasAmmoInMagazine && weaponState.HasRoundInChamber)
			return;

		TurretWeaponVariant variant = _weaponSlot.Definition.TurretWeaponVariant;
		ItemDefinition magItem = variant == TurretWeaponVariant.Mk19
			? m_DefaultMk19MagazineItem
			: m_DefaultM2MagazineItem;
		AmmoDefinition ammo = variant == TurretWeaponVariant.Mk19
			? m_DefaultMk19Ammo
			: m_DefaultM2Ammo;

		if (magItem == null || magItem.MagazineDefinition == null || ammo == null)
			return;

		if (!weaponState.HasMagazine)
		{
			InventorySlotRuntimeData magSlot = BuildFullMagazineSlot(magItem, ammo);
			weaponState.TryInsertMagazine(magSlot);
		}
		else if (!weaponState.HasAmmoInMagazine)
		{
			MagazineRuntimeState mag = weaponState.CurrentMagazine;
			if (mag != null)
				mag.Configure(mag.Definition != null ? mag.Definition : magItem.MagazineDefinition, ammo,
					magItem.MagazineDefinition.Capacity);
		}

		if (!weaponState.HasRoundInChamber)
			weaponState.TryChamberRoundFromMagazine();
	}

	private static InventorySlotRuntimeData BuildFullMagazineSlot(ItemDefinition _magItem, AmmoDefinition _ammo)
	{
		InventorySlotRuntimeData slot = InventorySlotRuntimeData.FromDefinition(_magItem);
		MagazineRuntimeState magState = slot.InstanceState != null ? slot.InstanceState.MagazineState : null;
		if (magState != null && _magItem.MagazineDefinition != null)
			magState.Configure(_magItem.MagazineDefinition, _ammo, _magItem.MagazineDefinition.Capacity);
		return slot;
	}

	private void EnsureEquippedWeaponComponent(TurretWeaponVariant _variant)
	{
		m_ActiveEquippedWeapon = null;
		if (_variant == TurretWeaponVariant.None || m_Hierarchy == null)
			return;

		Transform pitch = m_Hierarchy.GetActiveWeaponPitch(_variant);
		if (pitch == null)
			return;

		if (!pitch.TryGetComponent(out EquippedWeapon equipped))
			equipped = pitch.gameObject.AddComponent<EquippedWeapon>();

		if (equipped.BarrelTransform == null)
		{
			Transform muzzle = CreateOrFindChild(pitch, EquippedWeapon.MuzzleExitTransformName);
			if (muzzle != null)
			{
				muzzle.localPosition = new Vector3(0f, 0f, 0.55f);
				muzzle.localRotation = Quaternion.identity;
			}
		}

		EnsureIkDummy(pitch, "LeftHandIkTarget",
			new Vector3(-0.116f, 0.044f, -0.617f),
			Quaternion.Euler(-24.635f, 0.305f, 86.518f));
		EnsureIkDummy(pitch, "RightHandIkTarget",
			new Vector3(0.116f, 0.044f, -0.617f),
			Quaternion.Euler(-24.635f, -0.305f, -86.518f));
		EnsureIkDummy(pitch, "LeftHandIkTarget_NotReady",
			new Vector3(-0.116f, 0.044f, -0.617f),
			Quaternion.Euler(-24.635f, 0.305f, 86.518f));
		EnsureIkDummy(pitch, "RightHandIkTarget_NotReady",
			new Vector3(0.116f, 0.044f, -0.617f),
			Quaternion.Euler(-24.635f, -0.305f, -86.518f));

		m_ActiveEquippedWeapon = equipped;
	}

	private static Transform CreateOrFindChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;
		Transform[] all = _parent.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name)
				return all[i];
		}

		GameObject go = new GameObject(_name);
		go.transform.SetParent(_parent, false);
		return go.transform;
	}

	private static void EnsureIkDummy(Transform _parent, string _name, Vector3 _localPos, Quaternion _localRot)
	{
		Transform t = CreateOrFindChild(_parent, _name);
		if (t.localPosition.sqrMagnitude < 0.0001f)
			t.localPosition = _localPos;
		if (ApproxIdentity(t.localRotation))
			t.localRotation = _localRot;
	}

	private static bool ApproxIdentity(Quaternion q)
	{
		return Mathf.Abs(q.x) < 0.0001f && Mathf.Abs(q.y) < 0.0001f
		    && Mathf.Abs(q.z) < 0.0001f && Mathf.Abs(q.w - 1f) < 0.0001f;
	}
	#endregion
}
