using System;
using UnityEngine;

/// <summary>
/// Постоянное состояние конкретного экземпляра магазина.
/// </summary>
[Serializable]
public sealed class MagazineRuntimeState
{
	#region Private Fields
	[SerializeField] private MagazineDefinition m_Definition;
	[SerializeField] private AmmoDefinition m_LoadedAmmoDefinition;
	[SerializeField, Min(0)] private int m_CurrentAmmoCount;
	#endregion

	#region Public Properties
	public MagazineDefinition Definition => m_Definition;
	public AmmoDefinition LoadedAmmoDefinition => m_LoadedAmmoDefinition;
	public int CurrentAmmoCount => m_CurrentAmmoCount;
	public bool HasMagazine => m_Definition != null;
	public bool HasAmmo => m_CurrentAmmoCount > 0 && m_LoadedAmmoDefinition != null;
	#endregion

	#region Public Methods
	public void Clear()
	{
		m_Definition = null;
		m_LoadedAmmoDefinition = null;
		m_CurrentAmmoCount = 0;
	}

	public void Configure(MagazineDefinition _definition, AmmoDefinition _ammoDefinition, int _ammoCount)
	{
		m_Definition = _definition;
		m_LoadedAmmoDefinition = _ammoDefinition;
		m_CurrentAmmoCount = Mathf.Max(0, _definition != null ? Mathf.Min(_ammoCount, _definition.Capacity) : 0);
	}

	public void SetAmmoCount(int _ammoCount)
	{
		if (m_Definition == null)
		{
			m_CurrentAmmoCount = 0;
			return;
		}

		m_CurrentAmmoCount = Mathf.Clamp(_ammoCount, 0, m_Definition.Capacity);
	}

	public bool CanLoadAmmo(AmmoDefinition _ammoDefinition)
	{
		if (m_Definition == null || _ammoDefinition == null)
			return false;
		if (m_CurrentAmmoCount >= m_Definition.Capacity)
			return false;
		if (m_Definition.SupportedCaliber != CaliberType.None &&
			_ammoDefinition.Caliber != m_Definition.SupportedCaliber)
			return false;
		if (m_LoadedAmmoDefinition != null && m_LoadedAmmoDefinition != _ammoDefinition)
			return false;

		return true;
	}

	public bool TryLoadRound(AmmoDefinition _ammoDefinition)
	{
		if (!CanLoadAmmo(_ammoDefinition))
			return false;

		m_LoadedAmmoDefinition = _ammoDefinition;
		m_CurrentAmmoCount++;
		return true;
	}

	public bool TryConsumeRound(out AmmoDefinition _ammoDefinition)
	{
		_ammoDefinition = null;
		if (!HasAmmo)
			return false;

		_ammoDefinition = m_LoadedAmmoDefinition;
		m_CurrentAmmoCount = Mathf.Max(0, m_CurrentAmmoCount - 1);
		if (m_CurrentAmmoCount == 0)
			m_LoadedAmmoDefinition = null;

		return true;
	}
	#endregion
}

/// <summary>
/// Постоянное состояние конкретного экземпляра оружия.
/// </summary>
[Serializable]
public sealed class WeaponRuntimeState
{
	#region Private Fields
	[SerializeField] private WeaponDefinition m_WeaponDefinition;
	[SerializeField] private WeaponFireMode m_SelectedFireMode = WeaponFireMode.SemiAuto;
	[Tooltip("Вставленный магазин хранится «плоско», без InventorySlotRuntimeData, чтобы не было цикла сериализации ItemInstanceState → Weapon → слот → ItemInstanceState.")]
	[SerializeField] private ItemDefinition m_CurrentMagazineDefinition;
	[SerializeField] private string m_CurrentMagazineDisplayName;
	[SerializeField] private string m_CurrentMagazineLocalizationKey;
	[SerializeField] private MagazineRuntimeState m_CurrentMagazineRuntimeState;
	[SerializeField, Range(0f, 1f)] private float m_Wear01;
	[SerializeField, Range(0f, 1f)] private float m_Fouling01;
	[Tooltip("Патрон в патроннике (после снаряжения затвора). Выстрел идёт из патронника; затем подача из магазина.")]
	[SerializeField] private bool m_HasRoundInChamber;
	[SerializeField] private AmmoDefinition m_ChamberedAmmoDefinition;

	[System.NonSerialized]
	private ItemInstanceState m_CachedMagazineSlotOwner;
	#endregion

