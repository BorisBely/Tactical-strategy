using UnityEngine;

/// <summary>
/// Звук выстрела по событию <see cref="UnitWeaponFireController.ShotFired"/>:
/// клип из <see cref="WeaponDefinition"/> или переопределение с <see cref="AmmoDefinition"/>.
/// Позиция воспроизведения — <see cref="EquippedWeapon.BarrelTransform"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(56)]
public sealed class UnitWeaponFireAudio : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Опционально свой AudioSource (не на корне юнита — иначе позиция под ствол сдвинет всего персонажа). Если пусто — создаётся дочерний FireAudioSource_Auto.")]
	[SerializeField] private AudioSource m_AudioSource;
	[Tooltip("Минимальная дистанция 3D (если источник в режиме 3D).")]
	[SerializeField, Min(0.01f)] private float m_SpatialMinDistance = 1f;
	[Tooltip("Максимальная дистанция слышимости.")]
	[SerializeField, Min(0.5f)] private float m_SpatialMaxDistance = 45f;
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

		EnsureAudioSource();
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
	private void EnsureAudioSource()
	{
		// Нельзя вешать источник на корень и двигать transform.position под ствол — переносится весь юнит.
		if (m_AudioSource != null && m_AudioSource.transform != transform)
		{
			ConfigureSpatial(m_AudioSource);
			return;
		}

		const string c_FireAudioChildName = "FireAudioSource_Auto";
		Transform child = transform.Find(c_FireAudioChildName);
		if (child == null)
		{
			GameObject go = new GameObject(c_FireAudioChildName);
			go.transform.SetParent(transform, false);
			child = go.transform;
		}

		if (!child.TryGetComponent(out m_AudioSource))
			m_AudioSource = child.gameObject.AddComponent<AudioSource>();

		m_AudioSource.playOnAwake = false;
		ConfigureSpatial(m_AudioSource);
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

		if (m_AudioSource == null || m_AudioSource.transform == transform)
			EnsureAudioSource();
		if (m_AudioSource == null)
			return;

		WeaponDefinition weapon = m_WeaponRuntime.CurrentWeaponDefinition;
		AudioClip clip = _ammo != null && _ammo.FireSoundOverride != null
			? _ammo.FireSoundOverride
			: weapon != null ? weapon.FireSound : null;
		if (clip == null)
			return;

		float volume = weapon != null ? weapon.FireSoundVolume : 1f;
		float variance = weapon != null ? weapon.FirePitchVariance : 0f;
		float pitch = 1f;
		if (variance > 0f)
			pitch = Random.Range(1f - variance, 1f + variance);

		Vector3 pos = transform.position;
		EquippedWeapon equipped = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (equipped != null && equipped.BarrelTransform != null)
			pos = equipped.BarrelTransform.position;

		m_AudioSource.transform.position = pos;
		float saved = m_AudioSource.pitch;
		m_AudioSource.pitch = pitch;
		m_AudioSource.PlayOneShot(clip, volume);
		m_AudioSource.pitch = saved;
	}
	#endregion
}
