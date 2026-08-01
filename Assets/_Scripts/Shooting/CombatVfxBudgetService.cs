using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Глобальные лимиты одновременных combat VFX в сцене.
/// Защищает от worst-case при массовой стрельбе в RTS.
/// </summary>
public static class CombatVfxBudgetService
{
	#region Constants
	private const int c_MaxActiveMuzzleFlashes = 32;
	private const int c_MaxActiveImpactParticles = 64;
	private const int c_MaxActiveDecals = 128;
	private const int c_MaxActiveBulletTrails = 48;
	private const int c_MaxActiveExplosions = 8;
	private const int c_MaxActiveSmokeClouds = 4;
	private const int c_ExplosionPoolDefaultCapacity = 2;
	private const int c_ExplosionPoolMaxSize = 8;
	private const int c_SmokePoolDefaultCapacity = 1;
	private const int c_SmokePoolMaxSize = 4;
	#endregion

	#region Nested Types
	public enum Category
	{
		MuzzleFlash = 0,
		ImpactParticle = 1,
		Decal = 2,
		BulletTrail = 3,
		Explosion = 4,
		SmokeCloud = 5,
	}

	private struct DecalEntry
	{
		public GameObject Instance;
		public ObjectPool<GameObject> Pool;
	}
	#endregion

	#region Static Fields
	private static int s_ActiveMuzzleFlashes;
	private static int s_ActiveImpactParticles;
	private static int s_ActiveDecals;
	private static int s_ActiveBulletTrails;
	private static int s_ActiveExplosions;
	private static int s_ActiveSmokeClouds;
	private static readonly LinkedList<DecalEntry> s_ActiveDecalEntries = new LinkedList<DecalEntry>();
	private static readonly Dictionary<GameObject, ObjectPool<GameObject>> s_ExplosionPools =
		new Dictionary<GameObject, ObjectPool<GameObject>>(2);
	private static readonly Dictionary<GameObject, ObjectPool<GameObject>> s_SmokePools =
		new Dictionary<GameObject, ObjectPool<GameObject>>(1);
	private static Transform s_PoolRoot;
	#endregion

	#region Public Methods
	public static bool TryAcquire(Category _category)
	{
		switch (_category)
		{
			case Category.MuzzleFlash:
				if (s_ActiveMuzzleFlashes >= c_MaxActiveMuzzleFlashes)
					return false;
				s_ActiveMuzzleFlashes++;
				return true;

			case Category.ImpactParticle:
				if (s_ActiveImpactParticles >= c_MaxActiveImpactParticles)
					return false;
				s_ActiveImpactParticles++;
				return true;

			case Category.Decal:
				if (s_ActiveDecals >= c_MaxActiveDecals && !TryEvictOldestDecal())
					return false;
				s_ActiveDecals++;
				return true;

			case Category.BulletTrail:
				if (s_ActiveBulletTrails >= c_MaxActiveBulletTrails)
					return false;
				s_ActiveBulletTrails++;
				return true;

			case Category.Explosion:
				if (s_ActiveExplosions >= c_MaxActiveExplosions)
					return false;
				s_ActiveExplosions++;
				return true;

			case Category.SmokeCloud:
				if (s_ActiveSmokeClouds >= c_MaxActiveSmokeClouds)
					return false;
				s_ActiveSmokeClouds++;
				return true;

			default:
				return true;
		}
	}

	public static void Release(Category _category)
	{
		switch (_category)
		{
			case Category.MuzzleFlash:
				s_ActiveMuzzleFlashes = Mathf.Max(0, s_ActiveMuzzleFlashes - 1);
				break;
			case Category.ImpactParticle:
				s_ActiveImpactParticles = Mathf.Max(0, s_ActiveImpactParticles - 1);
				break;
			case Category.Decal:
				s_ActiveDecals = Mathf.Max(0, s_ActiveDecals - 1);
				break;
			case Category.BulletTrail:
				s_ActiveBulletTrails = Mathf.Max(0, s_ActiveBulletTrails - 1);
				break;
			case Category.Explosion:
				s_ActiveExplosions = Mathf.Max(0, s_ActiveExplosions - 1);
				break;
			case Category.SmokeCloud:
				s_ActiveSmokeClouds = Mathf.Max(0, s_ActiveSmokeClouds - 1);
				break;
		}
	}

