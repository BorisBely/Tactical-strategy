using UnityEngine;

/// <summary>
/// Визуальные FX выстрела для конкретной оружейной платформы.
/// Привязывается к <see cref="WeaponDefinition"/>; юнит только оркестрирует спавн в сокетах оружия.
/// </summary>
[CreateAssetMenu(fileName = "WeaponVfxProfile", menuName = "Polygone/Shooting/Weapon VFX Profile", order = 12)]
public sealed class WeaponVfxProfile : ScriptableObject
{
	#region Serialized Fields
	[Header("Muzzle Flash")]
	[SerializeField] private bool m_EnableMuzzleFlash = true;
	[SerializeField] private GameObject m_UnsuppressedMuzzleFlashPrefab;
	[SerializeField] private GameObject m_SuppressedMuzzleFlashPrefab;
	[SerializeField, Min(0.02f)] private float m_UnsuppressedMuzzleLifetimeSeconds = 0.18f;
	[SerializeField, Min(0.02f)] private float m_SuppressedMuzzleLifetimeSeconds = 0.12f;
	[SerializeField, Min(0.01f)] private float m_UnsuppressedMuzzleScale = 1f;
	[SerializeField, Min(0.01f)] private float m_SuppressedMuzzleScale = 0.35f;

	[Header("Shell Ejection")]
	[SerializeField] private WeaponShellEjectionVisualMode m_ShellEjectionMode = WeaponShellEjectionVisualMode.Physical;
	[SerializeField] private GameObject m_ShellParticlePrefab;
	[SerializeField, Min(0.05f)] private float m_ShellParticleLifetimeSeconds = 2.5f;
	[Tooltip("Множитель scale корня FX поверх startSize префаба (у FX_ShellEjection_Particle startSize=0.5).")]
	[SerializeField, Min(0.01f)] private float m_ShellParticleScale = 2f;
	[SerializeField] private Vector3 m_ShellPrefabEjectionAxis = Vector3.right;
	[SerializeField] private Vector3 m_ShellLocalEulerOffset;
	[Tooltip("Hybrid / near-camera detail: ближе этой дистанции до active camera — физическая гильза, visual kick и цикл затвора; дальше — particle-гильза без kick/bolt motion.")]
	[SerializeField, Min(0f)] private float m_HybridPhysicalShellDistanceMeters = 12f;

	[Header("Bullet Flight")]
	[SerializeField] private bool m_EnableBulletFlight = true;
	[SerializeField] private GameObject m_BulletFlightPrefab;
	[Tooltip("Общий множитель scale корня FX (ширина/высота/длина streak).")]
	[SerializeField, Min(0.01f)] private float m_BulletFlightScale = 1f;
	[Tooltip("Дополнительный множитель длины streak вдоль локальной оси Z prefab.")]
	[SerializeField, Min(0.01f)] private float m_BulletFlightLengthScale = 0.2f;
	[Tooltip("Множитель к AmmoDefinition.Velocity только для визуала. Меньше 1 = медленнее полёт, заметнее на близкой дистанции.")]
	[SerializeField, Range(0.05f, 2f)] private float m_BulletVisualSpeedMultiplier = 0.35f;
	[Tooltip("Минимальная длительность полёта, чтобы пуля была заметна вблизи.")]
	[SerializeField, Min(0f)] private float m_BulletMinFlightSeconds = 0.045f;
	[Tooltip("Ограничение длительности полёта на дальних дистанциях.")]
	[SerializeField, Min(0.01f)] private float m_BulletMaxFlightSeconds = 0.85f;
	[SerializeField] private bool m_ShowBulletFlightOnMiss = true;

	[Header("Body Impact FX")]
	[SerializeField] private bool m_EnableBodyImpactFx = true;
	[SerializeField] private GameObject m_ArmorDeflectImpactPrefab;
	[SerializeField] private GameObject m_FleshImpactPrefab;
	[SerializeField, Min(0.02f)] private float m_ArmorDeflectImpactLifetimeSeconds = 0.35f;
	[SerializeField, Min(0.02f)] private float m_FleshImpactLifetimeSeconds = 0.8f;
	[SerializeField, Min(0.01f)] private float m_ArmorDeflectImpactScale = 1f;
	[SerializeField, Min(0.001f)] private float m_FleshImpactScale = 0.2f;
	[SerializeField, Min(0f)] private float m_BodyImpactSurfaceOffset = 0.01f;

	[Header("Impact Surfaces")]
	[SerializeField] private bool m_EnableImpactDecals = true;
	[SerializeField] private bool m_EnableImpactAudio = true;
	[Tooltip("Слои, на которых спавнятся декали/звуки попадания по поверхности.")]
	[SerializeField] private LayerMask m_ImpactSurfaceLayers;
	[Tooltip("Наборы по Physics Material. Первое совпадение по material; иначе DefaultSurfaceName.")]
	[SerializeField] private WeaponImpactSurfaceSet[] m_ImpactSurfaces;
	[Tooltip("Имя поверхности-фолбэка, если Physics Material не совпал (обычно Concrete).")]
	[SerializeField] private string m_DefaultSurfaceName = "Concrete";
	[SerializeField, Min(0f)] private float m_DecalSurfaceOffset = 0.012f;
	[SerializeField, Min(0.01f)] private float m_DecalScale = 0.45f;
	[SerializeField, Min(0.05f)] private float m_DecalLifetimeSeconds = 20f;
	[SerializeField, Min(1f)] private float m_ImpactAudioMaxDistance = 45f;

