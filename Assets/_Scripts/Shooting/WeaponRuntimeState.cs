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
	[Tooltip("Установленные на этом экземпляре модули (пока вручную или из будущей системы слотов); пусто = множители 1.")]
	[SerializeField] private WeaponAttachmentDefinition[] m_EquippedAttachments;
	[Tooltip("ItemDefinition-обёртки установленных модулей для UI/пресетов. Параллельно EquippedAttachments; стрельба использует только definitions.")]
	[SerializeField] private ItemDefinition[] m_EquippedAttachmentItems;
	[Tooltip("Окончательная неисправность: нельзя экипировать, снятие с ремонтом (мастерская) — отдельная фича.")]
	[SerializeField] private bool m_IsTerminallyBroken;
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
	public bool HasMagazine => CurrentMagazine != null && CurrentMagazine.HasMagazine;
	public bool IsMagazineNonRemovable =>
		(m_WeaponDefinition != null && m_WeaponDefinition.UsesShellByShellReload) ||
		(CurrentMagazine != null && CurrentMagazine.Definition != null && CurrentMagazine.Definition.IsNonRemovable);
	public bool HasAmmoInMagazine => CurrentMagazine != null && CurrentMagazine.HasAmmo;
	public bool HasRoundInChamber => m_HasRoundInChamber && m_ChamberedAmmoDefinition != null;
	public AmmoDefinition ChamberedAmmoDefinition => m_ChamberedAmmoDefinition;
	public float Wear01 => m_Wear01;
	public float Fouling01 => m_Fouling01;
	public WeaponAttachmentDefinition[] EquippedAttachments => m_EquippedAttachments;
	public ItemDefinition[] EquippedAttachmentItems => m_EquippedAttachmentItems;
	public bool IsTerminallyBroken => m_IsTerminallyBroken;
	public bool HasWeapon => m_WeaponDefinition != null;
	public ItemDefinition InsertedMagazineDefinition => m_CurrentMagazineDefinition;
	#endregion

	#region Public Methods
	public void Clear()
	{
		m_WeaponDefinition = null;
		m_SelectedFireMode = WeaponFireMode.SemiAuto;
		ClearInsertedMagazineFields();
		m_Wear01 = 0f;
		m_Fouling01 = 0f;
		m_EquippedAttachments = null;
		m_EquippedAttachmentItems = null;
		m_IsTerminallyBroken = false;
		ClearChamber();
	}

	public void SetWeaponDefinition(WeaponDefinition _weaponDefinition)
	{
		m_WeaponDefinition = _weaponDefinition;
		m_SelectedFireMode = _weaponDefinition != null ? _weaponDefinition.DefaultFireMode : WeaponFireMode.SemiAuto;
		m_IsTerminallyBroken = false;
		m_EquippedAttachments = null;
		m_EquippedAttachmentItems = null;
		ClearInsertedMagazineFields();
		ClearChamber();
		EnsureValidSelectedFireMode();
	}

	/// <summary>
	/// Сбрасывает режим огня на допустимый для текущего <see cref="WeaponDefinition"/>.
	/// Нужно после восстановления оружия из preset snapshot, если в state остался режим другого оружия.
	/// </summary>
	public void EnsureValidSelectedFireMode()
	{
		if (m_WeaponDefinition == null)
		{
			m_SelectedFireMode = WeaponFireMode.SemiAuto;
			return;
		}

		if (IsFireModeSupported(m_SelectedFireMode))
			return;

		m_SelectedFireMode = m_WeaponDefinition.DefaultFireMode;
		if (IsFireModeSupported(m_SelectedFireMode))
			return;

		WeaponFireMode[] availableModes = m_WeaponDefinition.AvailableFireModes;
		if (availableModes != null && availableModes.Length > 0)
			m_SelectedFireMode = availableModes[0];
		else
			m_SelectedFireMode = WeaponFireMode.SemiAuto;
	}

	public void SetEquippedAttachments(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null || _attachments.Length == 0)
		{
			m_EquippedAttachments = null;
			m_EquippedAttachmentItems = null;
		}
		else
		{
			m_EquippedAttachments = (WeaponAttachmentDefinition[])_attachments.Clone();
			if (m_EquippedAttachmentItems != null && m_EquippedAttachmentItems.Length != m_EquippedAttachments.Length)
				m_EquippedAttachmentItems = null;
		}
	}

	public void SetEquippedAttachmentItems(ItemDefinition[] _attachmentItems)
	{
		if (_attachmentItems == null || _attachmentItems.Length == 0)
		{
			m_EquippedAttachmentItems = null;
			return;
		}

		m_EquippedAttachmentItems = (ItemDefinition[])_attachmentItems.Clone();
	}

	public void SetEquippedAttachmentSlotItems(WeaponAttachmentDefinition[] _attachments, ItemDefinition[] _attachmentItems)
	{
		if (_attachments == null || _attachments.Length == 0)
		{
			m_EquippedAttachments = null;
			m_EquippedAttachmentItems = null;
			return;
		}

		m_EquippedAttachments = (WeaponAttachmentDefinition[])_attachments.Clone();
		m_EquippedAttachmentItems = _attachmentItems != null && _attachmentItems.Length > 0
			? (ItemDefinition[])_attachmentItems.Clone()
			: null;
	}

	/// <summary>После успешного выстрела: износ и загрязнение от патрона и модулей.</summary>
	public void ApplyConditionAfterSuccessfulShot(AmmoDefinition _firedAmmo)
	{
		if (m_WeaponDefinition == null || _firedAmmo == null)
			return;

		float wearMul = GetAttachmentWearPerShotProduct();
		float foulMul = GetAttachmentFoulingPerShotProduct();
		float dur = Mathf.Max(1f, m_WeaponDefinition.BaseDurability);
		float foulBudget = Mathf.Max(1f, m_WeaponDefinition.BaseFoulingBudget);

		float dWear = _firedAmmo.WearPerShot * wearMul / dur;
		float dFoul = _firedAmmo.FoulingPerShot * foulMul / foulBudget;

		SetWear(m_Wear01 + dWear);
		SetFouling(m_Fouling01 + dFoul);
	}

	/// <summary>Произведение множителей клина: патрон в патроннике, магазин, модули.</summary>
	public float GetJamRiskProductForShot(AmmoDefinition _chamberedAmmo)
	{
		float m = 1f;
		if (_chamberedAmmo != null)
			m *= _chamberedAmmo.JamRiskModifier;

		MagazineRuntimeState mag = CurrentMagazine;
		if (mag != null && mag.Definition != null)
			m *= mag.Definition.JamRiskModifier;

		m *= GetAttachmentJamRiskProduct();
		return Mathf.Clamp(m, 0f, 10f);
	}

	public float GetAttachmentAimTimeProduct()
	{
		return ProductAttachmentFloat(static a => a.AimTimeModifier);
	}

	public float GetAttachmentDistanceAimTimeProduct(float _distanceMeters)
	{
		return ProductAttachmentFloat(a => a.GetDistanceAimTimeMultiplier(_distanceMeters));
	}

	public float GetAttachmentEffectiveRangeProduct()
	{
		return ProductAttachmentFloat(static a => a.EffectiveRangeModifier);
	}

	public float GetAttachmentDistanceDispersionProduct(float _distanceMeters)
	{
		return ProductAttachmentFloat(a => a.GetDistanceDispersionMultiplier(_distanceMeters));
	}

	public float GetAttachmentRecoilProduct()
	{
		return ProductAttachmentFloat(static a => a.RecoilModifier);
	}

	public float GetAttachmentRecoilProduct(WeaponFireMode _fireMode)
	{
		return ProductAttachmentFloat(a => a.GetRecoilModifier(_fireMode));
	}

	public float GetAttachmentReloadTimeProduct()
	{
		return ProductAttachmentFloat(static a => a.ReloadTimeModifier);
	}

	public WeaponAttachmentDefinition GetFirstEquippedAttachmentForSlot(WeaponAttachmentSlotType _slotType)
	{
		if (m_EquippedAttachments == null || m_EquippedAttachments.Length == 0)
			return null;

		for (int i = 0; i < m_EquippedAttachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = m_EquippedAttachments[i];
			if (attachment != null && attachment.SupportsSlot(_slotType))
				return attachment;
		}

		return null;
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

	public bool TryInsertBuiltInMagazine(MagazineDefinition _magazineDefinition, AmmoDefinition _ammo, int _roundCount)
	{
		if (_magazineDefinition == null || HasMagazine)
			return false;

		MagazineRuntimeState magazineState = new MagazineRuntimeState();
		magazineState.Configure(_magazineDefinition, _ammo, _roundCount);

		m_CurrentMagazineDefinition = null;
		m_CurrentMagazineDisplayName = null;
		m_CurrentMagazineLocalizationKey = null;
		m_CurrentMagazineRuntimeState = magazineState;
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

		if (IsMagazineNonRemovable)
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

	public void SetTerminallyBroken(bool _broken)
	{
		m_IsTerminallyBroken = _broken;
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

	private float GetAttachmentWearPerShotProduct()
	{
		return ProductAttachmentFloat(static a => a.WearPerShotMultiplier);
	}

	private float GetAttachmentFoulingPerShotProduct()
	{
		return ProductAttachmentFloat(static a => a.FoulingPerShotMultiplier);
	}

	private float GetAttachmentJamRiskProduct()
	{
		return ProductAttachmentFloat(static a => a.JamRiskModifier);
	}

	private float ProductAttachmentFloat(System.Func<WeaponAttachmentDefinition, float> _selector)
	{
		if (m_EquippedAttachments == null || m_EquippedAttachments.Length == 0)
			return 1f;

		float p = 1f;
		for (int i = 0; i < m_EquippedAttachments.Length; i++)
		{
			WeaponAttachmentDefinition a = m_EquippedAttachments[i];
			if (a != null)
				p *= Mathf.Max(0f, _selector(a));
		}

		return p;
	}

	private InventorySlotRuntimeData BuildCurrentMagazineSlot()
	{
		if (!HasMagazine)
			return default;

		if (m_CachedMagazineSlotOwner == null)
			m_CachedMagazineSlotOwner = ItemInstanceState.CreateMagazineSlotOwner(m_CurrentMagazineRuntimeState);

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
	[SerializeField, Min(0)] private int m_ConsecutiveBurstShotsFired;
	[SerializeField] private float m_NextAllowedShotTime;
	[SerializeField] private WeaponMalfunctionKind m_MalfunctionKind;
	[SerializeField] private WeaponMalfunctionChannel m_MalfunctionChannel;
	[SerializeField] private WeaponMalfunctionPhase m_MalfunctionPhase;
	[SerializeField, Range(0, 2)] private int m_MalfunctionRackAttemptIndex;
	[SerializeField] private bool m_MalfunctionBoltAnimInProgress;
	#endregion

	#region Public Properties
	public const float FullAimProgress01 = 1f;
	public float AimProgress01 => m_AimProgress01;
	public bool IsFullyAimed => m_AimProgress01 >= FullAimProgress01;
	public float RecoilPenalty => m_RecoilPenalty;
	public int ConsecutiveBurstShotsFired => m_ConsecutiveBurstShotsFired;
	public float NextAllowedShotTime => m_NextAllowedShotTime;
	public WeaponMalfunctionKind MalfunctionKind => m_MalfunctionKind;
	public WeaponMalfunctionChannel MalfunctionChannel => m_MalfunctionChannel;
	public WeaponMalfunctionPhase MalfunctionPhase => m_MalfunctionPhase;
	public int MalfunctionRackAttemptIndex => m_MalfunctionRackAttemptIndex;
	public bool MalfunctionBoltAnimInProgress => m_MalfunctionBoltAnimInProgress;
	public bool HasActiveMalfunction => m_MalfunctionKind != WeaponMalfunctionKind.None;
	#endregion

	#region Public Methods
	public void Clear()
	{
		m_AimProgress01 = 0f;
		m_RecoilPenalty = 0f;
		m_ConsecutiveBurstShotsFired = 0;
		m_NextAllowedShotTime = 0f;
		ClearMalfunction();
	}

	public void ClearMalfunction()
	{
		m_MalfunctionKind = WeaponMalfunctionKind.None;
		m_MalfunctionChannel = WeaponMalfunctionChannel.None;
		m_MalfunctionPhase = WeaponMalfunctionPhase.None;
		m_MalfunctionRackAttemptIndex = 0;
		m_MalfunctionBoltAnimInProgress = false;
	}

	public void SetMalfunction(WeaponMalfunctionKind _kind, WeaponMalfunctionChannel _channel, WeaponMalfunctionPhase _phase)
	{
		m_MalfunctionKind = _kind;
		m_MalfunctionChannel = _channel;
		m_MalfunctionPhase = _phase;
		m_MalfunctionRackAttemptIndex = 0;
		m_MalfunctionBoltAnimInProgress = false;
	}

	public void SetMalfunctionPhase(WeaponMalfunctionPhase _phase)
	{
		m_MalfunctionPhase = _phase;
		m_MalfunctionRackAttemptIndex = 0;
	}

	public void SetMalfunctionRackAttemptIndex(int _index)
	{
		m_MalfunctionRackAttemptIndex = Mathf.Clamp(_index, 0, 2);
	}

	public void SetMalfunctionBoltAnimInProgress(bool _value)
	{
		m_MalfunctionBoltAnimInProgress = _value;
	}

	public void SetAimProgress(float _value)
	{
		m_AimProgress01 = Mathf.Clamp01(_value);
	}

	public void SetRecoilPenalty(float _value)
	{
		m_RecoilPenalty = Mathf.Max(0f, _value);
	}

	public int GetNextBurstShotIndex() => m_ConsecutiveBurstShotsFired + 1;

	public void RegisterBurstShotFired()
	{
		m_ConsecutiveBurstShotsFired = Mathf.Max(0, m_ConsecutiveBurstShotsFired + 1);
	}

	public void ResetBurstShotCounter()
	{
		m_ConsecutiveBurstShotsFired = 0;
	}

	public void SetNextAllowedShotTime(float _time)
	{
		m_NextAllowedShotTime = _time;
	}
	#endregion
}
