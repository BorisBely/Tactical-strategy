using UnityEngine;

/// <summary>
/// Логи MK19: попытки выстрела, боезапас, reload/discipline и момент остановки очереди.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(52)]
public sealed class VehicleTurretMk19FireDiagnostics : MonoBehaviour
{
	private const string c_LogTag = "[Mk19Fire]";

	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;
	[SerializeField] private VehicleTurretReloadController m_TurretReload;

	[Header("Diagnostics")]
	[SerializeField] private bool m_LogDiagnostics = false;
	[SerializeField, Min(0.1f)] private float m_StatusLogIntervalSeconds = 2f;
	[SerializeField, Min(0.05f)] private float m_RepeatFailLogIntervalSeconds = 0.5f;

	private WeaponShotAttemptResult m_LastLoggedResult = (WeaponShotAttemptResult)(-1);
	private bool m_LastFiringCommandActive;
	private int m_LastLoggedSuccessfulShots;
	private float m_NextStatusLogTime;
	private float m_NextRepeatFailLogTime;
	private WeaponShotAttemptResult m_LastRepeatFailResult;
	private string m_LastStopReason;

	private void Awake()
	{
		if (m_Bridge == null)
			TryGetComponent(out m_Bridge);
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		if (m_TurretReload == null)
			TryGetComponent(out m_TurretReload);
	}

