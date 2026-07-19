using System.Collections;
using UnityEngine;

/// <summary>
/// Короткий импульс Point Light при активации (для flashbang).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class TimedPointLightPulse : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Light m_Light;
	[SerializeField, Min(0.01f)] private float m_PeakIntensity = 8f;
	[SerializeField, Min(0.05f)] private float m_DurationSeconds = 0.18f;
	#endregion

	#region Private Fields
	private Coroutine m_PulseRoutine;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Light == null)
			m_Light = GetComponent<Light>();
	}

	private void OnEnable()
	{
		if (m_PulseRoutine != null)
			StopCoroutine(m_PulseRoutine);
		m_PulseRoutine = StartCoroutine(PulseRoutine());
	}

	private void OnDisable()
	{
		if (m_PulseRoutine != null)
		{
			StopCoroutine(m_PulseRoutine);
			m_PulseRoutine = null;
		}

		if (m_Light != null)
			m_Light.intensity = 0f;
	}
	#endregion

	#region Private Methods
	private IEnumerator PulseRoutine()
	{
		if (m_Light == null)
			yield break;

		float duration = Mathf.Max(0.01f, m_DurationSeconds);
		float elapsed = 0f;
		m_Light.enabled = true;
		m_Light.intensity = m_PeakIntensity;

		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			m_Light.intensity = Mathf.Lerp(m_PeakIntensity, 0f, t * t);
			yield return null;
		}

		m_Light.intensity = 0f;
		m_Light.enabled = false;
		m_PulseRoutine = null;
	}
	#endregion
}

/// <summary>
/// Запускает все smoke VFX на тестовой раскладке SmokeVfxComparison в Play Mode.
/// </summary>
[DisallowMultipleComponent]
public sealed class SmokeVfxComparisonPlayer : MonoBehaviour
{
	#region Unity Lifecycle
	private void Start()
	{
		PlayAll();
	}

	private void OnEnable()
	{
		if (Application.isPlaying)
			PlayAll();
	}

	private void PlayAll()
	{
		ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			if (systems[i] == null)
				continue;

			systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			systems[i].Clear(true);
			systems[i].Play(true);
		}
	}
	#endregion
}
