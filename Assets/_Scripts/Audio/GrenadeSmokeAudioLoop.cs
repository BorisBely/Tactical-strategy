using System.Collections;
using UnityEngine;

/// <summary>
/// Зацикливает нецикличный клип через два <see cref="AudioSource"/> с длинным equal-power кроссфейдом.
/// </summary>
[DisallowMultipleComponent]
public sealed class GrenadeSmokeAudioLoop : MonoBehaviour
{
	#region Constants
	private const float c_DefaultCrossfadeSeconds = 2.2f;
	private const float c_DefaultFadeInSeconds = 1.2f;
	private const float c_DefaultFadeOutSeconds = 3f;
	private const float c_MinCrossfadeSeconds = 1f;
	#endregion

	#region Private Fields
	private AudioSource m_SourceA;
	private AudioSource m_SourceB;
	private AudioClip m_Clip;
	private float m_TargetVolume = 0.7f;
	private float m_MaxDistance = 45f;
	private float m_CrossfadeSeconds = c_DefaultCrossfadeSeconds;
	private float m_FadeInSeconds = c_DefaultFadeInSeconds;
	private float m_FadeOutSeconds = c_DefaultFadeOutSeconds;
	private bool m_IsPlaying;
	private bool m_IsStopping;
	private bool m_SuppressDisableStop;
	private Coroutine m_LoopRoutine;
	private Coroutine m_FadeRoutine;
	#endregion

	#region Unity Lifecycle
	private void OnDisable()
	{
		if (!m_SuppressDisableStop)
			StopImmediate();
	}
	#endregion

	#region Public Methods
	public void Configure(
		AudioClip _clip,
		float _volume,
		float _maxDistance,
		float _fadeInSeconds = c_DefaultFadeInSeconds,
		float _fadeOutSeconds = c_DefaultFadeOutSeconds,
		float _crossfadeSeconds = c_DefaultCrossfadeSeconds)
	{
		m_Clip = _clip;
		m_TargetVolume = Mathf.Clamp01(_volume);
		m_MaxDistance = Mathf.Max(2f, _maxDistance);
		m_FadeInSeconds = Mathf.Max(0.1f, _fadeInSeconds);
		m_FadeOutSeconds = Mathf.Max(0.1f, _fadeOutSeconds);
		m_CrossfadeSeconds = Mathf.Max(c_MinCrossfadeSeconds, _crossfadeSeconds);
		EnsureSources();
		ApplySourceSettings(m_SourceA);
		ApplySourceSettings(m_SourceB);
	}

	public void Play()
	{
		if (m_Clip == null || m_TargetVolume <= 0f)
			return;

		EnsureSources();
		m_SuppressDisableStop = true;
		StopImmediate();
		m_SuppressDisableStop = false;
		m_IsPlaying = true;
		m_IsStopping = false;
		m_LoopRoutine = StartCoroutine(CrossfadeLoopRoutine());
	}

	public void FadeOutAndStop()
	{
		if (!m_IsPlaying || m_IsStopping)
			return;

		m_IsStopping = true;
		if (m_FadeRoutine != null)
			StopCoroutine(m_FadeRoutine);

		m_FadeRoutine = StartCoroutine(FadeOutRoutine());
	}
	#endregion

	#region Private Methods
	private void EnsureSources()
	{
		if (m_SourceA == null)
			m_SourceA = CreateSource("SmokeGasVoice_A");
		if (m_SourceB == null)
			m_SourceB = CreateSource("SmokeGasVoice_B");
	}

	private AudioSource CreateSource(string _name)
	{
		GameObject go = new GameObject(_name);
		go.transform.SetParent(transform, false);
		AudioSource source = go.AddComponent<AudioSource>();
		source.playOnAwake = false;
		source.loop = false;
		source.spatialBlend = 1f;
		source.dopplerLevel = 0f;
		source.rolloffMode = AudioRolloffMode.Linear;
		source.minDistance = 2.5f;
		return source;
	}

	private void ApplySourceSettings(AudioSource _source)
	{
		if (_source == null)
			return;

		_source.clip = m_Clip;
		_source.volume = 0f;
		_source.maxDistance = m_MaxDistance;
		_source.spatialBlend = 1f;
		_source.dopplerLevel = 0f;
	}

