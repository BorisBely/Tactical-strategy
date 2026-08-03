using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(49)]
public sealed class VehicleTurretShellEjection : MonoBehaviour
{
	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private int m_PoolCapacity = 24;

	private Transform m_ShellEjectPoint;
	private Transform m_BeltEjectPoint;
	private GameObject m_ShellPoolRoot;
	private GameObject m_BeltPoolRoot;
	private int m_ShellIndex;
	private int m_BeltIndex;
	private GameObject[] m_ShellPool;
	private GameObject[] m_BeltPool;
	private bool m_Subscribed;
	private GameObject m_ShellPrefab;
	private GameObject m_BeltLinkPrefab;

	private void Awake()
	{
		if (m_Bridge == null) TryGetComponent(out m_Bridge);
		if (m_Hierarchy == null) TryGetComponent(out m_Hierarchy);
		m_Hierarchy?.EnsureBound();
		ResolveEjectPoints();
	}

	private void OnEnable() => TrySubscribe();
	private void OnDisable() => TryUnsubscribe();
	private void Update() => TrySubscribe();

	private void TrySubscribe()
	{
		if (m_Subscribed || m_Bridge == null || !m_Bridge.HasBoundGunner) return;
		var fc = m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>();
		if (fc == null) return;
		fc.ShotFired += HandleShotFired;
		m_Subscribed = true;
	}

	private void TryUnsubscribe()
	{
		if (!m_Subscribed) return;
		if (m_Bridge != null && m_Bridge.HasBoundGunner)
			m_Bridge.BoundGunner.GetComponent<UnitWeaponFireController>().ShotFired -= HandleShotFired;
		m_Subscribed = false;
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		EnsureEjectPointsResolved();
		EnsurePoolsFromAmmo(_ammo);
		EjectShell(_ammo);
		EjectBeltLink(_ammo);
	}

	private void EnsurePoolsFromAmmo(AmmoDefinition _ammo)
	{
		if (_ammo == null) return;
		if (m_ShellPool == null && _ammo.HasShellPrefab)
		{
			m_ShellPrefab = _ammo.ShellPrefab;
			m_ShellPoolRoot = new GameObject("ShellPool_M2") { transform = { parent = transform } };
			m_ShellPoolRoot.SetActive(false);
			m_ShellPool = new GameObject[m_PoolCapacity];
			for (int i = 0; i < m_PoolCapacity; i++)
				m_ShellPool[i] = Instantiate(m_ShellPrefab, m_ShellPoolRoot.transform);
		}
		if (m_BeltPool == null && _ammo.HasBeltLinkPrefab)
		{
			m_BeltLinkPrefab = _ammo.BeltLinkPrefab;
			m_BeltPoolRoot = new GameObject("BeltPool_M2") { transform = { parent = transform } };
			m_BeltPoolRoot.SetActive(false);
			m_BeltPool = new GameObject[m_PoolCapacity];
			for (int i = 0; i < m_PoolCapacity; i++)
				m_BeltPool[i] = Instantiate(m_BeltLinkPrefab, m_BeltPoolRoot.transform);
		}
	}

	private void EjectShell(AmmoDefinition _ammo)
	{
		if (m_ShellEjectPoint == null || m_ShellPool == null) return;
		var go = m_ShellPool[m_ShellIndex % m_PoolCapacity];
		m_ShellIndex++;
		go.transform.SetParent(null, true);
		go.transform.position = m_ShellEjectPoint.position;
		go.transform.rotation = m_ShellEjectPoint.rotation;
		go.SetActive(true);
		if (go.TryGetComponent(out Rigidbody rb))
		{
			rb.useGravity = true;
			rb.isKinematic = false;
			// M2: гильза вываливается из окна под гравитацией, без бокового «выстрела».
			float dropSpeed = _ammo != null ? Mathf.Clamp(_ammo.ShellEjectUpSpeed * 0.15f, 0.1f, 0.45f) : 0.25f;
			rb.linearVelocity = Vector3.down * dropSpeed + Random.insideUnitSphere * 0.08f;
			rb.angularVelocity = Random.insideUnitSphere * (_ammo != null ? _ammo.ShellAngularVelocity * 0.35f : 3f);
		}
	}

	private void EjectBeltLink(AmmoDefinition _ammo)
	{
		if (m_BeltEjectPoint == null || m_BeltPool == null) return;
		var go = m_BeltPool[m_BeltIndex % m_PoolCapacity];
		m_BeltIndex++;
		go.transform.SetParent(null, true);
		go.transform.position = m_BeltEjectPoint.position;
		go.transform.rotation = m_BeltEjectPoint.rotation;
		go.SetActive(true);
		if (go.TryGetComponent(out Rigidbody rb))
		{
			float speed = _ammo != null ? _ammo.BeltLinkEjectSpeed : 1.5f;
			rb.linearVelocity = m_BeltEjectPoint.right * speed * 0.3f + Vector3.down * 0.5f;
			rb.angularVelocity = Random.insideUnitSphere * (_ammo != null ? _ammo.BeltLinkAngularVelocity : 4f);
		}
	}

	private void EnsureEjectPointsResolved()
	{
		if (m_ShellEjectPoint != null && m_BeltEjectPoint != null)
			return;
		ResolveEjectPoints();
	}

	private void ResolveEjectPoints()
	{
		m_Hierarchy?.EnsureBound();
		Transform pitch = m_Hierarchy?.GetActiveWeaponPitch(TurretWeaponVariant.Browning127);
		if (pitch == null)
			return;

		VehicleTurretCombatSockets.PrepareM2PitchRuntime(pitch);
		m_ShellEjectPoint = VehicleTurretCombatSockets.FindShellEject(pitch);
		m_BeltEjectPoint = VehicleTurretCombatSockets.FindBeltEject(pitch);
	}
}
