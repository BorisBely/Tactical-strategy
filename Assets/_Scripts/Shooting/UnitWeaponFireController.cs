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
	[SerializeField] private UnitWeaponAimProgressController m_AimProgressController;
	[SerializeField] private UnitWeaponFireDisciplineController m_FireDisciplineController;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[SerializeField] private UnitWeaponVisualRecoilKick m_VisualRecoilKick;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitTeam m_Team;

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

	[Header("Line of Fire Safety")]
	[Tooltip("Радиус SphereCast для проверки союзников/нейтралов на линии огня.")]
	[SerializeField, Range(0.05f, 1f)] private float m_LineOfFireSafetyRadius = 0.35f;
	[Tooltip("Интервал кэширования результата проверки линии огня при блокировке. Предотвращает повторные SphereCast каждый кадр.")]
	[SerializeField, Range(0.05f, 1f)] private float m_LineOfFireBlockedRetrySeconds = 0.15f;
	[Tooltip("Слои для проверки линии огня. Должны включать слои дружественных/нейтральных юнитов.")]
	[SerializeField] private LayerMask m_LineOfFireLayers = ~0;

	[Header("Aiming Gate")]
	[Tooltip("Запрещать выстрел, пока не достигнут порог выбранного режима прицеливания. Для Burst/FullAuto — только 1-й выстрел серии или очереди.")]
	[SerializeField] private bool m_RequireFullAimToFire = true;
	[Tooltip("Запрещать выстрел, пока визуальный ствол ещё не вернулся к точке цели после kick.")]
	[SerializeField] private bool m_RequireBarrelAlignedToFire = true;
	[Tooltip("Допуск угла ствола (градусы) при стоянии на месте без активного перемещения.")]
	[SerializeField, Range(0f, 30f)] private float m_MaxBarrelAimErrorDegrees = 3f;
	[Tooltip("Допуск угла ствола (градусы) в приседе/лёжа. Сидячие позы чаще дают небольшой визуальный перекос оружия.")]
	[SerializeField, Range(0f, 30f)] private float m_MaxBarrelAimErrorDegreesCrouch = 9f;
	[Tooltip("Допуск угла ствола (градусы) в приседе/лёжа при активном перемещении.")]
	[SerializeField, Range(0f, 30f)] private float m_MaxBarrelAimErrorDegreesCrouchMoving = 10f;
	[Tooltip("Допуск угла ствола (градусы) при активном перемещении (ходьба, бег, заказ пути).")]
	[SerializeField, Range(0f, 30f)] private float m_MaxBarrelAimErrorDegreesMoving = 8f;

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
	private UnitClickToMove m_ClickToMove;
	private UnitNavLocomotionDriver m_LocomotionDriver;
	private UnitFallenDragController m_FallenDragController;
	private int m_BurstShotsRemainingInWave;
	private float m_NextBurstWaveTime;
	private float m_NextOutOfAmmoReloadAttemptTime;
	private bool m_SemiShotConsumedForCurrentTrigger;
	private Transform m_LastVisibleTargetForFire;
	private bool m_HasDisciplineBurstOverride;
	private int m_DisciplineBurstRoundsOverride = 3;
	private float m_DisciplineBurstPauseOverrideSeconds;
	private RaycastHit[] m_LineOfFireHits;
	private const int c_LofHitBufferSize = 16;
	private float m_NextLineOfFireCheckTime;
	private bool m_LastLineOfFireBlocked;
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
		if (m_AimProgressController == null)
			m_AimProgressController = GetComponent<UnitWeaponAimProgressController>();
		if (m_FireDisciplineController == null)
			m_FireDisciplineController = GetComponent<UnitWeaponFireDisciplineController>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();
		if (m_VisualRecoilKick == null)
			m_VisualRecoilKick = GetComponent<UnitWeaponVisualRecoilKick>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
		if (GetComponent<UnitStanceCombatModifiers>() == null)
			gameObject.AddComponent<UnitStanceCombatModifiers>();
		m_LineOfFireHits = new RaycastHit[c_LofHitBufferSize];
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
		TryReleaseSemiTriggerForReAim();

		if (!m_IsFiringCommandActive || !m_EnableAutomaticFireLoop)
			return;

		if (IsFireBlockedByBusyState())
		{
			StopFiring();
			return;
		}

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

		if (IsFireBlockedByBusyState())
		{
			StopFiring();
			return;
		}

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
		if (!ShouldHoldVirtualTriggerIgnoringAim())
			return false;

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		m_DebugCurrentAimProgress = transientState != null ? transientState.AimProgress01 : 0f;
		return IsAimedEnoughToFire();
	}

	/// <summary>
	/// Базовые условия для вступления в огневой контакт без проверки порога прицела.
	/// Используется огневой дисциплиной, чтобы копить AimProgress между сериями.
	/// </summary>
	public bool ShouldHoldVirtualTriggerIgnoringAim()
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

		if (IsFireBlockedByBusyState())
			return false;

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return false;

		if (m_RequireVisibleTarget && !HasEngageableVisibleTarget())
			return false;

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		m_DebugCurrentAimProgress = transientState != null ? transientState.AimProgress01 : 0f;
		return true;
	}

	public void ConfigureDisciplineBurstOverride(int _burstRounds, float _burstPauseSeconds)
	{
		m_HasDisciplineBurstOverride = true;
		m_DisciplineBurstRoundsOverride = Mathf.Max(2, _burstRounds);
		m_DisciplineBurstPauseOverrideSeconds = Mathf.Max(0f, _burstPauseSeconds);
	}

	public void ClearDisciplineBurstOverride()
	{
		m_HasDisciplineBurstOverride = false;
		m_DisciplineBurstRoundsOverride = 3;
		m_DisciplineBurstPauseOverrideSeconds = 0f;
	}

	public WeaponFireMode ResolveEffectiveFireMode()
	{
		WeaponFireMode selectedMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;

		WeaponFireMode effectiveMode;
		if (m_FireDisciplineController != null &&
		    m_FireDisciplineController.TryGetEffectiveFireModeOverride(out WeaponFireMode disciplineFireMode))
		{
			effectiveMode = disciplineFireMode;
		}
		else if (m_HitscanShooting != null &&
			m_HitscanShooting.TrySelectAutoModes(out WeaponAutoModeSelectionResult selection))
		{
			effectiveMode = selection.EffectiveFireMode;
		}
		else
		{
			effectiveMode = m_WeaponRuntime != null
				? m_WeaponRuntime.ResolveEffectiveFireMode(EstimateTargetDistanceMeters())
				: WeaponFireMode.SemiAuto;
		}

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

	/// <summary>
	/// Повторный одиночный выстрел при уже удержанном курке после повторного набора прицела.
	/// </summary>
	public WeaponShotAttemptResult TryContinueHeldSemiFire()
	{
		if (!m_IsFiringCommandActive)
			return WeaponShotAttemptResult.Busy;

		if (WeaponFireModeUtility.IsAutomaticEffectiveMode(ResolveEffectiveFireMode()))
			return WeaponShotAttemptResult.Busy;

		if (m_SemiShotConsumedForCurrentTrigger)
			return WeaponShotAttemptResult.Busy;

		WeaponShotAttemptResult result = TryFireSingleShot();
		if (result == WeaponShotAttemptResult.Success)
			m_SemiShotConsumedForCurrentTrigger = true;
		return result;
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

		if (IsFireBlockedByBusyState())
			return WeaponShotAttemptResult.Busy;

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return WeaponShotAttemptResult.Busy;

		if (m_RequireVisibleTarget && !HasEngageableVisibleTarget())
			return WeaponShotAttemptResult.NoVisibleTarget;

		if (!IsAimedEnoughToFire())
		{
			return WeaponShotAttemptResult.NotAimed;
		}

		if (IsLineOfFireBlocked())
		{
			m_Vision?.SuppressCurrentTargetForLineOfFire(m_LineOfFireBlockedRetrySeconds);
			return WeaponShotAttemptResult.LineOfFireBlocked;
		}

		WeaponFireMode fireMode = ResolveEffectiveFireMode();
		return m_WeaponRuntime.TryConsumeShot(_currentTime, fireMode, out _firedAmmoDefinition);
	}

	public void StopFiring()
	{
		m_IsFiringCommandActive = false;
		m_BurstShotsRemainingInWave = 0;
		m_NextBurstWaveTime = 0f;
		m_SemiShotConsumedForCurrentTrigger = false;
		ResetBurstSpreadCounter();
		ResetRecoilAfterStopFiring();
	}

	private void ResetRecoilAfterStopFiring()
	{
		m_RecoilController?.ResetRecoilPenalty();
		m_VisualRecoilKick?.ResetVisualKick();
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
		ClearDisciplineBurstOverride();
		ResetBurstSpreadCounter();
		m_FireDisciplineController?.InvalidateCurrentSeries();
	}

	/// <summary>
	/// Жёсткий запрет выстрелов экипированного оружия: reload / throw / rocket / stabilize / proximity.
	/// </summary>
	private bool IsFireBlockedByBusyState()
	{
		if (m_BusyState == null)
			return false;

		return m_BusyState.HasReason(UnitBusyState.BusyReason.Reload) ||
		       m_BusyState.HasReason(UnitBusyState.BusyReason.Throw) ||
		       m_BusyState.HasReason(UnitBusyState.BusyReason.RocketLauncher) ||
		       m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization) ||
		       m_BusyState.HasReason(UnitBusyState.BusyReason.StabilizeOther) ||
		       m_BusyState.HasReason(UnitBusyState.BusyReason.ProximityRelax);
	}

	private bool IsAimedEnoughToFire()
	{
		EquippedWeaponTransientState transientState = m_WeaponRuntime != null ? m_WeaponRuntime.TransientState : null;
		m_DebugCurrentAimProgress = transientState != null ? transientState.AimProgress01 : 0f;
		if (ShouldRequireAimProgressForNextShot() && !HasRequiredAimProgress(transientState))
			return false;

		if (!m_RequireBarrelAlignedToFire || !HasEngageableVisibleTarget())
			return true;

		if (ShouldSkipBarrelAlignmentForBoltCycle())
			return true;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : null;
		if (fireOrigin == null)
			return false;

		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = m_Vision.GetEngageableVisibleTarget().position;

		Vector3 toTarget = targetPoint - fireOrigin.position;
		if (toTarget.sqrMagnitude < 1e-6f)
		{
			m_DebugLastBarrelAimErrorDegrees = 0f;
			return true;
		}

		float maxError = ResolveMaxBarrelAimErrorDegrees();
		m_DebugLastBarrelAimErrorDegrees = Vector3.Angle(fireOrigin.forward, toTarget.normalized);
		return m_DebugLastBarrelAimErrorDegrees <= maxError;
	}

	private bool IsLineOfFireBlocked()
	{
		if (m_WeaponRuntime == null || m_Vision == null || m_Equipment == null)
			return false;

		EquippedWeapon weapon = m_Equipment.EquippedWeapon;
		if (weapon == null)
			return false;

		Transform fireOrigin = weapon.FireOriginTransform;
		if (fireOrigin == null)
			return false;

		Vector3 aimPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (aimPoint == Vector3.zero)
			return false;

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		if (transientState != null && transientState.NextAllowedShotTime > Time.time && !m_LastLineOfFireBlocked && m_NextLineOfFireCheckTime > Time.time)
			return false;

		if (Time.time < m_NextLineOfFireCheckTime)
			return m_LastLineOfFireBlocked;

		Vector3 dir = aimPoint - fireOrigin.position;
		float dist = dir.magnitude;
		if (dist < 0.05f)
			return false;

		dir /= dist;

		int hitCount = Physics.SphereCastNonAlloc(
			fireOrigin.position,
			m_LineOfFireSafetyRadius,
			dir,
			m_LineOfFireHits,
			dist,
			m_LineOfFireLayers,
			QueryTriggerInteraction.Ignore);

		UnitTeamId myTeam = m_Team != null ? m_Team.Team : UnitTeamId.Player;

		bool blocked = false;
		string blockerName = null;
		var seenRoots = new System.Collections.Generic.HashSet<Transform>();
		for (int h = 0; h < hitCount; h++)
		{
			RaycastHit hit = m_LineOfFireHits[h];
			Collider hc = hit.collider;
			if (hc == null)
				continue;

			Transform hitTransform = hc.transform;
			if (hitTransform == transform || hitTransform.IsChildOf(transform))
				continue;

			if (hc.GetComponent<UnitBodyHitZone>() == null && hc.GetComponentInParent<UnitBodyHitZone>() == null)
				continue;

			UnitVision hitVision = hitTransform.GetComponentInParent<UnitVision>();
			if (hitVision != null && !seenRoots.Add(hitVision.transform))
				continue;

			UnitTeam hitTeam = hc.GetComponentInParent<UnitTeam>();
			if (hitTeam == null)
				continue;

			if (hitTeam.Team == myTeam || hitTeam.Team == UnitTeamId.Neutral)
			{
				if (hitTransform.GetComponentInParent<UnitVision>() == null)
					continue;

				blocked = true;
				blockerName = hc.name;
				break;
			}
		}

		m_NextLineOfFireCheckTime = Time.time + m_LineOfFireBlockedRetrySeconds;
		m_LastLineOfFireBlocked = blocked;

		return blocked;
	}

	private float ResolveMaxBarrelAimErrorDegrees()
	{
		LocomotionStance stance = m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;
		if (stance == LocomotionStance.Crouch || stance == LocomotionStance.Prone)
		{
			return IsMovingForBarrelAimGate()
				? m_MaxBarrelAimErrorDegreesCrouchMoving
				: m_MaxBarrelAimErrorDegreesCrouch;
		}

		if (IsMovingForBarrelAimGate())
			return m_MaxBarrelAimErrorDegreesMoving;

		return m_MaxBarrelAimErrorDegrees;
	}

	private bool IsMovingForBarrelAimGate()
	{
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			return false;

		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.HasMoveIntent;
	}

	private bool ShouldSkipBarrelAlignmentForBoltCycle()
	{
		WeaponRuntimeState runtimeState = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		if (runtimeState == null)
			return false;

		return !runtimeState.HasRoundInChamber &&
		       runtimeState.HasMagazine &&
		       runtimeState.HasAmmoInMagazine;
	}

	/// <summary>
	/// После одиночного выстрела при удержании «курка» сбрасывает блок повторного выстрела,
	/// когда прицел снова набран (AimProgressController сбрасывает прогресс после выстрела).
	/// </summary>
	private void TryReleaseSemiTriggerForReAim()
	{
		if (!m_SemiShotConsumedForCurrentTrigger || m_WeaponRuntime == null)
			return;

		if (WeaponFireModeUtility.IsAutomaticEffectiveMode(ResolveEffectiveFireMode()))
			return;

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		if (transientState == null || !HasRequiredAimProgress(transientState))
			m_SemiShotConsumedForCurrentTrigger = false;
	}

	private bool HasRequiredAimProgress(EquippedWeaponTransientState _transientState)
	{
		if (_transientState == null || m_WeaponRuntime == null)
			return false;

		float distanceMeters = EstimateTargetDistanceMeters();
		if (m_FireDisciplineController != null &&
		    m_FireDisciplineController.TryGetAimGateOverride(out float disciplineRequiredProgress, out _))
		{
			return _transientState.AimProgress01 >= disciplineRequiredProgress;
		}

		WeaponAimMode effectiveAimMode = m_HitscanShooting != null &&
			m_HitscanShooting.TrySelectAutoModes(out WeaponAutoModeSelectionResult selection)
			? selection.EffectiveAimMode
			: WeaponAimModeUtility.ResolveEffectiveMode(
				WeaponFireDisciplineModeUtility.MapToAimMode(
					m_WeaponRuntime.SelectedFireDisciplineMode,
					distanceMeters),
				distanceMeters);
		float requiredProgress = WeaponAimModeUtility.GetRequiredAimProgress01(
			effectiveAimMode,
			distanceMeters,
			EstimateFullAimTimeSeconds());
		return _transientState.AimProgress01 >= requiredProgress;
	}

	private float EstimateFullAimTimeSeconds()
	{
		if (m_AimProgressController != null)
			return Mathf.Max(0.01f, m_AimProgressController.CurrentAimTimeSeconds);

		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return 0.25f;

		return Mathf.Max(0.01f, WeaponDistanceAimEvaluator.GetRequiredAimTimeSeconds(
			m_WeaponRuntime.CurrentWeaponDefinition,
			m_WeaponRuntime.RuntimeState != null ? m_WeaponRuntime.RuntimeState.EquippedAttachments : null,
			EstimateTargetDistanceMeters()));
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
		Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : transform;
		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		return Vector3.Distance(fireOrigin.position, targetPoint);
	}

	private void UpdateBurstFire(float _time)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (weaponDefinition == null)
			return;

		int burstSize = m_HasDisciplineBurstOverride
			? Mathf.Max(2, m_DisciplineBurstRoundsOverride)
			: Mathf.Max(2, weaponDefinition.BurstRounds);
		float pause = m_HasDisciplineBurstOverride
			? Mathf.Max(0f, m_DisciplineBurstPauseOverrideSeconds)
			: Mathf.Max(0f, weaponDefinition.BurstPauseSeconds);

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
				{
					m_NextBurstWaveTime = _time + pause;
					m_WeaponRuntime?.SetAimProgress(0f);
				}
				break;
			case WeaponShotAttemptResult.FireRateLimited:
			case WeaponShotAttemptResult.Busy:
			case WeaponShotAttemptResult.NeedsBoltCycle:
			case WeaponShotAttemptResult.NotAimed:
			case WeaponShotAttemptResult.LineOfFireBlocked:
				break;
			default:
				m_BurstShotsRemainingInWave = 0;
				m_NextBurstWaveTime = _time + pause;
				m_WeaponRuntime?.SetAimProgress(0f);
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

		m_NextLineOfFireCheckTime = 0f;
		m_LastLineOfFireBlocked = false;
		Transform previousTarget = m_LastVisibleTargetForFire;
		m_LastVisibleTargetForFire = engageableTarget;

		if (m_Vision != null && m_Vision.ShouldReacquireAimAfterSwitch(previousTarget, engageableTarget))
			StopFiring();
	}
	#endregion
}
