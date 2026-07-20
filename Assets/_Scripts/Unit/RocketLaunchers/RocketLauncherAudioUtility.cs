using UnityEngine;

/// <summary>
/// Воспроизведение SFX гранатомётов через <see cref="CombatAudioManager.TryPlayRocketLauncher"/>.
/// </summary>
public static class RocketLauncherAudioUtility
{
	#region Public Methods
	public static void PlayFire(
		RocketLauncherData _data,
		RocketLauncherType _type,
		Vector3 _position,
		Transform _ownerOrNull)
	{
		if (_data == null)
			return;

		if (_data.TryPickFireWhooshClip(out AudioClip whoosh))
		{
			CombatAudioManager.TryPlayRocketLauncher(
				whoosh,
				_position,
				_data.FireWhooshVolume,
				_data.FireAudioMaxDistance,
				_ownerOrNull);
		}

		if (_data.TryPickFireAccentClip(_type, out AudioClip accent))
		{
			CombatAudioManager.TryPlayRocketLauncher(
				accent,
				_position,
				_data.FireAccentVolume,
				_data.FireAudioMaxDistance,
				_ownerOrNull);
		}
	}

	public static void PlayExplosion(
		RocketLauncherData _data,
		RocketLauncherType _type,
		Vector3 _position)
	{
		if (_data == null || !_data.TryPickExplosionClip(_type, out AudioClip clip) || clip == null)
			return;

		CombatAudioManager.TryPlayRocketLauncher(
			clip,
			_position,
			_data.ExplosionAudioVolume,
			_data.ExplosionAudioMaxDistance);
	}

	public static void PlayFlyby(RocketLauncherData _data, float _volume, float _pitch = 1f)
	{
		if (_data == null || !_data.TryPickFlybyClip(out AudioClip clip) || clip == null)
			return;

		if (_volume <= 0.001f)
			return;

		Vector3 listenerPosition = GetListenerPosition();
		CombatAudioManager.TryPlayRocketLauncher(
			clip,
			listenerPosition,
			_volume,
			_maxDistance: 1f,
			_ownerOrNull: null,
			_pitch: _pitch,
			_nonSpatial: true);
	}

	public static void PlayRpgReloadInsert(
		RocketLauncherData _data,
		Vector3 _position,
		Transform _ownerOrNull)
	{
		if (_data == null || !_data.TryPickRpgReloadInsertClip(out AudioClip clip) || clip == null)
			return;

		CombatAudioManager.TryPlayRocketLauncher(
			clip,
			_position,
			_data.RpgReloadInsertVolume,
			_data.RpgReloadInsertMaxDistance,
			_ownerOrNull);
	}
	#endregion

	#region Private Methods
	private static Vector3 GetListenerPosition()
	{
		Camera mainCamera = Camera.main;
		if (mainCamera != null)
			return mainCamera.transform.position;

		AudioListener listener = Object.FindAnyObjectByType<AudioListener>();
		return listener != null ? listener.transform.position : Vector3.zero;
	}
	#endregion
}
