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
	[Tooltip("Огневая дисциплина юнита: экономный / точный / подавляющий / авто. Заменяет ручной выбор режимов прицеливания.")]
	[SerializeField] private WeaponFireDisciplineMode m_SelectedFireDisciplineMode = WeaponFireDisciplineMode.Auto;
	#endregion

	#region Private Fields
	private ItemInstanceState m_BoundItemState;
	private WeaponRuntimeState m_BoundWeaponState;
	private UnitWeaponMalfunctionController m_MalfunctionController;
	private bool m_ExternalWeaponBound;
	#endregion

	#region Public Properties
	public WeaponRuntimeState RuntimeState => m_BoundWeaponState;
	public EquippedWeaponTransientState TransientState => m_TransientState;
	public ItemInstanceState BoundItemState => m_BoundItemState;
	public WeaponDefinition CurrentWeaponDefinition => m_BoundWeaponState != null ? m_BoundWeaponState.WeaponDefinition : null;
	public WeaponFireDisciplineMode SelectedFireDisciplineMode => m_SelectedFireDisciplineMode;
	/// <summary>Производный aim-mode для accuracy/логов из текущей дисциплины и дистанции.</summary>
	public WeaponAimMode SelectedAimMode => WeaponFireDisciplineModeUtility.MapToAimMode(
		m_SelectedFireDisciplineMode == WeaponFireDisciplineMode.Auto
			? WeaponFireDisciplineMode.Precision
			: m_SelectedFireDisciplineMode,
		0f);
	public MagazineRuntimeState CurrentMagazine => m_BoundWeaponState != null ? m_BoundWeaponState.CurrentMagazine : null;
	public bool HasLoadedMagazine => m_BoundWeaponState != null && m_BoundWeaponState.HasMagazine;
	public bool HasAmmoInMagazine => m_BoundWeaponState != null && m_BoundWeaponState.HasAmmoInMagazine;
	public bool HasRoundInChamber => m_BoundWeaponState != null && m_BoundWeaponState.HasRoundInChamber;
	public bool IsExternalWeaponBound => m_ExternalWeaponBound;
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
	/// <summary>
	/// Привязать runtime оружия турели (инвентарь юнита не меняется).
	/// </summary>
	public void BindExternalWeaponState(ItemInstanceState _itemState)
	{
		WeaponRuntimeState weaponState = _itemState != null ? _itemState.WeaponState : null;
		if (weaponState == null)
		{
			ClearExternalWeaponBind();
			return;
		}

		m_ExternalWeaponBound = true;
		m_BoundItemState = _itemState;
		m_BoundWeaponState = weaponState;
		weaponState.EnsureValidSelectedFireMode();
		m_TransientState.Clear();
	}

	public void ClearExternalWeaponBind()
	{
		if (!m_ExternalWeaponBound)
			return;

		m_ExternalWeaponBound = false;
		RefreshFromEquipment();
	}

	public void RefreshFromEquipment()
	{
		if (m_ExternalWeaponBound)
			return;

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
			weaponState.EnsureValidSelectedFireMode();
			WeaponBuiltInMagazineUtility.TryEnsureBuiltInMagazine(
				weaponState,
				weaponState.WeaponDefinition?.BuiltInMagazineDefaultAmmo,
				_fillIfEmpty: false);
			SyncInsertedMagazineVisual();
			SyncAttachmentVisuals();
			return;
		}

		m_BoundItemState = itemState;
		m_BoundWeaponState = weaponState;
		weaponState.EnsureValidSelectedFireMode();
		WeaponBuiltInMagazineUtility.TryEnsureBuiltInMagazine(
			weaponState,
			weaponState.WeaponDefinition?.BuiltInMagazineDefaultAmmo,
			_fillIfEmpty: false);
		m_TransientState.Clear();
		SyncInsertedMagazineVisual();
		SyncAttachmentVisuals();
	}

	public bool TryInsertMagazine(InventorySlotRuntimeData _magazineItem, bool _syncVisual = true, int _slotIndex = 0)
	{
		if (m_BoundWeaponState == null)
			return false;

		bool inserted = m_BoundWeaponState.TryInsertMagazine(_magazineItem, _slotIndex);
		if (inserted && _syncVisual)
			SyncInsertedMagazineVisual();

		return inserted;
	}

	public bool TryEjectMagazine(out InventorySlotRuntimeData _magazineItem, bool _syncVisual = true)
	{
		if (m_BoundWeaponState == null)
		{
			_magazineItem = default;
			return false;
		}

		bool ejected = m_BoundWeaponState.TryEjectMagazine(out _magazineItem);
		if (ejected && _syncVisual)
			SyncInsertedMagazineVisual();

		return ejected;
	}

	public bool TryEjectMagazine(int _slotIndex, out InventorySlotRuntimeData _magazineItem, bool _syncVisual = true)
	{
		if (m_BoundWeaponState == null)
		{
			_magazineItem = default;
			return false;
		}

		bool ejected = m_BoundWeaponState.TryEjectMagazine(_slotIndex, out _magazineItem);
		if (ejected && _syncVisual)
			SyncInsertedMagazineVisual();

		return ejected;
	}

	public void SyncInsertedMagazineVisualFromState()
	{
		SyncInsertedMagazineVisual();
	}

	public bool TryLoadRoundIntoInsertedMagazine(AmmoDefinition _ammoDefinition)
	{
		return m_BoundWeaponState != null && m_BoundWeaponState.TryLoadRoundIntoInsertedMagazine(_ammoDefinition);
	}

	/// <summary>Подача из магазина в патронник по ивенту анимации затвора.</summary>
	public bool TryChamberRoundFromMagazine()
	{
		return m_BoundWeaponState != null && m_BoundWeaponState.TryChamberRoundFromMagazine();
	}

	public void SetSelectedFireMode(WeaponFireMode _fireMode)
	{
		if (m_BoundWeaponState == null)
			return;

		m_BoundWeaponState.SetSelectedFireMode(_fireMode);
	}

	/// <summary>Следующий режим из <see cref="WeaponDefinition.AvailableFireModes"/>; false если одно значение или нет оружия.</summary>
	public bool TryCycleToNextFireMode()
	{
		if (m_BoundWeaponState == null)
			return false;

		WeaponDefinition weaponDefinition = m_BoundWeaponState.WeaponDefinition;
		if (weaponDefinition == null)
			return false;

		WeaponFireMode[] modes = weaponDefinition.AvailableFireModes;
		if (modes == null || modes.Length <= 1)
			return false;

		WeaponFireMode current = m_BoundWeaponState.SelectedFireMode;
		int currentIdx = -1;
		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] == current)
			{
				currentIdx = i;
				break;
			}
		}

		int nextIdx = currentIdx >= 0 ? (currentIdx + 1) % modes.Length : 0;
		SetSelectedFireMode(modes[nextIdx]);
		return true;
	}

	public WeaponFireMode ResolveEffectiveFireMode(float _targetDistanceMeters)
	{
		if (m_BoundWeaponState == null)
			return WeaponFireMode.SemiAuto;

		WeaponDefinition weaponDefinition = m_BoundWeaponState.WeaponDefinition;
		WeaponFireMode selectedMode = m_BoundWeaponState.SelectedFireMode;
		WeaponFireMode[] availableModes = weaponDefinition != null ? weaponDefinition.AvailableFireModes : null;
		return WeaponFireModeUtility.ResolveEffectiveMode(selectedMode, _targetDistanceMeters, availableModes);
	}

	public bool TryCycleToNextFireDisciplineMode(out WeaponFireDisciplineMode _selectedDisciplineMode)
	{
		m_SelectedFireDisciplineMode = WeaponFireDisciplineModeUtility.GetNextMode(m_SelectedFireDisciplineMode);
		_selectedDisciplineMode = m_SelectedFireDisciplineMode;
		return true;
	}

	public void SetSelectedFireDisciplineMode(WeaponFireDisciplineMode _mode)
	{
		m_SelectedFireDisciplineMode = _mode;
	}

	/// <summary>Совместимость со старым API: цикл дисциплины вместо aim mode.</summary>
	public bool TryCycleToNextAimMode(out WeaponAimMode _selectedAimMode)
	{
		TryCycleToNextFireDisciplineMode(out _);
		_selectedAimMode = SelectedAimMode;
		return true;
	}

	public void SetAimProgress(float _value)
	{
		m_TransientState.SetAimProgress(_value);
	}

	public void SetRecoilOffset(Vector2 _offset, float _patternValue, int _shotIndex)
	{
		m_TransientState.SetRecoilOffset(_offset, _patternValue, _shotIndex);
	}

	public void SetRecoilOffset(Vector2 _offset)
	{
		m_TransientState.SetRecoilOffset(_offset);
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

	public void RegisterMalfunctionController(UnitWeaponMalfunctionController _controller)
	{
		m_MalfunctionController = _controller;
	}

	public void UnregisterMalfunctionController(UnitWeaponMalfunctionController _controller)
	{
		if (m_MalfunctionController == _controller)
			m_MalfunctionController = null;
	}

	public WeaponShotAttemptResult TryConsumeShot(float _currentTime, WeaponFireMode _effectiveFireMode, out AmmoDefinition _firedAmmoDefinition)
	{
		_firedAmmoDefinition = null;

		if (m_BoundWeaponState == null || m_BoundWeaponState.WeaponDefinition == null)
			return WeaponShotAttemptResult.NoWeapon;

		if (m_MalfunctionController != null &&
		    m_MalfunctionController.EvaluateBeforeChamberedShot(_currentTime, out WeaponShotAttemptResult malfunctionResult))
			return malfunctionResult;

		if (_currentTime < m_TransientState.NextAllowedShotTime)
			return WeaponShotAttemptResult.FireRateLimited;

		if (!m_BoundWeaponState.HasRoundInChamber)
		{
			if (m_BoundWeaponState.HasMagazine && m_BoundWeaponState.HasAmmoInMagazine)
			{
				WeaponDefinition weaponDefinition = m_BoundWeaponState.WeaponDefinition;
				if (weaponDefinition != null && weaponDefinition.UsesShellByShellReload)
				{
					if (!m_BoundWeaponState.TryChamberRoundFromMagazine())
						return WeaponShotAttemptResult.EmptyMagazine;
				}
				else
					return WeaponShotAttemptResult.NeedsBoltCycle;
			}
			else if (!m_BoundWeaponState.HasMagazine)
				return WeaponShotAttemptResult.NoMagazine;
			else
				return WeaponShotAttemptResult.EmptyMagazine;
		}

		if (!m_BoundWeaponState.TryConsumeRound(out _firedAmmoDefinition))
			return WeaponShotAttemptResult.EmptyMagazine;

		m_BoundWeaponState.ApplyConditionAfterSuccessfulShot(_firedAmmoDefinition);
		m_TransientState.SetNextAllowedShotTime(_currentTime + GetSecondsPerShot(_effectiveFireMode));
		return WeaponShotAttemptResult.Success;
	}
	#endregion

	#region Private Methods
	private void HandleEquipmentChanged()
	{
		RefreshFromEquipment();
	}

	private float GetSecondsPerShot(WeaponFireMode _effectiveFireMode)
	{
		WeaponDefinition weaponDefinition = m_BoundWeaponState != null ? m_BoundWeaponState.WeaponDefinition : null;
		if (weaponDefinition == null || weaponDefinition.FireRateRpm <= 0f)
			return 0.1f;

		float rpm = weaponDefinition.FireRateRpm;
		if (m_BoundWeaponState != null &&
			_effectiveFireMode == WeaponFireMode.SemiAuto &&
			weaponDefinition.SemiAutoFireRateRpm > 0f)
			rpm = weaponDefinition.SemiAutoFireRateRpm;

		return rpm > 0f ? 60f / rpm : 0.1f;
	}

	private void UnbindCurrentWeapon()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon != null)
		{
			equippedWeapon.ClearInsertedMagazineVisual();
			equippedWeapon.ClearAttachmentVisuals();
		}

		m_BoundItemState = null;
		m_BoundWeaponState = null;
		m_TransientState.Clear();
	}

	private void SyncInsertedMagazineVisual()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon == null)
			return;

		if (m_BoundWeaponState != null && m_BoundWeaponState.IsMagazineNonRemovable)
		{
			equippedWeapon.ClearAllMagazineVisuals();
			return;
		}

		InventorySlotRuntimeData currentMagazineItem = m_BoundWeaponState != null
			? m_BoundWeaponState.CurrentMagazineItem
			: default;
		ItemDefinition magazineDefinition = currentMagazineItem.Definition;
		if (magazineDefinition == null || currentMagazineItem.InstanceState == null || currentMagazineItem.InstanceState.MagazineState == null)
		{
			equippedWeapon.ClearInsertedMagazineVisual();
		}
		else
		{
			equippedWeapon.SetInsertedMagazineVisual(magazineDefinition);
		}

		if (m_BoundWeaponState != null && m_BoundWeaponState.WeaponDefinition != null && m_BoundWeaponState.WeaponDefinition.UsesDualMagazineSlots)
		{
			InventorySlotRuntimeData secondaryItem = m_BoundWeaponState.CurrentSecondaryMagazineItem;
			ItemDefinition secondaryDef = secondaryItem.Definition;
			if (secondaryDef != null && secondaryItem.InstanceState != null && secondaryItem.InstanceState.MagazineState != null)
				equippedWeapon.SetSecondaryMagazineVisual(secondaryDef);
			else
				equippedWeapon.ClearSecondaryMagazineVisual();
		}
	}

	private void SyncAttachmentVisuals()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon == null)
			return;

		if (m_BoundWeaponState == null || m_BoundWeaponState.WeaponDefinition == null)
		{
			equippedWeapon.ClearAttachmentVisuals();
			return;
		}

		equippedWeapon.TryCopyEquippedAttachmentsPresetToWeaponStateIfEmpty(m_BoundWeaponState);
		equippedWeapon.RefreshAttachmentVisualsFromState(m_BoundWeaponState.WeaponDefinition, m_BoundWeaponState);

		if (m_UnitEquipment != null)
			m_UnitEquipment.RefreshHandIkTargets();
	}

	private void ClearInsertedMagazineVisual()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon != null)
			equippedWeapon.ClearInsertedMagazineVisual();
	}
	#endregion
}
