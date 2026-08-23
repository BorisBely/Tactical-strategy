using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class VehicleTurretGrenadeProjectile : MonoBehaviour
{
	#region Constants
	private const float c_RpgExplosionVfxScale = 2.15f;
	private const float c_RpgExplosionMaxDistanceMeters = 600f;
	private const float c_RpgExplosionAudioMaxDistance = 600f;
	private const float c_RpgExplosionLifetimeSeconds = 5.5f;
	private const string c_RocketLauncherDataResourcesPath = "Combat/RocketLauncherData";
	#endregion

	[Header("Explosion")]
	[SerializeField] private GameObject m_ExplosionPrefab;
	[SerializeField] private float m_ExplosionLifetime = c_RpgExplosionLifetimeSeconds;
	[SerializeField, Min(10f)] private float m_ExplosionMaxDistanceMeters = c_RpgExplosionMaxDistanceMeters;
	[SerializeField, Min(0.01f)] private float m_ExplosionVfxScale = c_RpgExplosionVfxScale;
	[SerializeField, Min(5f)] private float m_ExplosionAudioMaxDistance = c_RpgExplosionAudioMaxDistance;

	[Header("Flight")]
	[SerializeField] private AudioClip[] m_FlightLoopClips;
	[SerializeField, Min(0.1f)] private float m_FlightSoundVolume = 0.4f;
	[SerializeField, Min(0.1f)] private float m_MaxLifetimeSeconds = 25f;

	public float MaxLifetimeSeconds => m_MaxLifetimeSeconds;

	[Header("Impact")]
	[SerializeField] private AudioClip[] m_ExplosionSoundClips;
	[SerializeField, Min(0.1f)] private float m_ExplosionSoundVolume = 1f;
	[SerializeField, Min(0f)] private float m_MinAirborneTime = 0.15f;
	[SerializeField] private LayerMask m_IgnoreCollisionLayers;

	[Header("Smoke (optional)")]
	[SerializeField] private GameObject m_SmokeCloudPrefab;
	[SerializeField, Min(10f)] private float m_SmokeMaxDistanceMeters = 250f;
	[SerializeField, Min(1f)] private float m_SmokeLifetimeSeconds = 12f;

	[Header("Grenade Data Reference")]
	[SerializeField] private GrenadeThrowData m_GrenadeThrowData;
	[SerializeField] private ItemDefinition m_FragGrenadeDefinition;
	[SerializeField] private RocketLauncherData m_RocketLauncherData;

	private float m_SpawnedTime;
	private bool m_HasExploded;
	private Rigidbody m_Rigidbody;
	private AudioSource m_FlightAudioSource;

	private Vector3 m_LaunchPosition;
	private Vector3 m_LaunchVelocity;
	private float m_LaunchTime;
	private bool m_LaunchDataCaptured;
	private int m_DiagnosticShotIndex;
	private Transform m_Shooter;

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_FlightAudioSource = gameObject.AddComponent<AudioSource>();
		m_FlightAudioSource.playOnAwake = false;
		m_FlightAudioSource.loop = true;
		m_FlightAudioSource.spatialBlend = 1f;
		m_FlightAudioSource.maxDistance = 150f;
		m_FlightAudioSource.rolloffMode = AudioRolloffMode.Linear;
	}

	private void OnEnable()
	{
		m_SpawnedTime = Time.time;
		m_LaunchTime = Time.time;
		m_HasExploded = false;
		m_LaunchDataCaptured = false;
		m_Shooter = null;

		m_LaunchPosition = transform.position;

		Collider[] colliders = GetComponentsInChildren<Collider>();
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = true;

		Renderer[] renderers = GetComponentsInChildren<Renderer>();
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].enabled = true;

		if (m_Rigidbody == null)
			m_Rigidbody = GetComponent<Rigidbody>();

		PrepareRigidbodyForFlight();

		if (m_FlightAudioSource == null)
		{
			m_FlightAudioSource = GetComponent<AudioSource>();
			if (m_FlightAudioSource == null)
				m_FlightAudioSource = gameObject.AddComponent<AudioSource>();
			m_FlightAudioSource.playOnAwake = false;
			m_FlightAudioSource.loop = true;
			m_FlightAudioSource.spatialBlend = 1f;
			m_FlightAudioSource.maxDistance = 150f;
			m_FlightAudioSource.rolloffMode = AudioRolloffMode.Linear;
		}

		StartFlightSound();
	}

	private void OnDisable()
	{
		StopFlightSound();
	}

	private void Update()
	{
		if (m_HasExploded)
			return;
		if (!m_LaunchDataCaptured && m_Rigidbody != null)
		{
			m_LaunchPosition = transform.position;
			m_LaunchVelocity = m_Rigidbody.linearVelocity;
			m_LaunchTime = Time.time;
			m_LaunchDataCaptured = true;
		}
		if (Time.time - m_SpawnedTime >= m_MaxLifetimeSeconds)
		{
			Debug.Log(
				$"[Mk19Grenade] shot#{m_DiagnosticShotIndex} lifetime timeout at {transform.position}",
				this);
			Detonate();
		}
	}

	private void OnCollisionEnter(Collision _collision)
	{
		if (m_HasExploded)
			return;
		if (Time.time - m_SpawnedTime < m_MinAirborneTime)
			return;
		if (m_IgnoreCollisionLayers != 0 && _collision.gameObject != null)
		{
			if ((m_IgnoreCollisionLayers.value & (1 << _collision.gameObject.layer)) != 0)
				return;
		}

		Detonate();
	}

	public void BindShooter(Transform _shooter)
	{
		m_Shooter = _shooter;
	}

	public void ConfigureDiagnostics(int _shotIndex)
	{
		m_DiagnosticShotIndex = _shotIndex;
	}

	private void Detonate()
	{
		m_HasExploded = true;
		StopFlightSound();
		Vector3 pos = transform.position;

		GameObject prefab = m_ExplosionPrefab;
		if (m_GrenadeThrowData != null)
		{
			GameObject dataPrefab = m_GrenadeThrowData.PickExplosionPrefab(m_FragGrenadeDefinition);
			if (dataPrefab != null)
				prefab = dataPrefab;
		}

		// 40 мм MK19: дальность/масштаб задаются на префабе снаряда (RPG-видимость в RTS).
		// GrenadeThrowData — тип VFX, yaw, звук и lifetime осколочной гранаты.
		float scale = m_ExplosionVfxScale * Random.Range(0.97f, 1.03f);
		float yaw = m_GrenadeThrowData != null
			? m_GrenadeThrowData.GetExplosionVfxYawOffsetDegrees(m_FragGrenadeDefinition) + Random.Range(-8f, 8f)
			: Random.Range(-8f, 8f);
		Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

		float maxDistance = m_ExplosionMaxDistanceMeters;
		float lifetime = m_GrenadeThrowData != null
			? m_GrenadeThrowData.GetDetonationVfxLifetimeSeconds(m_FragGrenadeDefinition)
			: m_ExplosionLifetime;

		CombatVfxBudgetService.TrySpawnExplosion(
			prefab,
			pos,
			rotation,
			Vector3.one * scale,
			maxDistance,
			lifetime);

		if (m_SmokeCloudPrefab != null)
		{
			CombatVfxBudgetService.TrySpawnSmokeCloud(
				m_SmokeCloudPrefab,
				pos,
				Quaternion.identity,
				Vector3.one,
				m_SmokeMaxDistanceMeters,
				m_SmokeLifetimeSeconds);
		}

		PlayExplosionSound(pos);
		WorldSoundHub.PublishExplosion(m_Shooter, pos);

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.isKinematic = true;
		}

		Collider[] colliders = GetComponentsInChildren<Collider>();
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		Renderer[] renderers = GetComponentsInChildren<Renderer>();
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].enabled = false;

		gameObject.SetActive(false);
	}

	private void PlayExplosionSound(Vector3 _position)
	{
		AudioClip clip = ResolveExplosionClip();
		if (clip == null)
			return;

		if (clip.loadState != AudioDataLoadState.Loaded)
			clip.LoadAudioData();

		float volume = m_ExplosionSoundVolume;
		if (m_GrenadeThrowData != null)
			volume = Mathf.Max(volume, m_GrenadeThrowData.GetExplosionVolume(m_FragGrenadeDefinition));

		float audioMaxDistance = Mathf.Max(m_ExplosionAudioMaxDistance, m_ExplosionMaxDistanceMeters);
		if (m_GrenadeThrowData != null)
			audioMaxDistance = Mathf.Max(audioMaxDistance, m_GrenadeThrowData.ExplosionAudioMaxDistance);
		if (m_RocketLauncherData != null)
			audioMaxDistance = Mathf.Max(audioMaxDistance, m_RocketLauncherData.ExplosionAudioMaxDistance);

		// Same voice path as RPG explosions — high priority, no NonFire attenuation.
		if (CombatAudioManager.TryPlayRocketLauncher(clip, _position, volume, audioMaxDistance))
			return;

		if (CombatAudioManager.TryPlayExplosion(clip, _position, volume, audioMaxDistance))
			return;

		// Guaranteed audible fallback for elevated RTS cameras / full voice pool.
		AudioSource.PlayClipAtPoint(clip, _position, Mathf.Clamp01(volume));
	}

	private AudioClip ResolveExplosionClip()
	{
		// Prefab clips first — wired to RPG-scale explosions for 40mm HE.
		if (m_ExplosionSoundClips != null && m_ExplosionSoundClips.Length > 0)
		{
			int start = Random.Range(0, m_ExplosionSoundClips.Length);
			for (int i = 0; i < m_ExplosionSoundClips.Length; i++)
			{
				AudioClip clip = m_ExplosionSoundClips[(start + i) % m_ExplosionSoundClips.Length];
				if (clip != null)
					return clip;
			}
		}

		EnsureRocketLauncherData();
		if (m_RocketLauncherData != null &&
		    m_RocketLauncherData.TryPickExplosionClip(RocketLauncherType.Rpg7, out AudioClip rpgClip) &&
		    rpgClip != null)
			return rpgClip;

		if (m_GrenadeThrowData != null &&
		    m_GrenadeThrowData.TryPickExplosionSound(m_FragGrenadeDefinition, out AudioClip dataClip) &&
		    dataClip != null)
			return dataClip;

		return null;
	}

	private void EnsureRocketLauncherData()
	{
		if (m_RocketLauncherData != null)
			return;
		m_RocketLauncherData = Resources.Load<RocketLauncherData>(c_RocketLauncherDataResourcesPath);
	}

	private void StartFlightSound()
	{
		if (m_FlightLoopClips == null || m_FlightLoopClips.Length == 0)
			return;
		AudioClip clip = m_FlightLoopClips[Random.Range(0, m_FlightLoopClips.Length)];
		if (clip == null)
			return;
		m_FlightAudioSource.clip = clip;
		m_FlightAudioSource.volume = m_FlightSoundVolume;
		m_FlightAudioSource.Play();
	}

	private void StopFlightSound()
	{
		if (m_FlightAudioSource != null && m_FlightAudioSource.isPlaying)
			m_FlightAudioSource.Stop();
	}

	public void SetVelocity(Vector3 _velocity)
	{
		if (m_Rigidbody == null)
			m_Rigidbody = GetComponent<Rigidbody>();
		if (m_Rigidbody == null)
			return;

		PrepareRigidbodyForFlight();
		m_Rigidbody.linearVelocity = _velocity;
		m_Rigidbody.angularVelocity = Random.insideUnitSphere * 2f;
		m_LaunchVelocity = _velocity;
		m_LaunchPosition = transform.position;
		m_LaunchTime = Time.time;
		m_LaunchDataCaptured = true;
	}

	private void PrepareRigidbodyForFlight()
	{
		if (m_Rigidbody == null)
			return;

		m_Rigidbody.isKinematic = false;
		m_Rigidbody.useGravity = true;
		m_Rigidbody.detectCollisions = true;
		m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;
	}

	private void OnDrawGizmos()
	{
		if (!Application.isPlaying || m_HasExploded || !isActiveAndEnabled || !m_LaunchDataCaptured)
			return;

		Vector3 currentPos = transform.position;
		Vector3 grav = Physics.gravity;
		float elapsed = Time.time - m_LaunchTime;
		float dt = 0.05f;
		int steps = 60;
		float totalTime = m_MaxLifetimeSeconds;

		Vector3 prevPoint = currentPos;

		Gizmos.color = Color.red;
		for (int i = 1; i <= steps; i++)
		{
			float t = i * dt;
			if (t + elapsed > totalTime)
				break;

			Vector3 point = m_LaunchPosition
				+ m_LaunchVelocity * (elapsed + t)
				+ 0.5f * grav * (elapsed + t) * (elapsed + t);

			Gizmos.DrawLine(prevPoint, point);
			prevPoint = point;
		}

		Gizmos.color = Color.yellow;
		Gizmos.DrawWireSphere(currentPos, 0.08f);
	}

}
