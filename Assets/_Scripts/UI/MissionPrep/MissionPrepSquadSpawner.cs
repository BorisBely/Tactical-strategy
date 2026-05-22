using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Спавн отряда на экране предмиссии: строки UI — из префаба ячейки (родитель = Content Scroll View) и/или из массива в <see cref="MissionPrepUnitListView"/>.
/// Точки спавна юнитов в мире опциональны; если массив пуст — позиции <c>m_SpawnAnchor.position + i * m_AutoSpawnPositionStep</c>.
/// Rank icon and preset label are left untouched. Display names are random placeholders until a real unit profile exists in the project.
/// Заспавненные корни регистрируются как «только витрина»: отключаются ввод движения/стоек/готов, <see cref="UnitVision"/>;
/// <see cref="RtsUnitSelectionManager"/> не выделяет таких юнитов (инвентарь по выбору к ним не привязывается).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepSquadSpawner : MonoBehaviour
{
	#region Constants
	private static readonly HashSet<GameObject> s_PresentationUnitRoots = new HashSet<GameObject>();

	private static readonly string[] s_CallSignPrefixes =
	{
		"Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Ghost", "Hawk", "Iron", "Jackal"
	};
	#endregion

	#region Private Fields
	[SerializeField, Min(1)] private int m_SquadSize = 5;
	[SerializeField] private GameObject m_UnitPrefab;
	[SerializeField] private Transform m_SpawnedUnitsParent;
	[SerializeField] private Transform m_SpawnAnchor;
	[SerializeField] private Vector3 m_AutoSpawnPositionStep = new Vector3(2f, 0f, 0f);
	[SerializeField] private Transform[] m_SpawnPoints = System.Array.Empty<Transform>();
	[SerializeField] private MissionPrepUnitListView m_UnitList;
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[Header("UI cells (optional)")]
	[Tooltip("Префаб строки со скриптом MissionPrepUnitCellView. Родитель — RectTransform контента списка (Scroll View → Viewport → Content).")]
	[SerializeField] private MissionPrepUnitCellView m_UnitCellPrefab;
	[SerializeField] private RectTransform m_CellsContentParent;
	[SerializeField] private bool m_DestroyRuntimeUiCellsWhenDisabled = true;
	[SerializeField] private bool m_SpawnOnStart = true;
	[SerializeField] private bool m_ClearCellBindingsBeforeSpawn = true;
	[SerializeField] private bool m_DestroySpawnedWhenDisabled = true;
	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(16);
	private readonly List<GameObject> m_RuntimeCellInstances = new List<GameObject>(16);
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
		if (m_UnitPrefab == null)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: assign Unit Prefab.", this);
			return;
		}

		if (m_UnitList == null)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: assign Unit List.", this);
			return;
		}

		if (!TryEnsureUnitCellsReady())
			return;

		DestroySpawnedInstances();

		if (m_ClearCellBindingsBeforeSpawn)
			m_UnitList.ClearAllUnitBindings();

		MissionPrepSharedPresetStore sharedStore = MissionPrepSharedPresetStore.GetOrCreate(this);
		if (sharedStore != null)
		{
			int presetCount = m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0 ? m_PresetCatalog.PresetCount : 2;
			sharedStore.EnsurePresetSnapshots(presetCount);
			sharedStore.EnsureDefaultsFromCatalog(m_PresetCatalog);
		}

		int cellCount = m_UnitList.UnitCellCount;
		if (cellCount <= 0)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: no unit cells after setup.", this);
			return;
		}

		int slots = Mathf.Min(m_SquadSize, cellCount);
		if (slots < m_SquadSize)
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepSquadSpawner)} on {name}: squad size {m_SquadSize} > cells {cellCount}; spawning {slots}.",
				this);
		}

		Transform parent = m_SpawnedUnitsParent != null ? m_SpawnedUnitsParent : transform;

		for (int i = 0; i < slots; i++)
		{
			Vector3 position = GetSpawnPosition(i);
			Quaternion rotation = GetSpawnRotation(i);

			GameObject instance = Instantiate(m_UnitPrefab, position, rotation, parent);
			m_SpawnedInstances.Add(instance);
			s_PresentationUnitRoots.Add(instance);
			ApplyPresentationLockdown(instance);
			MissionPrepUnitPresetState presetState = MissionPrepUnitPresetState.GetOrCreate(instance, 0);

			CharacterInventory inventory = instance.GetComponentInChildren<CharacterInventory>(true);
			if (inventory != null)
				presetState.ApplyActivePresetToRuntime(inventory);

			MissionPrepUnitArmorVisualController.GetOrCreate(instance, presetState.ArmorVisualIndex);

			MissionPrepUnitCellView cell = m_UnitList.GetUnitCell(i);
			if (cell != null)
			{
				cell.BindToUnit(instance, GenerateRandomCallsign());
				cell.SetPresetDisplayName(GetDefaultPresetLabelForSpawnedUnit());
			}
		}
	}
	#endregion

	#region Private Methods
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
			m_UnitList.SetUnitCells(System.Array.Empty<MissionPrepUnitCellView>());
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

	private static void ApplyPresentationLockdown(GameObject _root)
	{
		if (_root == null)
			return;

		UnitClickToMove clickToMove = _root.GetComponentInChildren<UnitClickToMove>(true);
		if (clickToMove != null)
			clickToMove.SetDirectInputEnabled(false);

		UnitAnimatorStance stance = _root.GetComponentInChildren<UnitAnimatorStance>(true);
		if (stance != null)
			stance.SetKeyboardInputEnabled(false);

		UnitWeaponReadyHandsLayer ready = _root.GetComponentInChildren<UnitWeaponReadyHandsLayer>(true);
		if (ready != null)
			ready.SetKeyboardInputEnabled(false);

		UnitVision vision = _root.GetComponentInChildren<UnitVision>(true);
		if (vision != null)
			vision.enabled = false;
	}

	private string GetDefaultPresetLabelForSpawnedUnit()
	{
		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.GetPresetLabel(0);

		return LocalizationManager.Get("mission_prep.equipment.preset.standard", "Standard");
	}

	private static string GenerateRandomCallsign()
	{
		string prefix = s_CallSignPrefixes[Random.Range(0, s_CallSignPrefixes.Length)];
		int number = Random.Range(10, 100);
		return $"{prefix}-{number:D2}";
	}
	#endregion
}
