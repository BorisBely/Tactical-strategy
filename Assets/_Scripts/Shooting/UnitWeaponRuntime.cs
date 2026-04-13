using UnityEngine;

/// <summary>
/// Привязывает персонажа к постоянному состоянию экипированного оружия.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(55)]
public sealed class UnitWeaponRuntime : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Источник визуально экипированного оружия и ItemDefinition.")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Инвентарь персонажа, где живёт постоянное состояние оружия.")]
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[Tooltip("Временное состояние оружия, пока оно экипировано именно сейчас.")]
	[SerializeField] private EquippedWeaponTransientState m_TransientState = new EquippedWeaponTransientState();
	#endregion

	#region Private Fields
	private ItemInstanceState m_BoundItemState;
	private WeaponRuntimeState m_BoundWeaponState;
	#endregion

	#region Public Properties
	public WeaponRuntimeState RuntimeState => m_BoundWeaponState;
	public EquippedWeaponTransientState TransientState => m_TransientState;
	public ItemInstanceState BoundItemState => m_BoundItemState;
	public WeaponDefinition CurrentWeaponDefinition => m_BoundWeaponState != null ? m_BoundWeaponState.WeaponDefinition : null;
	public MagazineRuntimeState CurrentMagazine => m_BoundWeaponState != null ? m_BoundWeaponState.CurrentMagazine : null;
	public bool HasLoadedMagazine => m_BoundWeaponState != null && m_BoundWeaponState.HasMagazine;
	public bool HasAmmoInMagazine => m_BoundWeaponState != null && m_BoundWeaponState.HasAmmoInMagazine;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInChildren<UnitEquipment>(true);
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponentInChildren<CharacterInventory>(true);

		RefreshFromEquipment();
	}

	private void OnEnable()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;

		RefreshFromEquipment();
	}

	private void OnDisable()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
	}
	#endregion

	#region Public Methods
	public void RefreshFromEquipment()
	{
		InventorySlotRuntimeData equippedSlot =
			m_CharacterInventory != null ? m_CharacterInventory.MainHandEquipment : default;
		ItemDefinition equippedItem = equippedSlot.Definition;
		ItemInstanceState itemState = equippedSlot.InstanceState;
		WeaponRuntimeState weaponState = itemState != null ? itemState.WeaponState : null;

		if (equippedItem == null || equippedItem.WeaponDefinition == null || weaponState == null)
		{
			UnbindCurrentWeapon();
			return;
		}

		if (ReferenceEquals(m_BoundWeaponState, weaponState))
		{
			SyncInsertedMagazineVisual();
			return;
		}

		m_BoundItemState = itemState;
		m_BoundWeaponState = weaponState;
		m_TransientState.Clear();
		SyncInsertedMagazineVisual();
	}

	public bool TryInsertMagazine(InventorySlotRuntimeData _magazineItem)
	{
		if (m_BoundWeaponState == null)
			return false;

		bool inserted = m_BoundWeaponState.TryInsertMagazine(_magazineItem);
		if (inserted)
			SyncInsertedMagazineVisual();

		return inserted;
	}

	public bool TryEjectMagazine(out InventorySlotRuntimeData _magazineItem)
	{
		if (m_BoundWeaponState == null)
		{
			_magazineItem = default;
			return false;
		}

		bool ejected = m_BoundWeaponState.TryEjectMagazine(out _magazineItem);
		if (ejected)
			SyncInsertedMagazineVisual();

		return ejected;
	}

	public bool TryLoadRoundIntoInsertedMagazine(AmmoDefinition _ammoDefinition)
	{
		return m_BoundWeaponState != null && m_BoundWeaponState.TryLoadRoundIntoInsertedMagazine(_ammoDefinition);
	}

	public void SetSelectedFireMode(WeaponFireMode _fireMode)
	{
		if (m_BoundWeaponState == null)
			return;

		m_BoundWeaponState.SetSelectedFireMode(_fireMode);
	}

	public void SetAimProgress(float _value)
	{
		m_TransientState.SetAimProgress(_value);
	}

	public void SetRecoilPenalty(float _value)
	{
		m_TransientState.SetRecoilPenalty(_value);
	}

	public void SetWear(float _value)
	{
		if (m_BoundWeaponState == null)
			return;

		m_BoundWeaponState.SetWear(_value);
	}

	public void SetFouling(float _value)
	{
		if (m_BoundWeaponState == null)
			return;

		m_BoundWeaponState.SetFouling(_value);
	}

	public void SetNextAllowedShotTime(float _time)
	{
		m_TransientState.SetNextAllowedShotTime(_time);
	}

	public WeaponShotAttemptResult TryConsumeShot(float _currentTime, out AmmoDefinition _firedAmmoDefinition)
	{
		_firedAmmoDefinition = null;

		if (m_BoundWeaponState == null || m_BoundWeaponState.WeaponDefinition == null)
			return WeaponShotAttemptResult.NoWeapon;
		if (!m_BoundWeaponState.HasMagazine)
			return WeaponShotAttemptResult.NoMagazine;
		if (_currentTime < m_TransientState.NextAllowedShotTime)
			return WeaponShotAttemptResult.FireRateLimited;
		if (!m_BoundWeaponState.TryConsumeRound(out _firedAmmoDefinition))
			return WeaponShotAttemptResult.EmptyMagazine;

		m_TransientState.SetNextAllowedShotTime(_currentTime + GetSecondsPerShot());
		return WeaponShotAttemptResult.Success;
	}
	#endregion

	#region Private Methods
	private void HandleEquipmentChanged()
	{
		RefreshFromEquipment();
	}

	private float GetSecondsPerShot()
	{
		WeaponDefinition weaponDefinition = m_BoundWeaponState != null ? m_BoundWeaponState.WeaponDefinition : null;
		if (weaponDefinition == null || weaponDefinition.FireRateRpm <= 0f)
			return 0.1f;

		return 60f / weaponDefinition.FireRateRpm;
	}

	private void UnbindCurrentWeapon()
	{
		ClearInsertedMagazineVisual();
		m_BoundItemState = null;
		m_BoundWeaponState = null;
		m_TransientState.Clear();
	}

	private void SyncInsertedMagazineVisual()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon == null)
			return;

		InventorySlotRuntimeData currentMagazineItem = m_BoundWeaponState != null
			? m_BoundWeaponState.CurrentMagazineItem
			: default;
		ItemDefinition magazineDefinition = currentMagazineItem.Definition;
		if (magazineDefinition == null || currentMagazineItem.InstanceState == null || currentMagazineItem.InstanceState.MagazineState == null)
		{
			equippedWeapon.ClearInsertedMagazineVisual();
			return;
		}

		equippedWeapon.SetInsertedMagazineVisual(magazineDefinition);
	}

	private void ClearInsertedMagazineVisual()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon != null)
			equippedWeapon.ClearInsertedMagazineVisual();
	}
	#endregion
}
