using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class VehicleTurretShellCasingBehaviour : MonoBehaviour
{
	[SerializeField] private AudioClip[] m_VehicleImpactClips;
	[SerializeField] private AudioClip[] m_GroundImpactClips;
	[SerializeField, Min(0.01f)] private float m_LifetimeAfterImpact = 2f;
	[SerializeField, Min(0.1f)] private float m_MinAirborneSeconds = 0.15f;

	private bool m_HasPlayedImpact;
	private float m_SpawnedTime;
	private float m_ReleaseTime = -1f;
	private Rigidbody m_Rigidbody;
	private AudioSource m_AudioSource;

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_AudioSource = GetComponent<AudioSource>();
		if (m_AudioSource == null)
			m_AudioSource = gameObject.AddComponent<AudioSource>();
		m_AudioSource.playOnAwake = false;
		m_AudioSource.spatialBlend = 1f;
		m_AudioSource.maxDistance = 15f;
		m_AudioSource.rolloffMode = AudioRolloffMode.Linear;
	}

	private void OnEnable()
	{
		m_HasPlayedImpact = false;
		m_SpawnedTime = Time.time;
		m_ReleaseTime = -1f;
	}

	private void Update()
	{
		if (m_ReleaseTime > 0f && Time.time >= m_ReleaseTime)
			gameObject.SetActive(false);
	}

	private void OnCollisionEnter(Collision _collision)
	{
		if (m_HasPlayedImpact)
			return;
		if (_collision.contactCount <= 0)
			return;
		if (Time.time - m_SpawnedTime < m_MinAirborneSeconds)
			return;

		m_HasPlayedImpact = true;
		Vector3 pos = _collision.GetContact(0).point;
		m_AudioSource.transform.position = pos;

		AudioClip[] clips = m_VehicleImpactClips != null && m_VehicleImpactClips.Length > 0
			? m_VehicleImpactClips : m_GroundImpactClips;

		if (clips != null && clips.Length > 0)
		{
			AudioClip clip = clips[Random.Range(0, clips.Length)];
			if (clip != null)
				m_AudioSource.PlayOneShot(clip, 0.25f);
		}

		m_ReleaseTime = Time.time + m_LifetimeAfterImpact;
	}
}
