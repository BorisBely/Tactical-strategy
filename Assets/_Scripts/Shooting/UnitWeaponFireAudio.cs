using UnityEngine;

/// <summary>
/// Звук выстрела по событию <see cref="UnitWeaponFireController.ShotFired"/>:
/// случайный клип из <see cref="WeaponFireSoundProfile"/> с 3D-затуханием Unity.
/// Позиция — <see cref="EquippedWeapon.BarrelTransform"/>. Пул <see cref="AudioSource"/> (round-robin),
/// чтобы очередь не забивала один источник.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(56)]
public sealed class UnitWeaponFireAudio : MonoBehaviour
{
	#region Constants
	private const float c_SubsonicSuppressedVolumeMultiplier = 0.5f;
	private const float c_RolloffMinAudibleVolume = 0.08f;
	private const float c_RolloffAttenuationPower = 1.35f;
	private const int c_RolloffCurveKeyCount = 9;
	#endregion

	#region Static Fields
	private static AnimationCurve s_FireRolloffCurve;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Опционально один AudioSource (дочерний, не корень). Если пусто — создаётся пул голосов. Если задан — используется только он (как раньше).")]
	[SerializeField] private AudioSource m_AudioSource;
	[Tooltip("Число источников для очереди: round-robin снимает перегрузку одного AudioSource при автоматической стрельбе. Не используется, если задан свой AudioSource выше.")]
	[SerializeField, Range(1, 8)] private int m_FireVoiceCount = 4;
	[Tooltip("Минимальная дистанция 3D (если источник в режиме 3D).")]
	[SerializeField, Min(0.01f)] private float m_SpatialMinDistance = 1f;
	[Tooltip("Максимальная дистанция слышимости по умолчанию, если в профиле оружия Max Audible Distance = 0.")]
	[SerializeField, Min(0.5f)] private float m_SpatialMaxDistance = 125f;
	#endregion

	#region Private Fields
	private AudioSource[] m_FireVoicePool;
	private int m_NextFireVoiceIndex;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();

