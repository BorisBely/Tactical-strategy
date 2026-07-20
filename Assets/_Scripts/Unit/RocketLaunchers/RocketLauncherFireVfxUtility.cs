using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Дульная вспышка и задний бластик гранатомёта — тот же prefab, что у винтовок, с увеличенным масштабом.
/// </summary>
public static class RocketLauncherFireVfxUtility
{
	#region Constants
	private const int c_DefaultPoolCapacity = 4;
	private const int c_MaxPoolSize = 16;
	#endregion

	#region Private Fields
	private static readonly Dictionary<GameObject, ObjectPool<GameObject>> s_Pools =
		new Dictionary<GameObject, ObjectPool<GameObject>>(2);
	#endregion

	#region Public Methods
	public static void PlayFireVfx(
		RocketLauncherData _data,
		Transform _muzzleOrNull,
		Transform _backblastOrNull)
	{
		if (_data == null || !_data.EnableFireMuzzleVfx)
			return;

		GameObject prefab = _data.FireMuzzleFlashPrefab;
		if (prefab == null)
			return;

		float lifetime = Mathf.Max(0.05f, _data.FireMuzzleVfxLifetimeSeconds);
		float maxDistance = _data.FireMuzzleVfxMaxDistanceMeters;

		if (_muzzleOrNull != null)
		{
			SpawnAt(
				prefab,
				_muzzleOrNull.position,
				_muzzleOrNull.rotation,
				_data.FireMuzzleVfxScale,
				lifetime,
				maxDistance);
		}

		if (_backblastOrNull != null)
		{
			SpawnAt(
				prefab,
				_backblastOrNull.position,
				_backblastOrNull.rotation,
				_data.FireBackblastVfxScale,
				lifetime,
				maxDistance);
		}
	}
	#endregion

	#region Private Methods
	private static void SpawnAt(
		GameObject _prefab,
		Vector3 _position,
		Quaternion _rotation,
		Vector3 _scale,
		float _lifetime,
		float _maxDistance)
	{
		if (!WeaponVfxUtility.IsWithinEffectDistance(_position, _maxDistance))
			return;

		if (!CombatVfxBudgetService.TryAcquire(CombatVfxBudgetService.Category.MuzzleFlash))
			return;

		ObjectPool<GameObject> pool = GetOrCreatePool(_prefab);
		GameObject instance = pool.Get();
		Transform t = instance.transform;
		t.SetPositionAndRotation(_position, _rotation);
		t.localScale = _scale;

		WeaponVfxUtility.PlayParticleSystems(instance);
		WeaponVfxRuntimeRelease.StartRelease(
			pool,
			instance,
			CombatVfxBudgetService.Category.MuzzleFlash,
			_lifetime,
			_waitForParticles: true);
	}

	private static ObjectPool<GameObject> GetOrCreatePool(GameObject _prefab)
	{
		if (s_Pools.TryGetValue(_prefab, out ObjectPool<GameObject> existing))
			return existing;

		ObjectPool<GameObject> pool = new ObjectPool<GameObject>(
			createFunc: () => Object.Instantiate(_prefab),
			actionOnGet: go => go.SetActive(true),
			actionOnRelease: go => go.SetActive(false),
			actionOnDestroy: Object.Destroy,
			collectionCheck: false,
			defaultCapacity: c_DefaultPoolCapacity,
			maxSize: c_MaxPoolSize);

		s_Pools.Add(_prefab, pool);
		return pool;
	}
	#endregion
}
