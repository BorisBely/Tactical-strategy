using UnityEngine;

/// <summary>
/// Автоматический драйвер стрельбы: строит короткие/длинные серии с паузами и порогом прицела
/// вместо бесконечного удержания спуска. Заменяет <see cref="UnitWeaponAutoFireWhenAimed"/>.
/// Порядок выполнения: 54 (раньше FireController = 56).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(54)]
public sealed class UnitWeaponFireDisciplineController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;

	[Header("Debug")]
	[SerializeField] private WeaponFireDisciplineMode m_DebugSelectedDiscipline = WeaponFireDisciplineMode.Auto;
	[SerializeField] private WeaponFireDisciplineMode m_DebugEffectiveDiscipline = WeaponFireDisciplineMode.Precision;
	[SerializeField] private WeaponFireMode m_DebugEffectiveFireMode = WeaponFireMode.SemiAuto;
	[SerializeField] private WeaponAimMode m_DebugEffectiveAimMode = WeaponAimMode.FullAim;
	[SerializeField, Range(0f, 1f)] private float m_DebugRequiredAimProgress = 1f;
	[SerializeField, Min(0)] private int m_DebugSeriesShotCount = 1;
	[SerializeField, Min(0)] private int m_DebugShotsFiredInSeries;
	[SerializeField, Min(0f)] private float m_DebugSeriesPauseSeconds;
	[SerializeField, Min(0f)] private float m_DebugPauseRemainingSeconds;
	[SerializeField] private string m_DebugPhase = "Idle";
	#endregion

	#region Private Fields
	private enum Phase
	{
		Idle = 0,
		Aiming = 1,
		Firing = 2,
		Pause = 3
	}

	private Phase m_Phase = Phase.Idle;
	private WeaponFireDisciplinePlan m_CurrentPlan;
	private bool m_HasPlan;
	private int m_ShotsFiredInSeries;
	private float m_PauseUntilTime;
	private Transform m_LastTarget;
	private Phase m_LastLoggedPhase = (Phase)(-1);
	#endregion

	#region Public Properties
	public bool HasActivePlan => m_HasPlan;
	public WeaponFireDisciplinePlan CurrentPlan => m_CurrentPlan;
	public WeaponFireMode PlannedEffectiveFireMode => m_HasPlan ? m_CurrentPlan.EffectiveFireMode : WeaponFireMode.SemiAuto;
	public WeaponAimMode PlannedEffectiveAimMode => m_HasPlan ? m_CurrentPlan.EffectiveAimMode : WeaponAimMode.FullAim;
	public float PlannedRequiredAimProgress01 => m_HasPlan ? m_CurrentPlan.RequiredAimProgress01 : 1f;
	public int PlannedSeriesShotCount => m_HasPlan ? m_CurrentPlan.SeriesShotCount : 1;
	public float PlannedSeriesPauseSeconds => m_HasPlan ? m_CurrentPlan.SeriesPauseSeconds : 0f;
	public bool IsInSeriesPause => m_Phase == Phase.Pause;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_IndividualTraits == null)
			m_IndividualTraits = GetComponent<UnitIndividualTraits>();
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
		if (m_TargetSelector != null)
			m_TargetSelector.SelectedTargetChanged += HandleSelectedTargetChanged;

		m_LastTarget = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
	}

	private void OnDisable()
	{
		if (m_FireController != null)
		{
			m_FireController.ShotFired -= HandleShotFired;
			m_FireController.ClearDisciplineBurstOverride();
			m_FireController.StopFiring();
		}

		if (m_TargetSelector != null)
			m_TargetSelector.SelectedTargetChanged -= HandleSelectedTargetChanged;

		ResetPlanState();
	}

	private void Update()
	{
		if (m_FireController == null || m_WeaponRuntime == null)
			return;

		if (!CanEngage())
		{
			if (m_Phase != Phase.Idle)
			{
				m_FireController.StopFiring();
				m_FireController.ClearDisciplineBurstOverride();
				ResetPlanState();
			}

			LogPhaseIfChanged();
			return;
		}

		switch (m_Phase)
		{
			case Phase.Idle:
				BeginNewSeries();
				break;
			case Phase.Aiming:
				UpdateAimingPhase();
				break;
			case Phase.Firing:
				UpdateFiringPhase();
				break;
			case Phase.Pause:
				UpdatePausePhase();
				break;
		}

		RefreshDebug();
		LogPhaseIfChanged();
	}
	#endregion

	#region Public Methods
	public bool TryGetAimGateOverride(out float _requiredAimProgress01, out WeaponAimMode _effectiveAimMode)
	{
		if (!m_HasPlan)
		{
			_requiredAimProgress01 = 1f;
			_effectiveAimMode = WeaponAimMode.FullAim;
			return false;
		}

		_requiredAimProgress01 = m_CurrentPlan.RequiredAimProgress01;
		_effectiveAimMode = m_CurrentPlan.EffectiveAimMode;
		return true;
	}

	public bool TryGetEffectiveFireModeOverride(out WeaponFireMode _effectiveFireMode)
	{
		if (!m_HasPlan)
		{
			_effectiveFireMode = WeaponFireMode.SemiAuto;
			return false;
		}

		_effectiveFireMode = m_CurrentPlan.EffectiveFireMode;
		return true;
	}

	public void InvalidateCurrentSeries()
	{
		m_FireController?.StopFiring();
		m_FireController?.ClearDisciplineBurstOverride();
		ResetPlanState();
	}
	#endregion

	#region Private Methods
	private bool CanEngage()
	{
		return m_FireController != null && m_FireController.ShouldHoldVirtualTriggerIgnoringAim();
	}

	private void BeginNewSeries()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		if (weaponDefinition == null || m_WeaponRuntime.RuntimeState == null)
			return;

		float distance = EstimateTargetDistanceMeters();
		WeaponFireMode selectedFireMode = m_WeaponRuntime.RuntimeState.SelectedFireMode;
		WeaponFireDisciplineMode selectedDiscipline = m_WeaponRuntime.SelectedFireDisciplineMode;

		m_CurrentPlan = WeaponFireDisciplinePlanner.CreatePlan(
			weaponDefinition,
			selectedFireMode,
			selectedDiscipline,
			distance,
			m_CombatStats,
			m_IndividualTraits);
		m_HasPlan = true;
		m_ShotsFiredInSeries = 0;
		m_Phase = Phase.Aiming;

		if (m_CurrentPlan.EffectiveFireMode == WeaponFireMode.Burst)
		{
			m_FireController.ConfigureDisciplineBurstOverride(
				m_CurrentPlan.SeriesShotCount,
				m_CurrentPlan.SeriesPauseSeconds);
		}
		else
			m_FireController.ClearDisciplineBurstOverride();
	}

	private void UpdateAimingPhase()
	{
		if (!m_HasPlan)
		{
			m_Phase = Phase.Idle;
			return;
		}

		EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
		float aimProgress = transientState != null ? transientState.AimProgress01 : 0f;
		if (aimProgress < m_CurrentPlan.RequiredAimProgress01)
		{
			if (m_FireController.IsFiringCommandActive)
				m_FireController.StopFiring();
			return;
		}

		m_Phase = Phase.Firing;
		m_FireController.StartFiring();
	}

	private void UpdateFiringPhase()
	{
		if (!m_HasPlan)
		{
			m_Phase = Phase.Idle;
			return;
		}

		if (m_CurrentPlan.EffectiveFireMode == WeaponFireMode.SemiAuto)
		{
			EquippedWeaponTransientState transientState = m_WeaponRuntime.TransientState;
			float aimProgress = transientState != null ? transientState.AimProgress01 : 0f;
			if (aimProgress >= m_CurrentPlan.RequiredAimProgress01)
			{
				if (!m_FireController.IsFiringCommandActive)
					m_FireController.StartFiring();
				else
					m_FireController.TryContinueHeldSemiFire();
			}
		}
		else if (!m_FireController.IsFiringCommandActive)
		{
			m_FireController.StartFiring();
		}

		if (m_ShotsFiredInSeries < m_CurrentPlan.SeriesShotCount)
			return;

		FinishSeriesAndEnterPause();
	}

	private void UpdatePausePhase()
	{
		float remaining = m_PauseUntilTime - Time.time;
		m_DebugPauseRemainingSeconds = Mathf.Max(0f, remaining);
		if (Time.time < m_PauseUntilTime)
			return;

		m_FireController.ClearDisciplineBurstOverride();
		m_HasPlan = false;
		m_Phase = Phase.Idle;
	}

	private void FinishSeriesAndEnterPause()
	{
		if (m_Phase == Phase.Pause)
			return;

		m_FireController.StopFiring();
		float pause = m_HasPlan ? m_CurrentPlan.SeriesPauseSeconds : 0.35f;
		float jitter = Random.Range(0.85f, 1.2f);
		m_PauseUntilTime = Time.time + pause * jitter;
		m_Phase = Phase.Pause;
		m_DebugPauseRemainingSeconds = pause * jitter;
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_Phase != Phase.Firing || !m_HasPlan)
			return;

		m_ShotsFiredInSeries++;
		if (m_ShotsFiredInSeries >= m_CurrentPlan.SeriesShotCount)
			FinishSeriesAndEnterPause();
	}

	private void HandleSelectedTargetChanged(Transform _newSelectedTarget)
	{
		Transform engageable = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		if (engageable == m_LastTarget)
			return;

		m_LastTarget = engageable;
		InvalidateCurrentSeries();
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		if (target == null)
			return 0f;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform fireOrigin = weapon != null && weapon.FireOriginTransform != null
			? weapon.FireOriginTransform
			: transform;
		Vector3 targetPoint = m_TargetSelector.GetEngageableAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		return Vector3.Distance(fireOrigin.position, targetPoint);
	}

	private void ResetPlanState()
	{
		m_Phase = Phase.Idle;
		m_HasPlan = false;
		m_ShotsFiredInSeries = 0;
		m_PauseUntilTime = 0f;
		m_DebugPauseRemainingSeconds = 0f;
		m_DebugPhase = "Idle";
	}

	private void RefreshDebug()
	{
		m_DebugPhase = m_Phase.ToString();
		m_DebugSelectedDiscipline = m_WeaponRuntime != null
			? m_WeaponRuntime.SelectedFireDisciplineMode
			: WeaponFireDisciplineMode.Auto;
		if (!m_HasPlan)
			return;

		m_DebugEffectiveDiscipline = m_CurrentPlan.EffectiveDiscipline;
		m_DebugEffectiveFireMode = m_CurrentPlan.EffectiveFireMode;
		m_DebugEffectiveAimMode = m_CurrentPlan.EffectiveAimMode;
		m_DebugRequiredAimProgress = m_CurrentPlan.RequiredAimProgress01;
		m_DebugSeriesShotCount = m_CurrentPlan.SeriesShotCount;
		m_DebugShotsFiredInSeries = m_ShotsFiredInSeries;
		m_DebugSeriesPauseSeconds = m_CurrentPlan.SeriesPauseSeconds;
	}

	private void LogPhaseIfChanged()
	{
		if (!UnitActionLog.Enabled || m_Phase == m_LastLoggedPhase)
			return;
		m_LastLoggedPhase = m_Phase;
		string plan = "none";
		if (m_HasPlan)
		{
			plan = "mode=" + m_CurrentPlan.EffectiveFireMode +
			       " aim=" + m_CurrentPlan.EffectiveAimMode +
			       " needAim=" + UnitActionLog.F2(m_CurrentPlan.RequiredAimProgress01) +
			       " series=" + m_CurrentPlan.SeriesShotCount +
			       " pause=" + UnitActionLog.F2(m_CurrentPlan.SeriesPauseSeconds) +
			       " fired=" + m_ShotsFiredInSeries;
		}

		string tgt = m_TargetSelector != null && m_TargetSelector.SelectedTarget != null
			? UnitActionLog.Slot(m_TargetSelector.SelectedTarget)
			: "none";
		UnitActionLog.Write(this, UnitActionLog.Disc, "phase=" + m_Phase + " tgt=" + tgt + " " + plan);
	}
	#endregion
}
