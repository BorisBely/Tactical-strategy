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

	#region Public Methods
	public static void TryPlayAddSound(ItemDefinition _definition, Vector3 _position)
	{
		if (_definition == null || !_definition.TryPickInventoryAddSound(out AudioClip clip))
			return;

		AudioSource.PlayClipAtPoint(clip, _position, _definition.InventoryAddSoundVolume);
	}

	public static void TryPlayRemoveSound(ItemDefinition _definition, Vector3 _position)
	{
		if (_definition == null || !_definition.TryPickInventoryRemoveSound(out AudioClip clip))
			return;

		AudioSource.PlayClipAtPoint(clip, _position, _definition.InventoryRemoveSoundVolume);
	}

	public static void TryPlayAddSoundFromSlot(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		bool _useMainHandPosition = false)
	{
		TryPlayAddSoundFromSlot(_data, _inventory, _useMainHandPosition);
	}

	public static void TryPlayAddSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		bool _useMainHandPosition = false)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return;

		TryPlayAddSound(_data.Definition, ResolveAddAudioPosition(_inventoryOrNull, _useMainHandPosition));
	}

	public static void TryPlayRemoveSoundFromSlot(
		CharacterInventory _inventory,
		InventorySlotRuntimeData _data,
		WorldPickupItem _spawnedOrNull)
	{
		TryPlayRemoveSoundFromSlot(_data, _inventory, _spawnedOrNull);
	}

	public static void TryPlayRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		WorldPickupItem _spawnedOrNull = null)
	{
		if (_data.IsEmpty || _data.Definition == null)
			return;

		TryPlayRemoveSound(_data.Definition, ResolveRemoveAudioPosition(_inventoryOrNull, _spawnedOrNull));
	}
	#endregion

	#region Private Methods
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

	private static Vector3 ResolveRemoveAudioPosition(CharacterInventory _inventoryOrNull, WorldPickupItem _spawnedOrNull)
	{
		if (_spawnedOrNull != null)
			return _spawnedOrNull.transform.position;

		if (_inventoryOrNull != null)
		{
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
