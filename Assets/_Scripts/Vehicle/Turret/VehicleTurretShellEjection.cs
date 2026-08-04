using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(49)]
public sealed class VehicleTurretShellEjection : MonoBehaviour
{
	[SerializeField] private VehicleTurretGunnerBridge m_Bridge;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;
	[SerializeField] private VehicleTurretBeltFeed m_BeltFeed;
	[SerializeField] private int m_PoolCapacity = 24;

	[Header("MK19 Shell Drop Sound (M2 shell drop clips)")]
	[SerializeField] private AudioClip[] m_Mk19ShellDropClips;
	[SerializeField, Range(0f, 1f)] private float m_ShellDropVolume = 0.25f;

	private Transform m_ShellEjectPoint;
	private Transform m_BeltEjectPoint;
	private Transform m_Mk19ShellEjectPoint;
	private GameObject m_ShellPoolRoot;
	private GameObject m_BeltPoolRoot;
	private GameObject m_Mk19ShellPoolRoot;
	private int m_ShellIndex;
	private int m_BeltIndex;
	private int m_Mk19ShellIndex;
	private GameObject[] m_ShellPool;
	private GameObject[] m_BeltPool;
	private GameObject[] m_Mk19ShellPool;
	private bool m_Subscribed;
	private GameObject m_ShellPrefab;
	private GameObject m_BeltLinkPrefab;
	private GameObject m_Mk19ShellPrefab;

	private void Awake()
	{
		if (m_Bridge == null) TryGetComponent(out m_Bridge);
		if (m_Hierarchy == null) TryGetComponent(out m_Hierarchy);
		if (m_Equipment == null) TryGetComponent(out m_Equipment);
		if (m_BeltFeed == null) TryGetComponent(out m_BeltFeed);
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
		if (GamePauseState.IsSimulationPaused)
			return;
		if (m_Bridge == null || !m_Bridge.HasBoundGunner)
			return;
		ItemDefinition activeWeapon = m_Equipment != null ? m_Equipment.ActiveWeaponItem : null;
		bool isMk19 = activeWeapon != null && activeWeapon.TurretWeaponVariant == TurretWeaponVariant.Mk19;

		if (isMk19)
		{
			EnsureMk19EjectPointResolved();
			EnsureMk19ShellPool(_ammo);
			EjectMk19Shell(_ammo);
		}
		else
		{
			EnsureEjectPointsResolved();
			EnsurePoolsFromAmmo(_ammo);
			EjectShell(_ammo);
			if (m_BeltFeed == null)
				EjectBeltLink(_ammo);
		}
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

	private void EnsureMk19EjectPointResolved()
	{
		if (m_Mk19ShellEjectPoint != null)
			return;
		m_Hierarchy?.EnsureBound();
		Transform pitch = m_Hierarchy?.GetActiveWeaponPitch(TurretWeaponVariant.Mk19);
		if (pitch == null)
			return;
		VehicleTurretCombatSockets.EnsureMissingMk19SocketsOnPitch(pitch);
		m_Mk19ShellEjectPoint = VehicleTurretCombatSockets.FindMk19ShellEject(pitch);
	}

	private void EnsureMk19ShellPool(AmmoDefinition _ammo)
	{
		if (_ammo == null || m_Mk19ShellPool != null)
			return;
		if (!_ammo.HasShellPrefab)
			return;
		m_Mk19ShellPrefab = _ammo.ShellPrefab;
		m_Mk19ShellPoolRoot = new GameObject("ShellPool_MK19") { transform = { parent = transform } };
		m_Mk19ShellPoolRoot.SetActive(false);
		m_Mk19ShellPool = new GameObject[m_PoolCapacity];
		for (int i = 0; i < m_PoolCapacity; i++)
			m_Mk19ShellPool[i] = Instantiate(m_Mk19ShellPrefab, m_Mk19ShellPoolRoot.transform);
	}

	private void EjectMk19Shell(AmmoDefinition _ammo)
	{
		if (m_Mk19ShellEjectPoint == null || m_Mk19ShellPool == null)
			return;
		var go = m_Mk19ShellPool[m_Mk19ShellIndex % m_PoolCapacity];
		m_Mk19ShellIndex++;
		go.transform.SetParent(null, true);
		go.transform.position = m_Mk19ShellEjectPoint.position;
		go.transform.rotation = m_Mk19ShellEjectPoint.rotation;
		go.SetActive(true);

		if (go.TryGetComponent(out Rigidbody rb))
		{
			rb.useGravity = true;
			rb.isKinematic = false;
			rb.linearVelocity = Vector3.down * 0.25f + Random.insideUnitSphere * 0.05f;
			rb.angularVelocity = Random.insideUnitSphere * 2f;
		}

		if (!go.TryGetComponent(out AudioSource audioSource))
			audioSource = go.AddComponent<AudioSource>();
		audioSource.playOnAwake = false;
		audioSource.spatialBlend = 1f;
		audioSource.maxDistance = 15f;
		audioSource.rolloffMode = AudioRolloffMode.Linear;

		var casing = go.GetComponent<VehicleTurretShellCasingBehaviour>();
		if (casing == null)
			casing = go.AddComponent<VehicleTurretShellCasingBehaviour>();
		casing.ConfigureImpactVolume(m_ShellDropVolume);

		if (m_Mk19ShellDropClips != null && m_Mk19ShellDropClips.Length > 0)
		{
			AudioClip clip = m_Mk19ShellDropClips[Random.Range(0, m_Mk19ShellDropClips.Length)];
			if (clip != null)
				audioSource.PlayOneShot(clip, m_ShellDropVolume);
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
