using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Визуальный полёт пули по данным <see cref="WeaponShotTraceInfo"/> и <see cref="WeaponVfxProfile"/>.
/// Геймплей остаётся hitscan; mesh летит от дула к точке попадания за время, рассчитанное по скорости патрона.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(57)]
public sealed class UnitWeaponBulletFlightVfx : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponHitscanShooting m_HitscanShooting;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;

	[Header("Pool")]
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 16;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 64;
	#endregion

	#region Private Fields
	private readonly Dictionary<GameObject, ObjectPool<GameObject>> m_Pools = new Dictionary<GameObject, ObjectPool<GameObject>>(2);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_HitscanShooting == null)
			m_HitscanShooting = GetComponent<UnitWeaponHitscanShooting>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
	}

	private void OnEnable()
	{
		if (m_HitscanShooting != null)
			m_HitscanShooting.ShotTrace += HandleShotTrace;
	}

	private void OnDisable()
	{
		if (m_HitscanShooting != null)
			m_HitscanShooting.ShotTrace -= HandleShotTrace;
	}
	#endregion

	#region Private Methods
	private void HandleShotTrace(WeaponShotTraceInfo _trace)
	{
		if (_trace.HitSelf)
			return;

		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
		if (profile == null || !profile.EnableBulletFlight)
			return;

		if (!_trace.HasHit && !profile.ShowBulletFlightOnMiss)
			return;

		GameObject prefab = profile.BulletFlightPrefab;
		if (prefab == null)
			return;

		float distance = Vector3.Distance(_trace.Origin, _trace.EndPoint);
		if (distance <= 0.001f)
			return;

		float ammoVelocity = _trace.Ammo != null ? _trace.Ammo.Velocity : 400f;
		float flightSeconds = profile.ComputeBulletFlightSeconds(distance, ammoVelocity);
		if (flightSeconds <= 0.0001f)
			return;

		ObjectPool<GameObject> pool = GetOrCreatePool(prefab);
		GameObject instance = pool.Get();
		PrepareInstance(instance, profile, _trace);
		StartCoroutine(AnimateFlight(pool, instance, _trace.Origin, _trace.EndPoint, flightSeconds));
	}

	private static void PrepareInstance(
		GameObject _instance,
		WeaponVfxProfile _profile,
		WeaponShotTraceInfo _trace)
	{
		Vector3 direction = (_trace.EndPoint - _trace.Origin).normalized;
		Quaternion rotation = direction.sqrMagnitude > 1e-6f
			? Quaternion.LookRotation(direction)
			: Quaternion.identity;
		float scale = _profile.BulletFlightScale;
		float lengthScale = _profile.BulletFlightLengthScale;

		Transform t = _instance.transform;
		t.SetPositionAndRotation(_trace.Origin, rotation);
		t.localScale = new Vector3(scale, scale, scale * lengthScale);
	}

	private static IEnumerator AnimateFlight(
		ObjectPool<GameObject> _pool,
		GameObject _instance,
		Vector3 _origin,
		Vector3 _endPoint,
		float _flightSeconds)
	{
		Transform t = _instance.transform;
		float elapsed = 0f;

		while (elapsed < _flightSeconds)
		{
			if (_instance == null || t == null)
				yield break;

			elapsed += Time.deltaTime;
			float progress = _flightSeconds > 1e-6f ? Mathf.Clamp01(elapsed / _flightSeconds) : 1f;
			t.position = Vector3.Lerp(_origin, _endPoint, progress);
			yield return null;
		}

		if (_instance != null)
			_pool.Release(_instance);
	}

	private ObjectPool<GameObject> GetOrCreatePool(GameObject _prefab)
	{
		if (m_Pools.TryGetValue(_prefab, out ObjectPool<GameObject> existing))
			return existing;

		ObjectPool<GameObject> pool = new ObjectPool<GameObject>(
			createFunc: () => Instantiate(_prefab),
			actionOnGet: go => go.SetActive(true),
			actionOnRelease: go => go.SetActive(false),
			actionOnDestroy: Destroy,
			collectionCheck: false,
			defaultCapacity: m_DefaultPoolCapacity,
			maxSize: m_MaxPoolSize);

		m_Pools.Add(_prefab, pool);
		return pool;
	}
	#endregion
}
