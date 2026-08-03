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
	private VehicleTurretGunnerBridge m_GunnerBridge;
	private VehicleTurretReloadController m_ReloadController;
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
		if (m_GunnerBridge == null)
			TryGetComponent(out m_GunnerBridge);
		if (m_ReloadController == null)
			TryGetComponent(out m_ReloadController);

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

		if (ShouldDeferEmptyBoxToTurretReload(weaponState))
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

	private bool ShouldDeferEmptyBoxToTurretReload(WeaponRuntimeState _weaponState)
	{
		if (_weaponState == null)
			return false;

		if (_weaponState.HasAmmoInMagazine || _weaponState.HasRoundInChamber)
			return false;

		if (m_GunnerBridge == null || !m_GunnerBridge.HasBoundGunner)
			return false;

		if (m_ReloadController == null)
			return false;

		ItemDefinition activeWeapon = ActiveWeaponItem;
		return activeWeapon != null &&
		       activeWeapon.TurretWeaponVariant == TurretWeaponVariant.Browning127;
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
		{
			Debug.LogWarning(
				$"[{nameof(VehicleTurretEquipmentController)}] EquippedWeapon not found on '{pitch.name}'. "
				+ "Add it on the prefab pitch; runtime will not create one to avoid duplicate components.",
				pitch);
		}
		else
		{
			VehicleTurretCombatSockets.PrepareM2PitchRuntime(pitch);
		}

		TryFindIkDummy(pitch, "LeftHandIkTarget",
			new Vector3(-0.116f, 0.044f, -0.617f),
			Quaternion.Euler(-24.635f, 0.305f, 86.518f));
		TryFindIkDummy(pitch, "RightHandIkTarget",
			new Vector3(0.116f, 0.044f, -0.617f),
			Quaternion.Euler(-24.635f, -0.305f, -86.518f));

		m_ActiveEquippedWeapon = equipped;
	}

	private static void TryFindIkDummy(Transform _parent, string _name, Vector3 _expectedLocalPos, Quaternion _expectedLocalRot)
	{
		Transform t = _parent.Find(_name);
		if (t != null)
			return;
		Transform[] all = _parent.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name && all[i].IsChildOf(_parent))
				return;
		}

		Debug.LogWarning(
			$"[{nameof(VehicleTurretEquipmentController)}] IK dummy '{_name}' not found as child of '{_parent.name}' on vehicle '{_parent.root.name}'. "
			+ $"Expected localPos={_expectedLocalPos:F3}, localRot={_expectedLocalRot.eulerAngles}. "
			+ "Place it in the prefab or scene.", _parent);
	}
	#endregion
}
