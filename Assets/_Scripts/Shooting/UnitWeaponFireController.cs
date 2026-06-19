using System;
using UnityEngine;

/// <summary>
/// Командный слой стрельбы юнита: start fire, stop fire и single shot attempt.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(56)]
public sealed class UnitWeaponFireController : MonoBehaviour
{
	#region Events
	public event Action<AmmoDefinition> ShotFired;
	#endregion

	#region Serialized Fields
	[Tooltip("Runtime оружия, привязанный к экипированному предмету.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[Tooltip("Источник визуально экипированного оружия для проверки наведения ствола.")]
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Проверка, что оружие действительно находится в состоянии ready.")]
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[Tooltip("Текущая видимая цель, если выстрелы требуют target lock.")]
	[SerializeField] private UnitVision m_Vision;
	[Tooltip("Во время reload-команд выстрелы блокируются.")]
	[SerializeField] private UnitBusyState m_BusyState;
	[Tooltip("После последнего патрона в магазине — запуск перезарядки (внутри свои проверки на сумку и т.д.).")]
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[Tooltip("Hitscan по сцене; вызывается до ShotFired (разброс без отдачи текущего выстрела).")]
	[SerializeField] private UnitWeaponHitscanShooting m_HitscanShooting;
	[SerializeField] private UnitConsciousness m_Consciousness;

	[Header("Fire Conditions")]
	[Tooltip("Запрещать выстрел, если оружие не на ready.")]
	[SerializeField] private bool m_RequireReady = true;
	[Tooltip("Запрещать выстрел, если сейчас нет видимой цели.")]
	[SerializeField] private bool m_RequireVisibleTarget = true;
	[Tooltip("При удержании курка: FullAuto стреляет каждый кадр (лимит по RPM), Burst ведёт очереди с паузой.")]
	[SerializeField] private bool m_EnableAutomaticFireLoop = true;
	[Tooltip("Если выстрел невозможен из‑за пустого магазина или отсутствия магазина в оружии — периодически вызывать TryStartReload (не каждый кадр, см. интервал).")]
	[SerializeField] private bool m_TryReloadWhenOutOfAmmo = true;
	[SerializeField, Min(0.05f)] private float m_OutOfAmmoReloadRetrySeconds = 0.35f;

	[Header("Aiming Gate")]
	[Tooltip("Запрещать выстрел, пока не достигнут порог выбранного режима прицеливания. Для Burst/FullAuto — только 1-й выстрел серии или очереди.")]
	[SerializeField] private bool m_RequireFullAimToFire = true;
	[Tooltip("Запрещать выстрел, пока визуальный ствол ещё не вернулся к точке цели после kick. Только для одиночного и когда Auto выбрал SemiAuto; Burst/FullAuto идут по RPM и разбросу.")]
	[SerializeField] private bool m_RequireBarrelAlignedToFire = true;
	[SerializeField, Range(0f, 30f)] private float m_MaxBarrelAimErrorDegrees = 1.5f;

	[Header("Debug")]
	[SerializeField] private bool m_IsFiringCommandActive;
	[SerializeField] private WeaponShotAttemptResult m_LastShotAttemptResult = WeaponShotAttemptResult.NoWeapon;
	[SerializeField] private AmmoDefinition m_LastFiredAmmoDefinition;
	[SerializeField] private int m_DebugSuccessfulShotCount;
	[SerializeField] private int m_DebugBurstShotsRemaining;
	[SerializeField] private float m_DebugNextBurstWaveTime;
	[SerializeField] private WeaponFireMode m_DebugSelectedFireMode = WeaponFireMode.SemiAuto;
	[SerializeField] private WeaponFireMode m_DebugEffectiveFireMode = WeaponFireMode.SemiAuto;
	[SerializeField, Range(0f, 1f)] private float m_DebugCurrentAimProgress;
	[SerializeField, Min(0f)] private float m_DebugLastBarrelAimErrorDegrees;
	#endregion

	#region Private Fields
	private int m_BurstShotsRemainingInWave;
	private float m_NextBurstWaveTime;
	private float m_NextOutOfAmmoReloadAttemptTime;
	private bool m_SemiShotConsumedForCurrentTrigger;
	private Transform m_LastVisibleTargetForFire;
	#endregion

	#region Public Properties
	public bool IsFiringCommandActive => m_IsFiringCommandActive;
	public WeaponShotAttemptResult LastShotAttemptResult => m_LastShotAttemptResult;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_HitscanShooting == null)
			m_HitscanShooting = GetComponent<UnitWeaponHitscanShooting>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (GetComponent<UnitStanceCombatModifiers>() == null)
			gameObject.AddComponent<UnitStanceCombatModifiers>();
	}