	public static void RegisterActiveDecal(GameObject _instance, ObjectPool<GameObject> _pool)
	{
		if (_instance == null || _pool == null)
			return;

		s_ActiveDecalEntries.AddLast(new DecalEntry
		{
			Instance = _instance,
			Pool = _pool,
		});
	}

	public static void UnregisterActiveDecal(GameObject _instance)
	{
		TryUnregisterActiveDecal(_instance);
	}

	public static bool IsDecalRegistered(GameObject _instance)
	{
		if (_instance == null)
			return false;

		for (LinkedListNode<DecalEntry> node = s_ActiveDecalEntries.First; node != null; node = node.Next)
		{
			if (node.Value.Instance == _instance)
				return true;
		}

		return false;
	}

	public static bool TryUnregisterActiveDecal(GameObject _instance)
	{
		if (_instance == null)
			return false;

		for (LinkedListNode<DecalEntry> node = s_ActiveDecalEntries.First; node != null; node = node.Next)
		{
			if (node.Value.Instance != _instance)
				continue;

			s_ActiveDecalEntries.Remove(node);
			return true;
		}

		return false;
	}

	public static bool TrySpawnExplosion(
		GameObject _prefab,
		Vector3 _position,
		Quaternion _rotation,
		Vector3 _scale,
		float _maxDistanceMeters,
		float _fallbackLifetimeSeconds)
	{
		if (_prefab == null)
			return false;

		WeaponVfxQualityTier tier = WeaponVfxUtility.ResolveEffectQualityTier(
			null,
			_position,
			_maxDistanceMeters,
			_maxDistanceMeters * 0.5f,
			_maxDistanceMeters);
		if (tier == WeaponVfxQualityTier.Skip)
			return false;

		if (!TryAcquire(Category.Explosion))
			return false;

		ObjectPool<GameObject> pool = GetOrCreateExplosionPool(_prefab);
		GameObject instance = pool.Get();
		Transform t = instance.transform;
		t.SetPositionAndRotation(_position, _rotation);
		t.localScale = tier == WeaponVfxQualityTier.Reduced
			? _scale * 0.75f
			: _scale;

		WeaponVfxUtility.PlayParticleSystems(instance);
		WeaponVfxRuntimeRelease.StartRelease(
			pool,
			instance,
			Category.Explosion,
			_fallbackLifetimeSeconds,
			_waitForParticles: true);
		return true;
	}

	/// <summary>
	/// Долгое дымовое облако: не форсирует non-loop и не делит бюджет со взрывами.
	/// </summary>
	public static bool TrySpawnSmokeCloud(
		GameObject _prefab,
		Vector3 _position,
		Quaternion _rotation,
		Vector3 _scale,
		float _maxDistanceMeters,
		float _fallbackLifetimeSeconds,
		AudioClip _loopClip = null,
		float _loopVolume = 0.7f,
		float _loopMaxDistance = 45f,
		float _fadeInSeconds = 1.4f,
		float _fadeOutSeconds = 3f,
		float _crossfadeSeconds = 2.2f,
		float _dissipateSeconds = 10f)
	{
		if (_prefab == null)
			return false;

		WeaponVfxQualityTier tier = WeaponVfxUtility.ResolveEffectQualityTier(
			null,
			_position,
			_maxDistanceMeters,
			_maxDistanceMeters * 0.5f,
			_maxDistanceMeters * 0.85f);
		if (tier == WeaponVfxQualityTier.Skip)
			return false;

		if (!TryAcquire(Category.SmokeCloud))
			return false;

		ObjectPool<GameObject> pool = GetOrCreateSmokePool(_prefab);
		GameObject instance = pool.Get();
		WeaponVfxUtility.ApplySmokeSpawnTransform(instance, _prefab, _position);
		WeaponVfxUtility.PlaySmokeParticleSystems(instance);

		if (_loopClip != null && instance.TryGetComponent(out GrenadeSmokeAudioLoop smokeAudio))
		{
			smokeAudio.Configure(
				_loopClip,
				_loopVolume,
				_loopMaxDistance,
				_fadeInSeconds,
				_fadeOutSeconds,
				_crossfadeSeconds);
			smokeAudio.Play();
		}

		WeaponVfxRuntimeRelease.StartSmokeCloudRelease(
			pool,
			instance,
			_fallbackLifetimeSeconds,
			_fadeOutSeconds,
			_dissipateSeconds);
		return true;
	}
	#endregion