	[Header("Effect Distance LOD")]
	[SerializeField, Min(0f)] private float m_MuzzleFlashMaxDistanceMeters = 50f;
	[SerializeField, Min(0f)] private float m_ImpactFxMaxDistanceMeters = 30f;
	[SerializeField, Min(0f)] private float m_BulletFlightMaxDistanceMeters = 40f;
	[SerializeField, Min(0f)] private float m_DecalMaxDistanceMeters = 20f;

	[Header("Effect Quality Tiers")]
	[Tooltip("Дистанция до active camera, внутри которой FX играют в полном качестве.")]
	[SerializeField, Min(0f)] private float m_EffectNearQualityDistanceMeters = 15f;
	[Tooltip("Дистанция до active camera, внутри которой FX играют в упрощённом качестве. Дальше — skip визуала.")]
	[SerializeField, Min(0f)] private float m_EffectMidQualityDistanceMeters = 35f;
	[SerializeField, Range(0.05f, 1f)] private float m_ReducedParticleScaleMultiplier = 0.6f;
	[SerializeField, Range(0.05f, 1f)] private float m_ReducedMaxParticlesMultiplier = 0.35f;
	[SerializeField, Range(0.05f, 1f)] private float m_ReducedMuzzleScaleMultiplier = 0.55f;
	[SerializeField, Range(0.05f, 1f)] private float m_ReducedBulletFlightScaleMultiplier = 0.7f;
	[SerializeField, Range(0.05f, 1f)] private float m_ReducedDecalLifetimeMultiplier = 0.5f;

	[Header("Turret / Heavy Weapon")]
	[Tooltip("Интенсивность дыма (множитель startSize/emission).")]
	[SerializeField, Min(0f)] private float m_SmokeIntensity = 1f;
	[Tooltip("Дополнительный множитель scale для вспышки (поверх MuzzleScale).")]
	[SerializeField, Min(0.01f)] private float m_FlashScale = 1f;
	[Tooltip("Каждая N-я пуля — трассер. 1 = каждая, 5 = каждая 5-я.")]
	[SerializeField, Min(1)] private int m_TracerEveryNShot = 1;
	#endregion

	#region Public Properties
	public bool EnableMuzzleFlash => m_EnableMuzzleFlash;
	public GameObject UnsuppressedMuzzleFlashPrefab => m_UnsuppressedMuzzleFlashPrefab;
	public GameObject SuppressedMuzzleFlashPrefab => m_SuppressedMuzzleFlashPrefab;
	public float UnsuppressedMuzzleLifetimeSeconds => m_UnsuppressedMuzzleLifetimeSeconds;
	public float SuppressedMuzzleLifetimeSeconds => m_SuppressedMuzzleLifetimeSeconds;
	public float UnsuppressedMuzzleScale => m_UnsuppressedMuzzleScale;
	public float SuppressedMuzzleScale => m_SuppressedMuzzleScale;

	public WeaponShellEjectionVisualMode ShellEjectionMode => m_ShellEjectionMode;
	public bool UseParticleShellEjection => m_ShellEjectionMode == WeaponShellEjectionVisualMode.Particle;
	public bool UsePhysicalShellEjection => m_ShellEjectionMode == WeaponShellEjectionVisualMode.Physical;
	public bool UseHybridShellEjection => m_ShellEjectionMode == WeaponShellEjectionVisualMode.Hybrid;
	public GameObject ShellParticlePrefab => m_ShellParticlePrefab;
	public float ShellParticleLifetimeSeconds => m_ShellParticleLifetimeSeconds;
	public float ShellParticleScale => m_ShellParticleScale;
	public Vector3 ShellPrefabEjectionAxis => m_ShellPrefabEjectionAxis;
	public Vector3 ShellLocalEulerOffset => m_ShellLocalEulerOffset;
	public float HybridPhysicalShellDistanceMeters => m_HybridPhysicalShellDistanceMeters;

	public bool EnableBulletFlight => m_EnableBulletFlight;
	public GameObject BulletFlightPrefab => m_BulletFlightPrefab;
	public float BulletFlightScale => m_BulletFlightScale;
	public float BulletFlightLengthScale => m_BulletFlightLengthScale;
	public float BulletVisualSpeedMultiplier => m_BulletVisualSpeedMultiplier;
	public float BulletMinFlightSeconds => m_BulletMinFlightSeconds;
	public float BulletMaxFlightSeconds => m_BulletMaxFlightSeconds;
	public bool ShowBulletFlightOnMiss => m_ShowBulletFlightOnMiss;

