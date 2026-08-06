using System;
using CombatVehicleSystem;
using UnityEngine;

/// <summary>
/// Realistic engine audio: start one-shot + crossfade between RPM loop layers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VehicleBrain))]
public sealed class VehicleEngineAudio : MonoBehaviour, IAdvancedEngineAudio
{
	#region Nested
	[Serializable]
	public struct RpmLayer
	{
		public float Rpm;
		public AudioClip Clip;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private AudioClip m_StartClip;
	[SerializeField] private RpmLayer[] m_RpmLayers = Array.Empty<RpmLayer>();
	[SerializeField, Range(0f, 1f)] private float m_Volume = 1f;
	[SerializeField, Range(0f, 1f)] private float m_SpatialBlend = 1f;
	[SerializeField, Min(1f)] private float m_MinDistance = 30f;
	[SerializeField, Min(1f)] private float m_MaxDistance = 180f;
	[SerializeField, Min(0.01f)] private float m_LoopFadeSeconds = 0.35f;
	[SerializeField, Min(1f)] private float m_RpmSmoothSpeed = 900f;
	[SerializeField, Range(0f, 1f)] private float m_ThrottleInfluence = 0.35f;
	[SerializeField, Range(0f, 1f)] private float m_SpeedInfluence = 0.65f;
	#endregion

	#region Private Fields
	private VehicleBrain m_Brain;
	private WheeledMotor m_WheeledMotor;
	private TrackedMotor m_TrackedMotor;
	private AudioSource m_StartSource;
	private AudioSource m_LoopSourceA;
	private AudioSource m_LoopSourceB;
	private float m_SmoothedRpm = 600f;
	private float m_LoopGain;
	private bool m_Subscribed;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Brain = GetComponent<VehicleBrain>();
		m_WheeledMotor = GetComponent<WheeledMotor>();
		m_TrackedMotor = GetComponent<TrackedMotor>();
		SortLayers();
		EnsureAudioSources();
		StopLoopSourcesImmediate();
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
		StopLoopSourcesImmediate();
	}

	private void Update()
	{
		if (m_Brain == null || !m_Brain.EngineRunning)
			return;

		UpdateLoopCrossfade();
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
		m_SmoothedRpm = GetMinRpm();
		m_LoopGain = 0f;
		StopLoopSourcesImmediate();

		if (m_StartClip != null && m_StartSource != null)
			m_StartSource.PlayOneShot(m_StartClip, m_Volume);
	}

	private void StopEngineAudio()
	{
		if (m_StartSource != null && m_StartSource.isPlaying)
			m_StartSource.Stop();

		StopLoopSourcesImmediate();
		m_SmoothedRpm = GetMinRpm();
		m_LoopGain = 0f;
	}

	private void UpdateLoopCrossfade()
	{
		if (m_RpmLayers == null || m_RpmLayers.Length == 0)
			return;

		float targetRpm = m_Brain.EngineReady ? EstimateTargetRpm() : GetMinRpm();
		m_SmoothedRpm = Mathf.MoveTowards(m_SmoothedRpm, targetRpm, m_RpmSmoothSpeed * Time.deltaTime);

		float targetGain = m_Brain.EngineReady ? m_Volume : 0f;
		m_LoopGain = Mathf.MoveTowards(m_LoopGain, targetGain, (m_Volume / Mathf.Max(0.01f, m_LoopFadeSeconds)) * Time.deltaTime);
		if (m_LoopGain <= 0.001f)
		{
			StopLoopSourcesImmediate();
			return;
		}

		FindAdjacentLayers(m_SmoothedRpm, out int lowerIndex, out int upperIndex, out float blend01);
		ApplyLayerToSource(m_LoopSourceA, lowerIndex, (1f - blend01) * m_LoopGain);
		ApplyLayerToSource(m_LoopSourceB, upperIndex, blend01 * m_LoopGain);
	}

