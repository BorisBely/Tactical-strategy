using UnityEngine;

/// <summary>
/// Вызывается из <see cref="UnitWeaponFireController"/> после расхода патрона; бросает hitscan из <see cref="EquippedWeapon.BarrelTransform"/> до события <c>ShotFired</c>.
/// Настрой на сцене: слой попаданий, дистанция; на целях — <see cref="DamageableTarget"/> и коллайдер.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(57)]
public sealed class UnitWeaponHitscanShooting : MonoBehaviour
{
	#region Events
	public event System.Action<WeaponShotTraceInfo> ShotTrace;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitVision m_Vision;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;
	[SerializeField] private UnitWeaponAimProgressController m_AimProgressController;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;

	[Header("Hitscan")]
	[Tooltip("Слои, по которым проверяем попадание. Создай слой Target и назначь мишеням.")]
	[SerializeField] private LayerMask m_HitLayers = ~0;
	[Tooltip("Максимальная дальность луча.")]
	[SerializeField, Min(0.5f)] private float m_MaxDistance = 500f;
	[Tooltip("Сдвиг начала луча вперёд от Barrel, чтобы не задевать свой коллайдер.")]
	[SerializeField, Min(0f)] private float m_BarrelRayStartOffset = 0.08f;
	[Tooltip("QueryTriggerInteraction для Raycast.")]
	[SerializeField] private QueryTriggerInteraction m_TriggerInteraction = QueryTriggerInteraction.Ignore;