	public bool EnableBodyImpactFx => m_EnableBodyImpactFx;
	public GameObject ArmorDeflectImpactPrefab => m_ArmorDeflectImpactPrefab;
	public GameObject FleshImpactPrefab => m_FleshImpactPrefab;
	public float ArmorDeflectImpactLifetimeSeconds => m_ArmorDeflectImpactLifetimeSeconds;
	public float FleshImpactLifetimeSeconds => m_FleshImpactLifetimeSeconds;
	public float ArmorDeflectImpactScale => m_ArmorDeflectImpactScale;
	public float FleshImpactScale => m_FleshImpactScale;
	public float BodyImpactSurfaceOffset => m_BodyImpactSurfaceOffset;

	public bool EnableImpactDecals => m_EnableImpactDecals;
	public bool EnableImpactAudio => m_EnableImpactAudio;
	public LayerMask ImpactSurfaceLayers => m_ImpactSurfaceLayers;
	public WeaponImpactSurfaceSet[] ImpactSurfaces => m_ImpactSurfaces;
	public string DefaultSurfaceName => m_DefaultSurfaceName;
	public float DecalSurfaceOffset => m_DecalSurfaceOffset;
	public float DecalScale => m_DecalScale;
	public float DecalLifetimeSeconds => m_DecalLifetimeSeconds;
	public float ImpactAudioMaxDistance => m_ImpactAudioMaxDistance;

	public float MuzzleFlashMaxDistanceMeters => m_MuzzleFlashMaxDistanceMeters;
	public float ImpactFxMaxDistanceMeters => m_ImpactFxMaxDistanceMeters;
	public float BulletFlightMaxDistanceMeters => m_BulletFlightMaxDistanceMeters;
	public float DecalMaxDistanceMeters => m_DecalMaxDistanceMeters;
	public float EffectNearQualityDistanceMeters => m_EffectNearQualityDistanceMeters;
	public float EffectMidQualityDistanceMeters => m_EffectMidQualityDistanceMeters;
	public float ReducedParticleScaleMultiplier => m_ReducedParticleScaleMultiplier;
	public float ReducedMaxParticlesMultiplier => m_ReducedMaxParticlesMultiplier;
	public float ReducedMuzzleScaleMultiplier => m_ReducedMuzzleScaleMultiplier;
	public float ReducedBulletFlightScaleMultiplier => m_ReducedBulletFlightScaleMultiplier;
	public float ReducedDecalLifetimeMultiplier => m_ReducedDecalLifetimeMultiplier;

	public float SmokeIntensity => m_SmokeIntensity;
	public float FlashScale => m_FlashScale;
	public int TracerEveryNShot => m_TracerEveryNShot;
	#endregion

	#region Public Methods
	public float ComputeBulletFlightSeconds(float _distanceMeters, float _ammoVelocityMetersPerSecond)
	{
		float distance = Mathf.Max(0f, _distanceMeters);
		if (distance <= 0.0001f)
			return 0f;

		float velocity = Mathf.Max(0.1f, _ammoVelocityMetersPerSecond) * Mathf.Max(0.05f, m_BulletVisualSpeedMultiplier);
		float seconds = distance / velocity;

		if (m_BulletMinFlightSeconds > 0f)
			seconds = Mathf.Max(seconds, m_BulletMinFlightSeconds);

		if (m_BulletMaxFlightSeconds > 0f)
			seconds = Mathf.Min(seconds, m_BulletMaxFlightSeconds);

		return seconds;
	}

	public bool IsImpactSurfaceLayer(int _layer)
	{
		int bit = 1 << _layer;
		return (m_ImpactSurfaceLayers.value & bit) != 0;
	}

	public bool TryResolveImpactSurface(Collider _hitCollider, out WeaponImpactSurfaceSet _surface)
	{
		_surface = null;
		if (m_ImpactSurfaces == null || m_ImpactSurfaces.Length == 0)
			return false;

		PhysicsMaterial hitMaterial = _hitCollider != null ? _hitCollider.sharedMaterial : null;
		if (hitMaterial != null)
		{
			for (int i = 0; i < m_ImpactSurfaces.Length; i++)
			{
				WeaponImpactSurfaceSet set = m_ImpactSurfaces[i];
				if (set == null || set.PhysicsMaterial == null)
					continue;

				if (set.PhysicsMaterial == hitMaterial)
				{
					_surface = set;
					return true;
				}
			}
		}

		_surface = FindSurfaceByName(m_DefaultSurfaceName);
		if (_surface != null)
			return true;

		for (int i = 0; i < m_ImpactSurfaces.Length; i++)
		{
			if (m_ImpactSurfaces[i] != null)
			{
				_surface = m_ImpactSurfaces[i];
				return true;
			}
		}

		return false;
	}

	public WeaponImpactSurfaceSet FindSurfaceByName(string _surfaceName)
	{
		if (m_ImpactSurfaces == null || string.IsNullOrEmpty(_surfaceName))
			return null;

		for (int i = 0; i < m_ImpactSurfaces.Length; i++)
		{
			WeaponImpactSurfaceSet set = m_ImpactSurfaces[i];
			if (set == null || string.IsNullOrEmpty(set.SurfaceName))
				continue;

			if (string.Equals(set.SurfaceName, _surfaceName, System.StringComparison.OrdinalIgnoreCase))
				return set;
		}

		return null;
	}
	#endregion
}