	private void OnEnable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged += HandleVisibleTargetChanged;

		m_LastVisibleTargetForFire = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
	}

	private void OnDisable()
	{
		if (m_Vision != null)
			m_Vision.VisibleTargetChanged -= HandleVisibleTargetChanged;
	}

	private void Update()
	{
		TrySyncEngagementTarget();

		if (!m_IsFiringCommandActive || !m_EnableAutomaticFireLoop)
			return;
		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return;

		WeaponFireMode mode = ResolveEffectiveFireMode();
		if (mode == WeaponFireMode.FullAuto)
		{
			TryFireSingleShot();
			return;
		}

		if (mode == WeaponFireMode.Burst)
			UpdateBurstFire(Time.time);
	}
	#endregion

	#region Public Methods
	public void StartFiring()
	{
		if (!IsConscious())
			return;

		m_IsFiringCommandActive = true;

		WeaponFireMode fireMode = ResolveEffectiveFireMode();

		if (fireMode == WeaponFireMode.FullAuto || fireMode == WeaponFireMode.Burst)
			return;

		if (m_SemiShotConsumedForCurrentTrigger)
			return;

		WeaponShotAttemptResult result = TryFireSingleShot();
		if (result == WeaponShotAttemptResult.Success)
			m_SemiShotConsumedForCurrentTrigger = true;
	}

	public bool ShouldHoldVirtualTrigger()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		if (!IsConscious())
			return false;

		if (m_WeaponRuntime.CurrentWeaponDefinition == null)
			return false;

		WeaponRuntimeState rs = m_WeaponRuntime.RuntimeState;
		bool canEventuallyFire = rs.HasRoundInChamber || (rs.HasMagazine && rs.HasAmmoInMagazine);
		if (!canEventuallyFire)
			return false;

		if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponReadyToFire()))
			return false;

		if (m_BusyState != null &&
		    (m_BusyState.HasReason(UnitBusyState.BusyReason.Reload) ||
		     m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization)))
			return false;

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return false;

		if (m_RequireVisibleTarget && !HasEngageableVisibleTarget())
			return false;

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		m_DebugCurrentAimProgress = transientState != null ? transientState.AimProgress01 : 0f;
		return !ShouldRequireAimProgressForNextShot() || HasRequiredAimProgress(transientState);
	}

	public WeaponFireMode ResolveEffectiveFireMode()
	{
		WeaponFireMode selectedMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
		WeaponFireMode effectiveMode = m_HitscanShooting != null &&
			m_HitscanShooting.TrySelectAutoModes(out WeaponAutoModeSelectionResult selection)
			? selection.EffectiveFireMode
			: m_WeaponRuntime != null
			? m_WeaponRuntime.ResolveEffectiveFireMode(EstimateTargetDistanceMeters())
			: WeaponFireMode.SemiAuto;

		m_DebugSelectedFireMode = selectedMode;
		m_DebugEffectiveFireMode = effectiveMode;
		return effectiveMode;
	}

	public WeaponFireMode GetSelectedFireMode()
	{
		return m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
	}

	public bool IsCurrentEffectiveFireModeAutomatic()
	{
		return WeaponFireModeUtility.IsAutomaticEffectiveMode(ResolveEffectiveFireMode());
	}

	public void ResetSemiTriggerState()
	{
		m_SemiShotConsumedForCurrentTrigger = false;
	}

	public WeaponShotAttemptResult TryFireSingleShot()
	{
		AmmoDefinition firedAmmoDefinition;
		WeaponShotAttemptResult result = TryFireSingleShotInternal(Time.time, out firedAmmoDefinition);
		m_LastShotAttemptResult = result;
		m_LastFiredAmmoDefinition = firedAmmoDefinition;

		if (result == WeaponShotAttemptResult.Success)
		{
			m_DebugSuccessfulShotCount++;
			m_HitscanShooting?.ProcessSuccessfulShot(firedAmmoDefinition);
			RegisterBurstSpreadShotIfNeeded();
			ShotFired?.Invoke(firedAmmoDefinition);

			if (m_WeaponRuntime != null && !m_WeaponRuntime.HasAmmoInMagazine && !m_WeaponRuntime.HasRoundInChamber)
				m_ReloadController?.TryStartReload();
		}
		else if (m_TryReloadWhenOutOfAmmo &&
			(result == WeaponShotAttemptResult.EmptyMagazine ||
			 result == WeaponShotAttemptResult.NoMagazine ||
			 result == WeaponShotAttemptResult.NeedsBoltCycle))
		{
			TryAutoReloadOrBoltCycle(result);
		}

		return result;
	}
	#endregion

	#region Private Methods
	private WeaponShotAttemptResult TryFireSingleShotInternal(float _currentTime, out AmmoDefinition _firedAmmoDefinition)
	{
		_firedAmmoDefinition = null;

		if (m_WeaponRuntime == null)
			return WeaponShotAttemptResult.NoWeapon;

		if (!IsConscious())
			return WeaponShotAttemptResult.Busy;

		if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponReadyToFire()))
			return WeaponShotAttemptResult.NotReady;

		if (m_BusyState != null &&
		    (m_BusyState.HasReason(UnitBusyState.BusyReason.Reload) ||
		     m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization)))
			return WeaponShotAttemptResult.Busy;

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return WeaponShotAttemptResult.Busy;

		if (m_RequireVisibleTarget && !HasEngageableVisibleTarget())
			return WeaponShotAttemptResult.NoVisibleTarget;

		if (!IsAimedEnoughToFire())
			return WeaponShotAttemptResult.NotAimed;

		return m_WeaponRuntime.TryConsumeShot(_currentTime, ResolveEffectiveFireMode(), out _firedAmmoDefinition);
	}

	public void StopFiring()
	{
		m_IsFiringCommandActive = false;
		m_BurstShotsRemainingInWave = 0;
		m_NextBurstWaveTime = 0f;
		m_SemiShotConsumedForCurrentTrigger = false;
		ResetBurstSpreadCounter();
	}

	/// <summary>
	/// Сброс очереди burst при смене режима огня. Не трогает <see cref="IsFiringCommandActive"/> —
	/// иначе в том же кадре <see cref="UnitWeaponAutoFireWhenAimed"/> снова вызовет <see cref="StartFiring"/>,
	/// и для полуавтомата повторится <see cref="TryFireSingleShot"/> (лишний выстрел/отдача).
	/// </summary>
	public void ResetBurstStateForFireModeChange()
	{
		m_BurstShotsRemainingInWave = 0;
		m_NextBurstWaveTime = 0f;
		m_SemiShotConsumedForCurrentTrigger = false;
		m_DebugBurstShotsRemaining = 0;
		m_DebugNextBurstWaveTime = 0f;
		ResetBurstSpreadCounter();
	}

	private bool IsAimedEnoughToFire()
	{
		EquippedWeaponTransientState transientState = m_WeaponRuntime != null ? m_WeaponRuntime.TransientState : null;
		m_DebugCurrentAimProgress = transientState != null ? transientState.AimProgress01 : 0f;
		if (ShouldRequireAimProgressForNextShot() && !HasRequiredAimProgress(transientState))
			return false;

		if (!m_RequireBarrelAlignedToFire || !HasEngageableVisibleTarget())
			return true;

		if (WeaponFireModeUtility.IsAutomaticEffectiveMode(ResolveEffectiveFireMode()))
		{
			m_DebugLastBarrelAimErrorDegrees = 0f;
			return true;
		}

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform barrel = weapon != null ? weapon.BarrelTransform : null;
		if (barrel == null)
			return false;

		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = m_Vision.GetEngageableVisibleTarget().position;

		Vector3 toTarget = targetPoint - barrel.position;
		if (toTarget.sqrMagnitude < 1e-6f)
		{
			m_DebugLastBarrelAimErrorDegrees = 0f;
			return true;
		}

		m_DebugLastBarrelAimErrorDegrees = Vector3.Angle(barrel.forward, toTarget.normalized);
		return m_DebugLastBarrelAimErrorDegrees <= m_MaxBarrelAimErrorDegrees;
	}

	private bool HasRequiredAimProgress(EquippedWeaponTransientState _transientState)
	{
		if (_transientState == null || m_WeaponRuntime == null)
			return false;

		WeaponAimMode effectiveAimMode = m_HitscanShooting != null &&
			m_HitscanShooting.TrySelectAutoModes(out WeaponAutoModeSelectionResult selection)
			? selection.EffectiveAimMode
			: WeaponAimModeUtility.ResolveEffectiveMode(m_WeaponRuntime.SelectedAimMode, EstimateTargetDistanceMeters());
		float requiredProgress = WeaponAimModeUtility.GetRequiredAimProgress01(effectiveAimMode, EstimateTargetDistanceMeters());
		return _transientState.AimProgress01 >= requiredProgress;
	}

	/// <summary>Burst/FullAuto: порог прицела только перед 1-м выстрелом серии; SemiAuto — каждый выстрел.</summary>
	private bool ShouldRequireAimProgressForNextShot()
	{
		if (!m_RequireFullAimToFire)
			return false;

		if (!WeaponFireModeUtility.IsAutomaticEffectiveMode(ResolveEffectiveFireMode()))
			return true;

		if (m_WeaponRuntime == null)
			return true;

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		return transientState == null || transientState.GetNextBurstShotIndex() <= 1;
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		if (target == null)
			return 0f;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform barrel = weapon != null ? weapon.BarrelTransform : transform;
		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		return Vector3.Distance(barrel.position, targetPoint);
	}

	private void UpdateBurstFire(float _time)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (weaponDefinition == null)
			return;

		int burstSize = Mathf.Max(2, weaponDefinition.BurstRounds);
		float pause = Mathf.Max(0f, weaponDefinition.BurstPauseSeconds);

		if (m_BurstShotsRemainingInWave <= 0)
		{
			if (_time < m_NextBurstWaveTime)
			{
				m_DebugBurstShotsRemaining = 0;
				m_DebugNextBurstWaveTime = m_NextBurstWaveTime;
				return;
			}

			m_BurstShotsRemainingInWave = burstSize;
			ResetBurstSpreadCounter();
		}

		WeaponShotAttemptResult result = TryFireSingleShot();

		switch (result)
		{
			case WeaponShotAttemptResult.Success:
				m_BurstShotsRemainingInWave--;
				if (m_BurstShotsRemainingInWave <= 0)
					m_NextBurstWaveTime = _time + pause;
				break;
			case WeaponShotAttemptResult.FireRateLimited:
			case WeaponShotAttemptResult.Busy:
			case WeaponShotAttemptResult.NeedsBoltCycle:
			case WeaponShotAttemptResult.NotAimed:
				break;
			default:
				m_BurstShotsRemainingInWave = 0;
				m_NextBurstWaveTime = _time + pause;
				break;
		}

		m_DebugBurstShotsRemaining = m_BurstShotsRemainingInWave;
		m_DebugNextBurstWaveTime = m_NextBurstWaveTime;
	}

	/// <summary>
	/// Повторные попытки перезарядки или снаряжения затвора с интервалом.
	/// </summary>
	private void TryAutoReloadOrBoltCycle(WeaponShotAttemptResult _result)
	{
		if (m_ReloadController == null || m_ReloadController.IsReloadBusy)
			return;

		float t = Time.time;
		if (t < m_NextOutOfAmmoReloadAttemptTime)
			return;

		m_NextOutOfAmmoReloadAttemptTime = t + m_OutOfAmmoReloadRetrySeconds;

		if (_result == WeaponShotAttemptResult.NeedsBoltCycle)
			m_ReloadController.TryStartBoltCycleOnly();
		else
			m_ReloadController.TryStartReload();
	}

	private void RegisterBurstSpreadShotIfNeeded()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return;

		WeaponFireMode mode = ResolveEffectiveFireMode();
		if (!WeaponFireModeUtility.IsAutomaticEffectiveMode(mode))
			return;

		m_WeaponRuntime.TransientState.RegisterBurstShotFired();
	}

	private void ResetBurstSpreadCounter()
	{
		m_WeaponRuntime?.TransientState.ResetBurstShotCounter();
	}

	private void HandleVisibleTargetChanged(Transform _newVisibleTarget)
	{
		TrySyncEngagementTarget();
	}

	private bool HasEngageableVisibleTarget()
	{
		return m_Vision != null && m_Vision.GetEngageableVisibleTarget() != null;
	}

	private bool IsConscious()
	{
		return m_Consciousness == null || m_Consciousness.IsConscious;
	}

	private void TrySyncEngagementTarget()
	{
		Transform engageableTarget = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		if (engageableTarget == m_LastVisibleTargetForFire)
			return;

		Transform previousTarget = m_LastVisibleTargetForFire;
		m_LastVisibleTargetForFire = engageableTarget;

		if (m_Vision != null && m_Vision.ShouldReacquireAimAfterSwitch(previousTarget, engageableTarget))
			StopFiring();
	}
	#endregion
}
