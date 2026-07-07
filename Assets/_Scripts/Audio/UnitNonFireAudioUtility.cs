using UnityEngine;

/// <summary>
/// Общие настройки громкости и 3D-затухания для звуков юнита, кроме выстрелов
/// (шаги, перезарядка, гильзы, щелчки режима огня и т.д.).
/// </summary>
public static class UnitNonFireAudioUtility
{
	#region Constants
	public const float VolumeMultiplier = 0.65f;
	private const float c_SpatialMinDistance = 2.5f;
	private const float c_RolloffMinAudibleVolume = 0.03f;
	private const float c_RolloffAttenuationPower = 1.6f;
	private const int c_RolloffCurveKeyCount = 9;
	#endregion

	#region Static Fields
	private static AnimationCurve s_RolloffCurve;
	#endregion

	#region Public Methods
	public static float ScaleVolume(float _baseVolume) => Mathf.Clamp01(_baseVolume * VolumeMultiplier);

	public static void ConfigureSpatial(AudioSource _source, float _maxDistance)
	{
		if (_source == null)
			return;

		_source.spatialBlend = 1f;
		_source.minDistance = c_SpatialMinDistance;
		_source.maxDistance = Mathf.Max(c_SpatialMinDistance + 0.01f, _maxDistance);
		_source.rolloffMode = AudioRolloffMode.Custom;
		_source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, GetRolloffCurve());
		_source.dopplerLevel = 0f;
	}

	public static void PlayAtPoint(AudioClip _clip, Vector3 _position, float _volume, float _maxDistance = 40f)
	{
		if (_clip == null || _volume <= 0f)
			return;

		float scaledVolume = ScaleVolume(_volume);
		if (scaledVolume <= 0f)
			return;

		GameObject go = new GameObject("NonFireAudioOneShot");
		go.transform.position = _position;
		AudioSource source = go.AddComponent<AudioSource>();
		source.playOnAwake = false;
		ConfigureSpatial(source, _maxDistance);
		source.PlayOneShot(_clip, scaledVolume);

		float lifetime = _clip.length / Mathf.Max(0.01f, source.pitch) + 0.05f;
		Object.Destroy(go, lifetime);
	}
	#endregion

	#region Private Methods
	private static AnimationCurve GetRolloffCurve()
	{
		if (s_RolloffCurve != null)
			return s_RolloffCurve;

		Keyframe[] keys = new Keyframe[c_RolloffCurveKeyCount];
		for (int i = 0; i < c_RolloffCurveKeyCount; i++)
		{
			float normalizedDistance = i / (float)(c_RolloffCurveKeyCount - 1);
			float volume = Mathf.Lerp(
				c_RolloffMinAudibleVolume,
				1f,
				Mathf.Pow(1f - normalizedDistance, c_RolloffAttenuationPower));
			keys[i] = new Keyframe(normalizedDistance, volume);
		}

		s_RolloffCurve = new AnimationCurve(keys);
		return s_RolloffCurve;
	}
	#endregion
}
