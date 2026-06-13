using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Общие снимки пресетов снаряжения (броня + инвентарь) по индексу каталога.
/// Все юниты с одним и тем же пресетом читают и пишут одни и те же данные.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepSharedPresetStore : MonoBehaviour
{
	#region Static Access
	private static MissionPrepSharedPresetStore s_Instance;

	public static MissionPrepSharedPresetStore Instance => s_Instance;
	#endregion

	#region Serialized Fields
	[SerializeField] private List<MissionPrepPresetSnapshot> m_PresetSnapshots = new List<MissionPrepPresetSnapshot>();
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (s_Instance != null && s_Instance != this)
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepSharedPresetStore)}: второй экземпляр на «{name}» игнорируется.",
				this);
			return;
		}

		s_Instance = this;
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Properties
	public int SnapshotCount => m_PresetSnapshots.Count;
	#endregion

	#region Public Methods
	public static MissionPrepSharedPresetStore GetOrCreate(MonoBehaviour _context)
	{
		if (s_Instance != null)
			return s_Instance;

		if (_context != null)
		{
			MissionPrepSharedPresetStore onContext = _context.GetComponent<MissionPrepSharedPresetStore>();
			if (onContext == null)
				onContext = _context.GetComponentInParent<MissionPrepSharedPresetStore>();

			if (onContext != null)
			{
				s_Instance = onContext;
				return s_Instance;
			}
		}

		s_Instance = FindAnyObjectByType<MissionPrepSharedPresetStore>();
		if (s_Instance != null)
			return s_Instance;

		if (_context == null)
			return null;

		s_Instance = _context.gameObject.AddComponent<MissionPrepSharedPresetStore>();
		return s_Instance;
	}

	public MissionPrepPresetSnapshot GetSnapshot(int _presetIndex)
	{
		EnsureSnapshotExists(_presetIndex);
		return m_PresetSnapshots[_presetIndex];
	}

	public int GetArmorForPreset(int _presetIndex)
	{
		if (_presetIndex < 0 || _presetIndex >= m_PresetSnapshots.Count || m_PresetSnapshots[_presetIndex] == null)
			return MissionPrepUnitArmorVisualController.LightArmorIndex;

		return m_PresetSnapshots[_presetIndex].ArmorVisualIndex;
	}

	public void SetArmorForPreset(int _presetIndex, int _armorIndex)
	{
		EnsureSnapshotExists(_presetIndex);
		m_PresetSnapshots[_presetIndex].SetArmorVisualIndex(_armorIndex);
	}

	public int GetCamouflageForPreset(int _presetIndex)
	{
		if (_presetIndex < 0 || _presetIndex >= m_PresetSnapshots.Count || m_PresetSnapshots[_presetIndex] == null)
			return 0;

		return m_PresetSnapshots[_presetIndex].CamouflageIndex;
	}

	public void SetCamouflageForPreset(int _presetIndex, int _camouflageIndex)
	{
		EnsureSnapshotExists(_presetIndex);
		m_PresetSnapshots[_presetIndex].SetCamouflageIndex(_camouflageIndex);
	}

	public void EnsurePresetSnapshots(int _presetCount)
	{
		int count = Mathf.Max(1, _presetCount);
		while (m_PresetSnapshots.Count < count)
			m_PresetSnapshots.Add(new MissionPrepPresetSnapshot());

		for (int i = m_PresetSnapshots.Count - 1; i >= count; i--)
			m_PresetSnapshots.RemoveAt(i);
	}

	public void AddEmptySnapshot()
	{
		m_PresetSnapshots.Add(new MissionPrepPresetSnapshot());
	}

	public void RemoveSnapshotAt(int _presetIndex)
	{
		if (_presetIndex < 0 || _presetIndex >= m_PresetSnapshots.Count)
			return;

		m_PresetSnapshots.RemoveAt(_presetIndex);
	}

	public void SavePresetFromRuntime(int _presetIndex, CharacterInventory _inventory, int _armorVisualIndex)
	{
		EnsureSnapshotExists(_presetIndex);
		m_PresetSnapshots[_presetIndex].SetFromInventory(_inventory, _armorVisualIndex);
	}

	public void ApplyPresetToInventory(int _presetIndex, CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		EnsureSnapshotExists(_presetIndex);
		m_PresetSnapshots[_presetIndex].ApplyToInventory(_inventory);
	}

	public void EnsureDefaultsFromCatalog(MissionPrepEquipmentPresetCatalog _catalog)
	{
		if (_catalog == null)
			return;

		int builtInCount = _catalog.PresetCount > 0 ? _catalog.PresetCount : 2;
		EnsureSnapshotExists(builtInCount - 1);

		for (int i = 0; i < builtInCount && i < m_PresetSnapshots.Count; i++)
			EnsureSnapshotDefaultsFromCatalog(i, _catalog);
	}

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
