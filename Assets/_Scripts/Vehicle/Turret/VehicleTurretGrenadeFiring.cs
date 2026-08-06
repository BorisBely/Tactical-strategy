using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(51)]
public sealed class VehicleTurretGrenadeFiring : MonoBehaviour
{
	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;

	[Header("Projectile")]
	[SerializeField] private GameObject m_ProjectilePrefab;
	[SerializeField, Min(1f)] private float m_MuzzleVelocity = 240f;
	[SerializeField] private int m_PoolCapacity = 12;

	[Header("Muzzle VFX")]
	[SerializeField] private GameObject m_MuzzleFlashPrefab;
	[SerializeField] private float m_MuzzleFlashLifetime = 0.15f;
	[SerializeField] private Vector3 m_MuzzleFlashScale = Vector3.one;

	[Header("Diagnostics")]
	[SerializeField] private bool m_LogMk19Shots = false;

	private Transform m_MuzzleExit;
	private GameObject m_ProjectilePoolRoot;
	private GameObject[] m_ProjectilePool;
	private int m_ProjectileIndex;
	private bool m_Subscribed;

	private void Awake()
	{
		if (m_Bridge == null) TryGetComponent(out m_Bridge);
		if (m_Hierarchy == null) TryGetComponent(out m_Hierarchy);
		if (m_Equipment == null) TryGetComponent(out m_Equipment);
		m_Hierarchy?.EnsureBound();
		if (m_ProjectilePrefab == null)
			m_ProjectilePrefab = Resources.Load<GameObject>("Turret/Shell_40mm_Projectile");
	}

	private void OnEnable() => TrySubscribe();
	private void OnDisable() => TryUnsubscribe();
	private void Update() => TrySubscribe();

	private void TrySubscribe()
	{
		if (m_Subscribed || m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;
		var fc = m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>();
		if (fc == null)
			return;
		fc.ShotFired += HandleShotFired;
		m_Subscribed = true;
	}

	private void TryUnsubscribe()
	{
		if (!m_Subscribed)
			return;
		if (m_Bridge != null && m_Bridge.HasBoundGunner)
			m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>().ShotFired -= HandleShotFired;
		m_Subscribed = false;
	}

	private int m_Mk19ProjectileShotCount;

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_Bridge == null || !m_Bridge.HasBoundGunner)
		{
			LogMk19("ShotFired ignored: no bound gunner");
			return;
		}

		ItemDefinition activeWeapon = m_Equipment != null ? m_Equipment.ActiveWeaponItem : null;
		if (activeWeapon == null || activeWeapon.TurretWeaponVariant != TurretWeaponVariant.Mk19)
			return;

		m_Mk19ProjectileShotCount++;
		string ammoName = _ammo != null ? _ammo.name : "null";
		LogMk19($"ShotFired #{m_Mk19ProjectileShotCount} ammo={ammoName}");

		ResolveMuzzleExit();
		if (m_MuzzleExit == null)
		{
			LogMk19Warning($"Shot #{m_Mk19ProjectileShotCount}: MuzzleExit missing — no projectile");
			return;
		}

		if (m_ProjectilePrefab == null)
		{
			LogMk19Warning($"Shot #{m_Mk19ProjectileShotCount}: projectile prefab missing");
			return;
		}

		SpawnMuzzleFlash();
		FireProjectile();
	}

	private void ResolveMuzzleExit()
	{
		m_Hierarchy?.EnsureBound();
		Transform pitch = m_Hierarchy?.GetActiveWeaponPitch(TurretWeaponVariant.Mk19);
		if (pitch == null)
		{
			m_MuzzleExit = null;
			return;
		}

		VehicleTurretCombatSockets.PrepareMk19PitchRuntime(pitch);
		m_MuzzleExit = VehicleTurretCombatSockets.FindMuzzleExit(pitch);
	}

	private void SpawnMuzzleFlash()
	{
		if (m_MuzzleFlashPrefab == null || m_MuzzleExit == null)
			return;

		GameObject fx = Instantiate(m_MuzzleFlashPrefab, m_MuzzleExit.position, m_MuzzleExit.rotation);
		fx.transform.localScale = m_MuzzleFlashScale;
		Destroy(fx, m_MuzzleFlashLifetime);
	}

	private void FireProjectile()
	{
		if (m_ProjectilePrefab == null || m_MuzzleExit == null)
			return;

		EnsureProjectilePool();

		GameObject proj = m_ProjectilePool[m_ProjectileIndex % m_PoolCapacity];
		m_ProjectileIndex++;

		if (proj == null)
		{
			proj = Instantiate(m_ProjectilePrefab, m_ProjectilePoolRoot.transform);
			m_ProjectilePool[(m_ProjectileIndex - 1) % m_PoolCapacity] = proj;
		}

		proj.transform.SetParent(null, true);
		proj.transform.position = m_MuzzleExit.position;
		proj.transform.rotation = m_MuzzleExit.rotation;
		proj.SetActive(true);

		Vector3 launchVelocity = m_MuzzleExit.forward * m_MuzzleVelocity;
		if (proj.TryGetComponent(out VehicleTurretGrenadeProjectile grenade))
		{
			grenade.ConfigureDiagnostics(m_Mk19ProjectileShotCount);
			grenade.SetVelocity(launchVelocity);
		}
		else if (proj.TryGetComponent(out Rigidbody rb))
		{
			rb.isKinematic = false;
			rb.useGravity = true;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			rb.linearVelocity = launchVelocity;
			rb.angularVelocity = Random.insideUnitSphere * 2f;
		}

		LogMk19(
			$"Projectile #{m_Mk19ProjectileShotCount} pool={((m_ProjectileIndex - 1) % m_PoolCapacity) + 1}/{m_PoolCapacity} " +
			$"pos={m_MuzzleExit.position} vel={launchVelocity.magnitude:F0}m/s");
	}

	private void EnsureProjectilePool()
	{
		if (m_ProjectilePool != null)
			return;

		m_ProjectilePoolRoot = new GameObject("MK19_ProjectilePool") { transform = { parent = transform } };
		m_ProjectilePoolRoot.SetActive(false);
		m_ProjectilePool = new GameObject[m_PoolCapacity];
		for (int i = 0; i < m_PoolCapacity; i++)
			m_ProjectilePool[i] = Instantiate(m_ProjectilePrefab, m_ProjectilePoolRoot.transform);
	}

	private void OnDrawGizmosSelected()
	{
		ResolveMuzzleExit();
		if (m_MuzzleExit == null)
			return;

		Vector3 origin = m_MuzzleExit.position;
		Vector3 dir = m_MuzzleExit.forward;
		Vector3 vel = dir * m_MuzzleVelocity;
		Vector3 grav = Physics.gravity;
		float dt = 0.05f;
		int steps = 80;
		Vector3 prev = origin;

		Gizmos.color = Color.green;
		for (int i = 1; i <= steps; i++)
		{
			float t = i * dt;
			Vector3 point = origin + vel * t + 0.5f * grav * t * t;
			Gizmos.DrawLine(prev, point);
			prev = point;
		}

		Gizmos.color = Color.red;
		Gizmos.DrawRay(origin, dir * 0.5f);
		Gizmos.DrawWireSphere(m_MuzzleExit.position, 0.05f);
	}

	private void LogMk19(string _message)
	{
		if (!m_LogMk19Shots)
			return;
		Debug.Log($"[Mk19Grenade] {name} {_message}", this);
	}

	private void LogMk19Warning(string _message)
	{
		if (!m_LogMk19Shots)
			return;
		Debug.LogWarning($"[Mk19Grenade] {name} {_message}", this);
	}
}
