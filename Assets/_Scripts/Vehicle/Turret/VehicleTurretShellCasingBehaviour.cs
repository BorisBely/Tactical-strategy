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
	private float m_ImpactVolume = 0.25f;
	private Rigidbody m_Rigidbody;
	private AudioSource m_AudioSource;

	public void ConfigureImpactVolume(float _volume) => m_ImpactVolume = Mathf.Clamp01(_volume);

	public void ConfigureImpactClips(AudioClip[] _vehicleClips, float _volume)
	{
		ConfigureImpactVolume(_volume);
		if (_vehicleClips != null && _vehicleClips.Length > 0)
			m_VehicleImpactClips = _vehicleClips;
	}

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_AudioSource = GetComponent<AudioSource>();
		if (m_AudioSource == null)
			m_AudioSource = gameObject.AddComponent<AudioSource>();
		m_AudioSource.playOnAwake = false;
		m_AudioSource.spatialBlend = 1f;
		m_AudioSource.maxDistance = 25f;
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
		if (GamePauseState.IsSimulationPaused)
			return;
		if (m_HasPlayedImpact)
			return;
		if (_collision.contactCount <= 0)
			return;
		if (Time.time - m_SpawnedTime < m_MinAirborneSeconds)
			return;

		m_HasPlayedImpact = true;
		Vector3 pos = _collision.GetContact(0).point;
		m_AudioSource.transform.position = pos;

		AudioClip clip = PickImpactClip();
		if (clip != null)
			m_AudioSource.PlayOneShot(clip, m_ImpactVolume);

		m_ReleaseTime = Time.time + m_LifetimeAfterImpact;
	}

	private AudioClip PickImpactClip()
	{
		AudioClip clip = PickRandomNonNull(m_VehicleImpactClips);
		if (clip != null)
			return clip;
		return PickRandomNonNull(m_GroundImpactClips);
	}

	private static AudioClip PickRandomNonNull(AudioClip[] _clips)
	{
		if (_clips == null || _clips.Length == 0)
			return null;

		int nonNullCount = 0;
		for (int i = 0; i < _clips.Length; i++)
		{
			if (_clips[i] != null)
				nonNullCount++;
		}

		if (nonNullCount <= 0)
			return null;

		int pick = Random.Range(0, nonNullCount);
		for (int i = 0; i < _clips.Length; i++)
		{
			if (_clips[i] == null)
				continue;
			if (pick == 0)
				return _clips[i];
			pick--;
		}

		return null;
	}
}
