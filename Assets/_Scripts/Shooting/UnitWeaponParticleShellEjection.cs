using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Particle-выброс гильзы по <see cref="WeaponVfxProfile"/>; позиция и ориентация берутся с <see cref="EquippedWeapon"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponParticleShellEjection : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;

	[Header("Pool")]
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 8;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 32;
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
		if (profile == null || !profile.UseParticleShellEjection || profile.ShellParticlePrefab == null)
			return;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (!WeaponVfxUtility.TryGetShellEjectionPose(weapon, out Vector3 position, out Vector3 direction))
			return;

		Quaternion rotation = WeaponVfxUtility.BuildParticleShellRotation(profile, direction);

		GameObject prefab = profile.ShellParticlePrefab;
		GameObject instance = GetOrCreatePool(prefab).Get();
		instance.transform.SetPositionAndRotation(position, rotation);
		instance.transform.localScale = Vector3.one * profile.ShellParticleScale;
		WeaponVfxUtility.PlayShellParticles(instance);
		StartCoroutine(ReleaseAfter(prefab, instance, profile.ShellParticleLifetimeSeconds));
	}

	private ObjectPool<GameObject> GetOrCreatePool(GameObject _prefab)
	{
		if (m_Pools.TryGetValue(_prefab, out ObjectPool<GameObject> existing))
			return existing;

		ObjectPool<GameObject> pool = new ObjectPool<GameObject>(
			createFunc: () => CreateShellInstance(_prefab),
			actionOnGet: go => go.SetActive(true),
			actionOnRelease: go => go.SetActive(false),
			actionOnDestroy: Destroy,
			collectionCheck: false,
			defaultCapacity: m_DefaultPoolCapacity,
			maxSize: m_MaxPoolSize);

		m_Pools.Add(_prefab, pool);
		return pool;
	}

	private static GameObject CreateShellInstance(GameObject _prefab)
	{
		GameObject instance = Instantiate(_prefab);
		WeaponVfxUtility.PrepareShellParticleInstance(instance);
		instance.SetActive(false);
		return instance;
	}

	private IEnumerator ReleaseAfter(GameObject _prefab, GameObject _instance, float _maxLifetimeSeconds)
	{
		yield return null;

		float elapsed = 0f;
		while (_instance != null && elapsed < _maxLifetimeSeconds)
		{
			if (!WeaponVfxUtility.IsParticleRootAlive(_instance))
				break;

			elapsed += Time.deltaTime;
			yield return null;
		}

		if (_instance != null)
			GetOrCreatePool(_prefab).Release(_instance);
	}
	#endregion
}