	[Header("Spread (множители к WeaponDefinition.BaseShotDispersion)")]
	[Tooltip("Градусы половины конуса: BaseShotDispersion, патрон, модуль прицела, отдача, умножить на этот коэффициент.")]
	[SerializeField, Min(0.001f)] private float m_BaseSpreadToDegrees = 0.35f;
	[Tooltip("Вклад RecoilPenalty: множитель разброса += penalty * это значение.")]
	[SerializeField, Min(0f)] private float m_RecoilSpreadScale = 0.22f;
	[Tooltip("Минимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0f)] private float m_MinHalfAngleDegrees = 0.04f;
	[Tooltip("Максимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0.01f)] private float m_MaxHalfAngleDegrees = 12f;

	[Header("Procedural Recoil Pattern")]
	[Tooltip("Геймплейный подъём паттерна на единицу накопленной отдачи. Случайный конус остаётся мелкой неточностью поверх этого смещения.")]
	[SerializeField, Min(0f)] private float m_RecoilPatternPitchDegreesPerPenaltyUnit = 0.09f;
	[Tooltip("Боковой увод паттерна как доля от вертикального подъёма.")]
	[SerializeField, Range(0f, 1f)] private float m_RecoilPatternYawFraction = 0.55f;
	[Tooltip("Небольшая процедурная неровность бокового увода, чтобы длинная очередь не была идеально синусоидальной.")]
	[SerializeField, Range(0f, 1f)] private float m_RecoilPatternChaosFraction = 0.22f;

	[Header("Advanced Spread Multipliers")]
	[Tooltip("Прямой множитель финального разброса стоя.")]
	[SerializeField, Min(0.01f)] private float m_StandingSpreadMultiplier = 1f;
	[Tooltip("Прямой множитель финального разброса в приседе.")]
	[SerializeField, Min(0.01f)] private float m_CrouchSpreadMultiplier = 0.9f;
	[Tooltip("Прямой множитель финального разброса лёжа.")]
	[SerializeField, Min(0.01f)] private float m_ProneSpreadMultiplier = 0.75f;
	[Tooltip("Прямой множитель финального разброса при движении.")]
	[SerializeField, Min(0.01f)] private float m_MovingSpreadMultiplier = 1.35f;
	[Tooltip("Прямой множитель финального разброса в спринте. Обычно спринт блокирует ready/fire раньше этого этапа.")]
	[SerializeField, Min(0.01f)] private float m_SprintSpreadMultiplier = 2f;

	[Header("Damage falloff")]
	[Tooltip("Если включено: за эффективной дальностью урон падает (см. кривую).")]
	[SerializeField] private bool m_UseDistanceFalloff = true;
	[Tooltip("При дистанции >= эффективной × этот множитель урон обнуляется.")]
	[SerializeField, Min(1.01f)] private float m_FalloffZeroRangeMultiplier = 2f;

	[Header("Debug")]
	[SerializeField] private bool m_LogShots = true;
	[SerializeField] private bool m_DrawDebugRays;
	[SerializeField, Min(0f)] private float m_DebugRayDuration = 10f;
	[SerializeField] private string m_DebugLastHitName;
	[SerializeField] private float m_DebugLastDamage;
	[SerializeField, Min(0f)] private float m_DebugLastHalfAngleDegrees;
	[SerializeField, Min(0f)] private float m_DebugLastTargetDistanceMeters;
	[SerializeField, Min(0f)] private float m_DebugLastRecoilMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastStanceMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastMovementMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastSkillMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastConditionMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastAimCompletionMultiplier = 1f;
	[SerializeField, Range(0f, 1f)] private float m_DebugLastAimProgress = 1f;
	[SerializeField, Min(0f)] private float m_DebugLastSpreadDiameterMeters;
	[SerializeField, Min(0f)] private float m_DebugAcceptableSpreadDiameterMeters;
	[SerializeField, Min(0f)] private float m_DebugLastRecoilPenalty;
	[SerializeField, Min(0f)] private float m_DebugLastPatternPitchDegrees;
	[SerializeField, Min(0f)] private float m_DebugLastPatternVerticalOffsetMeters;
	#endregion

	#region Private Fields
	private Transform m_ShooterRoot;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		m_CombatStats = ResolveCombatStats();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
		if (m_StanceCombatModifiers == null)
			m_StanceCombatModifiers = GetComponent<UnitStanceCombatModifiers>();
		if (m_AimProgressController == null)
			m_AimProgressController = GetComponent<UnitWeaponAimProgressController>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();

		m_ShooterRoot = transform.root;
	}
	#endregion

	#region Public Methods
	/// <summary>Вызывается из <see cref="UnitWeaponFireController"/> до события ShotFired, чтобы разброс не включал отдачу только что сделанного выстрела.</summary>
	public void ProcessSuccessfulShot(AmmoDefinition _ammo)
	{
		HandleShotFired(_ammo);
	}

	public bool TrySelectAutoModes(out WeaponAutoModeSelectionResult _selection)
	{
		return TrySelectAutoModes(null, out _selection);
	}

	public bool TrySelectAutoModes(AmmoDefinition _ammo, out WeaponAutoModeSelectionResult _selection)
	{
		_selection = default;
		if (!TryBuildAutoSelectionInput(_ammo, out WeaponAutoModeSelectionInput input))
			return false;

		_selection = WeaponAutoModeSelectionUtility.Select(input);
		return true;
	}
	#endregion

	#region Private Methods
	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (_ammo == null || m_Equipment == null || m_WeaponRuntime == null)
			return;

		EquippedWeapon weapon = m_Equipment.EquippedWeapon;
		if (weapon == null)
			return;

		Transform barrel = weapon.BarrelTransform;
		Vector3 origin = barrel.position + barrel.forward * m_BarrelRayStartOffset;
		Vector3 baseDirection = GetGameplayShotDirection(origin, barrel);
		WeaponShotAccuracyContext accuracyContext = BuildAccuracyContext(_ammo);
		ProceduralRecoilPatternResult patternResult = ApplyProceduralRecoilPattern(baseDirection, accuracyContext);
		Vector3 patternedDirection = patternResult.Direction;
		float halfAngle = accuracyContext.HalfAngleDegrees;
		StoreDebugAccuracyContext(accuracyContext, patternResult);

		WeaponShotHitResult aggregateHitResult = WeaponShotHitResult.Miss;
		int projectileCount = Mathf.Max(1, _ammo.ProjectileCount);
		for (int i = 0; i < projectileCount; i++)
		{
			Vector3 dir = ApplyConeSpread(patternedDirection, halfAngle);
			WeaponShotHitResult shotResult = TryHit(origin, dir, _ammo);
			aggregateHitResult = CombineHitResults(aggregateHitResult, shotResult);
		}

		if (m_LogShots)
		{
			WeaponShotRecoilLogInfo recoilLogInfo = BuildRecoilLogInfo(_ammo, accuracyContext, patternResult);
			LogShot(_ammo, weapon, accuracyContext, recoilLogInfo, aggregateHitResult, projectileCount);
		}
	}

	private UnitCombatStats ResolveCombatStats()
	{
		return UnitCombatStatsLookup.ResolveOnUnit(this);
	}

	private WeaponShotAccuracyContext BuildAccuracyContext(AmmoDefinition _ammo)
	{
		float targetDistanceMeters = EstimateTargetDistanceMeters();
		WeaponShotAccuracyInput accuracyInput = BuildAccuracyInput(_ammo, targetDistanceMeters);

		if (TrySelectAutoModes(_ammo, out WeaponAutoModeSelectionResult selection))
		{
			accuracyInput.FireMode = selection.EffectiveFireMode;
			accuracyInput.AimMode = selection.EffectiveAimMode;
			return WeaponShotAccuracyEvaluator.Evaluate(accuracyInput);
		}

		return WeaponShotAccuracyEvaluator.Evaluate(accuracyInput);
	}

	private bool TryBuildAutoSelectionInput(AmmoDefinition _ammo, out WeaponAutoModeSelectionInput _input)
	{
		_input = default;
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		float targetDistanceMeters = EstimateTargetDistanceMeters();
		WeaponShotAccuracyInput accuracyInput = BuildAccuracyInput(_ammo, targetDistanceMeters);
		_input = new WeaponAutoModeSelectionInput
		{
			AccuracyInput = accuracyInput,
			SelectedFireMode = accuracyInput.SelectedFireMode,
			SelectedAimMode = m_WeaponRuntime.SelectedAimMode,
			AvailableFireModes = m_WeaponRuntime.CurrentWeaponDefinition.AvailableFireModes,
			TargetDistanceMeters = targetDistanceMeters
		};
		return true;
	}

	private WeaponShotAccuracyInput BuildAccuracyInput(AmmoDefinition _ammo, float _targetDistanceMeters)
	{
		UnitCombatStats combatStats = ResolveCombatStats();
		WeaponFireMode selectedFireMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
		WeaponAimMode selectedAimMode = m_WeaponRuntime != null ? m_WeaponRuntime.SelectedAimMode : WeaponAimMode.FullAim;
		return new WeaponShotAccuracyInput
		{
			WeaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition,
			WeaponState = m_WeaponRuntime.RuntimeState,
			TransientState = m_WeaponRuntime.TransientState,
			AmmoDefinition = _ammo != null ? _ammo : ResolveExpectedAmmoDefinition(),
			CombatStats = combatStats,
			CombatCondition = m_CombatCondition,
			TargetDistanceMeters = _targetDistanceMeters,
			BaseSpreadToDegrees = m_BaseSpreadToDegrees,
			RecoilSpreadScale = m_RecoilSpreadScale,
			MinHalfAngleDegrees = m_MinHalfAngleDegrees,
			MaxHalfAngleDegrees = m_MaxHalfAngleDegrees,
			Stance = GetCurrentStance(),
			IsMoving = IsMoving(),
			IsSprinting = IsSprinting(),
			StandingSpreadMultiplier = m_StandingSpreadMultiplier,
			CrouchSpreadMultiplier = m_CrouchSpreadMultiplier,
			ProneSpreadMultiplier = m_ProneSpreadMultiplier,
			MovingSpreadMultiplier = m_MovingSpreadMultiplier,
			SprintSpreadMultiplier = m_SprintSpreadMultiplier,
			PostureSpreadMultiplier = m_StanceCombatModifiers != null
				? m_StanceCombatModifiers.GetSpreadMultiplier()
				: 0f,
			AimProgress01 = m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
				? m_WeaponRuntime.TransientState.AimProgress01
				: 1f,
			SelectedAimMode = selectedAimMode,
			AimMode = selectedAimMode,
			SelectedFireMode = selectedFireMode,
			FireMode = selectedFireMode,
			BurstShotIndex = m_WeaponRuntime != null
				? m_WeaponRuntime.TransientState.GetNextBurstShotIndex()
				: 1
		};
	}

	private AmmoDefinition ResolveExpectedAmmoDefinition()
	{
		WeaponRuntimeState weaponState = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		if (weaponState == null)
			return null;
		if (weaponState.ChamberedAmmoDefinition != null)
			return weaponState.ChamberedAmmoDefinition;
		return weaponState.CurrentAmmoDefinition;
	}

	private ProceduralRecoilPatternResult ApplyProceduralRecoilPattern(
		Vector3 _baseDirection,
		WeaponShotAccuracyContext _accuracyContext)
	{
		Vector3 normalizedBase = _baseDirection.normalized;
		if (!WeaponFireModeUtility.IsAutomaticEffectiveMode(_accuracyContext.EffectiveFireMode))
			return ProceduralRecoilPatternResult.CreateUnchanged(normalizedBase);
		if (m_WeaponRuntime == null || m_WeaponRuntime.TransientState == null)
			return ProceduralRecoilPatternResult.CreateUnchanged(normalizedBase);

		float recoilPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		if (recoilPenalty <= 0.0001f)
			return ProceduralRecoilPatternResult.CreateUnchanged(normalizedBase);

		int shotIndex = Mathf.Max(1, _accuracyContext.BurstShotIndex);
		float pitchDegrees = recoilPenalty * m_RecoilPatternPitchDegreesPerPenaltyUnit;
		float yawDegrees = CalculateProceduralPatternYaw(shotIndex, pitchDegrees);

		Vector3 forward = normalizedBase;
		Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
		Vector3 right = Vector3.Cross(up, forward).normalized;
		Quaternion patternRotation = Quaternion.AngleAxis(yawDegrees, up) * Quaternion.AngleAxis(-pitchDegrees, right);
		Vector3 direction = (patternRotation * forward).normalized;
		return new ProceduralRecoilPatternResult(direction, recoilPenalty, pitchDegrees, yawDegrees, true);
	}

	private float CalculateProceduralPatternYaw(int _shotIndex, float _pitchDegrees)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float seed = weaponDefinition != null ? Mathf.Abs(weaponDefinition.GetInstanceID() % 997) * 0.01f : 0f;
		float mainWave = Mathf.Sin(_shotIndex * 1.73f + seed);
		float chaosWave = Mathf.Sin(_shotIndex * 0.47f + seed * 2.31f) * m_RecoilPatternChaosFraction;
		return (mainWave + chaosWave) * _pitchDegrees * m_RecoilPatternYawFraction;
	}

	private WeaponShotHitResult TryHit(Vector3 _origin, Vector3 _direction, AmmoDefinition _ammo)
	{
		Vector3 dir = _direction.normalized;
		float maxDist = m_MaxDistance;

		if (!Physics.Raycast(_origin, dir, out RaycastHit hit, maxDist, m_HitLayers, m_TriggerInteraction))
		{
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * maxDist, Color.yellow, m_DebugRayDuration);
			m_DebugLastHitName = "";
			m_DebugLastDamage = 0f;
			ShotTrace?.Invoke(WeaponShotTraceInfo.CreateMiss(_origin, dir, _origin + dir * maxDist, _ammo));
			return WeaponShotHitResult.Miss;
		}

		if (IsSelfCollider(hit.collider))
		{
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * hit.distance, new Color(1f, 0.5f, 0f), m_DebugRayDuration);
			ShotTrace?.Invoke(WeaponShotTraceInfo.CreateBlockedBySelf(_origin, dir, hit, _ammo));
			return WeaponShotHitResult.BlockedBySelf;
		}

