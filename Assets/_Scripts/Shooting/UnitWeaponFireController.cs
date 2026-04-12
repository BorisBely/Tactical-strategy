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

	[Header("Fire Conditions")]
	[Tooltip("Запрещать выстрел, если оружие не на ready.")]
	[SerializeField] private bool m_RequireReady = true;
	[Tooltip("Запрещать выстрел, если сейчас нет видимой цели.")]
	[SerializeField] private bool m_RequireVisibleTarget = true;
	[Tooltip("Когда триггер удерживается, FullAuto будет сам пытаться стрелять каждый кадр.")]
	[SerializeField] private bool m_EnableAutomaticFireLoop = true;

	[Header("Debug")]
	[SerializeField] private bool m_IsFiringCommandActive;
	[SerializeField] private WeaponShotAttemptResult m_LastShotAttemptResult = WeaponShotAttemptResult.NoWeapon;
	[SerializeField] private AmmoDefinition m_LastFiredAmmoDefinition;
	[SerializeField] private int m_DebugSuccessfulShotCount;
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
	}

	private void Update()
	{
		if (!m_IsFiringCommandActive || !m_EnableAutomaticFireLoop)
			return;
		if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return;
		if (m_WeaponRuntime.RuntimeState.SelectedFireMode != WeaponFireMode.FullAuto)
			return;

		TryFireSingleShot();
	}
	#endregion

	#region Public Methods
	public void StartFiring()
	{
		m_IsFiringCommandActive = true;

		WeaponFireMode fireMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;

		// Пока burst ведёт себя как одиночный клик. Отдельная очередь burst появится следующим шагом.
		if (fireMode != WeaponFireMode.FullAuto)
			TryFireSingleShot();
	}

	public void StopFiring()
	{
		m_IsFiringCommandActive = false;
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
			ShotFired?.Invoke(firedAmmoDefinition);
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

		if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponEquippedAndReady()))
			return WeaponShotAttemptResult.NotReady;

		if (m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.Reload))
			return WeaponShotAttemptResult.Busy;

		if (m_RequireVisibleTarget && (m_Vision == null || m_Vision.VisibleTarget == null))
			return WeaponShotAttemptResult.NoVisibleTarget;

		return m_WeaponRuntime.TryConsumeShot(_currentTime, out _firedAmmoDefinition);
	}
	#endregion
}
