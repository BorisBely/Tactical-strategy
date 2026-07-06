using System;
using UnityEngine;

/// <summary>
/// Список вариантов одного звука: при воспроизведении выбирается случайный непустой клип.
/// </summary>
[Serializable]
public sealed class WeaponRandomAudioClipSet
{
	#region Private Fields
	[SerializeField] private AudioClip[] m_Clips;
	#endregion

	#region Public Properties
	public AudioClip[] Clips => m_Clips;
	public bool HasAnyClips => HasValidClips(m_Clips);
	#endregion

	#region Public Methods
	public bool TryPickClip(out AudioClip _clip) => TryPickRandomClip(m_Clips, out _clip);
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
