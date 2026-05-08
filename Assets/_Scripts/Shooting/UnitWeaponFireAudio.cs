using UnityEngine;

/// <summary>
/// Звук выстрела по событию <see cref="UnitWeaponFireController.ShotFired"/>:
/// клип из <see cref="WeaponDefinition"/> или переопределение с <see cref="AmmoDefinition"/>.
/// Позиция — <see cref="EquippedWeapon.BarrelTransform"/>. Несколько <see cref="AudioSource"/> в пуле (round-robin),
/// чтобы очередь не забивала один источник.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(56)]
public sealed class UnitWeaponFireAudio : MonoBehaviour
{
	#region Constants
	private const float c_SubsonicSuppressedVolumeMultiplier = 0.5f;
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
	[Tooltip("Максимальная дистанция слышимости.")]
	[SerializeField, Min(0.5f)] private float m_SpatialMaxDistance = 45f;
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

		// Нельзя вешать источник на корень и двигать transform.position под ствол — переносится весь юнит.
		if (m_AudioSource != null && m_AudioSource.transform != transform)
		{
			m_AudioSource.playOnAwake = false;
			ConfigureSpatial(m_AudioSource);
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

			if (!voiceTr.TryGetComponent(out AudioSource src))
				src = voiceTr.gameObject.AddComponent<AudioSource>();

			src.playOnAwake = false;
			ConfigureSpatial(src);
			m_FireVoicePool[i] = src;
		}
	}

	private void ConfigureSpatial(AudioSource _source)
	{
		if (_source == null)
			return;

		_source.spatialBlend = 1f;
		_source.minDistance = m_SpatialMinDistance;
		_source.maxDistance = m_SpatialMaxDistance;
		_source.rolloffMode = AudioRolloffMode.Linear;
		_source.dopplerLevel = 0f;
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_WeaponRuntime == null)
			return;

		EnsureFireAudioPool();
		if (m_FireVoicePool == null || m_FireVoicePool.Length == 0)
			return;

		AudioSource voiceSource = m_FireVoicePool[m_NextFireVoiceIndex];
		m_NextFireVoiceIndex = (m_NextFireVoiceIndex + 1) % m_FireVoicePool.Length;

		WeaponDefinition weapon = m_WeaponRuntime.CurrentWeaponDefinition;
		WeaponRuntimeState runtimeState = m_WeaponRuntime.RuntimeState;
		AudioClip clip = null;
		float volumeMultiplier = 1f;

		if (_ammo != null && _ammo.FireSoundOverride != null)
		{
			clip = _ammo.FireSoundOverride;
		}
		else
		{
			WeaponAttachmentDefinition suppressor = TryGetEquippedSuppressor(runtimeState);
			if (suppressor != null)
			{
				clip = suppressor.SuppressedFireSound != null ? suppressor.SuppressedFireSound : weapon != null ? weapon.FireSound : null;
				if (_ammo != null && _ammo.IsSubsonic)
					volumeMultiplier = c_SubsonicSuppressedVolumeMultiplier;
			}
			else
				clip = weapon != null ? weapon.FireSound : null;
		}

		if (clip == null)
			return;

		float volume = (weapon != null ? weapon.FireSoundVolume : 1f) * volumeMultiplier;
		float variance = weapon != null ? weapon.FirePitchVariance : 0f;
		float pitch = 1f;
		if (variance > 0f)
			pitch = Random.Range(1f - variance, 1f + variance);

		Vector3 pos = transform.position;
		EquippedWeapon equipped = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (equipped != null && equipped.BarrelTransform != null)
			pos = equipped.BarrelTransform.position;

		voiceSource.transform.position = pos;
		voiceSource.pitch = pitch;
		voiceSource.PlayOneShot(clip, volume);
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
			WeaponAttachmentDefinition a = attachments[i];
			if (a != null && a.AttachmentType == WeaponAttachmentType.Suppressor)
				return a;
		}

		return null;
	}
	#endregion
}
