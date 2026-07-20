using UnityEngine;

/// <summary>
/// Летящая ракета гранатомёта: баллистический полёт (гравитация), попадание, VFX взрыва.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class RocketProjectile : MonoBehaviour
{
	#region Private Fields
	private Rigidbody m_Rigidbody;
	private RocketLauncherData m_Data;
	private RocketLauncherType m_LauncherType = RocketLauncherType.Rpg7;
	private float m_LifetimeSeconds = 8f;
	private float m_Gravity = 9.81f;
	private float m_SpawnTime;
	private bool m_Launched;
	private bool m_HasExploded;
	private bool m_FlybyPlayed;
	private Vector3 m_SpawnPosition;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		if (m_Rigidbody != null)
		{
			m_Rigidbody.useGravity = false;
			m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
		}
	}

	private void FixedUpdate()
	{
		if (!m_Launched || m_HasExploded || m_Rigidbody == null)
			return;

		m_Rigidbody.AddForce(Vector3.down * m_Gravity, ForceMode.Acceleration);

		Vector3 velocity = m_Rigidbody.linearVelocity;
		if (velocity.sqrMagnitude > 0.25f)
			transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
	}

	private void Update()
	{
		if (!m_Launched || m_HasExploded)
			return;

		if (Time.time - m_SpawnTime >= m_LifetimeSeconds)
		{
			Detonate(transform.position);
			return;
		}

		TryPlayFlybyAudio();
	}

	private void OnCollisionEnter(Collision _collision)
	{
		if (!m_Launched || m_HasExploded)
			return;

		Vector3 point = _collision != null && _collision.contactCount > 0
			? _collision.GetContact(0).point
			: transform.position;
		Detonate(point);
	}
	#endregion

	#region Public Methods
	public void Launch(
		Vector3 _direction,
		float _speed,
		float _lifetimeSeconds,
		float _gravity,
		float _linearDamping,
		RocketLauncherData _data,
		GameObject _ignoreCollisionsWith,
		RocketLauncherType _launcherType)
	{
		m_Data = _data;
		m_LauncherType = _launcherType;
		m_LifetimeSeconds = Mathf.Max(0.5f, _lifetimeSeconds);
		m_Gravity = Mathf.Max(0f, _gravity);
		m_SpawnTime = Time.time;
		m_SpawnPosition = transform.position;
		m_Launched = true;
		m_HasExploded = false;
		m_FlybyPlayed = false;

		EnsureImpactCollider();
		IgnoreCollisionsWith(_ignoreCollisionsWith);

		Vector3 dir = _direction.sqrMagnitude > 0.0001f ? _direction.normalized : transform.forward;
		if (dir.sqrMagnitude > 0.0001f)
			transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

		if (m_Rigidbody == null)
			m_Rigidbody = GetComponent<Rigidbody>();

		if (m_Rigidbody != null)
		{
			m_Rigidbody.useGravity = false;
			m_Rigidbody.isKinematic = false;
			m_Rigidbody.detectCollisions = true;
			m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			m_Rigidbody.linearDamping = Mathf.Max(0f, _linearDamping);
			m_Rigidbody.angularDamping = 0.05f;
			m_Rigidbody.linearVelocity = dir * Mathf.Max(1f, _speed);
			m_Rigidbody.angularVelocity = Vector3.zero;
		}
	}
	#endregion

	#region Private Methods
	private void Detonate(Vector3 _position)
	{
		if (m_HasExploded)
			return;

		m_HasExploded = true;
		RocketLauncherAudioUtility.PlayExplosion(m_Data, m_LauncherType, _position);
		SpawnExplosionVfx(_position);
		Destroy(gameObject);
	}

	private void TryPlayFlybyAudio()
	{
		if (m_FlybyPlayed || m_Data == null)
			return;

		Vector3 listenerPosition = GetListenerPosition();
		if ((m_SpawnPosition - listenerPosition).sqrMagnitude <
		    m_Data.FlybyMinSpawnDistanceMeters * m_Data.FlybyMinSpawnDistanceMeters)
			return;

		float distance = Vector3.Distance(transform.position, listenerPosition);
		float radius = m_Data.FlybyRadiusMeters;
		if (distance > radius)
			return;

		float normalizedProximity = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, radius));
		float volume = m_Data.FlybyVolume * Mathf.Lerp(0.18f, 1f, normalizedProximity);
		float pitch = Random.Range(0.94f, 1.06f);
		RocketLauncherAudioUtility.PlayFlyby(m_Data, volume, pitch);
		m_FlybyPlayed = true;
	}

	private static Vector3 GetListenerPosition()
	{
		Camera mainCamera = Camera.main;
		if (mainCamera != null)
			return mainCamera.transform.position;

		AudioListener listener = FindAnyObjectByType<AudioListener>();
		return listener != null ? listener.transform.position : Vector3.zero;
	}

	private void SpawnExplosionVfx(Vector3 _position)
	{
		if (m_Data == null || m_Data.ExplosionPrefab == null)
			return;

		float yaw = m_Data.ExplosionVfxYawOffsetDegrees + Random.Range(-10f, 10f);
		Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
		float scale = m_Data.ExplosionVfxScale * Random.Range(0.97f, 1.04f);

		CombatVfxBudgetService.TrySpawnExplosion(
			m_Data.ExplosionPrefab,
			_position,
			rotation,
			Vector3.one * scale,
			m_Data.ExplosionMaxDistanceMeters,
			m_Data.ExplosionVfxDurationSeconds);
	}

	private void EnsureImpactCollider()
	{
		Collider[] existing = GetComponentsInChildren<Collider>(true);
		bool hasEnabled = false;
		for (int i = 0; i < existing.Length; i++)
		{
			if (existing[i] == null)
				continue;

			existing[i].enabled = true;
			existing[i].isTrigger = false;
			hasEnabled = true;
		}

		if (hasEnabled)
			return;

		CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
		capsule.direction = 2; // Z-forward
		capsule.radius = 0.06f;
		capsule.height = 0.45f;
		capsule.center = new Vector3(0f, 0f, 0.1f);
		capsule.isTrigger = false;
	}

	private void IgnoreCollisionsWith(GameObject _other)
	{
		if (_other == null)
			return;

		Collider[] myColliders = GetComponentsInChildren<Collider>(true);
		Collider[] otherColliders = _other.GetComponentsInChildren<Collider>(true);

		for (int i = 0; i < myColliders.Length; i++)
		{
			for (int j = 0; j < otherColliders.Length; j++)
			{
				if (myColliders[i] != null && otherColliders[j] != null)
					Physics.IgnoreCollision(myColliders[i], otherColliders[j], true);
			}
		}
	}
	#endregion
}
