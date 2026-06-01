using UnityEngine;

/// <summary>
/// Вызывается из <see cref="UnitWeaponFireController"/> после расхода патрона; бросает hitscan из <see cref="EquippedWeapon.BarrelTransform"/> до события <c>ShotFired</c>.
/// Настрой на сцене: слой попаданий, дистанция; на целях — <see cref="DamageableTarget"/> и коллайдер.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(57)]
public sealed class UnitWeaponHitscanShooting : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
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

	[Header("Spread (множители к WeaponDefinition.BaseShotDispersion)")]
	[Tooltip("Градусы половины конуса: BaseShotDispersion, патрон, модуль прицела, отдача, умножить на этот коэффициент.")]
	[SerializeField, Min(0.001f)] private float m_BaseSpreadToDegrees = 0.35f;
	[Tooltip("Насколько полный AimProgress сужает разброс: 0 = не влияет, 1 = при полном прицеле множитель (1 - tighten).")]
	[SerializeField, Range(0f, 1f)] private float m_AimProgressTighten = 0.55f;
	[Tooltip("Вклад RecoilPenalty: множитель разброса += penalty * это значение.")]
	[SerializeField, Min(0f)] private float m_RecoilSpreadScale = 0.22f;
	[Tooltip("Минимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0f)] private float m_MinHalfAngleDegrees = 0.04f;
	[Tooltip("Максимальный полу-угол конуса в градусах.")]
	[SerializeField, Min(0.01f)] private float m_MaxHalfAngleDegrees = 12f;

	[Header("Damage falloff")]
	[Tooltip("Если включено: за эффективной дальностью урон падает (см. кривую).")]
	[SerializeField] private bool m_UseDistanceFalloff = true;
	[Tooltip("При дистанции >= эффективной × этот множитель урон обнуляется.")]
	[SerializeField, Min(1.01f)] private float m_FalloffZeroRangeMultiplier = 2f;

	[Header("Debug")]
	[SerializeField] private bool m_DrawDebugRays;
	[SerializeField] private float m_DebugRayDuration = 0.15f;
	[SerializeField] private string m_DebugLastHitName;
	[SerializeField] private float m_DebugLastDamage;
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
		float halfAngle = ComputeHalfAngleDegrees(_ammo);

		for (int i = 0; i < _ammo.ProjectileCount; i++)
		{
			Vector3 dir = ApplyConeSpread(barrel.forward, halfAngle);
			TryHit(origin, dir, _ammo);
		}
	}

	private float ComputeHalfAngleDegrees(AmmoDefinition _ammo)
	{
		WeaponDefinition wd = m_WeaponRuntime.CurrentWeaponDefinition;
		float baseDispersion = wd != null ? wd.BaseShotDispersion : 1f;
		float aim = m_WeaponRuntime.TransientState.AimProgress01;
		float recoil = m_WeaponRuntime.TransientState.RecoilPenalty;
		float targetDistanceMeters = EstimateTargetDistanceMeters();
		float weaponDistanceFactor = wd != null ? wd.GetDistanceDispersionMultiplier(targetDistanceMeters) : 1f;
		WeaponRuntimeState weaponState = m_WeaponRuntime.RuntimeState;
		float attachmentDistanceFactor = weaponState != null
			? weaponState.GetAttachmentDistanceDispersionProduct(targetDistanceMeters)
			: 1f;

		float aimFactor = 1f - aim * m_AimProgressTighten;
		float recoilFactor = 1f + recoil * m_RecoilSpreadScale;
		float raw = baseDispersion * _ammo.SpreadModifier * weaponDistanceFactor * attachmentDistanceFactor * aimFactor * recoilFactor * m_BaseSpreadToDegrees;
		return Mathf.Clamp(raw, m_MinHalfAngleDegrees, m_MaxHalfAngleDegrees);
	}

	private void TryHit(Vector3 _origin, Vector3 _direction, AmmoDefinition _ammo)
	{
		Vector3 dir = _direction.normalized;
		float maxDist = m_MaxDistance;

		if (!Physics.Raycast(_origin, dir, out RaycastHit hit, maxDist, m_HitLayers, m_TriggerInteraction))
		{
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * maxDist, Color.yellow, m_DebugRayDuration);
			m_DebugLastHitName = "";
			m_DebugLastDamage = 0f;
			return;
		}

		if (IsSelfCollider(hit.collider))
		{
			if (m_DrawDebugRays)
				Debug.DrawRay(_origin, dir * hit.distance, new Color(1f, 0.5f, 0f), m_DebugRayDuration);
			return;
		}

		DamageableTarget target = hit.collider.GetComponentInParent<DamageableTarget>();
		float damage = _ammo.BaseDamage;
		if (m_UseDistanceFalloff)
			damage *= ComputeFalloffMultiplier(hit.distance, _ammo);

		if (m_DrawDebugRays)
			Debug.DrawRay(_origin, dir * hit.distance, Color.red, m_DebugRayDuration);

		m_DebugLastHitName = hit.collider.name;
		m_DebugLastDamage = damage;

		if (target != null)
			target.ApplyDamage(damage, hit.point, hit.normal, -dir, _ammo);
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