		EnsureFireAudioPool();
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
	}
	#endregion

	#region Private Methods
	private void EnsureFireAudioPool()
	{
		if (m_FireVoicePool != null)
			return;

		if (m_AudioSource != null && m_AudioSource.transform != transform)
		{
			m_AudioSource.playOnAwake = false;
			ConfigureSpatial(m_AudioSource, m_SpatialMaxDistance);
			m_FireVoicePool = new[] { m_AudioSource };
			return;
		}

		const string c_PoolRootName = "FireAudioSource_Pool";
		Transform poolRoot = transform.Find(c_PoolRootName);
		if (poolRoot == null)
		{
			GameObject rootGo = new GameObject(c_PoolRootName);
			rootGo.transform.SetParent(transform, false);
			poolRoot = rootGo.transform;
		}

		int count = Mathf.Clamp(m_FireVoiceCount, 1, 8);
		m_FireVoicePool = new AudioSource[count];
		for (int i = 0; i < count; i++)
		{
			string voiceName = $"Voice_{i}";
			Transform voiceTr = poolRoot.Find(voiceName);
			if (voiceTr == null)
			{
				GameObject voiceGo = new GameObject(voiceName);
				voiceGo.transform.SetParent(poolRoot, false);
				voiceTr = voiceGo.transform;
			}

			if (!voiceTr.TryGetComponent(out AudioSource source))
				source = voiceTr.gameObject.AddComponent<AudioSource>();

			source.playOnAwake = false;
			ConfigureSpatial(source, m_SpatialMaxDistance);
			m_FireVoicePool[i] = source;
		}
	}

	private void ConfigureSpatial(AudioSource _source, float _maxDistance)
	{
		if (_source == null)
			return;

		_source.spatialBlend = 1f;
		_source.minDistance = m_SpatialMinDistance;
		_source.maxDistance = Mathf.Max(m_SpatialMinDistance + 0.01f, _maxDistance);
		_source.rolloffMode = AudioRolloffMode.Custom;
		_source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, GetFireRolloffCurve());
		_source.dopplerLevel = 0f;
	}

	private static AnimationCurve GetFireRolloffCurve()
	{
		if (s_FireRolloffCurve != null)
			return s_FireRolloffCurve;

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

		s_FireRolloffCurve = new AnimationCurve(keys);
		return s_FireRolloffCurve;
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_WeaponRuntime == null)
			return;

		EnsureFireAudioPool();
		if (m_FireVoicePool == null || m_FireVoicePool.Length == 0)
			return;

		WeaponDefinition weapon = m_WeaponRuntime.CurrentWeaponDefinition;
		WeaponRuntimeState runtimeState = m_WeaponRuntime.RuntimeState;
		float volumeMultiplier = 1f;
		WeaponFireSoundProfile profile = ResolveFireSoundProfile(_ammo, weapon, runtimeState, ref volumeMultiplier);

		Vector3 pos = ResolveBarrelPosition();
		float baseVolume = (weapon != null ? weapon.FireSoundVolume : 1f) * volumeMultiplier;
		float pitch = ResolvePitch(weapon);

		if (profile == null || !profile.TryPickClip(out AudioClip clip))
			return;

		float maxDistance = profile.ResolveMaxAudibleDistance(m_SpatialMaxDistance);
		PlayShot(clip, baseVolume, pitch, pos, maxDistance);
	}

	private void PlayShot(
		AudioClip _clip,
		float _volume,
		float _pitch,
		Vector3 _position,
		float _maxDistance)
	{
		AudioSource voiceSource = m_FireVoicePool[m_NextFireVoiceIndex];
		m_NextFireVoiceIndex = (m_NextFireVoiceIndex + 1) % m_FireVoicePool.Length;

		if (voiceSource == null || _clip == null || _volume <= 0f)
			return;

		ConfigureSpatial(voiceSource, _maxDistance);
		voiceSource.transform.position = _position;
		voiceSource.pitch = _pitch;
		voiceSource.PlayOneShot(_clip, _volume);
	}

	private static float ResolvePitch(WeaponDefinition _weapon)
	{
		float variance = _weapon != null ? _weapon.FirePitchVariance : 0f;
		if (variance <= 0f)
			return 1f;

		return Random.Range(1f - variance, 1f + variance);
	}

	private Vector3 ResolveBarrelPosition()
	{
		Vector3 pos = transform.position;
		EquippedWeapon equipped = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (equipped != null && equipped.BarrelTransform != null)
			pos = equipped.BarrelTransform.position;

		return pos;
	}

	private static WeaponFireSoundProfile ResolveFireSoundProfile(
		AmmoDefinition _ammo,
		WeaponDefinition _weapon,
		WeaponRuntimeState _runtimeState,
		ref float _volumeMultiplier)
	{
		if (_ammo != null && _ammo.FireSoundOverrideProfile != null && _ammo.FireSoundOverrideProfile.HasAnyClips)
			return _ammo.FireSoundOverrideProfile;

		WeaponAttachmentDefinition suppressor = TryGetEquippedSuppressor(_runtimeState);
		if (suppressor != null)
		{
			if (suppressor.SuppressedFireSoundProfile != null && suppressor.SuppressedFireSoundProfile.HasAnyClips)
			{
				if (_ammo != null && _ammo.IsSubsonic)
					_volumeMultiplier = c_SubsonicSuppressedVolumeMultiplier;

				return suppressor.SuppressedFireSoundProfile;
			}

			if (_ammo != null && _ammo.IsSubsonic)
				_volumeMultiplier = c_SubsonicSuppressedVolumeMultiplier;
		}

		if (_weapon != null && _weapon.FireSoundProfile != null && _weapon.FireSoundProfile.HasAnyClips)
			return _weapon.FireSoundProfile;

		return null;
	}

	private static WeaponAttachmentDefinition TryGetEquippedSuppressor(WeaponRuntimeState _state)
	{
		if (_state == null)
			return null;

		WeaponAttachmentDefinition[] attachments = _state.EquippedAttachments;
		if (attachments == null || attachments.Length == 0)
			return null;

		for (int i = 0; i < attachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = attachments[i];
			if (attachment != null && attachment.AttachmentType == WeaponAttachmentType.Suppressor)
				return attachment;
		}

		return null;
	}
	#endregion
}
