using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Спавн player-отряда для mission prep: каждый юнит получает свой preset index и runtime-инвентарь из snapshot.
/// Изменения пресета или инвентаря в UI сразу применяются к юнитам через <see cref="MissionPrepLoadoutCoordinator"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class MissionPrepSquadSpawner : MonoBehaviour
{
	#region Constants
	private const int c_StandardPresetIndex = 0;

	private static readonly HashSet<GameObject> s_PresentationUnitRoots = new HashSet<GameObject>();

	private static readonly string[] s_CallSignPrefixes =
	{
		"Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Ghost", "Hawk", "Iron", "Jackal"
	};
	#endregion

	#region Private Fields
	[SerializeField, Min(1)] private int m_SquadSize = 5;
	[SerializeField] private GameObject m_UnitPrefab;
	[Tooltip("Родитель заспавненных юнитов в мире. Должен быть на активном объекте сцены.")]
	[SerializeField] private Transform m_SpawnedUnitsParent;
	[SerializeField] private Transform m_SpawnAnchor;
	[SerializeField] private Vector3 m_AutoSpawnPositionStep = new Vector3(2f, 0f, 0f);
	[SerializeField] private Transform[] m_SpawnPoints = Array.Empty<Transform>();
	[SerializeField] private MissionPrepUnitListView m_UnitList;
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[Header("UI cells (optional)")]
	[Tooltip("Префаб строки со скриптом MissionPrepUnitCellView. Родитель — RectTransform контента списка (Scroll View → Viewport → Content).")]
	[SerializeField] private MissionPrepUnitCellView m_UnitCellPrefab;
	[SerializeField] private RectTransform m_CellsContentParent;
	[SerializeField] private bool m_DestroyRuntimeUiCellsWhenDisabled = true;
	[SerializeField] private bool m_SpawnOnStart = true;
	[SerializeField] private bool m_ClearCellBindingsBeforeSpawn = true;
	[Tooltip("Не уничтожать player-юнитов при закрытии экрана mission prep.")]
	[SerializeField] private bool m_DestroySpawnedWhenDisabled;
	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(16);
	private readonly List<GameObject> m_RuntimeCellInstances = new List<GameObject>(16);
	private static bool s_SceneLoadSpawnHandled;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (m_SpawnOnStart)
			SpawnAndBind();
	}

	private void OnDisable()
	{
		if (m_DestroySpawnedWhenDisabled)
		{
			if (m_UnitList != null)
				m_UnitList.ClearAllUnitBindings();

			DestroySpawnedInstances();
		}

		if (m_DestroyRuntimeUiCellsWhenDisabled)
			ClearRuntimeUiCells();
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void TrySpawnSquadAfterSceneLoad()
	{
		if (s_SceneLoadSpawnHandled)
			return;

		MissionPrepSquadSpawner[] spawners = FindObjectsByType<MissionPrepSquadSpawner>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None);
		for (int i = 0; i < spawners.Length; i++)
		{
			MissionPrepSquadSpawner spawner = spawners[i];
			if (spawner == null || !spawner.m_SpawnOnStart)
				continue;

			spawner.SpawnAndBind();
			s_SceneLoadSpawnHandled = true;
			return;
		}
	}
	#endregion

	#region Public Methods
	public static bool IsMissionPrepPresentationMember(RtsUnitMember _unit)
	{
		if (_unit == null)
			return false;

		for (Transform t = _unit.transform; t != null; t = t.parent)
		{
			if (s_PresentationUnitRoots.Contains(t.gameObject))
				return true;
		}

		return false;
	}

	public void SpawnAndBind()
	{
		if (m_UnitList == null)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: assign Unit List.", this);
			return;
		}

		if (!TryEnsureUnitCellsReady())
			return;

		PurgeNullSpawnedInstances();

		MissionPrepSharedPresetStore sharedStore = MissionPrepSharedPresetStore.GetOrCreate(this);
		MissionPrepRuntimePresetRegistry registry = MissionPrepRuntimePresetRegistry.GetOrCreate(this);

		if (HasLiveSquad())
		{
			if (sharedStore != null && registry != null)
			{
				PrepareStandardPresetOnly(sharedStore, registry);
				ApplyStandardPresetToSpawnedUnits();
			}

			RebindExistingSquad();
			return;
		}

		if (m_UnitPrefab == null)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: assign Unit Prefab.", this);
			return;
		}

		if (m_ClearCellBindingsBeforeSpawn)
			m_UnitList.ClearAllUnitBindings();

		DestroySpawnedInstances();
		SpawnPlayerSquad();
	}
	#endregion

	#region Private Methods
	private void SpawnPlayerSquad()
	{
		MissionPrepSharedPresetStore sharedStore = MissionPrepSharedPresetStore.GetOrCreate(this);
		MissionPrepRuntimePresetRegistry registry = MissionPrepRuntimePresetRegistry.GetOrCreate(this);
		if (sharedStore == null || registry == null)
			return;

		PrepareStandardPresetOnly(sharedStore, registry);

		int cellCount = m_UnitList.UnitCellCount;
		if (cellCount <= 0)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: no unit cells after setup.", this);
			return;
		}

		int slots = Mathf.Min(m_SquadSize, cellCount);
		Transform parent = ResolveActiveSpawnParent();

		for (int i = 0; i < slots; i++)
		{
			Vector3 position = GetSpawnPosition(i);
			Quaternion rotation = GetSpawnRotation(i);

			GameObject instance = Instantiate(m_UnitPrefab, position, rotation, parent);
			m_SpawnedInstances.Add(instance);

			DisableStarterLoadout(instance);
			ApplyPlayerUnitRole(instance);
			ConfigureUnitForStandardPreset(instance);
			UnitRosterDisplayState.GetOrCreate(instance)?.SetCallsign(GenerateRandomCallsign());

			MissionPrepUnitCellView cell = m_UnitList.GetUnitCell(i);
			if (cell != null)
			{
				UnitCellDisplayBinder.Apply(cell, instance);
				cell.SetInteractionEnabled(true);
			}
		}

		RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
		NotifyShootingRangeUiAfterSpawn();
	}

	private static void NotifyShootingRangeUiAfterSpawn()
	{
#if UNITY_2023_1_OR_NEWER
		ShootingRangeUiController uiController = UnityEngine.Object.FindAnyObjectByType<ShootingRangeUiController>(FindObjectsInactive.Exclude);
#else
		ShootingRangeUiController uiController = UnityEngine.Object.FindObjectOfType<ShootingRangeUiController>();
#endif
		uiController?.RefreshPanelState();
	}

	private void PrepareStandardPresetOnly(MissionPrepSharedPresetStore _sharedStore, MissionPrepRuntimePresetRegistry _registry)
	{
		_registry.ClearAllUserPresets();

		int builtInCount = m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0 ? m_PresetCatalog.PresetCount : 1;
		_registry.ConfigureBuiltInPresetCount(builtInCount);
		_sharedStore.EnsurePresetSnapshots(1);
		_sharedStore.EnsureSnapshotDefaultsFromCatalog(c_StandardPresetIndex, m_PresetCatalog);
	}

	private void ApplyStandardPresetToSpawnedUnits()
	{
		PurgeNullSpawnedInstances();

		for (int i = 0; i < m_SpawnedInstances.Count; i++)
		{
			GameObject instance = m_SpawnedInstances[i];
			if (instance != null)
				ConfigureUnitForStandardPreset(instance);
		}
	}

	private void ConfigureUnitForStandardPreset(GameObject _instance)
	{
		if (_instance == null)
			return;

		MissionPrepUnitPresetState presetState = MissionPrepUnitPresetState.GetOrCreate(_instance, c_StandardPresetIndex);
		presetState.SetActivePresetIndex(c_StandardPresetIndex, 1);

		CharacterInventory inventory = _instance.GetComponentInChildren<CharacterInventory>(true);
		if (inventory != null)
		{
			presetState.ApplyActivePresetToRuntime(inventory);
			UnitWeaponRuntime weaponRuntime = inventory.GetComponentInChildren<UnitWeaponRuntime>(true);
			if (weaponRuntime != null)
			{
				weaponRuntime.RefreshFromEquipment();
				weaponRuntime.RuntimeState?.EnsureValidSelectedFireMode();
			}
		}

		ApplyPresetVisualsToUnit(_instance, presetState);
		UnitIndividualTraits.GetOrCreate(_instance);
	}

	private void RebindExistingSquad()
	{
		PurgeNullSpawnedInstances();

		int slots = Mathf.Min(m_SquadSize, m_UnitList.UnitCellCount, m_SpawnedInstances.Count);
		for (int i = 0; i < slots; i++)
		{
			GameObject unitRoot = m_SpawnedInstances[i];
			if (unitRoot == null)
				continue;

			MissionPrepUnitCellView cell = m_UnitList.GetUnitCell(i);
			if (cell == null)
				continue;

			UnitCellDisplayBinder.Apply(cell, unitRoot);
			cell.SetInteractionEnabled(true);
		}
	}

	private bool HasLiveSquad()
	{
		PurgeNullSpawnedInstances();
		return m_SpawnedInstances.Count >= Mathf.Min(m_SquadSize, m_UnitList != null ? m_UnitList.UnitCellCount : m_SquadSize);
	}

	private void PurgeNullSpawnedInstances()
	{
		for (int i = m_SpawnedInstances.Count - 1; i >= 0; i--)
		{
			if (m_SpawnedInstances[i] == null)
				m_SpawnedInstances.RemoveAt(i);
		}
	}

	private Transform ResolveActiveSpawnParent()
	{
		if (m_SpawnedUnitsParent != null && m_SpawnedUnitsParent.gameObject.activeInHierarchy)
			return m_SpawnedUnitsParent;

		if (m_SpawnPoints != null)
		{
			for (int i = 0; i < m_SpawnPoints.Length; i++)
			{
				Transform spawnPoint = m_SpawnPoints[i];
				if (spawnPoint == null)
					continue;

				Transform root = spawnPoint.root;
				if (root != null && root.gameObject.activeInHierarchy)
					return root;
			}
		}

		return null;
	}

	private static void DisableStarterLoadout(GameObject _root)
	{
		if (_root == null)
			return;

		CharacterInventoryStarterLoadout[] starters = _root.GetComponentsInChildren<CharacterInventoryStarterLoadout>(true);
		for (int i = 0; i < starters.Length; i++)
		{
			CharacterInventoryStarterLoadout starter = starters[i];
			if (starter != null)
				starter.enabled = false;
		}
	}

	private static void ApplyPresetVisualsToUnit(GameObject _unitRoot, MissionPrepUnitPresetState _presetState)
	{
		if (_unitRoot == null || _presetState == null)
			return;

		int armorIndex = _presetState.ArmorVisualIndex;
		MissionPrepUnitArmorVisualController.GetOrCreate(_unitRoot, armorIndex).ApplyArmorVisual(armorIndex);
		UnitArmor armor = _unitRoot.GetComponent<UnitArmor>() ?? _unitRoot.AddComponent<UnitArmor>();
		armor.SetArmorFromPresetIndex(armorIndex);

		UnitCharacterMaterialAppearance materialAppearance = UnitCharacterMaterialAppearance.GetOrCreate(_unitRoot);
		if (materialAppearance != null)
			materialAppearance.SetCamouflageIndex(_presetState.GetCamouflageForPreset(_presetState.PresetCatalogIndex));
	}

	private bool TryEnsureUnitCellsReady()
	{
		bool wantRuntimeCells = m_UnitCellPrefab != null && m_CellsContentParent != null;
		if (!wantRuntimeCells)
		{
			if (m_UnitList.UnitCellCount > 0)
				return true;

			Debug.LogWarning(
				$"{nameof(MissionPrepSquadSpawner)} on {name}: задайте префаб ячейки + Cells Content Parent, либо заполните массив ячеек в MissionPrepUnitListView.",
				this);
			return false;
		}

		if (m_UnitList.UnitCellCount != m_SquadSize || m_RuntimeCellInstances.Count != m_SquadSize)
			BuildRuntimeUiCells();

		return m_UnitList.UnitCellCount > 0;
	}

	private void BuildRuntimeUiCells()
	{
		ClearRuntimeUiCells();

		var cells = new MissionPrepUnitCellView[m_SquadSize];
		for (int i = 0; i < m_SquadSize; i++)
		{
			MissionPrepUnitCellView cell = Instantiate(m_UnitCellPrefab, m_CellsContentParent);
			cell.gameObject.name = $"{m_UnitCellPrefab.name}_{i}";
			cells[i] = cell;
			m_RuntimeCellInstances.Add(cell.gameObject);
		}

		m_UnitList.SetUnitCells(cells);
	}

	private void ClearRuntimeUiCells()
	{
		for (int i = 0; i < m_RuntimeCellInstances.Count; i++)
		{
			GameObject cellObject = m_RuntimeCellInstances[i];
			if (cellObject != null)
				Destroy(cellObject);
		}

		m_RuntimeCellInstances.Clear();
		if (m_UnitList != null)
			m_UnitList.SetUnitCells(Array.Empty<MissionPrepUnitCellView>());
	}

	private Vector3 GetSpawnPosition(int _index)
	{
		if (m_SpawnPoints != null && _index < m_SpawnPoints.Length && m_SpawnPoints[_index] != null)
			return m_SpawnPoints[_index].position;

		Vector3 origin = m_SpawnAnchor != null ? m_SpawnAnchor.position : transform.position;
		return origin + m_AutoSpawnPositionStep * _index;
	}

	private Quaternion GetSpawnRotation(int _index)
	{
		if (m_SpawnPoints != null && _index < m_SpawnPoints.Length && m_SpawnPoints[_index] != null)
			return m_SpawnPoints[_index].rotation;

		return m_SpawnAnchor != null ? m_SpawnAnchor.rotation : Quaternion.identity;
	}

	private void DestroySpawnedInstances()
	{
		for (int i = 0; i < m_SpawnedInstances.Count; i++)
		{
			GameObject instance = m_SpawnedInstances[i];
			if (instance != null)
			{
				s_PresentationUnitRoots.Remove(instance);
				Destroy(instance);
			}
		}

		m_SpawnedInstances.Clear();
	}

	private static void ApplyPlayerUnitRole(GameObject _root)
	{
		if (_root == null)
			return;

		if (!_root.TryGetComponent(out UnitFactionConfigurator configurator))
			return;

		configurator.Configure(UnitFactionConfigurator.CreatePlayerConfig(new UnitSpawnLoadout(), false));
		configurator.ApplyConfiguration();
	}

	private static string GenerateRandomCallsign()
	{
		string prefix = s_CallSignPrefixes[UnityEngine.Random.Range(0, s_CallSignPrefixes.Length)];
		int number = UnityEngine.Random.Range(10, 100);
		return $"{prefix}-{number:D2}";
	}
	#endregion
}
