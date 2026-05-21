using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Снаряжение юнита: у каждого пресета один снимок (броня + инвентарь).
/// Активен только <see cref="PresetCatalogIndex"/>; правки UI пишутся в снимок активного пресета.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitPresetState : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(0)] private int m_PresetCatalogIndex;
	[SerializeField] private List<MissionPrepPresetSnapshot> m_PresetSnapshots = new List<MissionPrepPresetSnapshot>();
	#endregion

	#region Public Properties
	/// <summary>Индекс активного пресета в каталоге.</summary>
	public int PresetCatalogIndex => m_PresetCatalogIndex;

	/// <summary>Броня активного пресета (из его снимка).</summary>
	public int ActivePresetArmorIndex => GetArmorForPreset(m_PresetCatalogIndex);

	public int ArmorVisualIndex => ActivePresetArmorIndex;
	#endregion

	#region Public Methods
	public MissionPrepPresetSnapshot GetActiveSnapshot()
	{
		EnsureSnapshotExists(m_PresetCatalogIndex);
		return m_PresetSnapshots[m_PresetCatalogIndex];
	}

	public MissionPrepPresetSnapshot GetSnapshot(int _presetIndex)
	{
		EnsureSnapshotExists(_presetIndex);
		return m_PresetSnapshots[_presetIndex];
	}

	public void SetActivePresetIndex(int _index, int _presetSlotCount)
	{
		EnsurePresetSnapshots(_presetSlotCount);
		m_PresetCatalogIndex = Mathf.Clamp(_index, 0, m_PresetSnapshots.Count - 1);
	}

	public void SetPresetCatalogIndex(int _index)
	{
		m_PresetCatalogIndex = Mathf.Max(0, _index);
	}

	public int GetArmorForPreset(int _presetIndex)
	{
		if (_presetIndex < 0 || _presetIndex >= m_PresetSnapshots.Count || m_PresetSnapshots[_presetIndex] == null)
			return MissionPrepUnitArmorVisualController.LightArmorIndex;

		return m_PresetSnapshots[_presetIndex].ArmorVisualIndex;
	}

	public void SetArmorOnActivePreset(int _armorIndex)
	{
		EnsureSnapshotExists(m_PresetCatalogIndex);
		m_PresetSnapshots[m_PresetCatalogIndex].SetArmorVisualIndex(_armorIndex);
	}

	public void SetArmorVisualIndex(int _index) => SetArmorOnActivePreset(_index);

	public void SetArmorForActivePreset(int _armorIndex) => SetArmorOnActivePreset(_armorIndex);

	public void EnsurePresetSnapshots(int _presetCount)
	{
		int count = Mathf.Max(1, _presetCount);
		while (m_PresetSnapshots.Count < count)
			m_PresetSnapshots.Add(new MissionPrepPresetSnapshot());

		for (int i = m_PresetSnapshots.Count - 1; i >= count; i--)
			m_PresetSnapshots.RemoveAt(i);
	}

	/// <summary>Записать инвентарь и броню активного пресета в его снимок.</summary>
	public void SaveActivePresetFromRuntime(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		EnsureSnapshotExists(m_PresetCatalogIndex);
		int armor = GetArmorForPreset(m_PresetCatalogIndex);
		m_PresetSnapshots[m_PresetCatalogIndex].SetFromInventory(_inventory, armor);
	}

	public void CapturePresetFromRuntime(int _presetIndex, CharacterInventory _inventory, int? _armorVisualIndex = null)
	{
		EnsureSnapshotExists(_presetIndex);
		int armor = _armorVisualIndex ?? GetArmorForPreset(_presetIndex);
		m_PresetSnapshots[_presetIndex].SetFromInventory(_inventory, armor);
	}

	/// <summary>Применить снимок активного пресета к runtime-инвентарю.</summary>
	public void ApplyActivePresetToRuntime(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		EnsureSnapshotExists(m_PresetCatalogIndex);
		m_PresetSnapshots[m_PresetCatalogIndex].ApplyToInventory(_inventory);
	}

	public void ApplyPresetToRuntime(int _presetIndex, CharacterInventory _inventory)
	{
		EnsureSnapshotExists(_presetIndex);
		m_PresetSnapshots[_presetIndex].ApplyToInventory(_inventory);
	}

	/// <summary>Сохранить активный пресет и переключить индекс (без применения к runtime).</summary>
	public void ChangeActivePresetIndex(int _newPresetIndex, CharacterInventory _inventory, int _presetSlotCount)
	{
		EnsurePresetSnapshots(_presetSlotCount);

		if (_inventory != null)
			SaveActivePresetFromRuntime(_inventory);

		m_PresetCatalogIndex = Mathf.Clamp(_newPresetIndex, 0, m_PresetSnapshots.Count - 1);
	}

	public void SwitchPreset(int _newPresetIndex, CharacterInventory _inventory, int _presetSlotCount)
	{
		ChangeActivePresetIndex(_newPresetIndex, _inventory, _presetSlotCount);

		if (_inventory != null)
			ApplyActivePresetToRuntime(_inventory);
	}

	public void EnsureDefaultsFromCatalog(MissionPrepEquipmentPresetCatalog _catalog)
	{
		if (_catalog == null)
			return;

		int presetCount = _catalog.PresetCount > 0 ? _catalog.PresetCount : 2;
		EnsurePresetSnapshots(presetCount);

		for (int i = 0; i < m_PresetSnapshots.Count; i++)
			EnsureSnapshotDefaultsFromCatalog(i, _catalog);
	}

	/// <summary>Заполнить снимок пресета стартовым инвентарём из каталога, если он ещё пустой.</summary>
	public void EnsureSnapshotDefaultsFromCatalog(int _presetIndex, MissionPrepEquipmentPresetCatalog _catalog)
	{
		if (_catalog == null)
			return;

		EnsureSnapshotExists(_presetIndex);
		MissionPrepPresetSnapshot snapshot = m_PresetSnapshots[_presetIndex];
		if (snapshot == null)
			return;

		MissionPrepEquipmentPresetCatalog.PresetEntry entry = _catalog.GetPresetEntry(_presetIndex);
		if (entry == null)
			return;

		bool needsDefaults = !snapshot.HasInventoryContent();
		if (!needsDefaults && entry.WeaponItem != null && snapshot.MainHandEquipment.IsEmpty)
			needsDefaults = true;

		if (!needsDefaults && entry.SpareLoadedMagazinesInBag > 0 && entry.MagazineItem != null)
		{
			int spareMagazinesInBag = CountBagItemsMatchingDefinition(snapshot, entry.MagazineItem);
			if (spareMagazinesInBag < entry.SpareLoadedMagazinesInBag)
				needsDefaults = true;
		}

		if (!needsDefaults)
			return;

		_catalog.ApplyDefaultLoadoutToSnapshot(_presetIndex, snapshot);
	}

	public bool HasAnySnapshotInventory()
	{
		for (int i = 0; i < m_PresetSnapshots.Count; i++)
		{
			MissionPrepPresetSnapshot snapshot = m_PresetSnapshots[i];
			if (snapshot != null && snapshot.HasInventoryContent())
				return true;
		}

		return false;
	}

	public void InitializeDefaultsFromCatalog(MissionPrepEquipmentPresetCatalog _catalog, bool _overwriteExistingInventory = false)
	{
		if (_catalog == null)
			return;

		EnsureDefaultsFromCatalog(_catalog);

		if (!_overwriteExistingInventory)
			return;

		int presetCount = _catalog.PresetCount > 0 ? _catalog.PresetCount : 2;
		EnsurePresetSnapshots(presetCount);

		for (int i = 0; i < m_PresetSnapshots.Count; i++)
			_catalog.ApplyDefaultLoadoutToSnapshot(i, m_PresetSnapshots[i]);
	}

	public static MissionPrepUnitPresetState GetOrCreate(GameObject _unitRoot, int _defaultPresetIndex = 0)
	{
		if (_unitRoot == null)
			return null;

		if (!_unitRoot.TryGetComponent(out MissionPrepUnitPresetState state))
		{
			state = _unitRoot.AddComponent<MissionPrepUnitPresetState>();
			state.m_PresetCatalogIndex = Mathf.Max(0, _defaultPresetIndex);
			state.EnsurePresetSnapshots(2);
		}

		return state;
	}
	#endregion

	#region Private Methods
	private static int CountBagItemsMatchingDefinition(MissionPrepPresetSnapshot _snapshot, ItemDefinition _definition)
	{
		if (_snapshot == null || _definition == null)
			return 0;

		int count = 0;
		IReadOnlyList<InventorySlotRuntimeData> bagItems = _snapshot.BagItems;
		for (int i = 0; i < bagItems.Count; i++)
		{
			InventorySlotRuntimeData slot = bagItems[i];
			if (!slot.IsEmpty && slot.Definition == _definition)
				count++;
		}

		return count;
	}

	private void EnsureSnapshotExists(int _presetIndex)
	{
		while (m_PresetSnapshots.Count <= _presetIndex)
			m_PresetSnapshots.Add(new MissionPrepPresetSnapshot());

		if (m_PresetSnapshots[_presetIndex] == null)
			m_PresetSnapshots[_presetIndex] = new MissionPrepPresetSnapshot();
	}
	#endregion
}
