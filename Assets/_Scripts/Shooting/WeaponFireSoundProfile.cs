using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Набор вариантов звука выстрела и опциональная дальность слышимости для 3D-источника.
/// </summary>
[Serializable]
public sealed class WeaponFireSoundProfile
{
	#region Constants
	private const float c_DefaultMaxAudibleDistanceMeters = 125f;
	#endregion

	#region Private Fields
	[Tooltip("Варианты звука выстрела. При каждом выстреле выбирается случайный валидный клип.")]
	[FormerlySerializedAs("m_NearClips")]
	[SerializeField] private AudioClip[] m_FireClips;
	[Tooltip("Максимальная дистанция слышимости (м) для 3D AudioSource. 0 = использовать дефолт UnitWeaponFireAudio.")]
	[SerializeField, Min(0f)] private float m_MaxAudibleDistanceMeters;
	#endregion

	#region Public Properties
	public AudioClip[] FireClips => m_FireClips;
	public float MaxAudibleDistanceMeters => m_MaxAudibleDistanceMeters;
	public bool HasAnyClips => HasValidClips(m_FireClips);
	#endregion

	#region Public Methods
	public bool TryPickClip(out AudioClip _clip) => TryPickRandomClip(m_FireClips, out _clip);

	public float ResolveMaxAudibleDistance(float _componentDefaultMaxDistance)
	{
		if (m_MaxAudibleDistanceMeters > 0f)
			return m_MaxAudibleDistanceMeters;

		return _componentDefaultMaxDistance > 0f ? _componentDefaultMaxDistance : c_DefaultMaxAudibleDistanceMeters;
	}
	#endregion

	#region Private Methods
	private static bool HasValidClips(AudioClip[] _clips)
	{
		if (_clips == null || _clips.Length == 0)
			return false;

		for (int i = 0; i < _clips.Length; i++)
		{
			if (_clips[i] != null)
				return true;
		}

		return false;
	}

	private static bool TryPickRandomClip(AudioClip[] _clips, out AudioClip _clip)
	{
		_clip = null;
		if (_clips == null || _clips.Length == 0)
			return false;

		int validCount = 0;
		for (int i = 0; i < _clips.Length; i++)
		{
			if (_clips[i] != null)
				validCount++;
		}

		if (validCount == 0)
			return false;

		int pick = UnityEngine.Random.Range(0, validCount);
		for (int i = 0; i < _clips.Length; i++)
		{
			if (_clips[i] == null)
				continue;

			if (pick == 0)
			{
				_clip = _clips[i];
				return true;
			}

			pick--;
		}

		return false;
	}
	#endregion
}