		DamageableTarget target = hit.collider.GetComponentInParent<DamageableTarget>();
		bool hitVisibleTarget = IsHitOnVisibleTarget(hit.collider);
		float damage = _ammo.BaseDamage;
		if (m_UseDistanceFalloff)
			damage *= ComputeFalloffMultiplier(hit.distance, _ammo);

		if (m_DrawDebugRays)
			Debug.DrawRay(_origin, dir * hit.distance, Color.red, m_DebugRayDuration);

		m_DebugLastHitName = hit.collider.name;
		m_DebugLastDamage = damage;
		ShotTrace?.Invoke(WeaponShotTraceInfo.CreateHit(_origin, dir, hit, _ammo, damage));

		if (target != null)
			target.ApplyDamage(damage, hit.point, hit.normal, -dir, _ammo, hit.collider);

		return hitVisibleTarget ? WeaponShotHitResult.HitTarget : WeaponShotHitResult.HitOther;
	}

	private bool IsSelfCollider(Collider _collider)
	{
		if (_collider == null || m_ShooterRoot == null)
			return false;

		return _collider.transform.IsChildOf(m_ShooterRoot);
	}

	private float ComputeFalloffMultiplier(float _distance, AmmoDefinition _ammo)
	{
		WeaponDefinition wd = m_WeaponRuntime.CurrentWeaponDefinition;
		float effective = wd != null ? wd.EffectiveRangeMeters : 100f;
		WeaponRuntimeState weaponState = m_WeaponRuntime.RuntimeState;
		if (weaponState != null)
			effective *= weaponState.GetAttachmentEffectiveRangeProduct();
		float ammoEff = _ammo.EffectiveRangeMeters;
		if (ammoEff > 0.1f)
			effective = Mathf.Min(effective, ammoEff);

		if (effective <= 0.1f)
			return 1f;

		if (_distance <= effective)
			return 1f;

		float zeroAt = effective * m_FalloffZeroRangeMultiplier;
		if (_distance >= zeroAt)
			return 0f;

		return 1f - (_distance - effective) / (zeroAt - effective);
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

	private Vector3 GetGameplayShotDirection(Vector3 _origin, Transform _barrel)
	{
		Transform target = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		if (target == null)
			return _barrel.forward;

		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		Vector3 toTarget = targetPoint - _origin;
		return toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : _barrel.forward;
	}

	private LocomotionStance GetCurrentStance()
	{
		return m_Stance != null ? m_Stance.CurrentStance : LocomotionStance.Standing;
	}

	private bool IsMoving()
	{
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.HasMoveIntent;
	}

	private bool IsSprinting()
	{
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.IsSprintMoveMode;
		return m_ClickToMove != null && m_ClickToMove.IsSprintMoveMode;
	}

	private WeaponShotRecoilLogInfo BuildRecoilLogInfo(
		AmmoDefinition _ammo,
		WeaponShotAccuracyContext _accuracyContext,
		ProceduralRecoilPatternResult _patternResult)
	{
		float maxPenalty = m_RecoilController != null ? m_RecoilController.MaxRecoilPenalty : 0f;
		float penaltyBeforeShot = _patternResult.RecoilPenaltyUsed;
		bool isAtCap = maxPenalty > 0.0001f && penaltyBeforeShot >= maxPenalty - 0.0001f;
		float verticalOffsetMeters = CalculatePatternVerticalOffsetMeters(
			_patternResult.PitchDegrees,
			_accuracyContext.TargetDistanceMeters);
		float recoilAddedPerShot = m_RecoilController != null
			? m_RecoilController.ComputeRecoilAddedPerShot(_ammo)
			: 0f;
		float recoveryPerSecond = m_RecoilController != null
			? m_RecoilController.GetCurrentRecoveryPerSecond()
			: 0f;
		bool isRecoveringWhileFiring = m_RecoilController != null && m_RecoilController.IsRecoveringWhileFiring;
		float estimatedNetPerSecond = EstimateNetRecoilPenaltyPerSecond(
			_accuracyContext.EffectiveFireMode,
			recoilAddedPerShot,
			recoveryPerSecond,
			isRecoveringWhileFiring);

		return new WeaponShotRecoilLogInfo(
			penaltyBeforeShot,
			maxPenalty,
			isAtCap,
			_patternResult.PatternApplied,
			_patternResult.PitchDegrees,
			_patternResult.YawDegrees,
			verticalOffsetMeters,
			recoilAddedPerShot,
			recoveryPerSecond,
			isRecoveringWhileFiring,
			estimatedNetPerSecond,
			m_RecoilPatternPitchDegreesPerPenaltyUnit,
			m_RecoilSpreadScale,
			_accuracyContext.RecoilMultiplier);
	}

	private static float CalculatePatternVerticalOffsetMeters(float _pitchDegrees, float _targetDistanceMeters)
	{
		if (_pitchDegrees <= 0.0001f)
			return 0f;

		float distance = Mathf.Max(0f, _targetDistanceMeters);
		return distance * Mathf.Tan(_pitchDegrees * Mathf.Deg2Rad);
	}

	private float EstimateNetRecoilPenaltyPerSecond(
		WeaponFireMode _effectiveFireMode,
		float _recoilAddedPerShot,
		float _recoveryPerSecond,
		bool _isRecoveringWhileFiring)
	{
		if (!WeaponFireModeUtility.IsAutomaticEffectiveMode(_effectiveFireMode))
			return -_recoveryPerSecond;

		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float rpm = weaponDefinition != null ? Mathf.Max(1f, weaponDefinition.FireRateRpm) : 600f;
		float shotsPerSecond = rpm / 60f;
		float accumulationPerSecond = _recoilAddedPerShot * shotsPerSecond;
		if (_isRecoveringWhileFiring)
			return accumulationPerSecond - _recoveryPerSecond;

		return accumulationPerSecond;
	}

	private void LogShot(
		AmmoDefinition _ammo,
		EquippedWeapon _weapon,
		WeaponShotAccuracyContext _accuracyContext,
		WeaponShotRecoilLogInfo _recoilLogInfo,
		WeaponShotHitResult _hitResult,
		int _projectileCount)
	{
		if (_ammo == null || m_WeaponRuntime == null)
			return;

		float targetDistanceMeters = _accuracyContext.TargetDistanceMeters;
		UnitCombatStats combatStats = ResolveCombatStats();
		float conditionAimTimeMultiplier = m_CombatCondition != null
			? m_CombatCondition.GetAimTimeMultiplier(IsMoving())
			: 1f;
		float fullAimTimeSeconds = m_AimProgressController != null
			? m_AimProgressController.CurrentAimTimeSeconds
			: WeaponDistanceAimEvaluator.GetRequiredAimTimeSeconds(
				m_WeaponRuntime.CurrentWeaponDefinition,
				m_WeaponRuntime.RuntimeState != null ? m_WeaponRuntime.RuntimeState.EquippedAttachments : null,
				targetDistanceMeters) * (combatStats != null ? combatStats.GetAimTimeMultiplier() : 1f) * conditionAimTimeMultiplier;
		WeaponAttachmentDefinition[] presetAttachments = _weapon != null
			? _weapon.PresetEquippedAttachments
			: null;
		WeaponShotPostureLogInfo postureLogInfo = m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetPostureLogInfo(IsSprinting())
			: default;

		WeaponShotCombatLogger.LogShot(
			this,
			gameObject.name,
			m_Equipment != null ? m_Equipment.EquippedDefinition : null,
			m_WeaponRuntime.CurrentWeaponDefinition,
			m_WeaponRuntime.RuntimeState,
			presetAttachments,
			combatStats,
			_accuracyContext,
			postureLogInfo,
			_recoilLogInfo,
			fullAimTimeSeconds,
			m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null,
			_hitResult,
			_projectileCount);
	}

	private static WeaponShotHitResult CombineHitResults(WeaponShotHitResult _current, WeaponShotHitResult _next)
	{
		if (_next == WeaponShotHitResult.HitTarget || _current == WeaponShotHitResult.HitTarget)
			return WeaponShotHitResult.HitTarget;
		if (_next == WeaponShotHitResult.HitOther || _current == WeaponShotHitResult.HitOther)
			return WeaponShotHitResult.HitOther;
		if (_next == WeaponShotHitResult.BlockedBySelf || _current == WeaponShotHitResult.BlockedBySelf)
			return WeaponShotHitResult.BlockedBySelf;
		return WeaponShotHitResult.Miss;
	}

	private bool IsHitOnVisibleTarget(Collider _hitCollider)
	{
		Transform visibleTarget = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		if (visibleTarget == null || _hitCollider == null)
			return false;

		if (_hitCollider.TryGetComponent(out ShootingRangeTarget hitRangeTarget) &&
			visibleTarget.TryGetComponent(out ShootingRangeTarget visibleRangeTarget))
		{
			return hitRangeTarget == visibleRangeTarget;
		}

		Transform hitTransform = _hitCollider.transform;
		return hitTransform == visibleTarget || hitTransform.IsChildOf(visibleTarget);
	}

	private void StoreDebugAccuracyContext(
		WeaponShotAccuracyContext _context,
		ProceduralRecoilPatternResult _patternResult)
	{
		m_DebugLastRecoilPenalty = _patternResult.RecoilPenaltyUsed;
		m_DebugLastPatternPitchDegrees = _patternResult.PitchDegrees;
		m_DebugLastPatternVerticalOffsetMeters = CalculatePatternVerticalOffsetMeters(
			_patternResult.PitchDegrees,
			_context.TargetDistanceMeters);
		m_DebugLastHalfAngleDegrees = _context.HalfAngleDegrees;
		m_DebugLastTargetDistanceMeters = _context.TargetDistanceMeters;
		m_DebugLastRecoilMultiplier = _context.RecoilMultiplier;
		m_DebugLastStanceMultiplier = _context.StanceMultiplier;
		m_DebugLastMovementMultiplier = _context.MovementMultiplier;
		m_DebugLastSkillMultiplier = _context.SkillMultiplier;
		m_DebugLastConditionMultiplier = _context.ConditionMultiplier;
		m_DebugLastAimCompletionMultiplier = _context.AimCompletionMultiplier;
		m_DebugLastAimProgress = _context.AimProgress01;
		m_DebugLastSpreadDiameterMeters = _context.SpreadDiameterMeters;
		m_DebugAcceptableSpreadDiameterMeters = WeaponAutoModeSelectionUtility.AcceptableSpreadDiameterMeters;
	}

	private static Vector3 ApplyConeSpread(Vector3 _forward, float _halfAngleDegrees)
	{
		Vector3 f = _forward.normalized;
		if (_halfAngleDegrees <= 0.0001f)
			return f;

		float tan = Mathf.Tan(_halfAngleDegrees * Mathf.Deg2Rad);
		Vector2 rnd = Random.insideUnitCircle * tan;

		Vector3 up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, f).normalized;
		Vector3 upOrtho = Vector3.Cross(f, right).normalized;
		return (f + right * rnd.x + upOrtho * rnd.y).normalized;
	}
	#endregion

	private readonly struct ProceduralRecoilPatternResult
	{
		public readonly Vector3 Direction;
		public readonly float RecoilPenaltyUsed;
		public readonly float PitchDegrees;
		public readonly float YawDegrees;
		public readonly bool PatternApplied;

		public ProceduralRecoilPatternResult(
			Vector3 _direction,
			float _recoilPenaltyUsed,
			float _pitchDegrees,
			float _yawDegrees,
			bool _patternApplied)
		{
			Direction = _direction;
			RecoilPenaltyUsed = _recoilPenaltyUsed;
			PitchDegrees = _pitchDegrees;
			YawDegrees = _yawDegrees;
			PatternApplied = _patternApplied;
		}

		public static ProceduralRecoilPatternResult CreateUnchanged(Vector3 _direction)
		{
			return new ProceduralRecoilPatternResult(_direction, 0f, 0f, 0f, false);
		}
	}
}

