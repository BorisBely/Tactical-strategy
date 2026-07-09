using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вызывается из <see cref="UnitWeaponFireController"/> после расхода патрона; бросает hitscan из <see cref="EquippedWeapon.FireOriginTransform"/> до события <c>ShotFired</c>.
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
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
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

	[Header("Target Leading")]
	[Tooltip("Доля упреждения по скорости цели (0 = без упреждения, 1 = полное физическое).")]
	[SerializeField, Range(0f, 1.5f)] private float m_TargetLeadFactor = 1f;

	[Header("Spread (множители к WeaponDefinition.BaseShotDispersion)")]
	[Tooltip("Градусы половины конуса: BaseShotDispersion, патрон, модуль прицела, отдача, умножить на этот коэффициент.")]
	[SerializeField, Min(0.001f)] private float m_BaseSpreadToDegrees = 0.35f;
	[Tooltip("Вклад RecoilPenalty: множитель разброса += penalty * это значение.")]
	[SerializeField, Min(0f)] private float m_RecoilSpreadScale = 0.22f;
	[Tooltip("Минимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0f)] private float m_MinHalfAngleDegrees = 0.04f;
	[Tooltip("Максимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0.01f)] private float m_MaxHalfAngleDegrees = 12f;

	[Header("Auto Fire")]
	[Tooltip("Множитель конуса разброса в FullAuto/Burst. 0.87 ≈ +15% точности в авто.")]
	[SerializeField, Range(0.5f, 1f)] private float m_AutoSpreadMultiplier = 0.869565f;

	[Header("Procedural Recoil Pattern")]
	[Tooltip("Геймплейный подъём паттерна на единицу накопленной отдачи. Случайный конус остаётся мелкой неточностью поверх этого смещения.")]
	[SerializeField, Min(0f)] private float m_RecoilPatternPitchDegreesPerPenaltyUnit = 0.09f;
	[Tooltip("Боковой увод паттерна как доля от вертикального подъёма.")]
	[SerializeField, Range(0f, 1f)] private float m_RecoilPatternYawFraction = 0.55f;
	[Tooltip("Небольшая процедурная неровность бокового увода, чтобы длинная очередь не была идеально синусоидальной.")]
	[SerializeField, Range(0f, 1f)] private float m_RecoilPatternChaosFraction = 0.22f;

	[Header("Full Auto Recoil Control")]
	[Tooltip("С какого выстрела очереди юнит начинает компенсировать отдачу (первые выстрелы — как раньше, в основном вверх).")]
	[SerializeField, Min(1)] private int m_FullAutoRecoilControlStartShot = 5;
	[Tooltip("К какому номеру выстрела компенсация выходит на полную силу.")]
	[SerializeField, Min(1)] private int m_FullAutoRecoilControlEndShot = 10;
	[Tooltip("Оставшаяся доля вертикального подъёма при полной компенсации.")]
	[SerializeField, Range(0.1f, 1f)] private float m_FullAutoControlledPitchScale = 0.38f;
	[Tooltip("Боковой увод при полной компенсации считается от полной отдачи (не от ослабленного pitch), чтобы траектория уходила в стороны сильнее, чем вверх.")]
	[SerializeField, Range(0.5f, 1.5f)] private float m_FullAutoControlledYawReferenceScale = 1f;
	[Tooltip("Множитель бокового увода при полной компенсации.")]
	[SerializeField, Min(0.5f)] private float m_FullAutoControlledYawBoost = 1.2f;
	[Tooltip("Доля бокового увода от вертикальной отдачи при полной компенсации (> Recoil Pattern Yaw Fraction — доминирует горизонталь).")]
	[SerializeField, Range(0f, 2f)] private float m_FullAutoControlledYawFraction = 1.05f;
	[Tooltip("Насколько RecoilControl юнита усиливает компенсацию (0 = одинаково для всех).")]
	[SerializeField, Range(0f, 1f)] private float m_FullAutoRecoilControlSkillInfluence = 0.65f;

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
	[SerializeField, Range(0f, 1f)] private float m_DebugLastFullAutoRecoilControlBlend;
	#endregion

	#region Private Fields
	private Transform m_ShooterRoot;
	private UnitTeam m_Team;
	private readonly HashSet<ProcessedBodyPartHit> m_ProcessedBodyPartHits = new HashSet<ProcessedBodyPartHit>();
	private readonly Dictionary<DamageableTarget, ShotgunTargetPelletBudget> m_ShotgunPelletBudgets =
		new Dictionary<DamageableTarget, ShotgunTargetPelletBudget>();
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
		m_IndividualTraits = ResolveIndividualTraits();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
		if (m_StanceCombatModifiers == null)
			m_StanceCombatModifiers = GetComponent<UnitStanceCombatModifiers>();
		if (m_AimProgressController == null)
			m_AimProgressController = GetComponent<UnitWeaponAimProgressController>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();

		m_ShooterRoot = transform;
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
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
	private void RaiseShotTrace(WeaponShotTraceInfo _trace)
	{
		ShotTrace?.Invoke(_trace);
		WeaponShotTraceBroadcast.Publish(_trace);
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (_ammo == null || m_Equipment == null || m_WeaponRuntime == null)
			return;

		EquippedWeapon weapon = m_Equipment.EquippedWeapon;
		if (weapon == null)
			return;

		Transform fireOrigin = weapon.FireOriginTransform;
		Vector3 origin = fireOrigin.position + fireOrigin.forward * m_BarrelRayStartOffset;
		Vector3 baseDirection = GetGameplayShotDirection(origin, fireOrigin, _ammo);
		WeaponShotAccuracyContext accuracyContext = BuildAccuracyContext(_ammo);
		ProceduralRecoilPatternResult patternResult = ApplyProceduralRecoilPattern(baseDirection, accuracyContext);
		Vector3 patternedDirection = patternResult.Direction;
		float halfAngle = accuracyContext.HalfAngleDegrees;
		StoreDebugAccuracyContext(accuracyContext, patternResult);

		m_ShotgunPelletBudgets.Clear();

		int projectileCount = Mathf.Max(1, _ammo.ProjectileCount);
		if (_ammo.UsesShotgunPelletPattern)
		{
			float shotgunHalfAngle = halfAngle * _ammo.GetShotgunSpreadDistanceScale(accuracyContext.TargetDistanceMeters);
			float patternYawDegrees = Random.Range(0f, 360f);
			for (int i = 0; i < projectileCount; i++)
			{
				Vector3 dir = ApplyShotgunPelletOffset(
					patternedDirection,
					shotgunHalfAngle,
					i,
					projectileCount,
					_ammo.ShotgunInnerRingRadius01,
					_ammo.ShotgunOuterRingRadius01,
					patternYawDegrees);
				TryHit(origin, dir, _ammo);
			}
		}
		else
		{
			for (int i = 0; i < projectileCount; i++)
			{
				Vector3 dir = ApplyConeSpread(patternedDirection, halfAngle);
				TryHit(origin, dir, _ammo);
			}
		}

		m_ShotgunPelletBudgets.Clear();
	}

	private UnitCombatStats ResolveCombatStats()
	{
		return UnitCombatStatsLookup.ResolveOnUnit(this);
	}

	private UnitIndividualTraits ResolveIndividualTraits()
	{
		if (m_IndividualTraits != null)
			return m_IndividualTraits;

		return GetComponent<UnitIndividualTraits>();
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
		UnitIndividualTraits individualTraits = ResolveIndividualTraits();
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
			IndividualTraits = individualTraits,
			CombatCondition = m_CombatCondition,
			TargetDistanceMeters = _targetDistanceMeters,
			BaseSpreadToDegrees = m_BaseSpreadToDegrees,
			AutoSpreadMultiplier = m_AutoSpreadMultiplier,
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
		if (WeaponFireModeUtility.IsFirstShotInAutomaticSeries(
			    _accuracyContext.EffectiveFireMode,
			    _accuracyContext.BurstShotIndex))
			return ProceduralRecoilPatternResult.CreateUnchanged(normalizedBase);
		if (m_WeaponRuntime == null || m_WeaponRuntime.TransientState == null)
			return ProceduralRecoilPatternResult.CreateUnchanged(normalizedBase);

		float recoilPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		if (recoilPenalty <= 0.0001f)
			return ProceduralRecoilPatternResult.CreateUnchanged(normalizedBase);

		int shotIndex = Mathf.Max(1, _accuracyContext.BurstShotIndex);
		float basePitchDegrees = recoilPenalty * m_RecoilPatternPitchDegreesPerPenaltyUnit * m_AutoSpreadMultiplier;
		float controlBlend = CalculateFullAutoRecoilControlBlend(_accuracyContext.EffectiveFireMode, shotIndex);
		float pitchScale = Mathf.Lerp(1f, m_FullAutoControlledPitchScale, controlBlend);
		float pitchDegrees = basePitchDegrees * pitchScale;

		float yawReferencePitch = basePitchDegrees * Mathf.Lerp(1f, m_FullAutoControlledYawReferenceScale, controlBlend);
		float yawFraction = Mathf.Lerp(m_RecoilPatternYawFraction, m_FullAutoControlledYawFraction, controlBlend);
		float yawBoost = Mathf.Lerp(1f, m_FullAutoControlledYawBoost, controlBlend);
		float yawDegrees = CalculateProceduralPatternYaw(shotIndex, yawReferencePitch, yawFraction) * yawBoost;
		m_DebugLastFullAutoRecoilControlBlend = controlBlend;

		Vector3 forward = normalizedBase;
		Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
		Vector3 right = Vector3.Cross(up, forward).normalized;
		Quaternion patternRotation = Quaternion.AngleAxis(yawDegrees, up) * Quaternion.AngleAxis(-pitchDegrees, right);
		Vector3 direction = (patternRotation * forward).normalized;
		return new ProceduralRecoilPatternResult(direction, recoilPenalty, pitchDegrees, yawDegrees, true);
	}

	private float CalculateProceduralPatternYaw(int _shotIndex, float _pitchDegrees, float _yawFraction)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		float seed = weaponDefinition != null ? Mathf.Abs(weaponDefinition.GetInstanceID() % 997) * 0.01f : 0f;
		float mainWave = Mathf.Sin(_shotIndex * 1.73f + seed);
		float chaosWave = Mathf.Sin(_shotIndex * 0.47f + seed * 2.31f) * m_RecoilPatternChaosFraction;
		return (mainWave + chaosWave) * _pitchDegrees * _yawFraction;
	}

	private float CalculateFullAutoRecoilControlBlend(WeaponFireMode _effectiveFireMode, int _shotIndex)
	{
		if (_effectiveFireMode != WeaponFireMode.FullAuto)
			return 0f;

		int startShot = Mathf.Max(1, m_FullAutoRecoilControlStartShot);
		int endShot = Mathf.Max(startShot + 1, m_FullAutoRecoilControlEndShot);
		if (_shotIndex <= startShot)
			return 0f;

		float shotBlend = Mathf.InverseLerp(startShot, endShot, _shotIndex);
		if (shotBlend <= 0f)
			return 0f;

		float skill01 = ResolveRecoilControlSkill01();
		float skillInfluence = Mathf.Clamp01(m_FullAutoRecoilControlSkillInfluence);
		return Mathf.Clamp01(Mathf.Lerp(shotBlend, shotBlend * skill01, skillInfluence));
	}

	private float ResolveRecoilControlSkill01()
	{
		UnitCombatStats combatStats = ResolveCombatStats();
		if (combatStats == null)
			return 0.5f;

		return Mathf.InverseLerp(0f, 100f, combatStats.RecoilControl);
	}

	private WeaponShotOutcome TryHit(Vector3 _origin, Vector3 _direction, AmmoDefinition _ammo)
	{
		Vector3 dir = _direction.normalized;
		float maxDist = m_MaxDistance;

		RaycastHit[] hits = Physics.RaycastAll(_origin, dir, maxDist, m_HitLayers, m_TriggerInteraction);
		if (hits.Length == 0)
		{
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * maxDist, Color.yellow, m_DebugRayDuration);
			m_DebugLastHitName = "";
			m_DebugLastDamage = 0f;
			RaiseShotTrace(WeaponShotTraceInfo.CreateMiss(_origin, dir, _origin + dir * maxDist, _ammo));
			return WeaponShotOutcome.Miss();
		}

		System.Array.Sort(hits, static (a, b) => a.distance.CompareTo(b.distance));

		RaycastHit? firstSelfHit = null;
		WeaponShotOutcome lastOutcome = default;
		bool hadProcessedHit = false;
		m_ProcessedBodyPartHits.Clear();

		for (int i = 0; i < hits.Length; i++)
		{
			RaycastHit hit = hits[i];
			if (IsSelfCollider(hit.collider))
			{
				firstSelfHit ??= hit;
				continue;
			}

			if (ShouldSkipDuplicateBodyPartHit(hit.collider))
				continue;

			WeaponShotOutcome outcome = ProcessRaycastHit(_origin, dir, hit, _ammo);
			lastOutcome = outcome;
			hadProcessedHit = true;
			RegisterProcessedBodyPartHit(hit.collider, outcome);

			if (!BodyPartTypeUtility.IsLimb(outcome.BodyPart))
				return outcome;
		}

		if (hadProcessedHit)
			return lastOutcome;

		if (firstSelfHit.HasValue)
		{
			RaycastHit selfHit = firstSelfHit.Value;
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * selfHit.distance, new Color(1f, 0.5f, 0f), m_DebugRayDuration);
			RaiseShotTrace(WeaponShotTraceInfo.CreateBlockedBySelf(_origin, dir, selfHit, _ammo));
			return WeaponShotOutcome.BlockedBySelf(selfHit.collider.name, selfHit.distance);
		}

		if (m_DrawDebugRays)
			Debug.DrawRay(_origin, dir * maxDist, Color.yellow, m_DebugRayDuration);
		m_DebugLastHitName = "";
		m_DebugLastDamage = 0f;
		RaiseShotTrace(WeaponShotTraceInfo.CreateMiss(_origin, dir, _origin + dir * maxDist, _ammo));
		return WeaponShotOutcome.Miss();
	}

	private WeaponShotOutcome ProcessRaycastHit(Vector3 _origin, Vector3 _dir, RaycastHit _hit, AmmoDefinition _ammo)
	{
		DamageableTarget target = _hit.collider.GetComponentInParent<DamageableTarget>();
		bool hitVisibleTarget = IsHitOnVisibleTarget(_hit.collider);
		UnitBodyHitZone hitZone = _hit.collider.GetComponent<UnitBodyHitZone>() ??
		                          _hit.collider.GetComponentInParent<UnitBodyHitZone>();
		BodyPartType bodyPartPreview = hitZone != null ? hitZone.BodyPart : BodyPartType.Unknown;

		float damage = _ammo.BaseDamage;
		if (_ammo.TryGetShotgunPelletDamageFalloff(_hit.distance, out float shotgunFalloff))
			damage *= shotgunFalloff;
		else if (m_UseDistanceFalloff)
			damage *= ComputeFalloffMultiplier(_hit.distance, _ammo);

		if (m_DrawDebugRays)
			Debug.DrawRay(_origin, _dir * _hit.distance, Color.red, m_DebugRayDuration);

		m_DebugLastHitName = _hit.collider.name;
		m_DebugLastDamage = damage;

		InjuryUiEntry resolvedInjury = default;
		bool hasResolvedInjury = false;
		bool damageApplied = target == null;
		bool armorFullyBlocked = false;
		UnitHealth targetHealth = null;
		if (target != null)
		{
			if (damage <= 0f || !TryConsumeShotgunPelletBudget(target, bodyPartPreview, _ammo))
			{
				damageApplied = false;
				damage = 0f;
			}
			else
			{
				damageApplied = target.ApplyDamage(
					damage,
					_hit.point,
					_hit.normal,
					-_dir,
					_ammo,
					_hit.collider,
					out resolvedInjury,
					out armorFullyBlocked);
				target.TryGetComponent(out targetHealth);
				hasResolvedInjury = targetHealth != null &&
				                    (!string.IsNullOrWhiteSpace(resolvedInjury.StatusLocalizationKey) ||
				                     !string.IsNullOrWhiteSpace(resolvedInjury.StatusDisplayName));
			}
		}

		BodyPartType bodyPart = bodyPartPreview;
		if (target != null && IsFriendlyOrNeutral(target))
			bodyPart = BodyPartType.Chest;

		WeaponShotImpactVfxKind impactVfxKind = ResolveImpactVfxKind(target, hitZone, armorFullyBlocked);
		float traceDamage = damageApplied ? damage : 0f;
		RaiseShotTrace(WeaponShotTraceInfo.CreateHit(_origin, _dir, _hit, _ammo, traceDamage, impactVfxKind));

		if (_hit.collider != null &&
		    _hit.collider.GetComponentInParent<ShootingRangeTarget>() is ShootingRangeTarget rangeTarget &&
		    rangeTarget.IsUserEnabled)
		{
			ShootingRangeHitLogger.LogHit(
				this,
				m_ShooterRoot,
				m_Equipment,
				m_WeaponRuntime,
				this,
				_ammo,
				rangeTarget,
				_hit,
				_hit.distance);
			rangeTarget.TryRegisterHit();
		}

		return new WeaponShotOutcome
		{
			Result = hitVisibleTarget ? WeaponShotHitResult.HitTarget : WeaponShotHitResult.HitOther,
			HitDistanceMeters = _hit.distance,
			Damage = damageApplied ? damage : 0f,
			HitColliderName = _hit.collider.name,
			HitRootName = _hit.collider.transform.root.name,
			BodyPart = bodyPart,
			BodyZone = hitZone != null ? hitZone.Zone : CombatBodyZone.Unknown,
			HasDamageableTarget = target != null,
			HasUnitHealth = targetHealth != null,
			ResolvedInjury = resolvedInjury,
			HasResolvedInjury = hasResolvedInjury,
			TargetHealth = targetHealth
		};
	}

	private static WeaponShotImpactVfxKind ResolveImpactVfxKind(
		DamageableTarget _target,
		UnitBodyHitZone _hitZone,
		bool _armorFullyBlocked)
	{
		if (_target == null || _hitZone == null)
			return WeaponShotImpactVfxKind.Environment;

		return _armorFullyBlocked
			? WeaponShotImpactVfxKind.ArmorDeflect
			: WeaponShotImpactVfxKind.Flesh;
	}

	private bool IsSelfCollider(Collider _collider)
	{
		if (_collider == null || m_ShooterRoot == null)
			return false;

		Transform hitTransform = _collider.transform;
		return hitTransform == m_ShooterRoot || hitTransform.IsChildOf(m_ShooterRoot);
	}

	private bool IsFriendlyOrNeutral(DamageableTarget _target)
	{
		if (_target == null || m_Team == null)
			return false;

		UnitTeam targetTeam = _target.GetComponent<UnitTeam>() ??
		                      _target.GetComponentInParent<UnitTeam>();

		if (targetTeam == null)
			return false;

		return targetTeam.Team == m_Team.Team || targetTeam.Team == UnitTeamId.Neutral;
	}

	private bool ShouldSkipDuplicateBodyPartHit(Collider _collider)
	{
		if (!TryResolveProcessedBodyPartHit(_collider, out ProcessedBodyPartHit processedHit))
			return false;

		return m_ProcessedBodyPartHits.Contains(processedHit);
	}

	private void RegisterProcessedBodyPartHit(Collider _collider, WeaponShotOutcome _outcome)
	{
		if (_outcome.BodyPart == BodyPartType.Unknown)
			return;

		DamageableTarget target = _collider != null ? _collider.GetComponentInParent<DamageableTarget>() : null;
		if (target == null)
			return;

		m_ProcessedBodyPartHits.Add(new ProcessedBodyPartHit(target, _outcome.BodyPart));
	}

	private static bool TryResolveProcessedBodyPartHit(Collider _collider, out ProcessedBodyPartHit _hit)
	{
		_hit = default;
		if (_collider == null)
			return false;

		DamageableTarget target = _collider.GetComponentInParent<DamageableTarget>();
		if (target == null)
			return false;

		UnitBodyHitZone hitZone = _collider.GetComponent<UnitBodyHitZone>() ??
		                          _collider.GetComponentInParent<UnitBodyHitZone>();
		if (hitZone == null || hitZone.BodyPart == BodyPartType.Unknown)
			return false;

		_hit = new ProcessedBodyPartHit(target, hitZone.BodyPart);
		return true;
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
		Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : transform;
		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;
		return Vector3.Distance(fireOrigin.position, targetPoint);
	}

	private Vector3 GetGameplayShotDirection(Vector3 _origin, Transform _fireOrigin, AmmoDefinition _ammo)
	{
		Transform target = m_Vision != null ? m_Vision.GetEngageableVisibleTarget() : null;
		if (target == null)
			return _fireOrigin != null ? _fireOrigin.forward : Vector3.forward;

		Vector3 targetPoint = m_Vision.GetVisibleTargetAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		// Упреждение по скорости цели
		if (m_TargetLeadFactor > 0.0001f && m_Vision != null)
		{
			Vector3 targetVelocity = m_Vision.GetVisibleTargetVelocity();
			if (targetVelocity.sqrMagnitude > 0.0001f)
			{
				float distance = Vector3.Distance(_origin, targetPoint);
				float ammoVelocity = _ammo != null ? _ammo.Velocity : 400f;
				if (ammoVelocity > 0.1f)
				{
					float timeOfFlight = distance / ammoVelocity;
					Vector3 leadOffset = targetVelocity * (timeOfFlight * m_TargetLeadFactor);
					targetPoint += leadOffset;
				}
			}
		}

		Vector3 toTarget = targetPoint - _origin;
		return toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : (_fireOrigin != null ? _fireOrigin.forward : Vector3.forward);
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

	private static float CalculatePatternVerticalOffsetMeters(float _pitchDegrees, float _targetDistanceMeters)
	{
		if (_pitchDegrees <= 0.0001f)
			return 0f;

		float distance = Mathf.Max(0f, _targetDistanceMeters);
		return distance * Mathf.Tan(_pitchDegrees * Mathf.Deg2Rad);
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

	private const float c_ShotgunCenterJitterRadius01 = 0.12f;
	private const float c_ShotgunInnerRadiusJitter = 0.18f;
	private const float c_ShotgunOuterRadiusJitter = 0.22f;
	private const float c_ShotgunInnerAngleJitterDegrees = 28f;
	private const float c_ShotgunOuterAngleJitterDegrees = 22f;

	/// <summary>
	/// Паттерн дроби: 1 центр + внутреннее кольцо + внешнее кольцо (для 9: 1+4+4).
	/// Базовые слоты сохраняются, но каждая дробинка получает джиттер радиуса/угла — не идеальный круг.
	/// </summary>
	private static Vector3 ApplyShotgunPelletOffset(
		Vector3 _forward,
		float _halfAngleDegrees,
		int _pelletIndex,
		int _pelletCount,
		float _innerRingRadius01,
		float _outerRingRadius01,
		float _patternYawDegrees)
	{
		Vector3 f = _forward.normalized;
		if (_pelletCount <= 1 || _halfAngleDegrees <= 0.0001f)
			return f;

		GetShotgunPelletRingOffset(
			_pelletIndex,
			_pelletCount,
			_innerRingRadius01,
			_outerRingRadius01,
			out float radius01,
			out float angleDegrees);

		float rotatedAngle = (angleDegrees + _patternYawDegrees) * Mathf.Deg2Rad;
		float tan = Mathf.Tan(_halfAngleDegrees * Mathf.Deg2Rad) * Mathf.Clamp(radius01, 0f, 1.5f);
		float offsetX = Mathf.Cos(rotatedAngle) * tan;
		float offsetY = Mathf.Sin(rotatedAngle) * tan;

		Vector3 up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, f).normalized;
		Vector3 upOrtho = Vector3.Cross(f, right).normalized;
		return (f + right * offsetX + upOrtho * offsetY).normalized;
	}

	private static void GetShotgunPelletRingOffset(
		int _pelletIndex,
		int _pelletCount,
		float _innerRingRadius01,
		float _outerRingRadius01,
		out float _radius01,
		out float _angleDegrees)
	{
		_radius01 = 0f;
		_angleDegrees = 0f;

		if (_pelletIndex <= 0)
		{
			// Центр тоже чуть «гуляет», иначе всегда идеальная точка.
			float centerAngle = Random.Range(0f, 360f);
			_radius01 = Random.Range(0f, c_ShotgunCenterJitterRadius01);
			_angleDegrees = centerAngle;
			return;
		}

		int remaining = Mathf.Max(0, _pelletCount - 1);
		int innerCount = Mathf.Max(1, remaining / 2);
		int outerCount = Mathf.Max(1, remaining - innerCount);

		if (_pelletIndex <= innerCount)
		{
			float baseAngle = 360f * (_pelletIndex - 1) / innerCount;
			float baseRadius = Mathf.Clamp01(_innerRingRadius01);
			_radius01 = Mathf.Clamp(
				baseRadius + Random.Range(-c_ShotgunInnerRadiusJitter, c_ShotgunInnerRadiusJitter),
				0.08f,
				1.2f);
			_angleDegrees = baseAngle + Random.Range(-c_ShotgunInnerAngleJitterDegrees, c_ShotgunInnerAngleJitterDegrees);
			return;
		}

		int outerIndex = _pelletIndex - 1 - innerCount;
		float outerBaseAngle = 360f * outerIndex / outerCount + (180f / Mathf.Max(1, outerCount));
		float outerBaseRadius = Mathf.Max(0f, _outerRingRadius01);
		_radius01 = Mathf.Clamp(
			outerBaseRadius + Random.Range(-c_ShotgunOuterRadiusJitter, c_ShotgunOuterRadiusJitter),
			0.35f,
			1.5f);
		_angleDegrees = outerBaseAngle + Random.Range(-c_ShotgunOuterAngleJitterDegrees, c_ShotgunOuterAngleJitterDegrees);
	}

	private bool TryConsumeShotgunPelletBudget(DamageableTarget _target, BodyPartType _bodyPart, AmmoDefinition _ammo)
	{
		if (_target == null || _ammo == null || !_ammo.UsesShotgunPelletPattern)
			return true;

		int softCap = Mathf.Max(0, _ammo.MaxPelletsPerTarget);
		int hardCap = Mathf.Max(softCap, _ammo.MaxPelletsPerTargetWithHead);
		if (hardCap <= 0)
			return true;

		if (!m_ShotgunPelletBudgets.TryGetValue(_target, out ShotgunTargetPelletBudget budget))
			budget = new ShotgunTargetPelletBudget();

		bool isHeadOrNeck = _bodyPart == BodyPartType.Head || _bodyPart == BodyPartType.Neck;
		if (isHeadOrNeck)
			budget.HasHeadOrNeckHit = true;

		int allowed = budget.HasHeadOrNeckHit ? hardCap : softCap;
		if (budget.AppliedCount >= allowed)
		{
			m_ShotgunPelletBudgets[_target] = budget;
			return false;
		}

		budget.AppliedCount++;
		m_ShotgunPelletBudgets[_target] = budget;
		return true;
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

	private readonly struct ProcessedBodyPartHit : System.IEquatable<ProcessedBodyPartHit>
	{
		private readonly DamageableTarget m_Target;
		private readonly BodyPartType m_BodyPart;

		public ProcessedBodyPartHit(DamageableTarget _target, BodyPartType _bodyPart)
		{
			m_Target = _target;
			m_BodyPart = _bodyPart;
		}

		public bool Equals(ProcessedBodyPartHit _other)
		{
			return ReferenceEquals(m_Target, _other.m_Target) && m_BodyPart == _other.m_BodyPart;
		}

		public override bool Equals(object _obj)
		{
			return _obj is ProcessedBodyPartHit other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return ((m_Target != null ? m_Target.GetInstanceID() : 0) * 397) ^ (int)m_BodyPart;
			}
		}
	}

	private struct ShotgunTargetPelletBudget
	{
		public int AppliedCount;
		public bool HasHeadOrNeckHit;
	}
}

/// <summary>Тип FX в точке попадания hitscan-выстрела.</summary>
public enum WeaponShotImpactVfxKind
{
	None = 0,
	Environment = 1,
	ArmorDeflect = 2,
	Flesh = 3
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
	public readonly WeaponShotImpactVfxKind ImpactVfxKind;

	private WeaponShotTraceInfo(
		Vector3 _origin,
		Vector3 _direction,
		Vector3 _endPoint,
		Vector3 _hitNormal,
		Collider _hitCollider,
		AmmoDefinition _ammo,
		float _damage,
		bool _hasHit,
		bool _hitSelf,
		WeaponShotImpactVfxKind _impactVfxKind)
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
		ImpactVfxKind = _impactVfxKind;
	}

	public static WeaponShotTraceInfo CreateHit(
		Vector3 _origin,
		Vector3 _direction,
		RaycastHit _hit,
		AmmoDefinition _ammo,
		float _damage,
		WeaponShotImpactVfxKind _impactVfxKind = WeaponShotImpactVfxKind.Environment)
	{
		return new WeaponShotTraceInfo(
			_origin,
			_direction,
			_hit.point,
			_hit.normal,
			_hit.collider,
			_ammo,
			_damage,
			true,
			false,
			_impactVfxKind);
	}

	public static WeaponShotTraceInfo CreateMiss(Vector3 _origin, Vector3 _direction, Vector3 _endPoint, AmmoDefinition _ammo)
	{
		return new WeaponShotTraceInfo(
			_origin,
			_direction,
			_endPoint,
			Vector3.zero,
			null,
			_ammo,
			0f,
			false,
			false,
			WeaponShotImpactVfxKind.None);
	}

	public static WeaponShotTraceInfo CreateBlockedBySelf(Vector3 _origin, Vector3 _direction, RaycastHit _hit, AmmoDefinition _ammo)
	{
		return new WeaponShotTraceInfo(
			_origin,
			_direction,
			_hit.point,
			_hit.normal,
			_hit.collider,
			_ammo,
			0f,
			true,
			true,
			WeaponShotImpactVfxKind.None);
	}
}