	#region Private Methods
	private static bool TryEvictOldestDecal()
	{
		if (s_ActiveDecalEntries.Count == 0)
			return false;

		DecalEntry oldest = s_ActiveDecalEntries.First.Value;
		s_ActiveDecalEntries.RemoveFirst();

		if (oldest.Instance != null)
		{
			oldest.Pool.Release(oldest.Instance);
			s_ActiveDecals = Mathf.Max(0, s_ActiveDecals - 1);
		}

		return true;
	}

	private static ObjectPool<GameObject> GetOrCreateExplosionPool(GameObject _prefab)
	{
		if (s_ExplosionPools.TryGetValue(_prefab, out ObjectPool<GameObject> existing))
			return existing;

		EnsurePoolRoot();
		ObjectPool<GameObject> pool = new ObjectPool<GameObject>(
			createFunc: () =>
			{
				GameObject instance = Object.Instantiate(_prefab, s_PoolRoot);
				WeaponVfxUtility.PrepareBodyImpactParticleInstance(instance);
				return instance;
			},
			actionOnGet: go => go.SetActive(true),
			actionOnRelease: go =>
			{
				if (go != null)
					go.SetActive(false);
			},
			actionOnDestroy: Object.Destroy,
			collectionCheck: false,
			defaultCapacity: c_ExplosionPoolDefaultCapacity,
			maxSize: c_ExplosionPoolMaxSize);

		s_ExplosionPools.Add(_prefab, pool);
		return pool;
	}

	private static ObjectPool<GameObject> GetOrCreateSmokePool(GameObject _prefab)
	{
		if (s_SmokePools.TryGetValue(_prefab, out ObjectPool<GameObject> existing))
			return existing;

		EnsurePoolRoot();
		ObjectPool<GameObject> pool = new ObjectPool<GameObject>(
			createFunc: () =>
			{
				GameObject instance = Object.Instantiate(_prefab, s_PoolRoot);
				WeaponVfxUtility.PrepareSmokeParticleInstance(instance);
				return instance;
			},
			actionOnGet: go =>
			{
				if (go != null)
					go.SetActive(true);
			},
			actionOnRelease: go =>
			{
				if (go != null)
				{
					WeaponVfxUtility.StopParticleSystems(go);
					go.SetActive(false);
				}
			},
			actionOnDestroy: Object.Destroy,
			collectionCheck: false,
			defaultCapacity: c_SmokePoolDefaultCapacity,
			maxSize: c_SmokePoolMaxSize);

		s_SmokePools.Add(_prefab, pool);
		return pool;
	}

	private static void EnsurePoolRoot()
	{
		if (s_PoolRoot != null)
			return;

		GameObject rootGo = new GameObject("[CombatVfxBudgetService]");
		Object.DontDestroyOnLoad(rootGo);
		s_PoolRoot = rootGo.transform;
	}
	#endregion
}