	private void LateUpdate()
	{
		if (!m_LogDiagnostics || m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;

		if (!IsMk19Active())
			return;

		RtsUnitMember gunner = m_Bridge.BoundGunner;
		if (gunner == null)
			return;

		UnitWeaponFireController fireController = gunner.GetComponent<UnitWeaponFireController>();
		if (fireController == null)
			return;

		TryLogShotAttemptChange(fireController, gunner);
		TryLogFiringCommandChange(fireController, gunner);
		TryLogSuccessfulShotCount(fireController, gunner);
		TryLogPeriodicStatus(fireController, gunner);
		TryLogSustainedFailure(fireController, gunner);
	}

	private bool IsMk19Active()
	{
		ItemDefinition weapon = m_Equipment != null ? m_Equipment.ActiveWeaponItem : null;
		return weapon != null && weapon.TurretWeaponVariant == TurretWeaponVariant.Mk19;
	}

	private void TryLogShotAttemptChange(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		WeaponShotAttemptResult result = _fireController.LastShotAttemptResult;
		if (result == m_LastLoggedResult)
			return;

		m_LastLoggedResult = result;

		if (result == WeaponShotAttemptResult.FireRateLimited)
			return;

		Debug.Log(
			$"{c_LogTag} {_gunner.name} attempt={result} {BuildAmmoSnapshot(_gunner)} " +
			$"{BuildCombatSnapshot(_fireController, _gunner)}",
			this);
	}

	private void TryLogFiringCommandChange(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		bool firing = _fireController.IsFiringCommandActive;
		if (firing == m_LastFiringCommandActive)
			return;

		if (firing)
		{
			Debug.Log(
				$"{c_LogTag} {_gunner.name} >>> START FIRING <<< {BuildAmmoSnapshot(_gunner)}",
				this);
		}
		else
		{
			m_LastStopReason = BuildStopReason(_fireController, _gunner);
			Debug.Log(
				$"{c_LogTag} {_gunner.name} >>> STOP FIRING <<< reason={m_LastStopReason} " +
				$"{BuildAmmoSnapshot(_gunner)} {BuildCombatSnapshot(_fireController, _gunner)}",
				this);
		}

		m_LastFiringCommandActive = firing;
	}

	private void TryLogSuccessfulShotCount(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		int count = GetSuccessfulShotCount(_fireController);
		if (count == m_LastLoggedSuccessfulShots)
			return;

		m_LastLoggedSuccessfulShots = count;
		AmmoDefinition ammo = _fireController.LastShotAttemptResult == WeaponShotAttemptResult.Success
			? GetLastFiredAmmo(_fireController)
			: null;
		string ammoName = ammo != null ? ammo.name : "?";
		Debug.Log(
			$"{c_LogTag} {_gunner.name} shot#{count} ammo={ammoName} {BuildAmmoSnapshot(_gunner)}",
			this);
	}

	private void TryLogPeriodicStatus(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		if (Time.time < m_NextStatusLogTime)
			return;

		m_NextStatusLogTime = Time.time + m_StatusLogIntervalSeconds;

		Debug.Log(
			$"{c_LogTag} {_gunner.name} STATUS firing={_fireController.IsFiringCommandActive} " +
			$"lastAttempt={_fireController.LastShotAttemptResult} {BuildAmmoSnapshot(_gunner)} " +
			$"{BuildCombatSnapshot(_fireController, _gunner)}",
			this);
	}

	private void TryLogSustainedFailure(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		if (!_fireController.IsFiringCommandActive)
		{
			m_LastRepeatFailResult = (WeaponShotAttemptResult)(-1);
			return;
		}

		WeaponShotAttemptResult result = _fireController.LastShotAttemptResult;
		if (result == WeaponShotAttemptResult.Success || result == WeaponShotAttemptResult.FireRateLimited)
		{
			m_LastRepeatFailResult = (WeaponShotAttemptResult)(-1);
			return;
		}

		if (result != m_LastRepeatFailResult)
		{
			m_LastRepeatFailResult = result;
			m_NextRepeatFailLogTime = 0f;
		}

		if (Time.time < m_NextRepeatFailLogTime)
			return;

		m_NextRepeatFailLogTime = Time.time + m_RepeatFailLogIntervalSeconds;
		Debug.LogWarning(
			$"{c_LogTag} {_gunner.name} BLOCKED while firing: {result} {BuildAmmoSnapshot(_gunner)} " +
			$"{BuildCombatSnapshot(_fireController, _gunner)}",
			this);
	}

	private static string BuildAmmoSnapshot(RtsUnitMember _gunner)
	{
		UnitWeaponRuntime runtime = _gunner.GetComponent<UnitWeaponRuntime>();
		if (runtime == null)
			return "ammo=(no runtime)";

		MagazineRuntimeState mag = runtime.CurrentMagazine;
		int magCount = mag != null ? mag.CurrentAmmoCount : 0;
		string chamber = runtime.HasRoundInChamber ? "yes" : "no";
		string magLoaded = runtime.HasLoadedMagazine ? "yes" : "no";
		return $"mag={magCount} chamber={chamber} hasMag={magLoaded}";
	}

	private string BuildCombatSnapshot(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		UnitWeaponFireDisciplineController discipline = _gunner.GetComponent<UnitWeaponFireDisciplineController>();
		UnitVision vision = _gunner.GetComponent<UnitVision>();
		UnitBusyState busy = _gunner.GetComponent<UnitBusyState>();
		UnitVehicleTurretReloadEvents turretReload = _gunner.GetComponent<UnitVehicleTurretReloadEvents>();

		string disciplineStr = discipline != null && discipline.HasActivePlan
			? $"discipline={discipline.PlannedEffectiveFireMode}/{discipline.PlannedSeriesShotCount} pause={discipline.IsInSeriesPause}"
			: "discipline=idle";

		Transform target = vision != null ? vision.GetEngageableVisibleTarget() : null;
		string targetStr = target != null ? target.name : "none";

		bool turretReloadAnim = turretReload != null && turretReload.IsReloadAnimationActive;
		bool vehicleReload = m_TurretReload != null && m_TurretReload.IsReloadBusy;

		string busyStr = busy != null && busy.IsBusy ? busy.Reasons.ToString() : "idle";

		return
			$"target={targetStr} {disciplineStr} turretReloadAnim={turretReloadAnim} vehReload={vehicleReload} busy={busyStr}";
	}

	private string BuildStopReason(UnitWeaponFireController _fireController, RtsUnitMember _gunner)
	{
		UnitBusyState busy = _gunner.GetComponent<UnitBusyState>();
		if (busy != null && busy.HasReason(UnitBusyState.BusyReason.Reload))
			return "busy:reload";

		if (m_TurretReload != null && m_TurretReload.IsReloadBusy)
			return "vehicle reload active";

		UnitVehicleTurretReloadEvents turretReload = _gunner.GetComponent<UnitVehicleTurretReloadEvents>();
		if (turretReload != null && turretReload.IsReloadAnimationActive)
			return "turret reload animation";

		WeaponShotAttemptResult last = _fireController.LastShotAttemptResult;
		if (last != WeaponShotAttemptResult.Success && last != WeaponShotAttemptResult.FireRateLimited)
			return $"lastAttempt={last}";

		return "command released / discipline pause";
	}

	private static int GetSuccessfulShotCount(UnitWeaponFireController _fireController)
	{
		return _fireController != null ? _fireController.DebugSuccessfulShotCountForDiagnostics : 0;
	}

	private static AmmoDefinition GetLastFiredAmmo(UnitWeaponFireController _fireController)
	{
		return _fireController != null ? _fireController.LastFiredAmmoDefinitionForDiagnostics : null;
	}
}
