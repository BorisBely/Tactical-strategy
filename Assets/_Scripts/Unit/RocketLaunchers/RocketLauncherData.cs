using UnityEngine;

/// <summary>
/// Общие настройки приказа гранатомёта: время прицеливания и точность по дистанции, скорость ракеты, VFX.
/// Кривые aim/dispersion — та же модель, что <see cref="WeaponDistanceAimProfile"/>.
/// </summary>
[CreateAssetMenu(fileName = "RocketLauncherData", menuName = "Polygone/Combat/Rocket Launcher Data", order = 1)]
public sealed class RocketLauncherData : ScriptableObject
{
	#region Constants
	private const float c_MaxBalanceDistanceMeters = 500f;
	private const float c_MinAimSecondsAtMaxDistance = 3f;
	#endregion

	#region Serialized Fields
	[Header("Aim — RPG-7")]
	[Tooltip("Базовое полное время прицеливания RPG-7 (сек) при множителе кривой = 1.")]
	[SerializeField, Min(0.05f)] private float m_RpgAimTimeSeconds = 2.3f;
	[SerializeField] private WeaponDistanceAimProfile m_RpgDistanceAimProfile = new WeaponDistanceAimProfile();

	[Header("Aim — Disposable")]
	[Tooltip("Базовое полное время прицеливания одноразового гранатомёта (сек) при множителе кривой = 1.")]
	[SerializeField, Min(0.05f)] private float m_DisposableAimTimeSeconds = 2f;
	[SerializeField] private WeaponDistanceAimProfile m_DisposableDistanceAimProfile = new WeaponDistanceAimProfile();

	[Header("Accuracy — cone half-angle (degrees)")]
	[Tooltip("Базовый полу-угол отклонения RPG-7 при множителе кривой = 1 (градусы).")]
	[SerializeField, Min(0.01f)] private float m_RpgBaseDispersionDegrees = 0.95f;
	[Tooltip("Базовый полу-угол отклонения одноразового при множителе кривой = 1 (градусы).")]
	[SerializeField, Min(0.01f)] private float m_DisposableBaseDispersionDegrees = 1.2f;
	[SerializeField, Min(0f)] private float m_MinHalfAngleDegrees = 0.08f;
	[SerializeField, Min(0.01f)] private float m_MaxHalfAngleDegrees = 8f;

	[Header("Aim — Animator")]
	[Tooltip("Длительность crossfade в aim-состояние (визуал; на готовность к огню не влияет).")]
	[SerializeField, Min(0.05f)] private float m_AimCrossfadeSeconds = 0.2f;

	[Header("Ballistics")]
	[Tooltip("Начальная скорость RPG-7 (м/с). PG-7V ≈ 115–120 м/с.")]
	[SerializeField, Min(1f)] private float m_RpgMuzzleSpeed = 115f;
	[Tooltip("Начальная скорость одноразового (м/с). M72-класс ≈ 130–145 м/с.")]
	[SerializeField, Min(1f)] private float m_DisposableMuzzleSpeed = 130f;
	[Tooltip("Ускорение свободного падения для ракет (м/с²).")]
	[SerializeField, Min(0f)] private float m_ProjectileGravity = 9.81f;
	[Tooltip("Линейное демпфирование Rigidbody (слабое — чуть гасит скорость).")]
	[SerializeField, Min(0f)] private float m_ProjectileLinearDamping = 0.02f;
	[Tooltip("Автоуничтожение снаряда, если не уничтожен иначе.")]
	[SerializeField, Min(1f)] private float m_ProjectileLifetimeSeconds = 12f;

	[Header("Aim Gizmo")]
	[SerializeField] private bool m_DrawAimTrajectoryGizmo = true;
	[SerializeField, Min(8)] private int m_AimGizmoPointCount = 48;
	[SerializeField, Min(0.02f)] private float m_AimGizmoStepSeconds = 0.08f;

	[Header("Explosion VFX")]
	[Tooltip("Тот же FX, что у осколочных гранат, но с увеличенным масштабом.")]
	[SerializeField] private GameObject m_ExplosionPrefab;
	[Tooltip("Масштаб VFX. У гранат frag ~1.25; для ракеты заметно мощнее.")]
	[SerializeField, Min(0.01f)] private float m_ExplosionVfxScale = 2.15f;
	[SerializeField, Min(0.05f)] private float m_ExplosionVfxDurationSeconds = 5.5f;
	[SerializeField, Min(0f)] private float m_ExplosionMaxDistanceMeters = 600f;
	[SerializeField, Range(-180f, 180f)] private float m_ExplosionVfxYawOffsetDegrees;

