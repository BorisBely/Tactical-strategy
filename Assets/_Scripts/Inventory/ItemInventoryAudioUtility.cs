using UnityEngine;

/// <summary>
/// 3D one-shot при добавлении / убирании предмета из инвентаря (клипы из <see cref="ItemDefinition"/>).
/// Работает для runtime-инвентаря и редактора пресетов mission prep (через опциональный <see cref="CharacterInventory"/> превью-юнита).
/// </summary>
public static class ItemInventoryAudioUtility
{
	#region Constants
	private const float c_AddAudioHeightFallback = 1.35f;
	private const float c_RemoveAudioHeightOffset = 0.08f;
	#endregion

	#region Static Fields
	private static AudioSource s_NonSpatialAudioSource;
	#endregion

	#region Public Methods
	/// <summary>2D one-shot без spatial blend — для UI-окна пресетов, когда юнит далеко от камеры.</summary>
	public static void PlayClipNonSpatial(AudioClip _clip, float _volume)
	{
		if (_clip == null || _volume <= 0f)
			return;

		EnsureNonSpatialAudioSource();
		s_NonSpatialAudioSource.PlayOneShot(_clip, Mathf.Clamp01(_volume));
	}
	public static void TryPlayInventoryAddSound(
		ItemDefinition _definition,
		Vector3 _position,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_definition == null || !_definition.TryPickInventoryAddSound(out AudioClip clip))
			return;

