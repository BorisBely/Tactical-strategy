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
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;
	[SerializeField] private UnitWeaponAimProgressController m_AimProgressController;
	[SerializeField] private UnitWeaponFireDisciplineController m_FireDisciplineController;
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitVision m_Vision;

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
	[Tooltip("Градусы половины конуса: BaseShotDispersion, патрон, модуль прицела, поза, умножить на этот коэффициент.")]
	[SerializeField, Min(0.001f)] private float m_BaseSpreadToDegrees = 0.35f;
	[Tooltip("Минимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0f)] private float m_MinHalfAngleDegrees = 0.04f;
	[Tooltip("Максимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0.01f)] private float m_MaxHalfAngleDegrees = 12f;

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
	[SerializeField] private Vector2 m_DebugLastRecoilOffset;
	[SerializeField, Min(0f)] private float m_DebugLastRecoilOffsetMeters;
	#endregion

	#region Private Fields
	private const int c_HitBufferSize = 32;
	private Transform m_ShooterRoot;
	private UnitTeam m_Team;
	private RaycastHit[] m_Hits;
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
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
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
		if (m_FireDisciplineController == null)
			m_FireDisciplineController = GetComponent<UnitWeaponFireDisciplineController>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();

		m_ShooterRoot = transform;
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		m_Hits = new RaycastHit[c_HitBufferSize];
	}
	#endregion

	#region Public Methods
	/// <summary>Вызывается из <see cref="UnitWeaponFireController"/> до события ShotFired, чтобы разброс не включал отдачу только что сделанного выстрела.</summary>
	public void ProcessSuccessfulShot(AmmoDefinition _ammo)
	{
		HandleShotFired(_ammo);
	}

	public float GetCappedMaxDistance()
	{
		float weapon = Mathf.Max(0.5f, m_MaxDistance);
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_Vision == null)
			return weapon;
		return Mathf.Min(weapon, Mathf.Max(0.5f, m_Vision.ResolvedMaxRange));
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

	private void LogActionShot(
		Vector3 _origin,
		Vector3 _dir,
		AmmoDefinition _ammo,
		in WeaponShotOutcome _outcome,
		int _pelletIndex,
		int _pelletCount,
		in WeaponShotAccuracyContext _accuracy)
	{
		if (!UnitActionLog.Enabled)
			return;

		string tgt = m_TargetSelector != null && m_TargetSelector.SelectedTarget != null
			? UnitActionLog.Slot(m_TargetSelector.SelectedTarget)
			: "none";
		Transform hitRoot = _outcome.TargetHealth != null ? _outcome.TargetHealth.transform : null;
		string hitSlot = hitRoot != null
			? UnitActionLog.Slot(hitRoot)
			: (_outcome.Result == WeaponShotHitResult.Miss ? "miss" : (_outcome.HitRootName ?? _outcome.Result.ToString()));

		string pose = m_ReadyHands != null ? m_ReadyHands.EffectivePoseState.ToString() : "?";
		float aim = m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
			? m_WeaponRuntime.TransientState.AimProgress01
			: 0f;
		string weapon = m_WeaponRuntime != null && m_WeaponRuntime.CurrentWeaponDefinition != null
			? m_WeaponRuntime.CurrentWeaponDefinition.name
			: "?";
		Vector3 aimPt = m_TargetSelector != null && m_TargetSelector.HasSelectedAimPoint
			? m_TargetSelector.SelectedAimPointWorld
			: _origin + _dir * 10f;
		string payload =
			"tgt=" + tgt +
			" hit=" + hitSlot +
			" result=" + _outcome.Result +
			" zone=" + _outcome.BodyZone +
			" part=" + _outcome.BodyPart +
			" dist=" + UnitActionLog.F1(_outcome.HitDistanceMeters > 0f ? _outcome.HitDistanceMeters : _accuracy.TargetDistanceMeters) +
			" dmg=" + UnitActionLog.F1(_outcome.Damage) +
			" pose=" + pose +
			" aimProg=" + UnitActionLog.F2(aim) +
			" weapon=" + weapon +
			" aimPt=" + UnitActionLog.Vec(aimPt) +
			" pellet=" + (_pelletIndex + 1) + "/" + _pelletCount;
		if (_ammo != null)
			payload += " ammo=" + _ammo.name;
		UnitActionLog.Write(this, UnitActionLog.Shot, payload);
		UnitActionLog.Timeline(UnitActionLog.Shot, "actor=" + UnitActionLog.Slot(this) + " " + payload);
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
		Vector2 recoilOffset = m_RecoilController != null
			? m_RecoilController.RecoilOffset
			: (m_WeaponRuntime.TransientState != null ? m_WeaponRuntime.TransientState.RecoilOffset : Vector2.zero);
		Vector3 shotDirection = WeaponRecoilMath.ApplyOffsetToDirection(baseDirection, recoilOffset);
		float halfAngle = accuracyContext.HalfAngleDegrees;
		StoreDebugAccuracyContext(accuracyContext, recoilOffset);

		m_ShotgunPelletBudgets.Clear();

		int projectileCount = Mathf.Max(1, _ammo.ProjectileCount);
		// Pellets scatter around the same RecoilOffset direction; they do not get a second P-cone.
		if (_ammo.UsesShotgunPelletPattern)
		{
			float shotgunHalfAngle = halfAngle * _ammo.GetShotgunSpreadDistanceScale(accuracyContext.TargetDistanceMeters);
			float patternYawDegrees = Random.Range(0f, 360f);
			for (int i = 0; i < projectileCount; i++)
			{
				Vector3 dir = ApplyShotgunPelletOffset(
					shotDirection,
					shotgunHalfAngle,
					i,
					projectileCount,
					_ammo.ShotgunInnerRingRadius01,
					_ammo.ShotgunOuterRingRadius01,
					patternYawDegrees);
				WeaponShotOutcome outcome = TryHit(origin, dir, _ammo);
				LogActionShot(origin, dir, _ammo, outcome, i, projectileCount, accuracyContext);
			}
		}
		else
		{
			for (int i = 0; i < projectileCount; i++)
			{
				Vector3 dir = ApplyConeSpread(shotDirection, halfAngle);
				WeaponShotOutcome outcome = TryHit(origin, dir, _ammo);
				LogActionShot(origin, dir, _ammo, outcome, i, projectileCount, accuracyContext);
			}
		}

		m_ShotgunPelletBudgets.Clear();
		PublishGunshotCombatEvent(origin);
	}

	private void PublishGunshotCombatEvent(Vector3 _origin)
	{
		Transform aimed = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
		CombatEventHub.Publish(CombatEvent.Gunshot(this, this, aimed, _origin));
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
		UnitCombatStats combatStats = ResolveCombatStats();
		bool turret = IsOperatingVehicleTurret();
		WeaponPoseState pose = m_ReadyHands != null
			? m_ReadyHands.EffectivePoseState
			: WeaponPoseState.Aiming;
		_input = new WeaponAutoModeSelectionInput
		{
			AccuracyInput = accuracyInput,
			SelectedFireMode = accuracyInput.SelectedFireMode,
			SelectedAimMode = ResolveSelectedAimMode(targetDistanceMeters),
			AvailableFireModes = m_WeaponRuntime.CurrentWeaponDefinition.AvailableFireModes,
			TargetDistanceMeters = targetDistanceMeters,
			StanceKickMultiplier = turret || m_StanceCombatModifiers == null
				? 1f
				: m_StanceCombatModifiers.GetRecoilAddedMultiplier(),
			StanceRecoveryMultiplier = turret || m_StanceCombatModifiers == null
				? 1f
				: m_StanceCombatModifiers.GetRecoilRecoveryMultiplier(),
			PoseKickMultiplier = turret ? 1f : WeaponPoseCombatModifiers.GetKickMultiplier(pose),
			PoseRecoveryMultiplier = turret ? 1f : WeaponPoseCombatModifiers.GetRecoveryMultiplier(pose),
			SkillKickMultiplier = turret || combatStats == null ? 1f : combatStats.GetRecoilAddedMultiplier(),
			SkillRecoveryMultiplier = turret || combatStats == null ? 1f : combatStats.GetRecoilRecoveryMultiplier()
		};
		return true;
	}

	private WeaponAimMode ResolveSelectedAimMode(float _targetDistanceMeters)
	{
		if (m_FireDisciplineController != null &&
		    m_FireDisciplineController.TryGetAimGateOverride(out _, out WeaponAimMode plannedAimMode))
			return plannedAimMode;

		if (m_WeaponRuntime == null)
			return WeaponAimMode.FullAim;

		WeaponFireDisciplineMode discipline = m_WeaponRuntime.SelectedFireDisciplineMode;
		if (discipline == WeaponFireDisciplineMode.Auto)
			return WeaponAimMode.Auto;

		return WeaponFireDisciplineModeUtility.MapToAimMode(
			discipline,
			_targetDistanceMeters,
			m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null);
	}

	private WeaponShotAccuracyInput BuildAccuracyInput(AmmoDefinition _ammo, float _targetDistanceMeters)
	{
		UnitCombatStats combatStats = ResolveCombatStats();
		UnitIndividualTraits individualTraits = ResolveIndividualTraits();
		WeaponFireMode selectedFireMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;
		WeaponAimMode selectedAimMode = ResolveSelectedAimMode(_targetDistanceMeters);

		WeaponFireMode effectiveFireMode = selectedFireMode;
		WeaponAimMode effectiveAimMode = selectedAimMode;
		if (m_FireDisciplineController != null &&
		    m_FireDisciplineController.TryGetEffectiveFireModeOverride(out WeaponFireMode plannedFireMode))
			effectiveFireMode = plannedFireMode;
		if (m_FireDisciplineController != null &&
		    m_FireDisciplineController.TryGetAimGateOverride(out _, out WeaponAimMode plannedAimMode))
			effectiveAimMode = plannedAimMode;

		WeaponPoseState weaponPose = m_ReadyHands != null
			? m_ReadyHands.EffectivePoseState
			: WeaponPoseState.Aiming;
		bool excludeOptics = weaponPose.IsHipFireHold()
		                     || weaponPose == WeaponPoseState.PointAim
		                     || weaponPose == WeaponPoseState.PreAim;
		float poseSpread = 1f;
		if (m_ReadyHands != null && m_ReadyHands.PoseCapabilityCache.IsValid)
			poseSpread = m_ReadyHands.PoseCapabilityCache.GetSpreadMult(weaponPose);
		else if (weaponPose.IsHipFireHold())
			poseSpread = WeaponPoseAutoCapabilityBaker.DefaultHipFireSpreadMult;
		else if (weaponPose == WeaponPoseState.PointAim)
			poseSpread = WeaponPoseAutoCapabilityBaker.DefaultPointAimSpreadMult;
		else if (weaponPose == WeaponPoseState.PreAim)
			poseSpread = PreAimPoseUtility.SpreadMult;

		poseSpread *= WeaponPoseDistanceCurves.GetAccuracyMultiplier(weaponPose, _targetDistanceMeters);
		if (weaponPose == WeaponPoseState.PointAim)
		{
			WeaponAttachmentDefinition[] attachments = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
				? m_WeaponRuntime.RuntimeState.EquippedAttachments
				: null;
			poseSpread *= WeaponLaserModifiers.GetPointAimSpreadProduct(attachments, _targetDistanceMeters);
		}

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
			MinHalfAngleDegrees = m_MinHalfAngleDegrees,
			MaxHalfAngleDegrees = m_MaxHalfAngleDegrees,
			Stance = GetCurrentStance(),
			IsMoving = IsMoving(),
			IsSprinting = !IsOperatingVehicleTurret() && IsSprinting(),
			StandingSpreadMultiplier = m_StandingSpreadMultiplier,
			CrouchSpreadMultiplier = m_CrouchSpreadMultiplier,
			ProneSpreadMultiplier = m_ProneSpreadMultiplier,
			MovingSpreadMultiplier = m_MovingSpreadMultiplier,
			SprintSpreadMultiplier = m_SprintSpreadMultiplier,
			PostureSpreadMultiplier = ResolvePostureSpreadMultiplier(),
			AimProgress01 = m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
				? m_WeaponRuntime.TransientState.AimProgress01
				: 1f,
			SelectedAimMode = selectedAimMode,
			AimMode = effectiveAimMode,
			SelectedFireMode = selectedFireMode,
			FireMode = effectiveFireMode,
			BurstShotIndex = m_WeaponRuntime != null
				? m_WeaponRuntime.TransientState.GetNextBurstShotIndex()
				: 1,
			WeaponPose = weaponPose,
			PoseSpreadMultiplier = poseSpread,
			ExcludeOpticAttachments = excludeOptics
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

	private WeaponShotOutcome TryHit(Vector3 _origin, Vector3 _direction, AmmoDefinition _ammo)
	{
		using (InfantryProfilerMarkers.Hitscan.Auto())
		{
			return TryHitUnguarded(_origin, _direction, _ammo);
		}
	}

	private WeaponShotOutcome TryHitUnguarded(Vector3 _origin, Vector3 _direction, AmmoDefinition _ammo)
	{
		Vector3 dir = _direction.normalized;
		float maxDist = GetCappedMaxDistance();

		if (m_Hits == null || m_Hits.Length == 0)
			m_Hits = new RaycastHit[c_HitBufferSize];

		int hitCount = Physics.RaycastNonAlloc(_origin, dir, m_Hits, maxDist, m_HitLayers, m_TriggerInteraction);
		while (hitCount == m_Hits.Length)
		{
			System.Array.Resize(ref m_Hits, m_Hits.Length * 2);
			hitCount = Physics.RaycastNonAlloc(_origin, dir, m_Hits, maxDist, m_HitLayers, m_TriggerInteraction);
		}

		if (hitCount == 0)
		{
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * maxDist, Color.yellow, m_DebugRayDuration);
			m_DebugLastHitName = "";
			m_DebugLastDamage = 0f;
			RaiseShotTrace(WeaponShotTraceInfo.CreateMiss(_origin, dir, _origin + dir * maxDist, _ammo));
			return WeaponShotOutcome.Miss();
		}

		SortRaycastHitsByDistance(m_Hits, hitCount);

		RaycastHit? firstSelfHit = null;
		WeaponShotOutcome lastOutcome = default;
		bool hadProcessedHit = false;
		m_ProcessedBodyPartHits.Clear();

		for (int i = 0; i < hitCount; i++)
		{
			RaycastHit hit = m_Hits[i];
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

	private static void SortRaycastHitsByDistance(RaycastHit[] _hits, int _count)
	{
		if (_hits == null || _count <= 1)
			return;

		for (int i = 1; i < _count; i++)
		{
			RaycastHit key = _hits[i];
			int j = i - 1;
			while (j >= 0 && _hits[j].distance > key.distance)
			{
				_hits[j + 1] = _hits[j];
				j--;
			}

			_hits[j + 1] = key;
		}
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

			CombatEventHub.Publish(CombatEvent.Hit(this, this, target, _hit.point));
		}
		else
		{
			CombatEventHub.Publish(CombatEvent.Impact(this, this, _hit.collider, _hit.point));
		}

		BodyPartType bodyPart = bodyPartPreview;
		if (target != null && IsFriendlyOrNeutral(target))
			bodyPart = BodyPartType.Chest;

		WeaponShotImpactVfxKind impactVfxKind = ResolveImpactVfxKind(target, hitZone, armorFullyBlocked);
		float traceDamage = damageApplied ? damage : 0f;
		bool hasImpactAudio = WeaponVfxUtility.WillPlaySurfaceImpactAudio(
			m_WeaponRuntime,
			_hit.collider,
			impactVfxKind);
		RaiseShotTrace(WeaponShotTraceInfo.CreateHit(
			_origin,
			_dir,
			_hit,
			_ammo,
			traceDamage,
			impactVfxKind,
			hasImpactAudio));

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
		float weaponRange = wd != null ? wd.EffectiveRangeMeters : 100f;
		float attachmentProduct = 1f;
		WeaponRuntimeState weaponState = m_WeaponRuntime.RuntimeState;
		if (weaponState != null)
			attachmentProduct = weaponState.GetAttachmentEffectiveRangeProduct();

		float ammoRange = _ammo != null ? _ammo.EffectiveRangeMeters : 0f;
		float effective = WeaponDamageRangeMath.ResolveEffectiveRangeMeters(
			weaponRange,
			attachmentProduct,
			ammoRange);
		return WeaponDamageRangeMath.ComputeFalloffMultiplier(
			_distance,
			effective,
			m_FalloffZeroRangeMultiplier);
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		if (target == null)
			return 0f;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : transform;
		Vector3 targetPoint = m_TargetSelector.GetEngageableAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;
		return Vector3.Distance(fireOrigin.position, targetPoint);
	}

	private Vector3 GetGameplayShotDirection(Vector3 _origin, Transform _fireOrigin, AmmoDefinition _ammo)
	{
		Transform target = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		if (target == null)
			return _fireOrigin != null ? _fireOrigin.forward : Vector3.forward;

		Vector3 targetPoint = m_TargetSelector.GetEngageableAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		// Упреждение по скорости цели
		if (m_TargetLeadFactor > 0.0001f && m_TargetSelector != null)
		{
			Vector3 targetVelocity = m_TargetSelector.SelectedTargetVelocity;
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
		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.HasMoveIntent;
		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.HasMoveIntent;
	}

	private bool IsSprinting()
	{
		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.IsSprintMoveMode;
		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.IsSprintMoveMode;
	}

	private bool IsOperatingVehicleTurret()
	{
		return m_Equipment != null && m_Equipment.IsOperatingVehicleTurret;
	}

	private float ResolvePostureSpreadMultiplier()
	{
		if (IsOperatingVehicleTurret())
			return 1f;

		return m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetSpreadMultiplier()
			: 0f;
	}

	private bool IsHitOnVisibleTarget(Collider _hitCollider)
	{
		Transform visibleTarget = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
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

	private void StoreDebugAccuracyContext(WeaponShotAccuracyContext _context, Vector2 _recoilOffset)
	{
		m_DebugLastRecoilOffset = _recoilOffset;
		m_DebugLastRecoilOffsetMeters = _context.TargetDistanceMeters *
		                                Mathf.Tan(_recoilOffset.magnitude * Mathf.Deg2Rad);
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
				return ((m_Target != null ? m_Target.GetEntityId().GetHashCode() : 0) * 397) ^ (int)m_BodyPart;
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
	public readonly bool HasImpactAudio;

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
		WeaponShotImpactVfxKind _impactVfxKind,
		bool _hasImpactAudio)
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
		HasImpactAudio = _hasImpactAudio;
	}

	public static WeaponShotTraceInfo CreateHit(
		Vector3 _origin,
		Vector3 _direction,
		RaycastHit _hit,
		AmmoDefinition _ammo,
		float _damage,
		WeaponShotImpactVfxKind _impactVfxKind = WeaponShotImpactVfxKind.Environment,
		bool _hasImpactAudio = false)
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
			_impactVfxKind,
			_hasImpactAudio);
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
			WeaponShotImpactVfxKind.None,
			false);
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
			WeaponShotImpactVfxKind.None,
			false);
	}
}
