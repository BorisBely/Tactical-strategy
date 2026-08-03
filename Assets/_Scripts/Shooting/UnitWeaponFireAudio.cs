using UnityEngine;

/// <summary>
/// Звук выстрела по событию <see cref="UnitWeaponFireController.ShotFired"/>:
/// случайный клип из <see cref="WeaponFireSoundProfile"/> с 3D-затуханием через <see cref="CombatAudioManager"/>.
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
	[Tooltip("Минимальная дистанция 3D (если источник в режиме 3D).")]
	[SerializeField, Min(0.01f)] private float m_SpatialMinDistance = 1f;
	[Tooltip("Максимальная дистанция слышимости по умолчанию, если в профиле оружия Max Audible Distance = 0.")]
	[SerializeField, Min(0.5f)] private float m_SpatialMaxDistance = 125f;
	#endregion

	#region Private Fields
	private Coroutine m_TailCoroutine;
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
	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_WeaponRuntime == null)
			return;

		WeaponDefinition weapon = m_WeaponRuntime.CurrentWeaponDefinition;
		WeaponRuntimeState runtimeState = m_WeaponRuntime.RuntimeState;
		float volumeMultiplier = 1f;
		WeaponAttachmentDefinition suppressor = TryGetEquippedSuppressor(runtimeState);
		WeaponFireSoundProfile profile = ResolveFireSoundProfile(_ammo, weapon, suppressor, ref volumeMultiplier);

		Vector3 pos = ResolveBarrelPosition();
		float baseVolume = (weapon != null ? weapon.FireSoundVolume : 1f) * volumeMultiplier;
		float pitch = ResolvePitch(weapon);

		if (profile != null && profile.TryPickClip(out AudioClip clip))
		{
			float maxDistance = profile.ResolveMaxAudibleDistance(m_SpatialMaxDistance);
			int weaponSignatureId = weapon != null ? weapon.GetInstanceID() : 0;
			CombatAudioManager.TryPlayGunshot(
				clip, pos, baseVolume, pitch, maxDistance,
				transform, m_SpatialMinDistance, weaponSignatureId);
		}

		if (profile != null && profile.HasAnyTailClips)
		{
			if (m_TailCoroutine != null)
				StopCoroutine(m_TailCoroutine);
			m_TailCoroutine = StartCoroutine(PlayTailAfterDelay(profile, pos, baseVolume, weapon));
		}
	}

	private System.Collections.IEnumerator PlayTailAfterDelay(WeaponFireSoundProfile _profile, Vector3 _pos, float _volume, WeaponDefinition _weapon)
	{
		yield return new WaitForSeconds(_profile.TailThresholdSeconds);
		if (_profile.TryPickTailClip(out AudioClip tailClip))
		{
			float maxDistance = _profile.ResolveMaxAudibleDistance(m_SpatialMaxDistance);
			int weaponSignatureId = _weapon != null ? _weapon.GetInstanceID() : 0;
			CombatAudioManager.TryPlayGunshot(
				tailClip, _pos, _volume * 0.6f, 1f, maxDistance,
				transform, m_SpatialMinDistance, weaponSignatureId);
		}
		m_TailCoroutine = null;
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
		if (equipped != null && equipped.FireOriginTransform != null)
			pos = equipped.FireOriginTransform.position;

		return pos;
	}

	private static WeaponFireSoundProfile ResolveFireSoundProfile(
		AmmoDefinition _ammo,
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition _suppressor,
		ref float _volumeMultiplier)
	{
		if (_ammo != null && _ammo.FireSoundOverrideProfile != null && _ammo.FireSoundOverrideProfile.HasAnyClips)
			return _ammo.FireSoundOverrideProfile;

		if (_suppressor != null)
		{
			WeaponFireSoundProfile dedicatedSuppressedProfile = ResolveDedicatedSuppressedFireSoundProfile(_weapon, _suppressor);
			if (dedicatedSuppressedProfile != null)
			{
				if (_ammo != null && _ammo.IsSubsonic)
					_volumeMultiplier *= c_SubsonicSuppressedVolumeMultiplier;

				return dedicatedSuppressedProfile;
			}

			_volumeMultiplier *= _suppressor.SuppressedFireVolumeMultiplier;

			if (_ammo != null && _ammo.IsSubsonic)
				_volumeMultiplier *= c_SubsonicSuppressedVolumeMultiplier;
		}

		if (_weapon != null && _weapon.FireSoundProfile != null && _weapon.FireSoundProfile.HasAnyClips)
			return _weapon.FireSoundProfile;

		return null;
	}

	private static WeaponFireSoundProfile ResolveDedicatedSuppressedFireSoundProfile(
		WeaponDefinition _weapon,
		WeaponAttachmentDefinition _suppressor)
	{
		if (_weapon != null)
		{
			WeaponFireSoundProfile weaponSuppressedProfile = _weapon.SuppressedFireSoundProfile;
			if (weaponSuppressedProfile != null && weaponSuppressedProfile.HasAnyClips)
				return weaponSuppressedProfile;
		}

		if (_suppressor != null)
		{
			WeaponFireSoundProfile suppressorProfile = _suppressor.SuppressedFireSoundProfile;
			if (suppressorProfile != null && suppressorProfile.HasAnyClips)
				return suppressorProfile;
		}

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
