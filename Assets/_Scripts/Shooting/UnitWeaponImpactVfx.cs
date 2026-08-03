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

		WeaponVfxQualityTier impactTier = WeaponVfxUtility.ResolveEffectQualityTier(
			profile,
			_trace.EndPoint,
			profile.ImpactFxMaxDistanceMeters);

		if (profile.EnableBodyImpactFx && _trace.ImpactVfxKind is WeaponShotImpactVfxKind.ArmorDeflect or WeaponShotImpactVfxKind.Flesh)
		{
			if (impactTier != WeaponVfxQualityTier.Skip)
				SpawnBodyImpact(profile, _trace, impactTier);
			return;
		}

		if (!profile.TryResolveImpactSurface(_trace.HitCollider, out WeaponImpactSurfaceSet surface) || surface == null)
			return;

		WeaponVfxQualityTier decalTier = WeaponVfxUtility.ResolveEffectQualityTier(
			profile,
			_trace.EndPoint,
			profile.DecalMaxDistanceMeters);

		if (profile.EnableImpactDecals && decalTier != WeaponVfxQualityTier.Skip)
			SpawnImpactDecal(profile, surface, _trace, decalTier);

		if (profile.EnableImpactAudio && impactTier != WeaponVfxQualityTier.Skip)
			PlayImpactAudio(profile, surface, _trace);
	}

	private void SpawnBodyImpact(
		WeaponVfxProfile _profile,
		WeaponShotTraceInfo _trace,
		WeaponVfxQualityTier _tier)
	{
		bool armorDeflect = _trace.ImpactVfxKind == WeaponShotImpactVfxKind.ArmorDeflect;
		GameObject prefab = armorDeflect ? _profile.ArmorDeflectImpactPrefab : _profile.FleshImpactPrefab;
		if (prefab == null)
			return;

		if (!CombatVfxBudgetService.TryAcquire(CombatVfxBudgetService.Category.ImpactParticle))
			return;

		Vector3 normal = _trace.HitNormal.sqrMagnitude > 1e-6f ? _trace.HitNormal.normalized : Vector3.up;
		Vector3 position = _trace.EndPoint + normal * _profile.BodyImpactSurfaceOffset;
		Quaternion rotation = Quaternion.LookRotation(normal);
		float intensityScale = armorDeflect ? _profile.ArmorDeflectImpactScale : _profile.FleshImpactScale;
		if (_tier == WeaponVfxQualityTier.Reduced)
			intensityScale *= _profile.ReducedParticleScaleMultiplier;

		float lifetime = armorDeflect
			? _profile.ArmorDeflectImpactLifetimeSeconds
			: _profile.FleshImpactLifetimeSeconds;
		Vector3 scale = armorDeflect
			? Vector3.Scale(prefab.transform.localScale, Vector3.one * intensityScale)
			: Vector3.one * intensityScale;

		SpawnParticleImpact(prefab, position, rotation, scale, lifetime, _profile, _tier);
	}

	private void SpawnImpactDecal(
		WeaponVfxProfile _profile,
		WeaponImpactSurfaceSet _surface,
		WeaponShotTraceInfo _trace,
		WeaponVfxQualityTier _tier)
	{
		GameObject decalPrefab = _surface.PickRandomDecal();
		if (decalPrefab == null)
			return;

		if (!CombatVfxBudgetService.TryAcquire(CombatVfxBudgetService.Category.Decal))
			return;

		Vector3 normal = _trace.HitNormal.sqrMagnitude > 1e-6f ? _trace.HitNormal.normalized : Vector3.up;
		Vector3 position = _trace.EndPoint + normal * _profile.DecalSurfaceOffset;
		Quaternion rotation = Quaternion.LookRotation(normal) *
			Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
		float lifetime = _profile.DecalLifetimeSeconds;
		if (_tier == WeaponVfxQualityTier.Reduced)
			lifetime *= _profile.ReducedDecalLifetimeMultiplier;

		SpawnPooled(
			decalPrefab,
			position,
			rotation,
			Vector3.one * _profile.DecalScale,
			lifetime);
	}

	private static void PlayImpactAudio(
		WeaponVfxProfile _profile,
		WeaponImpactSurfaceSet _surface,
		WeaponShotTraceInfo _trace)
	{
		if (!_surface.TryPickImpactSound(out AudioClip clip, out float volume))
			return;

		CombatAudioManager.TryPlayImpact(
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
		float _lifetime,
		WeaponVfxProfile _profile,
		WeaponVfxQualityTier _tier)
	{
		ObjectPool<GameObject> pool = GetOrCreatePool(_prefab);
		GameObject instance = pool.Get();
		Transform t = instance.transform;
		t.SetPositionAndRotation(_position, _rotation);
		t.localScale = _scale;

		WeaponVfxUtility.ApplyParticleQualityTier(instance, _profile, _tier);
		WeaponVfxUtility.PlayShellParticles(instance);
		WeaponVfxRuntimeRelease.StartRelease(
			pool,
			instance,
			CombatVfxBudgetService.Category.ImpactParticle,
			_lifetime,
			_waitForParticles: true);
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

		WeaponVfxRuntimeRelease.StartDecalRelease(pool, instance, _lifetime);
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
	#endregion
}
