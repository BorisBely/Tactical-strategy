using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// После выстрела спавнит гильзу из пула. Ищет <see cref="ShellCasingBehaviour"/> через GetComponentInChildren (можно на дочернем объекте).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(57)]
public sealed class UnitWeaponShellEjection : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private Transform m_PoolRoot;
	[SerializeField] private AudioSource m_ImpactAudio;
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 12;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 48;
	#endregion

	#region Private Fields
	private readonly Dictionary<int, ObjectPool<GameObject>> m_Pools = new Dictionary<int, ObjectPool<GameObject>>(8);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_Equipment == null)
			m_Equipment = GetComponentInChildren<UnitEquipment>(true);

		EnsurePoolRoot();
		EnsureImpactAudio();
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

	#region Public Methods
	/// <summary>Выброс гильзы без выстрела (например извлечение патронника при снятии отказа).</summary>
	public void SpawnShellForAmmo(AmmoDefinition _ammo)
	{
		SpawnShellInternal(_ammo);
	}
	#endregion

	#region Private Methods
	private void HandleShotFired(AmmoDefinition _ammo)
	{
		SpawnShellInternal(_ammo);
	}

	private void SpawnShellInternal(AmmoDefinition _ammo)
	{
		if (_ammo == null || !_ammo.HasShellPrefab)
			return;

		GameObject prefab = _ammo.ShellPrefab;
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon == null)
			return;

		Transform barrel = weapon.BarrelTransform;
		if (barrel == null)
			return;

		Transform eject = weapon.ShellEjectTransform;
		Vector3 pos = eject != null ? eject.position : barrel.position;
		Vector3 dir = eject != null ? eject.forward : (-barrel.right);
		float dirLen = dir.sqrMagnitude;
		if (dirLen > 1e-6f)
			dir /= Mathf.Sqrt(dirLen);
		else
			dir = Vector3.right;

		float speed = _ammo.ShellEjectSpeed + Random.Range(-_ammo.ShellEjectSpeedVariance, _ammo.ShellEjectSpeedVariance);
		speed = Mathf.Max(0.1f, speed);
		Vector3 vel = dir * speed + Vector3.up * _ammo.ShellEjectUpSpeed;
		Vector3 angVel = Random.insideUnitSphere * _ammo.ShellAngularVelocity;

		ObjectPool<GameObject> pool = GetOrCreatePool(prefab);
		GameObject shell = pool.Get();
		ShellCasingBehaviour behaviour = shell.GetComponentInChildren<ShellCasingBehaviour>(true);
		if (behaviour == null)
		{
			pool.Release(shell);
			Debug.LogWarning(
				$"{nameof(UnitWeaponShellEjection)}: нет {nameof(ShellCasingBehaviour)} на префабе гильзы (ни на корне, ни на дочерних). Проверь AmmoDefinition → Shell Prefab.",
				this);
			return;
		}

		Quaternion rot = Random.rotationUniform;
		behaviour.ActivateFromPool(pool, shell, m_ImpactAudio, _ammo, pos, rot, vel, angVel);
	}

	private ObjectPool<GameObject> GetOrCreatePool(GameObject _prefab)
	{
		int id = _prefab.GetInstanceID();
		if (m_Pools.TryGetValue(id, out ObjectPool<GameObject> existing))
			return existing;

		Transform poolRoot = m_PoolRoot;
		ObjectPool<GameObject> pool = new ObjectPool<GameObject>(
			createFunc: () => Instantiate(_prefab),
			actionOnGet: go =>
			{
				go.transform.SetParent(null, true);
				go.SetActive(true);
			},
			actionOnRelease: go =>
			{
				go.SetActive(false);
				go.transform.SetParent(poolRoot, false);
				Rigidbody rb = go.GetComponentInChildren<Rigidbody>(true);
				if (rb != null)
				{
					rb.linearVelocity = Vector3.zero;
					rb.angularVelocity = Vector3.zero;
				}
			},
			actionOnDestroy: Destroy,
			collectionCheck: false,
			defaultCapacity: m_DefaultPoolCapacity,
			maxSize: m_MaxPoolSize);

		m_Pools[id] = pool;
		return pool;
	}

	private void EnsurePoolRoot()
	{
		if (m_PoolRoot != null)
			return;

		const string c_Name = "ShellCasingPool";
		Transform child = transform.Find(c_Name);
		if (child == null)
		{
			GameObject go = new GameObject(c_Name);
			go.transform.SetParent(transform, false);
			child = go.transform;
		}

		m_PoolRoot = child;
	}

	private void EnsureImpactAudio()
	{
		if (m_ImpactAudio != null && m_ImpactAudio.transform != transform)
		{
			ConfigureImpactAudio(m_ImpactAudio);
			return;
		}

		const string c_Name = "ShellImpactAudio_Auto";
		Transform audioChild = transform.Find(c_Name);
		if (audioChild == null)
		{
			GameObject go = new GameObject(c_Name);
			go.transform.SetParent(transform, false);
			audioChild = go.transform;
		}

		if (!audioChild.TryGetComponent(out m_ImpactAudio))
			m_ImpactAudio = audioChild.gameObject.AddComponent<AudioSource>();

		ConfigureImpactAudio(m_ImpactAudio);
	}

	private static void ConfigureImpactAudio(AudioSource _source)
	{
		_source.playOnAwake = false;
		_source.spatialBlend = 1f;
		_source.minDistance = 1f;
		_source.maxDistance = 35f;
		_source.rolloffMode = AudioRolloffMode.Linear;
		_source.dopplerLevel = 0f;
	}
	#endregion
}
