using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// FX попадания по данным из <see cref="WeaponVfxProfile"/>.
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

		if (!_trace.HasHit || _trace.HitCollider == null)
			return;

		if (profile.EnableBodyImpactFx && _trace.ImpactVfxKind is WeaponShotImpactVfxKind.ArmorDeflect or WeaponShotImpactVfxKind.Flesh)
		{
			SpawnBodyImpact(profile, _trace);
			return;
		}

		if (!profile.IsImpactSurfaceLayer(_trace.HitCollider.gameObject.layer))
			return;

		if (!profile.TryResolveImpactSurface(_trace.HitCollider, out WeaponImpactSurfaceSet surface) || surface == null)
			return;

		if (profile.EnableImpactDecals)
			SpawnImpactDecal(profile, surface, _trace);

		if (profile.EnableImpactAudio)
			PlayImpactAudio(profile, surface, _trace);
	}

	private void SpawnBodyImpact(WeaponVfxProfile _profile, WeaponShotTraceInfo _trace)
	{
		bool armorDeflect = _trace.ImpactVfxKind == WeaponShotImpactVfxKind.ArmorDeflect;
		GameObject prefab = armorDeflect ? _profile.ArmorDeflectImpactPrefab : _profile.FleshImpactPrefab;
		if (prefab == null)
			return;

		Vector3 normal = _trace.HitNormal.sqrMagnitude > 1e-6f ? _trace.HitNormal.normalized : Vector3.up;
		Vector3 position = _trace.EndPoint + normal * _profile.BodyImpactSurfaceOffset;
		Quaternion rotation = Quaternion.LookRotation(normal);
		float intensityScale = armorDeflect ? _profile.ArmorDeflectImpactScale : _profile.FleshImpactScale;
		float lifetime = armorDeflect
			? _profile.ArmorDeflectImpactLifetimeSeconds
			: _profile.FleshImpactLifetimeSeconds;
		Vector3 scale = armorDeflect
			? Vector3.Scale(prefab.transform.localScale, Vector3.one * intensityScale)
			: Vector3.one * intensityScale;

		SpawnParticleImpact(prefab, position, rotation, scale, lifetime);
	}

	private void SpawnImpactDecal(
		WeaponVfxProfile _profile,
		WeaponImpactSurfaceSet _surface,
		WeaponShotTraceInfo _trace)
	{
		GameObject decalPrefab = _surface.PickRandomDecal();
		if (decalPrefab == null)
			return;

		Vector3 normal = _trace.HitNormal.sqrMagnitude > 1e-6f ? _trace.HitNormal.normalized : Vector3.up;
		Vector3 position = _trace.EndPoint + normal * _profile.DecalSurfaceOffset;
		Quaternion rotation = Quaternion.LookRotation(normal) *
			Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
		SpawnPooled(
			decalPrefab,
			position,
			rotation,
			Vector3.one * _profile.DecalScale,
			_profile.DecalLifetimeSeconds);
	}

	private static void PlayImpactAudio(
		WeaponVfxProfile _profile,
		WeaponImpactSurfaceSet _surface,
		WeaponShotTraceInfo _trace)
	{
		if (!_surface.TryPickImpactSound(out AudioClip clip, out float volume))
			return;

		UnitNonFireAudioUtility.PlayAtPoint(
			clip,
			_trace.EndPoint,
			volume,
			_profile.ImpactAudioMaxDistance);
	}

	private void SpawnParticleImpact(
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

		WeaponVfxUtility.PlayShellParticles(instance);
		StartCoroutine(ReleaseParticleAfter(pool, instance, _lifetime));
	}

	private static IEnumerator ReleaseParticleAfter(ObjectPool<GameObject> _pool, GameObject _instance, float _minSeconds)
	{
		if (_minSeconds > 0f)
			yield return new WaitForSeconds(_minSeconds);

		while (_instance != null && WeaponVfxUtility.IsParticleRootAlive(_instance))
			yield return null;

		if (_instance != null)
			_pool.Release(_instance);
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
			createFunc: () =>
			{
				GameObject instance = Instantiate(_prefab);
				WeaponVfxUtility.PrepareBodyImpactParticleInstance(instance);
				return instance;
			},
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
