using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Дульная вспышка: читает префабы и тайминги из <see cref="WeaponVfxProfile"/> текущего оружия.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponMuzzleVfx : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;

	[Header("Pool")]
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 6;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 24;
	#endregion

	#region Private Fields
	private readonly Dictionary<GameObject, ObjectPool<GameObject>> m_Pools = new Dictionary<GameObject, ObjectPool<GameObject>>(2);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_Equipment == null)
			m_Equipment = GetComponentInChildren<UnitEquipment>(true);
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
	}
	#endregion

	#region Private Methods
	private void HandleShotFired(AmmoDefinition _ammo)
	{
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
		if (profile == null || !profile.EnableMuzzleFlash)
			return;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : null;
		if (fireOrigin == null)
			return;

		if (!WeaponVfxUtility.IsWithinEffectDistance(fireOrigin.position, profile.MuzzleFlashMaxDistanceMeters))
			return;

		if (!CombatVfxBudgetService.TryAcquire(CombatVfxBudgetService.Category.MuzzleFlash))
			return;

		bool suppressed = WeaponVfxUtility.HasSuppressor(m_WeaponRuntime);
		GameObject prefab = suppressed && profile.SuppressedMuzzleFlashPrefab != null
			? profile.SuppressedMuzzleFlashPrefab
			: profile.UnsuppressedMuzzleFlashPrefab;
		if (prefab == null)
		{
			CombatVfxBudgetService.Release(CombatVfxBudgetService.Category.MuzzleFlash);
			return;
		}

		float scale = suppressed ? profile.SuppressedMuzzleScale : profile.UnsuppressedMuzzleScale;
		float lifetime = suppressed ? profile.SuppressedMuzzleLifetimeSeconds : profile.UnsuppressedMuzzleLifetimeSeconds;
		SpawnEffect(prefab, fireOrigin.position, fireOrigin.rotation, Vector3.one * scale, lifetime);
	}

	private void SpawnEffect(
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

		WeaponVfxUtility.PlayParticleSystems(instance);
		WeaponVfxRuntimeRelease.StartRelease(
			pool,
			instance,
			CombatVfxBudgetService.Category.MuzzleFlash,
			_lifetime,
			_waitForParticles: true);
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
