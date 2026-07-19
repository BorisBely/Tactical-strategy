using UnityEngine;

/// <summary>
/// Звуки инвентаря и модулей в UI-окнах (пресеты / runtime-инвентарь):
/// если юнит далеко от камеры, one-shot играет как 2D на окне, иначе — 3D на юните.
/// </summary>
public static class InventoryWindowAudioUtility
{
	#region Constants
	private const float c_UnitFarFromCameraDistanceMeters = 10f;
	private const float c_WindowVolumeMultiplier = 0.5f;
	#endregion

	#region Public Methods
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
		bool _useMainHandPosition = false)
	{
		ItemInventoryAudioUtility.TryPlayInventoryAddSoundFromSlot(
			_data,
			_inventoryOrNull,
			_useMainHandPosition,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
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
		WorldPickupItem _spawnedOrNull = null)
	{
		ItemInventoryAudioUtility.TryPlayInventoryRemoveSoundFromSlot(
			_data,
			_inventoryOrNull,
			_spawnedOrNull,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
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
		bool _useMainHandPosition = false)
	{
		ItemInventoryAudioUtility.TryPlayEquipmentAddSoundFromSlot(
			_data,
			_inventoryOrNull,
			_useMainHandPosition,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
	}

	public static void TryPlayEquipmentRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		bool _useMainHandPosition = false)
	{
		ItemInventoryAudioUtility.TryPlayEquipmentRemoveSoundFromSlot(
			_data,
			_inventoryOrNull,
			_spawnedOrNull: null,
			_useMainHandPosition,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
	}

	public static void TryPlayRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		bool _fromMainHandEquipmentSlot)
	{
		ItemInventoryAudioUtility.TryPlayRemoveSoundFromSlot(
			_data,
			_inventoryOrNull,
			_spawnedOrNull: null,
			_fromMainHandEquipmentSlot,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
	}

	public static void TryPlayRemoveSoundFromSlot(
		InventorySlotRuntimeData _data,
		CharacterInventory _inventoryOrNull,
		WorldPickupItem _spawnedOrNull,
		bool _fromMainHandEquipmentSlot)
	{
		ItemInventoryAudioUtility.TryPlayRemoveSoundFromSlot(
			_data,
			_inventoryOrNull,
			_spawnedOrNull,
			_fromMainHandEquipmentSlot,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
	}

	public static void TryPlayAttachmentAttachSound(CharacterInventory _inventoryOrNull, bool _useMainHandPosition)
	{
		WeaponModificationAudioUtility.TryPlayAttachmentAttachSound(
			_inventoryOrNull,
			_useMainHandPosition,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
	}

	public static void TryPlayAttachmentDetachSound(CharacterInventory _inventoryOrNull, bool _useMainHandPosition)
	{
		WeaponModificationAudioUtility.TryPlayAttachmentDetachSound(
			_inventoryOrNull,
			_useMainHandPosition,
			ShouldPlayAtWindow(_inventoryOrNull),
			c_WindowVolumeMultiplier);
	}
	#endregion

	#region Private Methods
	private static bool ShouldPlayAtWindow(CharacterInventory _inventoryOrNull)
	{
		if (!IsInventoryUiContextActive())
			return false;

		if (_inventoryOrNull == null)
			return true;

		Camera camera = Camera.main;
		if (camera == null)
			return true;

		float threshold = c_UnitFarFromCameraDistanceMeters;
		Vector3 delta = _inventoryOrNull.transform.position - camera.transform.position;
		return delta.sqrMagnitude > threshold * threshold;
	}

	private static bool IsInventoryUiContextActive()
	{
		if (MissionPrepScreenBindings.Instance != null && MissionPrepScreenBindings.Instance.IsMissionPrepOpen)
			return true;

		return InventoryScreenBindings.Instance != null && InventoryScreenBindings.Instance.IsInventoryOpen;
	}
	#endregion
}
