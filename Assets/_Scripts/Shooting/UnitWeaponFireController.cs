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

	[Header("Debug")]
	[SerializeField] private bool m_IsFiringCommandActive;
	[SerializeField] private WeaponShotAttemptResult m_LastShotAttemptResult = WeaponShotAttemptResult.NoWeapon;
	[SerializeField] private AmmoDefinition m_LastFiredAmmoDefinition;
	[SerializeField] private int m_DebugSuccessfulShotCount;
	[SerializeField] private int m_DebugBurstShotsRemaining;
	[SerializeField] private float m_DebugNextBurstWaveTime;
	#endregion

	#region Private Fields
	private int m_BurstShotsRemainingInWave;
	private float m_NextBurstWaveTime;
	private float m_NextOutOfAmmoReloadAttemptTime;
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
	}

	private void Update()
	{
		if (!m_IsFiringCommandActive || !m_EnableAutomaticFireLoop)
			return;
		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return;

		WeaponFireMode mode = m_WeaponRuntime.RuntimeState.SelectedFireMode;
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
		m_IsFiringCommandActive = true;

		WeaponFireMode fireMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;

		if (fireMode == WeaponFireMode.FullAuto || fireMode == WeaponFireMode.Burst)
			return;

		TryFireSingleShot();
	}

	public void StopFiring()
	{
		m_IsFiringCommandActive = false;
		m_BurstShotsRemainingInWave = 0;
		m_NextBurstWaveTime = 0f;
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
		m_DebugBurstShotsRemaining = 0;
		m_DebugNextBurstWaveTime = 0f;
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

		if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponReadyToFire()))
			return WeaponShotAttemptResult.NotReady;

		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.Reload))
			return WeaponShotAttemptResult.Busy;

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return WeaponShotAttemptResult.Busy;

		if (m_RequireVisibleTarget && (m_Vision == null || m_Vision.VisibleTarget == null))
			return WeaponShotAttemptResult.NoVisibleTarget;

		return m_WeaponRuntime.TryConsumeShot(_currentTime, out _firedAmmoDefinition);
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
	#endregion
}