	[Header("Fire Muzzle / Backblast VFX")]
	[Tooltip("Тот же FX дульной вспышки, что у винтовок (FX_MuzzleFlash_Smoke).")]
	[SerializeField] private bool m_EnableFireMuzzleVfx = true;
	[SerializeField] private GameObject m_FireMuzzleFlashPrefab;
	[Tooltip("Дуло: больше и сильнее вперёд (Z).")]
	[SerializeField] private Vector3 m_FireMuzzleVfxScale = new Vector3(5.5f, 5.5f, 10f);
	[Tooltip("Задний бластик: больше в радиусе.")]
	[SerializeField] private Vector3 m_FireBackblastVfxScale = new Vector3(7.5f, 7.5f, 7f);
	[SerializeField, Min(0.05f)] private float m_FireMuzzleVfxLifetimeSeconds = 0.4f;
	[SerializeField, Min(0f)] private float m_FireMuzzleVfxMaxDistanceMeters = 70f;

	[Header("Disposable Discard")]
	[SerializeField, Min(1f)] private float m_DiscardedLauncherLifetimeSeconds = 30f;
	[Tooltip("Локальный импульс выброса: +X вправо, +Y вверх, +Z вперёд (~2–3 м).")]
	[SerializeField] private Vector3 m_DiscardImpulseLocal = new Vector3(2.6f, 2.0f, 3.2f);
	[SerializeField, Min(0f)] private float m_DiscardTorque = 3.5f;

	[Header("Audio — Fire")]
	[Tooltip("FLYBY_Missile_02_Slow — общий whoosh при выстреле RPG и одноразового.")]
	[SerializeField] private WeaponRandomAudioClipSet m_FireWhooshClips = new WeaponRandomAudioClipSet();
	[Tooltip("Pistol Shot_14 — доп. акцент одноразового (одновременно с whoosh).")]
	[SerializeField] private WeaponRandomAudioClipSet m_DisposableFireAccentClips = new WeaponRandomAudioClipSet();
	[Tooltip("Pistol Shot_15 — доп. акцент RPG (одновременно с whoosh).")]
	[SerializeField] private WeaponRandomAudioClipSet m_RpgFireAccentClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_FireWhooshVolume = 1f;
	[SerializeField, Range(0f, 1f)] private float m_FireAccentVolume = 0.95f;
	[SerializeField, Min(5f)] private float m_FireAudioMaxDistance = 120f;

	[Header("Audio — Flyby")]
	[Tooltip("FLYBY_Missile_01_Fast — пролёт мимо камеры.")]
	[SerializeField] private WeaponRandomAudioClipSet m_FlybyClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_FlybyVolume = 1f;
	[SerializeField, Min(0.5f)] private float m_FlybyRadiusMeters = 10f;
	[SerializeField, Min(0.1f)] private float m_FlybyMinSpawnDistanceMeters = 2.5f;

	[Header("Audio — Explosion")]
	[Tooltip("Small Explosion 01–03 — одноразовый гранатомёт.")]
	[SerializeField] private WeaponRandomAudioClipSet m_DisposableExplosionClips = new WeaponRandomAudioClipSet();
	[Tooltip("Small Explosion 04–05 — RPG-7.")]
	[SerializeField] private WeaponRandomAudioClipSet m_RpgExplosionClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_ExplosionAudioVolume = 1f;
	[SerializeField, Min(5f)] private float m_ExplosionAudioMaxDistance = 110f;

	[Header("Audio — RPG Reload")]
	[Tooltip("Gun Reload 9_3 — вставка ракеты в слот во время перезарядки RPG-7.")]
	[SerializeField] private WeaponRandomAudioClipSet m_RpgReloadInsertClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_RpgReloadInsertVolume = 1f;
	[SerializeField, Min(5f)] private float m_RpgReloadInsertMaxDistance = 45f;

