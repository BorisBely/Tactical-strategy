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
	[SerializeField] private UnitWeaponAimProgressController m_AimProgressController;

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
		if (m_AimProgressController == null)
			m_AimProgressController = GetComponent<UnitWeaponAimProgressController>();

		m_ShooterRoot = transform.root;
	}
	#endregion

	#region Public Methods
	/// <summary>Вызывается из <see cref="UnitWeaponFireController"/> до события ShotFired, чтобы разброс не включал отдачу только что сделанного выстрела.</summary>
	public void ProcessSuccessfulShot(AmmoDefinition _ammo)
	{
		HandleShotFired(_ammo);
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
		float halfAngle = accuracyContext.HalfAngleDegrees;
		StoreDebugAccuracyContext(accuracyContext);

		WeaponShotHitResult aggregateHitResult = WeaponShotHitResult.Miss;
		int projectileCount = Mathf.Max(1, _ammo.ProjectileCount);
		for (int i = 0; i < projectileCount; i++)
		{
			Vector3 dir = ApplyConeSpread(baseDirection, halfAngle);
			WeaponShotHitResult shotResult = TryHit(origin, dir, _ammo);
			aggregateHitResult = CombineHitResults(aggregateHitResult, shotResult);
		}

		if (m_LogShots)
			LogShot(_ammo, weapon, accuracyContext, aggregateHitResult, projectileCount);
	}

	private UnitCombatStats ResolveCombatStats()
	{
		return UnitCombatStatsLookup.ResolveOnUnit(this);
	}

	private WeaponShotAccuracyContext BuildAccuracyContext(AmmoDefinition _ammo)
	{
		UnitCombatStats combatStats = ResolveCombatStats();
		WeaponShotAccuracyInput input = new WeaponShotAccuracyInput
		{
			WeaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition,
			WeaponState = m_WeaponRuntime.RuntimeState,
			TransientState = m_WeaponRuntime.TransientState,
			AmmoDefinition = _ammo,
			CombatStats = combatStats,
			CombatCondition = m_CombatCondition,
			TargetDistanceMeters = EstimateTargetDistanceMeters(),
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
			FireMode = m_WeaponRuntime != null && m_WeaponRuntime.RuntimeState != null
				? m_WeaponRuntime.RuntimeState.SelectedFireMode
				: WeaponFireMode.SemiAuto,
			BurstShotIndex = m_WeaponRuntime != null
				? m_WeaponRuntime.TransientState.GetNextBurstShotIndex()
				: 1
		};

		return WeaponShotAccuracyEvaluator.Evaluate(input);
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
		Transform target = m_Vision != null ? m_Vision.VisibleTarget : null;
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
		Transform target = m_Vision != null ? m_Vision.VisibleTarget : null;
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

	private void LogShot(
		AmmoDefinition _ammo,
		EquippedWeapon _weapon,
		WeaponShotAccuracyContext _accuracyContext,
		WeaponShotHitResult _hitResult,
		int _projectileCount)
	{
		if (_ammo == null || m_WeaponRuntime == null)
			return;

		float targetDistanceMeters = _accuracyContext.TargetDistanceMeters;
		float weaponAimTimeSeconds = WeaponDistanceAimEvaluator.GetRequiredAimTimeSeconds(
			m_WeaponRuntime.CurrentWeaponDefinition,
			m_WeaponRuntime.RuntimeState != null ? m_WeaponRuntime.RuntimeState.EquippedAttachments : null,
			targetDistanceMeters);
		UnitCombatStats combatStats = ResolveCombatStats();
		float unitAimTimeMultiplier = combatStats != null ? combatStats.GetAimTimeMultiplier() : 1f;
		float conditionAimTimeMultiplier = m_CombatCondition != null
			? m_CombatCondition.GetAimTimeMultiplier(IsMoving())
			: 1f;
		float overallAimTimeSeconds = m_AimProgressController != null
			? m_AimProgressController.CurrentAimTimeSeconds
			: weaponAimTimeSeconds * unitAimTimeMultiplier * conditionAimTimeMultiplier;
		WeaponAttachmentDefinition[] presetAttachments = _weapon != null
			? _weapon.PresetEquippedAttachments
			: null;

		WeaponShotCombatLogger.LogShot(
			this,
			gameObject.name,
			m_Equipment != null ? m_Equipment.EquippedDefinition : null,
			m_WeaponRuntime.CurrentWeaponDefinition,
			m_WeaponRuntime.RuntimeState,
			presetAttachments,
			combatStats,
			_accuracyContext,
			weaponAimTimeSeconds,
			overallAimTimeSeconds,
			m_Vision != null ? m_Vision.VisibleTarget : null,
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
		Transform visibleTarget = m_Vision != null ? m_Vision.VisibleTarget : null;
		if (visibleTarget == null || _hitCollider == null)
			return false;

		Transform hitTransform = _hitCollider.transform;
		return hitTransform == visibleTarget || hitTransform.IsChildOf(visibleTarget);
	}

	private void StoreDebugAccuracyContext(WeaponShotAccuracyContext _context)
	{
		m_DebugLastHalfAngleDegrees = _context.HalfAngleDegrees;
		m_DebugLastTargetDistanceMeters = _context.TargetDistanceMeters;
		m_DebugLastRecoilMultiplier = _context.RecoilMultiplier;
		m_DebugLastStanceMultiplier = _context.StanceMultiplier;
		m_DebugLastMovementMultiplier = _context.MovementMultiplier;
		m_DebugLastSkillMultiplier = _context.SkillMultiplier;
		m_DebugLastConditionMultiplier = _context.ConditionMultiplier;
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