	#region Public Properties
	public WeaponDefinition WeaponDefinition => m_WeaponDefinition;
	public WeaponFireMode SelectedFireMode => m_SelectedFireMode;
	public InventorySlotRuntimeData CurrentMagazineItem => BuildCurrentMagazineSlot();
	public MagazineRuntimeState CurrentMagazine => m_CurrentMagazineRuntimeState;
	public AmmoDefinition CurrentAmmoDefinition => CurrentMagazine != null ? CurrentMagazine.LoadedAmmoDefinition : null;
	public int CurrentAmmoCount => CurrentMagazine != null ? CurrentMagazine.CurrentAmmoCount : 0;
	public bool HasMagazine => m_CurrentMagazineDefinition != null && CurrentMagazine != null && CurrentMagazine.HasMagazine;
	public bool HasAmmoInMagazine => CurrentMagazine != null && CurrentMagazine.HasAmmo;
	public bool HasRoundInChamber => m_HasRoundInChamber && m_ChamberedAmmoDefinition != null;
	public AmmoDefinition ChamberedAmmoDefinition => m_ChamberedAmmoDefinition;
	public float Wear01 => m_Wear01;
	public float Fouling01 => m_Fouling01;
	public bool HasWeapon => m_WeaponDefinition != null;
	#endregion

	#region Public Methods
	public void Clear()
	{
		m_WeaponDefinition = null;
		m_SelectedFireMode = WeaponFireMode.SemiAuto;
		ClearInsertedMagazineFields();
		m_Wear01 = 0f;
		m_Fouling01 = 0f;
		ClearChamber();
	}

	public void SetWeaponDefinition(WeaponDefinition _weaponDefinition)
	{
		m_WeaponDefinition = _weaponDefinition;
		m_SelectedFireMode = _weaponDefinition != null ? _weaponDefinition.DefaultFireMode : WeaponFireMode.SemiAuto;
		ClearInsertedMagazineFields();
		ClearChamber();
	}

	public void SetSelectedFireMode(WeaponFireMode _fireMode)
	{
		if (m_WeaponDefinition == null)
		{
			m_SelectedFireMode = _fireMode;
			return;
		}

		if (!IsFireModeSupported(_fireMode))
			return;

		m_SelectedFireMode = _fireMode;
	}

	public bool CanAcceptMagazineItem(InventorySlotRuntimeData _magazineItem)
	{
		if (_magazineItem.IsEmpty || _magazineItem.Definition == null || _magazineItem.InstanceState == null)
			return false;

		MagazineRuntimeState magazineState = _magazineItem.InstanceState.MagazineState;
		MagazineDefinition magazineDefinition = _magazineItem.Definition.MagazineDefinition;
		if (magazineState == null || magazineDefinition == null)
			return false;

		if (magazineState.Definition != magazineDefinition)
			magazineState.Configure(magazineDefinition, magazineState.LoadedAmmoDefinition, magazineState.CurrentAmmoCount);

		if (m_WeaponDefinition == null)
			return true;

		if (m_WeaponDefinition.SupportedMagazineType != MagazineType.None &&
			magazineDefinition.MagazineType != m_WeaponDefinition.SupportedMagazineType)
			return false;
		if (m_WeaponDefinition.SupportedCaliber != CaliberType.None &&
			magazineDefinition.SupportedCaliber != m_WeaponDefinition.SupportedCaliber)
			return false;

		AmmoDefinition loadedAmmo = magazineState.LoadedAmmoDefinition;
		if (loadedAmmo != null &&
			m_WeaponDefinition.SupportedCaliber != CaliberType.None &&
			loadedAmmo.Caliber != m_WeaponDefinition.SupportedCaliber)
			return false;

		return true;
	}

	public bool TryInsertMagazine(InventorySlotRuntimeData _magazineItem)
	{
		if (!CanAcceptMagazineItem(_magazineItem))
			return false;
		if (HasMagazine)
			return false;

		_magazineItem.WorldSource = null;
		m_CurrentMagazineDefinition = _magazineItem.Definition;
		m_CurrentMagazineDisplayName = _magazineItem.DisplayName;
		m_CurrentMagazineLocalizationKey = _magazineItem.LocalizationKey;
		m_CurrentMagazineRuntimeState = _magazineItem.InstanceState != null ? _magazineItem.InstanceState.MagazineState : null;
		m_CachedMagazineSlotOwner = null;
		return true;
	}

	public bool TryEjectMagazine(out InventorySlotRuntimeData _magazineItem)
	{
		if (!HasMagazine)
		{
			_magazineItem = default;
			return false;
		}

		_magazineItem = BuildCurrentMagazineSlot();
		ClearInsertedMagazineFields();
		return true;
	}