	[Header("Fallback Prefabs (optional overrides)")]
	[SerializeField] private GameObject m_FallbackRpgHandPrefab;
	[SerializeField] private GameObject m_FallbackRpgProjectilePrefab;
	[SerializeField] private GameObject m_FallbackRpgRocketHandPrefab;
	[SerializeField] private GameObject m_FallbackDisposableHandPrefab;
	[SerializeField] private GameObject m_FallbackDisposableProjectilePrefab;
	#endregion

	#region Public Properties
	public float RpgAimTimeSeconds => m_RpgAimTimeSeconds;
	public float DisposableAimTimeSeconds => m_DisposableAimTimeSeconds;
	public WeaponDistanceAimProfile RpgDistanceAimProfile => m_RpgDistanceAimProfile;
	public WeaponDistanceAimProfile DisposableDistanceAimProfile => m_DisposableDistanceAimProfile;
	public float RpgBaseDispersionDegrees => m_RpgBaseDispersionDegrees;
	public float DisposableBaseDispersionDegrees => m_DisposableBaseDispersionDegrees;
	public float MinHalfAngleDegrees => m_MinHalfAngleDegrees;
	public float MaxHalfAngleDegrees => m_MaxHalfAngleDegrees;
	public float AimCrossfadeSeconds => m_AimCrossfadeSeconds;
	public float RpgMuzzleSpeed => m_RpgMuzzleSpeed;
	public float DisposableMuzzleSpeed => m_DisposableMuzzleSpeed;
	public float ProjectileGravity => m_ProjectileGravity;
	public float ProjectileLinearDamping => m_ProjectileLinearDamping;
	public float ProjectileLifetimeSeconds => m_ProjectileLifetimeSeconds;
	public bool DrawAimTrajectoryGizmo => m_DrawAimTrajectoryGizmo;
	public int AimGizmoPointCount => m_AimGizmoPointCount;
	public float AimGizmoStepSeconds => m_AimGizmoStepSeconds;

