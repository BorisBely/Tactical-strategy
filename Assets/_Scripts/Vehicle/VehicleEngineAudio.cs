using CombatVehicleSystem;
using UnityEngine;

/// <summary>
/// Engine audio: start one-shot + single looping clip pitched by speed/throttle load.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VehicleBrain))]
public sealed class VehicleEngineAudio : MonoBehaviour, IAdvancedEngineAudio
{
	#region Serialized Fields
	[SerializeField] private AudioClip m_StartClip;
	[SerializeField] private AudioClip m_LoopClip;
	[SerializeField, Range(0f, 1f)] private float m_Volume = 1f;
	[SerializeField, Range(0f, 1f)] private float m_SpatialBlend = 1f;
	[SerializeField, Min(1f)] private float m_MinDistance = 30f;
	[SerializeField, Min(1f)] private float m_MaxDistance = 180f;
	[SerializeField, Min(0.01f)] private float m_LoopFadeSeconds = 0.35f;
	[SerializeField, Min(0.01f)] private float m_LoadSmoothSpeed = 1.5f;
	[SerializeField, Range(0.1f, 3f)] private float m_IdlePitch = 1f;
	[SerializeField, Range(0.1f, 3f)] private float m_MaxPitch = 2f;
	[SerializeField, Range(0f, 1f)] private float m_ThrottleInfluence = 0.35f;
	[SerializeField, Range(0f, 1f)] private float m_SpeedInfluence = 0.65f;
	#endregion

	#region Private Fields
	private VehicleBrain m_Brain;
	private WheeledMotor m_WheeledMotor;
	private TrackedMotor m_TrackedMotor;
	private AudioSource m_StartSource;
	private AudioSource m_LoopSource;
	private float m_SmoothedLoad;
	private float m_LoopGain;
	private bool m_Subscribed;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Brain = GetComponent<VehicleBrain>();
		m_WheeledMotor = GetComponent<WheeledMotor>();
		m_TrackedMotor = GetComponent<TrackedMotor>();
		EnsureAudioSources();
		StopLoopSourceImmediate();
	}

	private void OnEnable()
	{
		Subscribe();
		if (m_Brain != null && m_Brain.EngineRunning)
			OnEngineStateChanged(true);
	}

	private void OnDisable()
	{
		Unsubscribe();
		StopLoopSourceImmediate();
	}

	private void Update()
	{
		if (m_Brain == null || !m_Brain.EngineRunning)
			return;

		UpdateLoopPlayback();
	}
	#endregion

	#region Private Methods
	private void Subscribe()
	{
		if (m_Subscribed || m_Brain == null)
			return;
		m_Brain.EngineStateChanged += OnEngineStateChanged;
		m_Subscribed = true;
	}

	private void Unsubscribe()
	{
		if (!m_Subscribed || m_Brain == null)
			return;
		m_Brain.EngineStateChanged -= OnEngineStateChanged;
		m_Subscribed = false;
	}

	private void OnEngineStateChanged(bool _running)
	{
		if (_running)
			BeginEngineStart();
		else
			StopEngineAudio();
	}

	private void BeginEngineStart()
	{
		m_SmoothedLoad = 0f;
		m_LoopGain = 0f;
		StopLoopSourceImmediate();

		if (m_StartClip == null || m_StartSource == null)
			return;

		m_StartSource.volume = m_Volume;
		m_StartSource.PlayOneShot(m_StartClip, m_Volume);
	}

	private void StopEngineAudio()
	{
		if (m_StartSource != null && m_StartSource.isPlaying)
			m_StartSource.Stop();

		StopLoopSourceImmediate();
		m_SmoothedLoad = 0f;
		m_LoopGain = 0f;
	}

	private void UpdateLoopPlayback()
	{
		if (m_LoopClip == null || m_LoopSource == null)
			return;

		float targetLoad = m_Brain.EngineReady ? EstimateLoad01() : 0f;
		m_SmoothedLoad = Mathf.MoveTowards(m_SmoothedLoad, targetLoad, m_LoadSmoothSpeed * Time.deltaTime);

		float targetGain = m_Brain.EngineReady ? m_Volume : 0f;
		m_LoopGain = Mathf.MoveTowards(
			m_LoopGain,
			targetGain,
			(m_Volume / Mathf.Max(0.01f, m_LoopFadeSeconds)) * Time.deltaTime);

		if (m_LoopGain <= 0.001f)
		{
			StopLoopSourceImmediate();
			return;
		}

		if (m_LoopSource.clip != m_LoopClip)
		{
			m_LoopSource.clip = m_LoopClip;
			m_LoopSource.time = 0f;
			m_LoopSource.Play();
		}
		else if (!m_LoopSource.isPlaying)
		{
			m_LoopSource.Play();
		}

		m_LoopSource.volume = Mathf.Clamp01(m_LoopGain);
		m_LoopSource.pitch = Mathf.Lerp(m_IdlePitch, m_MaxPitch, m_SmoothedLoad);
	}

	private float EstimateLoad01()
	{
		float topSpeed = m_Brain.Tuning != null ? Mathf.Max(1f, m_Brain.Tuning.TopSpeedKmh) : 100f;
		float speedRatio = Mathf.Clamp01(m_Brain.CurrentSpeedKmh / topSpeed);
		float throttle = 0f;
		if (m_WheeledMotor != null)
			throttle = Mathf.Abs(m_WheeledMotor.SmoothedThrottle);
		else if (m_TrackedMotor != null)
			throttle = speedRatio;

		float load = m_ThrottleInfluence * throttle + m_SpeedInfluence * speedRatio;
		return Mathf.Clamp01(Mathf.Max(speedRatio, load));
	}

	private void StopLoopSourceImmediate()
	{
		if (m_LoopSource == null)
			return;
		if (m_LoopSource.isPlaying)
			m_LoopSource.Stop();
		m_LoopSource.volume = 0f;
		m_LoopSource.pitch = m_IdlePitch;
	}

	private void EnsureAudioSources()
	{
		m_StartSource = EnsureChildSource("EngineStartAudio", _loop: false);
		m_LoopSource = EnsureChildSource("EngineLoopAudio", _loop: true);

		// Drop legacy dual-loop children from the previous RPM crossfade setup.
		DestroyChildIfPresent("EngineLoopAudioA");
		DestroyChildIfPresent("EngineLoopAudioB");
	}

	private void DestroyChildIfPresent(string _name)
	{
		Transform child = transform.Find(_name);
		if (child == null)
			return;
		if (Application.isPlaying)
			Destroy(child.gameObject);
		else
			DestroyImmediate(child.gameObject);
	}

	private AudioSource EnsureChildSource(string _name, bool _loop)
	{
		Transform child = transform.Find(_name);
		if (child == null)
		{
			var go = new GameObject(_name);
			go.transform.SetParent(transform, false);
			child = go.transform;
		}

		if (!child.TryGetComponent(out AudioSource source))
			source = child.gameObject.AddComponent<AudioSource>();

		source.playOnAwake = false;
		source.loop = _loop;
		source.spatialBlend = m_SpatialBlend;
		source.minDistance = m_MinDistance;
		source.maxDistance = m_MaxDistance;
		source.rolloffMode = AudioRolloffMode.Linear;
		source.dopplerLevel = 0.25f;
		source.volume = 0f;
		source.pitch = 1f;
		return source;
	}
	#endregion
}
