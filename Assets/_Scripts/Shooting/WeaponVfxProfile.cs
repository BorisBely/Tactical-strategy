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
	[Tooltip("Множитель scale корня FX поверх startSize префаба (у FX_Bullet_Ejection_01 startSize=0.5).")]
	[SerializeField, Min(0.01f)] private float m_ShellParticleScale = 2f;
	[SerializeField] private Vector3 m_ShellPrefabEjectionAxis = Vector3.right;
	[SerializeField] private Vector3 m_ShellLocalEulerOffset;

	[Header("Bullet Trail")]
	[SerializeField] private bool m_EnableBulletTrail = true;
	[SerializeField] private GameObject m_BulletTrailPrefab;
	[SerializeField, Min(0.01f)] private float m_TrailLifetimeSeconds = 0.06f;
	[SerializeField, Min(0.001f)] private float m_TrailWidthScale = 0.035f;
	[SerializeField, Min(0.001f)] private float m_TrailLengthMultiplier = 1f;
	[SerializeField, Min(0.1f)] private float m_MaxTrailDistance = 120f;

	[Header("Impact Decals")]
	[SerializeField] private bool m_EnableImpactDecals = true;
	[Tooltip("Варианты бетонной декали; при попадании выбирается случайный непустой префаб.")]
	[SerializeField] private GameObject[] m_ConcreteImpactDecalPrefabs;
	[SerializeField] private LayerMask m_ConcreteDecalLayers;
	[SerializeField, Min(0f)] private float m_DecalSurfaceOffset = 0.012f;
	[SerializeField, Min(0.01f)] private float m_DecalScale = 0.45f;
	[SerializeField, Min(0.05f)] private float m_DecalLifetimeSeconds = 20f;
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
	public GameObject ShellParticlePrefab => m_ShellParticlePrefab;
	public float ShellParticleLifetimeSeconds => m_ShellParticleLifetimeSeconds;
	public float ShellParticleScale => m_ShellParticleScale;
	public Vector3 ShellPrefabEjectionAxis => m_ShellPrefabEjectionAxis;
	public Vector3 ShellLocalEulerOffset => m_ShellLocalEulerOffset;

	public bool EnableBulletTrail => m_EnableBulletTrail;
	public GameObject BulletTrailPrefab => m_BulletTrailPrefab;
	public float TrailLifetimeSeconds => m_TrailLifetimeSeconds;
	public float TrailWidthScale => m_TrailWidthScale;
	public float TrailLengthMultiplier => m_TrailLengthMultiplier;
	public float MaxTrailDistance => m_MaxTrailDistance;

	public bool EnableImpactDecals => m_EnableImpactDecals;
	public GameObject[] ConcreteImpactDecalPrefabs => m_ConcreteImpactDecalPrefabs;
	public LayerMask ConcreteDecalLayers => m_ConcreteDecalLayers;
	public float DecalSurfaceOffset => m_DecalSurfaceOffset;
	public float DecalScale => m_DecalScale;
	public float DecalLifetimeSeconds => m_DecalLifetimeSeconds;
	#endregion

	#region Public Methods
	public GameObject PickRandomConcreteImpactDecal()
	{
		if (m_ConcreteImpactDecalPrefabs == null || m_ConcreteImpactDecalPrefabs.Length == 0)
			return null;

		int validCount = 0;
		for (int i = 0; i < m_ConcreteImpactDecalPrefabs.Length; i++)
		{
			if (m_ConcreteImpactDecalPrefabs[i] != null)
				validCount++;
		}

		if (validCount == 0)
			return null;

		int pick = Random.Range(0, validCount);
		for (int i = 0; i < m_ConcreteImpactDecalPrefabs.Length; i++)
		{
			GameObject prefab = m_ConcreteImpactDecalPrefabs[i];
			if (prefab == null)
				continue;

			if (pick == 0)
				return prefab;

			pick--;
		}

		return null;
	}
	#endregion
}