	/// <summary>Совместимость: скорость RPG.</summary>
	public float ProjectileSpeed => m_RpgMuzzleSpeed;
	public GameObject ExplosionPrefab => m_ExplosionPrefab;
	public float ExplosionVfxScale => m_ExplosionVfxScale;
	public float ExplosionVfxDurationSeconds => m_ExplosionVfxDurationSeconds;
	public float ExplosionMaxDistanceMeters => m_ExplosionMaxDistanceMeters;
	public float ExplosionVfxYawOffsetDegrees => m_ExplosionVfxYawOffsetDegrees;
	public bool EnableFireMuzzleVfx => m_EnableFireMuzzleVfx;
	public GameObject FireMuzzleFlashPrefab => m_FireMuzzleFlashPrefab;
	public Vector3 FireMuzzleVfxScale => m_FireMuzzleVfxScale;
	public Vector3 FireBackblastVfxScale => m_FireBackblastVfxScale;
	public float FireMuzzleVfxLifetimeSeconds => m_FireMuzzleVfxLifetimeSeconds;
	public float FireMuzzleVfxMaxDistanceMeters => m_FireMuzzleVfxMaxDistanceMeters;
	public float DiscardedLauncherLifetimeSeconds => m_DiscardedLauncherLifetimeSeconds;
	public Vector3 DiscardImpulseLocal => m_DiscardImpulseLocal;
	public float DiscardTorque => m_DiscardTorque;
	public float FireWhooshVolume => m_FireWhooshVolume;
	public float FireAccentVolume => m_FireAccentVolume;
	public float FireAudioMaxDistance => m_FireAudioMaxDistance;
	public float FlybyVolume => m_FlybyVolume;
	public float FlybyRadiusMeters => m_FlybyRadiusMeters;
	public float FlybyMinSpawnDistanceMeters => m_FlybyMinSpawnDistanceMeters;
	public float ExplosionAudioVolume => m_ExplosionAudioVolume;
	public float ExplosionAudioMaxDistance => m_ExplosionAudioMaxDistance;
	public float RpgReloadInsertVolume => m_RpgReloadInsertVolume;
	public float RpgReloadInsertMaxDistance => m_RpgReloadInsertMaxDistance;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		ApplyDefaultAimBalance();
	}

	private void OnValidate()
	{
		if (m_RpgDistanceAimProfile == null)
			m_RpgDistanceAimProfile = new WeaponDistanceAimProfile();
		if (m_DisposableDistanceAimProfile == null)
			m_DisposableDistanceAimProfile = new WeaponDistanceAimProfile();
	}
	#endregion

	#region Public Methods
	public float GetBaseAimTimeSeconds(RocketLauncherType _type)
	{
		return _type == RocketLauncherType.Disposable
			? m_DisposableAimTimeSeconds
			: m_RpgAimTimeSeconds;
	}

	public float GetBaseDispersionDegrees(RocketLauncherType _type)
	{
		return _type == RocketLauncherType.Disposable
			? m_DisposableBaseDispersionDegrees
			: m_RpgBaseDispersionDegrees;
	}

	public float GetMuzzleSpeed(RocketLauncherType _type)
	{
		return _type == RocketLauncherType.Disposable
			? m_DisposableMuzzleSpeed
			: m_RpgMuzzleSpeed;
	}

	public WeaponDistanceAimProfile GetDistanceAimProfile(RocketLauncherType _type)
	{
		return _type == RocketLauncherType.Disposable
			? m_DisposableDistanceAimProfile
			: m_RpgDistanceAimProfile;
	}

	public float GetDistanceAimTimeMultiplier(RocketLauncherType _type, float _distanceMeters)
	{
		WeaponDistanceAimProfile profile = GetDistanceAimProfile(_type);
		return profile != null ? profile.GetAimTimeMultiplier(_distanceMeters) : 1f;
	}

	public float GetDistanceDispersionMultiplier(RocketLauncherType _type, float _distanceMeters)
	{
		WeaponDistanceAimProfile profile = GetDistanceAimProfile(_type);
		return profile != null ? profile.GetDispersionMultiplier(_distanceMeters) : 1f;
	}

	/// <summary>
	/// Полное время прицеливания: база типа × дистанционная кривая.
	/// </summary>
	public float GetRequiredAimTimeSeconds(RocketLauncherType _type, float _distanceMeters)
	{
		float aimTimeSeconds = GetBaseAimTimeSeconds(_type);
		aimTimeSeconds *= GetDistanceAimTimeMultiplier(_type, _distanceMeters);
		return Mathf.Max(0.05f, aimTimeSeconds);
	}

	/// <summary>
	/// Нижняя граница времени прицеливания по дистанции: на 500 м не меньше 3 с (после ранга тоже).
	/// </summary>
	public float GetMinimumAimTimeSeconds(float _distanceMeters)
	{
		float t = Mathf.Clamp01(_distanceMeters / c_MaxBalanceDistanceMeters);
		return Mathf.Lerp(0f, c_MinAimSecondsAtMaxDistance, t);
	}

	/// <summary>
	/// Полу-угол конуса отклонения: база × кривая дистанции × внешние множители (ранг и т.п.).
	/// </summary>
	public float GetHalfAngleDegrees(
		RocketLauncherType _type,
		float _distanceMeters,
		float _externalDispersionMultiplier)
	{
		float halfAngle = GetBaseDispersionDegrees(_type);
		halfAngle *= GetDistanceDispersionMultiplier(_type, _distanceMeters);
		halfAngle *= Mathf.Max(0.01f, _externalDispersionMultiplier);
		return Mathf.Clamp(halfAngle, m_MinHalfAngleDegrees, m_MaxHalfAngleDegrees);
	}

	/// <summary>
	/// Реалистичный баланс RPG / disposable: базы 2.3 / 2.0 с, кривые aim и accuracy 0..500 м.
	/// </summary>
	public void ApplyDefaultAimBalance()
	{
		m_RpgAimTimeSeconds = 2.3f;
		m_DisposableAimTimeSeconds = 2f;
		m_RpgBaseDispersionDegrees = 0.95f;
		m_DisposableBaseDispersionDegrees = 1.2f;
		m_RpgMuzzleSpeed = 115f;
		m_DisposableMuzzleSpeed = 130f;
		m_ProjectileGravity = 9.81f;
		m_ProjectileLinearDamping = 0.02f;
		m_ProjectileLifetimeSeconds = 12f;

		if (m_RpgDistanceAimProfile == null)
			m_RpgDistanceAimProfile = new WeaponDistanceAimProfile();
		if (m_DisposableDistanceAimProfile == null)
			m_DisposableDistanceAimProfile = new WeaponDistanceAimProfile();

		// RPG-7: оптика PGO — точнее disposable; aim на 500 м ≈ 4.03 с (пол после ранга ≥ 3 с).
		m_RpgDistanceAimProfile.SetCurves(
			BuildRoleCurve(0.85f, 1.00f, 1.40f, 1.95f, 2.70f),
			BuildRoleCurve(0.75f, 1.00f, 1.25f, 1.50f, 1.75f));

		// Disposable: проще прицел — шире конус и чуть быстрее aim вблизи.
		m_DisposableDistanceAimProfile.SetCurves(
			BuildRoleCurve(1.00f, 1.20f, 1.70f, 2.40f, 3.30f),
			BuildRoleCurve(0.70f, 1.00f, 1.25f, 1.50f, 1.75f));
	}

	public GameObject ResolveHandPrefab(ItemDefinition _launcher)
	{
		if (_launcher != null && _launcher.RocketLauncherHandPrefab != null)
			return _launcher.RocketLauncherHandPrefab;

		if (_launcher == null)
			return null;

		return _launcher.RocketLauncherType switch
		{
			RocketLauncherType.Rpg7 => m_FallbackRpgHandPrefab,
			RocketLauncherType.Disposable => m_FallbackDisposableHandPrefab,
			_ => null
		};
	}

	public GameObject ResolveProjectilePrefab(ItemDefinition _launcher)
	{
		if (_launcher != null && _launcher.RocketProjectilePrefab != null)
			return _launcher.RocketProjectilePrefab;

		if (_launcher == null)
			return null;

		return _launcher.RocketLauncherType switch
		{
			RocketLauncherType.Rpg7 => m_FallbackRpgProjectilePrefab,
			RocketLauncherType.Disposable => m_FallbackDisposableProjectilePrefab,
			_ => null
		};
	}

	public GameObject ResolveRpgRocketHandPrefab(ItemDefinition _launcher)
	{
		if (_launcher != null && _launcher.RpgRocketHandPrefab != null)
			return _launcher.RpgRocketHandPrefab;

		return m_FallbackRpgRocketHandPrefab;
	}

	public void PlayFireAudio(RocketLauncherType _type, Vector3 _position, Transform _ownerOrNull)
	{
		RocketLauncherAudioUtility.PlayFire(this, _type, _position, _ownerOrNull);
	}

	public void PlayRpgReloadInsertAudio(Vector3 _position, Transform _ownerOrNull)
	{
		RocketLauncherAudioUtility.PlayRpgReloadInsert(this, _position, _ownerOrNull);
	}

	public bool TryPickFireWhooshClip(out AudioClip _clip) => m_FireWhooshClips.TryPickClip(out _clip);

	public bool TryPickFireAccentClip(RocketLauncherType _type, out AudioClip _clip)
	{
		WeaponRandomAudioClipSet set = _type == RocketLauncherType.Disposable
			? m_DisposableFireAccentClips
			: m_RpgFireAccentClips;
		return set.TryPickClip(out _clip);
	}

	public bool TryPickExplosionClip(RocketLauncherType _type, out AudioClip _clip)
	{
		WeaponRandomAudioClipSet set = _type == RocketLauncherType.Disposable
			? m_DisposableExplosionClips
			: m_RpgExplosionClips;
		return set.TryPickClip(out _clip);
	}

	public bool TryPickFlybyClip(out AudioClip _clip) => m_FlybyClips.TryPickClip(out _clip);

	public bool TryPickRpgReloadInsertClip(out AudioClip _clip) => m_RpgReloadInsertClips.TryPickClip(out _clip);
	#endregion

	#region Private Methods
	private static AnimationCurve BuildRoleCurve(float _d0, float _d25, float _d50, float _d75, float _d100)
	{
		return OpticDistanceCurveLibrary.BuildCurve(new[]
		{
			new OpticDistanceCurveLibrary.DistanceKeyframe(0f, _d0),
			new OpticDistanceCurveLibrary.DistanceKeyframe(125f, _d25),
			new OpticDistanceCurveLibrary.DistanceKeyframe(250f, _d50),
			new OpticDistanceCurveLibrary.DistanceKeyframe(375f, _d75),
			new OpticDistanceCurveLibrary.DistanceKeyframe(500f, _d100)
		});
	}
	#endregion
}
