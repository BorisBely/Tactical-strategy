using UnityEngine;

/// <summary>
/// 3D one-shot при установке / снятии модулей оружия (не магазинов) в runtime-инвентаре и mission prep.
/// </summary>
public static class WeaponModificationAudioUtility
{
	#region Constants
	private const string c_SettingsResourcesPath = "Audio/WeaponModificationAudioSettings";
	private const float c_AudioHeightFallback = 1.35f;
	#endregion

	#region Static Fields
	private static WeaponModificationAudioSettings s_Settings;
	#endregion

	#region Public Methods
	public static bool IsAttachmentSlot(ItemModificationSlotDescriptor _slotDescriptor) =>
		_slotDescriptor.Kind == ItemModificationSlotKind.Attachment;

	public static void TryPlayAttachmentAttachSound(CharacterInventory _inventoryOrNull, bool _useMainHandPosition)
	{
		if (!TryPickAttachmentAttachSound(out AudioClip clip))
			return;

		AudioSource.PlayClipAtPoint(clip, ResolveAudioPosition(_inventoryOrNull, _useMainHandPosition), GetSettings().AttachmentAttachSoundVolume);
	}

	public static void TryPlayAttachmentDetachSound(CharacterInventory _inventoryOrNull, bool _useMainHandPosition)
	{
		if (!TryPickAttachmentDetachSound(out AudioClip clip))
			return;

		AudioSource.PlayClipAtPoint(clip, ResolveAudioPosition(_inventoryOrNull, _useMainHandPosition), GetSettings().AttachmentDetachSoundVolume);
	}
	#endregion

	#region Private Methods
	private static bool TryPickAttachmentAttachSound(out AudioClip _clip)
	{
		_clip = null;
		WeaponModificationAudioSettings settings = GetSettings();
		return settings != null && settings.TryPickAttachmentAttachSound(out _clip);
	}

	private static bool TryPickAttachmentDetachSound(out AudioClip _clip)
	{
		_clip = null;
		WeaponModificationAudioSettings settings = GetSettings();
		return settings != null && settings.TryPickAttachmentDetachSound(out _clip);
	}

	private static WeaponModificationAudioSettings GetSettings()
	{
		if (s_Settings != null)
			return s_Settings;

		s_Settings = Resources.Load<WeaponModificationAudioSettings>(c_SettingsResourcesPath);
		return s_Settings;
	}

	private static Vector3 ResolveAudioPosition(CharacterInventory _inventoryOrNull, bool _useMainHandPosition)
	{
		if (_inventoryOrNull != null)
		{
			if (_useMainHandPosition)
			{
				UnitEquipment equipment = _inventoryOrNull.GetComponentInChildren<UnitEquipment>(true);
				if (equipment != null && equipment.MainWeaponRoot != null)
					return equipment.MainWeaponRoot.position;
			}

			return _inventoryOrNull.transform.position + Vector3.up * c_AudioHeightFallback;
		}

		Camera mainCamera = Camera.main;
		if (mainCamera != null)
			return mainCamera.transform.position;

		return Vector3.up * c_AudioHeightFallback;
	}
	#endregion
}