	private IEnumerator CrossfadeLoopRoutine()
	{
		float clipLength = Mathf.Max(0.2f, m_Clip.length);
		float crossfade = Mathf.Clamp(m_CrossfadeSeconds, c_MinCrossfadeSeconds, clipLength * 0.48f);
		float crossfadeStart = clipLength - crossfade;

		AudioSource lead = m_SourceA;
		AudioSource follow = m_SourceB;
		lead.clip = m_Clip;
		follow.clip = m_Clip;
		lead.volume = 0f;
		follow.volume = 0f;
		follow.Stop();

		lead.Play();
		yield return FadeSourceVolume(lead, 0f, m_TargetVolume, m_FadeInSeconds);

		while (m_IsPlaying && !m_IsStopping && m_Clip != null)
		{
			while (m_IsPlaying && !m_IsStopping && lead.isPlaying && lead.time < crossfadeStart)
				yield return null;

			if (!m_IsPlaying || m_IsStopping)
				yield break;

			follow.time = 0f;
			follow.volume = 0f;
			follow.Play();

			float elapsed = 0f;
			while (elapsed < crossfade && m_IsPlaying && !m_IsStopping)
			{
				elapsed += Time.unscaledDeltaTime;
				float t = SmoothStep(Mathf.Clamp01(elapsed / crossfade));
				ApplyEqualPowerCrossfade(lead, follow, t);
				yield return null;
			}

			if (!m_IsPlaying || m_IsStopping)
				yield break;

			lead.Stop();
			lead.volume = 0f;
			follow.volume = m_TargetVolume;

			AudioSource swap = lead;
			lead = follow;
			follow = swap;
		}
	}

	private void ApplyEqualPowerCrossfade(AudioSource _outgoing, AudioSource _incoming, float _t)
	{
		float fadeOut = Mathf.Cos(_t * Mathf.PI * 0.5f);
		float fadeIn = Mathf.Sin(_t * Mathf.PI * 0.5f);
		if (_outgoing != null)
			_outgoing.volume = m_TargetVolume * fadeOut;
		if (_incoming != null)
			_incoming.volume = m_TargetVolume * fadeIn;
	}

	private IEnumerator FadeOutRoutine()
	{
		if (m_LoopRoutine != null)
		{
			StopCoroutine(m_LoopRoutine);
			m_LoopRoutine = null;
		}

		float startA = m_SourceA != null ? m_SourceA.volume : 0f;
		float startB = m_SourceB != null ? m_SourceB.volume : 0f;
		float elapsed = 0f;
		while (elapsed < m_FadeOutSeconds)
		{
			elapsed += Time.unscaledDeltaTime;
			float t = SmoothStep(Mathf.Clamp01(elapsed / m_FadeOutSeconds));
			if (m_SourceA != null)
				m_SourceA.volume = Mathf.Lerp(startA, 0f, t);
			if (m_SourceB != null)
				m_SourceB.volume = Mathf.Lerp(startB, 0f, t);
			yield return null;
		}

		StopImmediate();
	}

	private IEnumerator FadeSourceVolume(AudioSource _source, float _from, float _to, float _seconds)
	{
		if (_source == null)
			yield break;

		float elapsed = 0f;
		_source.volume = _from;
		while (elapsed < _seconds && m_IsPlaying && !m_IsStopping)
		{
			elapsed += Time.unscaledDeltaTime;
			float t = SmoothStep(Mathf.Clamp01(elapsed / _seconds));
			_source.volume = Mathf.Lerp(_from, _to, t);
			yield return null;
		}

		if (_source != null && m_IsPlaying && !m_IsStopping)
			_source.volume = _to;
	}

	private static float SmoothStep(float _t)
	{
		return _t * _t * (3f - 2f * _t);
	}

	private void StopImmediate()
	{
		m_IsPlaying = false;
		m_IsStopping = false;

		if (m_LoopRoutine != null)
		{
			StopCoroutine(m_LoopRoutine);
			m_LoopRoutine = null;
		}

		if (m_FadeRoutine != null)
		{
			StopCoroutine(m_FadeRoutine);
			m_FadeRoutine = null;
		}

		if (m_SourceA != null)
		{
			m_SourceA.Stop();
			m_SourceA.volume = 0f;
		}

		if (m_SourceB != null)
		{
			m_SourceB.Stop();
			m_SourceB.volume = 0f;
		}
	}
	#endregion
}