	public bool TryLoadRoundIntoInsertedMagazine(AmmoDefinition _ammoDefinition)
	{
		MagazineRuntimeState magazineState = CurrentMagazine;
		return magazineState != null && magazineState.TryLoadRound(_ammoDefinition);
	}

	public bool TryConsumeRound(out AmmoDefinition _ammoDefinition)
	{
		_ammoDefinition = null;
		if (!m_HasRoundInChamber || m_ChamberedAmmoDefinition == null)
			return false;

		_ammoDefinition = m_ChamberedAmmoDefinition;
		MagazineRuntimeState magazineState = CurrentMagazine;

		if (magazineState != null && magazineState.HasAmmo)
		{
			if (magazineState.TryConsumeRound(out AmmoDefinition nextRound))
			{
				m_ChamberedAmmoDefinition = nextRound;
				m_HasRoundInChamber = true;
			}
			else
			{
				ClearChamber();
			}
		}
		else
			ClearChamber();

		return true;
	}

	/// <summary>Подача одного патрона из магазина в патронник (после передёргивания затвора). Патронник должен быть пуст.</summary>
	public bool TryChamberRoundFromMagazine()
	{
		if (HasRoundInChamber)
			return false;

		MagazineRuntimeState magazineState = CurrentMagazine;
		if (magazineState == null || !magazineState.HasAmmo)
			return false;

		if (!magazineState.TryConsumeRound(out AmmoDefinition round))
			return false;

		m_ChamberedAmmoDefinition = round;
		m_HasRoundInChamber = true;
		return true;
	}

	public void ClearChamber()
	{
		m_HasRoundInChamber = false;
		m_ChamberedAmmoDefinition = null;
	}

	public void SetWear(float _value)
	{
		m_Wear01 = Mathf.Clamp01(_value);
	}

	public void SetFouling(float _value)
	{
		m_Fouling01 = Mathf.Clamp01(_value);
	}
	#endregion

	#region Private Methods
	private bool IsFireModeSupported(WeaponFireMode _fireMode)
	{
		WeaponFireMode[] availableModes = m_WeaponDefinition.AvailableFireModes;
		if (availableModes == null || availableModes.Length == 0)
			return _fireMode == m_WeaponDefinition.DefaultFireMode;

		for (int i = 0; i < availableModes.Length; i++)
		{
			if (availableModes[i] == _fireMode)
				return true;
		}

		return false;
	}

	private void ClearInsertedMagazineFields()
	{
		m_CurrentMagazineDefinition = null;
		m_CurrentMagazineDisplayName = null;
		m_CurrentMagazineLocalizationKey = null;
		m_CurrentMagazineRuntimeState = null;
		m_CachedMagazineSlotOwner = null;
	}

	private InventorySlotRuntimeData BuildCurrentMagazineSlot()
	{
		if (!HasMagazine)
			return default;

		if (m_CachedMagazineSlotOwner == null)
			m_CachedMagazineSlotOwner = ItemInstanceState.CreateMagazineSlotOwner(m_CurrentMagazineDefinition, m_CurrentMagazineRuntimeState);

		return new InventorySlotRuntimeData
		{
			DisplayName = m_CurrentMagazineDisplayName,
			LocalizationKey = m_CurrentMagazineLocalizationKey,
			Definition = m_CurrentMagazineDefinition,
			InstanceState = m_CachedMagazineSlotOwner,
			WorldSource = null
		};
	}
	#endregion
}

/// <summary>
/// Временное состояние оружия, пока оно сейчас находится в руках юнита.
/// </summary>
[Serializable]
public sealed class EquippedWeaponTransientState
{
	#region Private Fields
	[SerializeField, Range(0f, 1f)] private float m_AimProgress01;
	[SerializeField, Min(0f)] private float m_RecoilPenalty;
	[SerializeField] private float m_NextAllowedShotTime;
	#endregion

	#region Public Properties
	public float AimProgress01 => m_AimProgress01;
	public float RecoilPenalty => m_RecoilPenalty;
	public float NextAllowedShotTime => m_NextAllowedShotTime;
	#endregion

	#region Public Methods
	public void Clear()
	{
		m_AimProgress01 = 0f;
		m_RecoilPenalty = 0f;
		m_NextAllowedShotTime = 0f;
	}

	public void SetAimProgress(float _value)
	{
		m_AimProgress01 = Mathf.Clamp01(_value);
	}

	public void SetRecoilPenalty(float _value)
	{
		m_RecoilPenalty = Mathf.Max(0f, _value);
	}

	public void SetNextAllowedShotTime(float _time)
	{
		m_NextAllowedShotTime = _time;
	}
	#endregion
}