		PlayInventoryClip(clip, _position, _definition.InventoryAddSoundVolume, _playNonSpatial, _nonSpatialVolumeScale);
	}

	public static void TryPlayInventoryRemoveSound(
		ItemDefinition _definition,
		Vector3 _position,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_definition == null || !_definition.TryPickInventoryRemoveSound(out AudioClip clip))
			return;

		PlayInventoryClip(clip, _position, _definition.InventoryRemoveSoundVolume, _playNonSpatial, _nonSpatialVolumeScale);
	}

	public static void TryPlayEquipmentAddSound(
		ItemDefinition _definition,
		Vector3 _position,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_definition == null || !_definition.TryPickEquipmentAddSound(out AudioClip clip))
			return;

		PlayInventoryClip(clip, _position, _definition.EquipmentAddSoundVolume, _playNonSpatial, _nonSpatialVolumeScale);
	}

	public static void TryPlayEquipmentRemoveSound(
		ItemDefinition _definition,
		Vector3 _position,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_definition == null || !_definition.TryPickEquipmentRemoveSound(out AudioClip clip))
			return;

		PlayInventoryClip(clip, _position, _definition.EquipmentRemoveSoundVolume, _playNonSpatial, _nonSpatialVolumeScale);
	}

	public static void TryPlayInventoryAddSoundFromSlot(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		bool _useMainHandPosition = false)
	{
		TryPlayInventoryAddSoundFromSlot(_data, _inventory, _useMainHandPosition);
	}

	public static void TryPlayInventoryAddSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		bool _useMainHandPosition = false,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return;

		TryPlayInventoryAddSound(
			_data.Definition,
			ResolveAddAudioPosition(_inventoryOrNull, _useMainHandPosition),
			_playNonSpatial,
			_nonSpatialVolumeScale);
	}

	public static void TryPlayInventoryRemoveSoundFromSlot(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		WorldPickupItem _spawnedOrNull)
	{
		TryPlayInventoryRemoveSoundFromSlot(_data, _inventory, _spawnedOrNull);
	}

	public static void TryPlayInventoryRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		WorldPickupItem _spawnedOrNull = null,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return;

		TryPlayInventoryRemoveSound(
			_data.Definition,
			ResolveRemoveAudioPosition(_inventoryOrNull, _spawnedOrNull),
			_playNonSpatial,
			_nonSpatialVolumeScale);
	}

	public static void TryPlayEquipmentAddSoundFromSlot(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		bool _useMainHandPosition = false)
	{
		TryPlayEquipmentAddSoundFromSlot(_data, _inventory, _useMainHandPosition);
	}

	public static void TryPlayEquipmentAddSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		bool _useMainHandPosition = false,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return;

		TryPlayEquipmentAddSound(
			_data.Definition,
			ResolveAddAudioPosition(_inventoryOrNull, _useMainHandPosition),
			_playNonSpatial,
			_nonSpatialVolumeScale);
	}

	public static void TryPlayEquipmentRemoveSoundFromSlot(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		WorldPickupItem _spawnedOrNull = null,
		bool _useMainHandPosition = false)
	{
		TryPlayEquipmentRemoveSoundFromSlot(_data, _inventory, _spawnedOrNull, _useMainHandPosition);
	}

	public static void TryPlayEquipmentRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		WorldPickupItem _spawnedOrNull = null,
		bool _useMainHandPosition = false,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return;

		TryPlayEquipmentRemoveSound(
			_data.Definition,
			ResolveRemoveAudioPosition(_inventoryOrNull, _spawnedOrNull, _useMainHandPosition),
			_playNonSpatial,
			_nonSpatialVolumeScale);
	}

	public static void TryPlayRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		WorldPickupItem _spawnedOrNull,
		bool _fromMainHandEquipmentSlot,
		bool _playNonSpatial = false,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_fromMainHandEquipmentSlot)
		{
			TryPlayEquipmentRemoveSoundFromSlot(
				_data,
				_inventoryOrNull,
				_spawnedOrNull,
				_useMainHandPosition: true,
				_playNonSpatial,
				_nonSpatialVolumeScale);
		}
		else
		{
			TryPlayInventoryRemoveSoundFromSlot(_data, _inventoryOrNull, _spawnedOrNull, _playNonSpatial, _nonSpatialVolumeScale);
		}
	}
	#endregion

	#region Private Methods
	private static void EnsureNonSpatialAudioSource()
	{
		if (s_NonSpatialAudioSource != null)
			return;

		GameObject audioRoot = new GameObject("InventoryNonSpatialAudio");
		Object.DontDestroyOnLoad(audioRoot);
		s_NonSpatialAudioSource = audioRoot.AddComponent<AudioSource>();
		s_NonSpatialAudioSource.playOnAwake = false;
		s_NonSpatialAudioSource.spatialBlend = 0f;
		s_NonSpatialAudioSource.dopplerLevel = 0f;
	}

	private static void PlayInventoryClip(
		AudioClip _clip,
		Vector3 _position,
		float _volume,
		bool _playNonSpatial,
		float _nonSpatialVolumeScale = 1f)
	{
		if (_playNonSpatial)
			PlayClipNonSpatial(_clip, _volume * _nonSpatialVolumeScale);
		else
			AudioSource.PlayClipAtPoint(_clip, _position, _volume);
	}

	private static Vector3 ResolveAddAudioPosition(CharacterInventory _inventoryOrNull, bool _useMainHandPosition)
	{
		if (_inventoryOrNull != null)
		{
			if (_useMainHandPosition)
			{
				UnitEquipment equipment = _inventoryOrNull.GetComponentInChildren<UnitEquipment>(true);
				if (equipment != null && equipment.MainWeaponRoot != null)
					return equipment.MainWeaponRoot.position;
			}

			return _inventoryOrNull.transform.position + Vector3.up * c_AddAudioHeightFallback;
		}

		return ResolveFallbackAudioPosition();
	}

	private static Vector3 ResolveRemoveAudioPosition(
		CharacterInventory _inventoryOrNull,
		WorldPickupItem _spawnedOrNull,
		bool _useMainHandPosition = false)
	{
		if (_spawnedOrNull != null)
			return _spawnedOrNull.transform.position;

		if (_inventoryOrNull != null)
		{
			if (_useMainHandPosition)
			{
				UnitEquipment equipment = _inventoryOrNull.GetComponentInChildren<UnitEquipment>(true);
				if (equipment != null && equipment.MainWeaponRoot != null)
					return equipment.MainWeaponRoot.position;
			}

			_inventoryOrNull.GetDropWorldPose(out Vector3 position, out _);
			return position + Vector3.up * c_RemoveAudioHeightOffset;
		}

		return ResolveFallbackAudioPosition();
	}

	private static Vector3 ResolveFallbackAudioPosition()
	{
		Camera mainCamera = Camera.main;
		if (mainCamera != null)
			return mainCamera.transform.position;

		return Vector3.up * c_AddAudioHeightFallback;
	}
	#endregion
}
