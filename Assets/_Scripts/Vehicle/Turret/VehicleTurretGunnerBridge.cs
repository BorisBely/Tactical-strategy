using UnityEngine;

/// <summary>
/// Мост: юнит в слоте Gunner стреляет из орудия машины (пехотный стек), без ready/reload.
/// Личный инвентарь юнита не меняется.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class VehicleTurretGunnerBridge : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleSeatLayout m_Seats;
	[SerializeField] private VehicleTurretAimController m_Aim;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;
	[SerializeField] private VehicleInventory m_Inventory;
	[SerializeField, Range(1f, 30f)] private float m_BarrelAlignToleranceDegrees = 8f;
	#endregion

	#region Private Fields
	private RtsUnitMember m_BoundGunner;
	private UnitWeaponFireController m_BoundFire;
	private UnitWeaponRuntime m_BoundRuntime;
	private UnitEquipment m_BoundEquipment;
	private UnitWeaponReadyHandsLayer m_BoundReady;
	private UnitVision m_BoundVision;
	private bool m_SavedRequireReady = true;
	private bool m_SavedTryReload = true;
	#endregion

	#region Public Properties
	public RtsUnitMember BoundGunner => m_BoundGunner;
	public bool HasBoundGunner => m_BoundGunner != null;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveRefs();
	}

	private void OnEnable()
	{
		if (m_Seats != null)
			m_Seats.OccupancyChanged += HandleOccupancyChanged;
		if (m_Inventory != null)
			m_Inventory.InventoryChanged += HandleInventoryChanged;
		ReconcileGunnerBinding();
	}

	private void OnDisable()
	{
		if (m_Seats != null)
			m_Seats.OccupancyChanged -= HandleOccupancyChanged;
		if (m_Inventory != null)
			m_Inventory.InventoryChanged -= HandleInventoryChanged;
		UnbindGunner();
	}

	private void LateUpdate()
	{
		if (m_BoundGunner == null || m_Aim == null || m_BoundVision == null)
			return;

		Transform target = m_BoundVision.GetEngageableVisibleTarget();
		if (target == null)
			target = m_BoundVision.VisibleTarget;
		if (target == null)
			return;

		m_Aim.SetActive(true);
		m_Aim.SetAimPoint(target.position);
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle)
	{
		m_Vehicle = _vehicle;
		ResolveRefs();
		ReconcileGunnerBinding();
	}

	public void ReconcileGunnerBinding()
	{
		if (m_Seats == null || !m_Seats.TryGetOccupant(VehicleSeatId.Gunner, out RtsUnitMember gunner) || gunner == null)
		{
			UnbindGunner();
			return;
		}

		if (m_BoundGunner == gunner)
		{
			RefreshWeaponBind();
			return;
		}

		UnbindGunner();
		BindGunner(gunner);
	}
	#endregion

	#region Private Methods
	private void ResolveRefs()
	{
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
		if (m_Seats == null)
			TryGetComponent(out m_Seats);
		if (m_Aim == null)
			TryGetComponent(out m_Aim);
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		if (m_Inventory == null)
			TryGetComponent(out m_Inventory);
	}

	private void HandleOccupancyChanged()
	{
		ReconcileGunnerBinding();
	}

	private void HandleInventoryChanged(VehicleInventory _)
	{
		RefreshWeaponBind();
	}

	private void BindGunner(RtsUnitMember _gunner)
	{
		if (_gunner == null)
			return;

		m_BoundGunner = _gunner;
		m_BoundFire = _gunner.GetComponent<UnitWeaponFireController>();
		m_BoundRuntime = _gunner.GetComponent<UnitWeaponRuntime>();
		m_BoundEquipment = _gunner.GetComponent<UnitEquipment>();
		m_BoundReady = _gunner.GetComponent<UnitWeaponReadyHandsLayer>();
		m_BoundVision = _gunner.GetComponent<UnitVision>();

		if (m_BoundFire != null)
		{
			m_SavedRequireReady = m_BoundFire.RequireReady;
			m_SavedTryReload = m_BoundFire.TryReloadWhenOutOfAmmo;
			m_BoundFire.RequireReady = false;
			m_BoundFire.TryReloadWhenOutOfAmmo = false;
		}

		if (m_BoundReady != null)
			m_BoundReady.SetReadyWanted(true);

		RefreshWeaponBind();
	}

	private void UnbindGunner()
	{
		if (m_BoundGunner == null)
		{
			m_Aim?.SetActive(false);
			m_Aim?.ClearAim();
			return;
		}

		if (m_BoundFire != null)
		{
			m_BoundFire.StopFiring();
			m_BoundFire.RequireReady = m_SavedRequireReady;
			m_BoundFire.TryReloadWhenOutOfAmmo = m_SavedTryReload;
		}

		m_BoundRuntime?.ClearExternalWeaponBind();
		m_BoundEquipment?.ClearTurretWeaponOverride();

		m_BoundGunner = null;
		m_BoundFire = null;
		m_BoundRuntime = null;
		m_BoundEquipment = null;
		m_BoundReady = null;
		m_BoundVision = null;

		m_Aim?.SetActive(false);
		m_Aim?.ClearAim();
	}

	private void RefreshWeaponBind()
	{
		if (m_BoundGunner == null || m_BoundRuntime == null || m_BoundEquipment == null)
			return;

		if (m_Inventory == null || !m_Inventory.HasTurretWeapon ||
		    m_Equipment == null || m_Equipment.ActiveEquippedWeapon == null)
		{
			m_BoundRuntime.ClearExternalWeaponBind();
			m_BoundEquipment.ClearTurretWeaponOverride();
			m_Aim?.SetActive(false);
			return;
		}

		InventorySlotRuntimeData weaponSlot = m_Inventory.TurretWeapon;
		m_BoundRuntime.BindExternalWeaponState(weaponSlot.InstanceState);
		m_BoundEquipment.SetTurretWeaponOverride(
			m_Equipment.ActiveEquippedWeapon,
			weaponSlot.Definition);

		m_Aim?.SetActive(true);
		m_Aim?.SetActiveVariant(
			weaponSlot.Definition != null
				? weaponSlot.Definition.TurretWeaponVariant
				: TurretWeaponVariant.Browning127);
	}
	#endregion
}
