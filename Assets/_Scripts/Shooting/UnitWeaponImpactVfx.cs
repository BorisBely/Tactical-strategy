using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// След пули и декаль попадания по данным из <see cref="WeaponVfxProfile"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponImpactVfx : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponHitscanShooting m_HitscanShooting;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;

	[Header("Pool")]
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 24;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 96;
	#endregion

	#region Private Fields
	private readonly Dictionary<GameObject, ObjectPool<GameObject>> m_Pools = new Dictionary<GameObject, ObjectPool<GameObject>>(4);
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
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
		if (profile == null || _trace.HitSelf)
			return;

		if (profile.EnableBulletTrail)
			SpawnTrail(profile, _trace);

		if (profile.EnableImpactDecals && _trace.HasHit && _trace.HitCollider != null)
			SpawnImpactDecal(profile, _trace);
	}

	private void SpawnTrail(WeaponVfxProfile _profile, WeaponShotTraceInfo _trace)
	{
		if (_profile.BulletTrailPrefab == null)
			return;

		Vector3 delta = _trace.EndPoint - _trace.Origin;
		float distance = Mathf.Min(delta.magnitude, _profile.MaxTrailDistance);
		if (distance <= 0.05f)
			return;

		Vector3 dir = delta.normalized;
		Vector3 center = _trace.Origin + dir * (distance * 0.5f);
		Quaternion rotation = Quaternion.LookRotation(dir, Vector3.up);
		Vector3 scale = new Vector3(
			_profile.TrailWidthScale,
			_profile.TrailWidthScale,
			distance * _profile.TrailLengthMultiplier);

		SpawnPooled(_profile.BulletTrailPrefab, center, rotation, scale, _profile.TrailLifetimeSeconds);
	}

	private void SpawnImpactDecal(WeaponVfxProfile _profile, WeaponShotTraceInfo _trace)
	{
		GameObject decalPrefab = _profile.PickRandomConcreteImpactDecal();
		if (decalPrefab == null)
			return;

		int hitLayerBit = 1 << _trace.HitCollider.gameObject.layer;
		if ((_profile.ConcreteDecalLayers.value & hitLayerBit) == 0)
			return;

		Vector3 position = _trace.EndPoint + _trace.HitNormal * _profile.DecalSurfaceOffset;
		Quaternion rotation = Quaternion.LookRotation(_trace.HitNormal) *
			Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
		SpawnPooled(
			decalPrefab,
			position,
			rotation,
			Vector3.one * _profile.DecalScale,
			_profile.DecalLifetimeSeconds);
	}

	private void SpawnPooled(
		GameObject _prefab,
		Vector3 _position,
		Quaternion _rotation,
		Vector3 _scale,
		float _lifetime)
	{
		ObjectPool<GameObject> pool = GetOrCreatePool(_prefab);
		GameObject instance = pool.Get();
		Transform t = instance.transform;
		t.SetPositionAndRotation(_position, _rotation);
		t.localScale = _scale;

		StartCoroutine(ReleaseAfter(pool, instance, _lifetime));
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

	private static IEnumerator ReleaseAfter(ObjectPool<GameObject> _pool, GameObject _instance, float _seconds)
	{
		yield return new WaitForSeconds(_seconds);
		if (_instance != null)
			_pool.Release(_instance);
	}
	#endregion
}
