using UnityEngine;

/// <summary>
/// Общие звуки установки / снятия модулей оружия (кроме магазинов).
/// Загружается из <see cref="Resources"/> по пути Audio/WeaponModificationAudioSettings.
/// </summary>
[CreateAssetMenu(fileName = "WeaponModificationAudioSettings", menuName = "Game/Audio/Weapon Modification Audio Settings")]
public sealed class WeaponModificationAudioSettings : ScriptableObject
{
	#region Serialized Fields
	[SerializeField] private WeaponRandomAudioClipSet m_AttachmentAttachSounds = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_AttachmentAttachSoundVolume = 0.9f;
	[SerializeField] private WeaponRandomAudioClipSet m_AttachmentDetachSounds = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_AttachmentDetachSoundVolume = 0.85f;
	#endregion

	#region Public Methods
	public bool TryPickAttachmentAttachSound(out AudioClip _clip) => m_AttachmentAttachSounds.TryPickClip(out _clip);

	public bool TryPickAttachmentDetachSound(out AudioClip _clip) => m_AttachmentDetachSounds.TryPickClip(out _clip);

	public float AttachmentAttachSoundVolume => m_AttachmentAttachSoundVolume;

	public float AttachmentDetachSoundVolume => m_AttachmentDetachSoundVolume;
	#endregion
}