/// <summary>Данные трассы одного hitscan-снаряда для визуальных эффектов.</summary>
public struct WeaponShotTraceInfo
{
	public readonly Vector3 Origin;
	public readonly Vector3 Direction;
	public readonly Vector3 EndPoint;
	public readonly Vector3 HitNormal;
	public readonly Collider HitCollider;
	public readonly AmmoDefinition Ammo;
	public readonly float Damage;
	public readonly bool HasHit;
	public readonly bool HitSelf;

	private WeaponShotTraceInfo(
		Vector3 _origin,
		Vector3 _direction,
		Vector3 _endPoint,
		Vector3 _hitNormal,
		Collider _hitCollider,
		AmmoDefinition _ammo,
		float _damage,
		bool _hasHit,
		bool _hitSelf)
	{
		Origin = _origin;
		Direction = _direction;
		EndPoint = _endPoint;
		HitNormal = _hitNormal;
		HitCollider = _hitCollider;
		Ammo = _ammo;
		Damage = _damage;
		HasHit = _hasHit;
		HitSelf = _hitSelf;
	}

	public static WeaponShotTraceInfo CreateHit(Vector3 _origin, Vector3 _direction, RaycastHit _hit, AmmoDefinition _ammo, float _damage)
	{
		return new WeaponShotTraceInfo(_origin, _direction, _hit.point, _hit.normal, _hit.collider, _ammo, _damage, true, false);
	}

	public static WeaponShotTraceInfo CreateMiss(Vector3 _origin, Vector3 _direction, Vector3 _endPoint, AmmoDefinition _ammo)
	{
		return new WeaponShotTraceInfo(_origin, _direction, _endPoint, Vector3.zero, null, _ammo, 0f, false, false);
	}

	public static WeaponShotTraceInfo CreateBlockedBySelf(Vector3 _origin, Vector3 _direction, RaycastHit _hit, AmmoDefinition _ammo)
	{
		return new WeaponShotTraceInfo(_origin, _direction, _hit.point, _hit.normal, _hit.collider, _ammo, 0f, true, true);
	}
}