	private float EstimateTargetRpm()
	{
		float minRpm = GetMinRpm();
		float maxRpm = GetMaxRpm();
		float topSpeed = m_Brain.Tuning != null ? Mathf.Max(1f, m_Brain.Tuning.TopSpeedKmh) : 100f;
		float speedRatio = Mathf.Clamp01(m_Brain.CurrentSpeedKmh / topSpeed);
		float throttle = 0f;
		if (m_WheeledMotor != null)
			throttle = Mathf.Abs(m_WheeledMotor.SmoothedThrottle);
		else if (m_TrackedMotor != null)
			throttle = speedRatio;

		float load = m_ThrottleInfluence * throttle + m_SpeedInfluence * speedRatio;
		float rpm01 = Mathf.Clamp01(Mathf.Max(speedRatio, load));
		return Mathf.Lerp(minRpm, maxRpm, rpm01);
	}

	private void FindAdjacentLayers(float _rpm, out int _lowerIndex, out int _upperIndex, out float _blend01)
	{
		_lowerIndex = 0;
		_upperIndex = 0;
		_blend01 = 0f;

		if (m_RpmLayers.Length == 1)
			return;

		if (_rpm <= m_RpmLayers[0].Rpm)
		{
			_lowerIndex = 0;
			_upperIndex = 1;
			_blend01 = 0f;
			return;
		}

		int last = m_RpmLayers.Length - 1;
		if (_rpm >= m_RpmLayers[last].Rpm)
		{
			_lowerIndex = last - 1;
			_upperIndex = last;
			_blend01 = 1f;
			return;
		}

		for (int i = 0; i < last; i++)
		{
			float lowRpm = m_RpmLayers[i].Rpm;
			float highRpm = m_RpmLayers[i + 1].Rpm;
			if (_rpm < lowRpm || _rpm > highRpm)
				continue;

			_lowerIndex = i;
			_upperIndex = i + 1;
			float span = Mathf.Max(1f, highRpm - lowRpm);
			_blend01 = (_rpm - lowRpm) / span;
			return;
		}
	}

	private void ApplyLayerToSource(AudioSource _source, int _layerIndex, float _volume)
	{
		if (_source == null || _layerIndex < 0 || _layerIndex >= m_RpmLayers.Length)
			return;

		AudioClip clip = m_RpmLayers[_layerIndex].Clip;
		if (clip == null)
		{
			if (_source.isPlaying)
				_source.Stop();
			return;
		}

		if (_source.clip != clip)
		{
			_source.clip = clip;
			_source.time = 0f;
			_source.Play();
		}
		else if (!_source.isPlaying)
		{
			_source.Play();
		}

		_source.volume = Mathf.Clamp01(_volume);
	}

	private void StopLoopSourcesImmediate()
	{
		StopSource(m_LoopSourceA);
		StopSource(m_LoopSourceB);
	}

	private static void StopSource(AudioSource _source)
	{
		if (_source == null)
			return;
		if (_source.isPlaying)
			_source.Stop();
		_source.volume = 0f;
	}

	private void EnsureAudioSources()
	{
		m_StartSource = EnsureChildSource("EngineStartAudio", _loop: false);
		m_LoopSourceA = EnsureChildSource("EngineLoopAudioA", _loop: true);
		m_LoopSourceB = EnsureChildSource("EngineLoopAudioB", _loop: true);
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
		return source;
	}

	private void SortLayers()
	{
		if (m_RpmLayers == null || m_RpmLayers.Length < 2)
			return;

		Array.Sort(m_RpmLayers, static (a, b) => a.Rpm.CompareTo(b.Rpm));
	}

	private float GetMinRpm()
	{
		if (m_RpmLayers == null || m_RpmLayers.Length == 0)
			return 600f;
		return m_RpmLayers[0].Rpm;
	}

	private float GetMaxRpm()
	{
		if (m_RpmLayers == null || m_RpmLayers.Length == 0)
			return 6000f;
		return m_RpmLayers[m_RpmLayers.Length - 1].Rpm;
	}
	#endregion
}
