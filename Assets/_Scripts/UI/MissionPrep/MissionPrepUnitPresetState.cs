using UnityEngine;

/// <summary>
/// Какой пресет каталога выбран у юнита. Броня и инвентарь хранятся в <see cref="MissionPrepSharedPresetStore"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitPresetState : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(0)] private int m_PresetCatalogIndex;
	#endregion

	#region Public Properties
	public int PresetCatalogIndex => m_PresetCatalogIndex;

	public int ActivePresetArmorIndex => GetArmorForPreset(m_PresetCatalogIndex);

	public int ArmorVisualIndex => ActivePresetArmorIndex;
	#endregion

	#region Public Methods
	public MissionPrepPresetSnapshot GetActiveSnapshot()
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		return store != null ? store.GetSnapshot(m_PresetCatalogIndex) : null;
	}

	public MissionPrepPresetSnapshot GetSnapshot(int _presetIndex)
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		return store != null ? store.GetSnapshot(_presetIndex) : null;
	}

	public void SetActivePresetIndex(int _index, int _presetSlotCount)
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		if (store != null)
			store.EnsurePresetSnapshots(_presetSlotCount);

		m_PresetCatalogIndex = store != null
			? Mathf.Clamp(_index, 0, Mathf.Max(0, _presetSlotCount - 1))
			: Mathf.Max(0, _index);
	}

	public void SetPresetCatalogIndex(int _index)
	{
		m_PresetCatalogIndex = Mathf.Max(0, _index);
	}

	public void AdjustPresetCatalogIndexAfterDeletion(int _deletedIndex)
	{
		if (_deletedIndex < 0)
			return;

		if (m_PresetCatalogIndex == _deletedIndex)
			m_PresetCatalogIndex = 0;
		else if (m_PresetCatalogIndex > _deletedIndex)
			m_PresetCatalogIndex--;
	}

	public int GetArmorForPreset(int _presetIndex)
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		return store != null
			? store.GetArmorForPreset(_presetIndex)
			: MissionPrepUnitArmorVisualController.LightArmorIndex;
	}

	public int GetCamouflageForPreset(int _presetIndex)
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		return store != null
			? store.GetCamouflageForPreset(_presetIndex)
			: 0;
	}

	public void SetArmorOnActivePreset(int _armorIndex)
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		store?.SetArmorForPreset(m_PresetCatalogIndex, _armorIndex);
	}

	public void SetArmorVisualIndex(int _index) => SetArmorOnActivePreset(_index);

	public void SetArmorForActivePreset(int _armorIndex) => SetArmorOnActivePreset(_armorIndex);

	public void SetCamouflageOnActivePreset(int _camouflageIndex)
	{
		ResolveStore()?.SetCamouflageForPreset(m_PresetCatalogIndex, _camouflageIndex);
	}

	public void SetCamouflageForActivePreset(int _camouflageIndex) => SetCamouflageOnActivePreset(_camouflageIndex);

	public void EnsurePresetSnapshots(int _presetCount)
	{
		ResolveStore()?.EnsurePresetSnapshots(_presetCount);
	}

	public void SaveActivePresetFromRuntime(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		MissionPrepSharedPresetStore store = ResolveStore();
		if (store == null)
			return;

		int armor = GetArmorForPreset(m_PresetCatalogIndex);
		store.SavePresetFromRuntime(m_PresetCatalogIndex, _inventory, armor);
	}

	public void CapturePresetFromRuntime(int _presetIndex, CharacterInventory _inventory, int? _armorVisualIndex = null)
	{
		if (_inventory == null)
			return;

		MissionPrepSharedPresetStore store = ResolveStore();
		if (store == null)
			return;

		int armor = _armorVisualIndex ?? GetArmorForPreset(_presetIndex);
		store.SavePresetFromRuntime(_presetIndex, _inventory, armor);
	}

	public void ApplyActivePresetToRuntime(CharacterInventory _inventory)
	{
		ResolveStore()?.ApplyPresetToInventory(m_PresetCatalogIndex, _inventory);
		RefreshInventoryBodyDecorations(_inventory);
	}

	public void ApplyPresetToRuntime(int _presetIndex, CharacterInventory _inventory)
	{
		ResolveStore()?.ApplyPresetToInventory(_presetIndex, _inventory);
		RefreshInventoryBodyDecorations(_inventory);
	}

	public void ChangeActivePresetIndex(int _newPresetIndex, CharacterInventory _inventory, int _presetSlotCount)
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		if (store != null)
			store.EnsurePresetSnapshots(_presetSlotCount);

		if (_inventory != null)
			SaveActivePresetFromRuntime(_inventory);

		m_PresetCatalogIndex = store != null
			? Mathf.Clamp(_newPresetIndex, 0, Mathf.Max(0, _presetSlotCount - 1))
			: Mathf.Max(0, _newPresetIndex);
	}

	public void SwitchPreset(int _newPresetIndex, CharacterInventory _inventory, int _presetSlotCount)
	{
		ChangeActivePresetIndex(_newPresetIndex, _inventory, _presetSlotCount);

		if (_inventory != null)
			ApplyActivePresetToRuntime(_inventory);
	}

	public void EnsureDefaultsFromCatalog(MissionPrepEquipmentPresetCatalog _catalog)
	{
		ResolveStore()?.EnsureDefaultsFromCatalog(_catalog);
	}

	public void EnsureSnapshotDefaultsFromCatalog(int _presetIndex, MissionPrepEquipmentPresetCatalog _catalog)
	{
		ResolveStore()?.EnsureSnapshotDefaultsFromCatalog(_presetIndex, _catalog);
	}

	public bool HasAnySnapshotInventory()
	{
		MissionPrepSharedPresetStore store = ResolveStore();
		if (store == null)
			return false;

		for (int i = 0; i < store.SnapshotCount; i++)
		{
			MissionPrepPresetSnapshot snapshot = store.GetSnapshot(i);
			if (snapshot != null && snapshot.HasInventoryContent())
				return true;
		}

		return false;
	}

	public void InitializeDefaultsFromCatalog(MissionPrepEquipmentPresetCatalog _catalog, bool _overwriteExistingInventory = false)
	{
		ResolveStore()?.InitializeDefaultsFromCatalog(_catalog, _overwriteExistingInventory);
	}

	public static MissionPrepUnitPresetState GetOrCreate(GameObject _unitRoot, int _defaultPresetIndex = 0)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out MissionPrepUnitPresetState state))
		{
			state = _unitRoot.AddComponent<MissionPrepUnitPresetState>();
			state.m_PresetCatalogIndex = Mathf.Max(0, _defaultPresetIndex);
		}

		MissionPrepSharedPresetStore.GetOrCreate(state);
		return state;
	}
	#endregion

	#region Private Methods
	private MissionPrepSharedPresetStore ResolveStore()
	{
		return MissionPrepSharedPresetStore.GetOrCreate(this);
	}

	private static void RefreshInventoryBodyDecorations(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		UnitInventoryBodyDecorations decorations = _inventory.GetComponentInParent<UnitInventoryBodyDecorations>(true);
		if (decorations == null)
			decorations = _inventory.GetComponentInChildren<UnitInventoryBodyDecorations>(true);

		decorations?.RefreshFromInventory(_inventory);
	}
	#endregion
}
