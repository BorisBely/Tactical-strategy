using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
	[Tooltip("Вторая колонка (на задание).")]
	[SerializeField] private MissionPrepUnitListView m_UnitList;
	[Tooltip("Первая колонка (на базе). Сюда попадают и юниты, и техника.")]
	[SerializeField] private MissionPrepUnitListView m_VehicleList;
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[Header("Vehicles (Mission Prep list)")]
	[SerializeField] private GameObject m_VehiclePrefab;
	[SerializeField, Min(0)] private int m_VehicleCount = 3;
	[SerializeField] private Vector3 m_VehicleSpawnOriginOffset = new Vector3(0f, 0f, 8f);
	[SerializeField] private Vector3 m_VehicleSpawnPositionStep = new Vector3(5f, 0f, 0f);
	[Header("UI cells (optional)")]
	[Tooltip("Префаб строки со скриптом MissionPrepUnitCellView. Родитель — RectTransform контента списка (Scroll View → Viewport → Content).")]
	[SerializeField] private MissionPrepUnitCellView m_UnitCellPrefab;
	[Tooltip("Content второй колонки (на задание). Стартует пустой, принимает drag юнитов и техники.")]
	[SerializeField] private RectTransform m_CellsContentParent;
	[Tooltip("Content первой колонки (на базе). Сюда спавнятся все юниты и машины.")]
	[SerializeField] private RectTransform m_VehicleCellsContentParent;
	[SerializeField] private bool m_DestroyRuntimeUiCellsWhenDisabled = true;
	[SerializeField] private bool m_SpawnOnStart = true;
	[SerializeField] private bool m_ClearCellBindingsBeforeSpawn = true;
	[Tooltip("Каждый заспавненный юнит получает пресет с тем же индексом (0 = первый пресет каталога).")]
	[SerializeField] private bool m_AssignPresetByUnitIndex = true;
	[Tooltip("Не уничтожать player-юнитов при закрытии экрана mission prep.")]
	[SerializeField] private bool m_DestroySpawnedWhenDisabled;
	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(16);
	private readonly List<VehicleController> m_SpawnedVehicles = new List<VehicleController>(4);
	private readonly List<GameObject> m_RuntimeCellInstances = new List<GameObject>(16);
	private readonly List<GameObject> m_RuntimeVehicleUiInstances = new List<GameObject>(32);
	private readonly List<MissionPrepUnitCellView> m_VehicleCells = new List<MissionPrepUnitCellView>(4);
	private readonly List<MissionPrepUnitCellView> m_UnitOnlyCells = new List<MissionPrepUnitCellView>(32);
	private readonly List<MissionPrepVehicleSeatSlotView> m_SeatSlots = new List<MissionPrepVehicleSeatSlotView>(32);
	private readonly List<VehicleSeatLayout.SeatBinding> m_SeatBuffer = new List<VehicleSeatLayout.SeatBinding>(8);
	private readonly MissionPrepVehicleAssignmentStore m_VehicleAssignments = new MissionPrepVehicleAssignmentStore();
	private static bool s_SceneLoadSpawnHandled;
	private static bool s_SpawningPresentationVehicles;
	private Coroutine m_DeferredSeatRefresh;
	#endregion

	#region Public Properties
	public MissionPrepVehicleAssignmentStore VehicleAssignments => m_VehicleAssignments;
	public IReadOnlyList<VehicleController> SpawnedVehicles => m_SpawnedVehicles;
	/// <summary>Первая колонка: доступные на базе юниты и техника.</summary>
	private RectTransform BaseRosterContent =>
		m_VehicleCellsContentParent != null ? m_VehicleCellsContentParent : m_CellsContentParent;
	/// <summary>Вторая колонка: состав на задание. Логика базы/миссии пока только UI.</summary>
	private RectTransform MissionRosterContent =>
		m_VehicleCellsContentParent != null ? m_CellsContentParent : null;
	/// <summary>True while Mission Prep instantiates presentation vehicles (Awake runs before marker component).</summary>
	public static bool IsSpawningPresentationVehicles => s_SpawningPresentationVehicles;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (m_SpawnOnStart)
			SpawnAndBind();
	}

	private void OnDisable()
	{
		m_VehicleAssignments.Changed -= HandleVehicleAssignmentsChanged;

		if (m_DestroySpawnedWhenDisabled)
		{
			if (m_UnitList != null)
				m_UnitList.ClearAllUnitBindings();
			if (m_VehicleList != null)
				m_VehicleList.ClearAllUnitBindings();

			DestroySpawnedInstances();
			DestroySpawnedVehicles();
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
			FindObjectsInactive.Include);
		for (int i = 0; i < spawners.Length; i++)
		{
			MissionPrepSquadSpawner spawner = spawners[i];
			if (spawner == null || !spawner.m_SpawnOnStart)
				continue;

			// Inactive GO не получает Start — активируем, чтобы lifecycle был нормальным.
			if (!spawner.gameObject.activeSelf)
				spawner.gameObject.SetActive(true);

			spawner.SpawnAndBind();
			s_SceneLoadSpawnHandled = true;
			return;
		}
	}
	#endregion

	#region Public Methods
	public void RefreshVehicleAssignmentUi()
	{
		RefreshSeatSlots();
	}

	/// <summary>
	/// Перенос строки (юнит) или блока машины между колонками. Без логики базы/миссии.
	/// </summary>
	public bool TryMoveRosterCellToColumn(MissionPrepUnitCellView _cell, RectTransform _targetContent)
	{
		if (_cell == null || _targetContent == null || _cell.IsInsideSeatSlot)
			return false;

		if (_cell.IsVehicleCell)
		{
			MissionPrepVehicleRosterBlock group = _cell.GetComponent<MissionPrepVehicleRosterBlock>();
			if (group == null)
				group = _cell.GetComponentInParent<MissionPrepVehicleRosterBlock>();

			if (group != null)
			{
				if (!group.MoveTo(_targetContent))
					return false;
			}
			else if (_cell.transform.parent != _targetContent)
			{
				_cell.transform.SetParent(_targetContent, false);
				_cell.transform.SetAsLastSibling();
			}
			else
			{
				return false;
			}
		}
		else
		{
			if (_cell.transform.parent == _targetContent)
				return false;

			_cell.transform.SetParent(_targetContent, false);
			_cell.transform.SetAsLastSibling();
		}

		SyncRosterListBindings();
		NormalizeUnitListContentLayout(BaseRosterContent);
		NormalizeUnitListContentLayout(MissionRosterContent);
		MissionPrepVehicleRosterBlock.SortVehiclesThenUnits(BaseRosterContent);
		MissionPrepVehicleRosterBlock.SortVehiclesThenUnits(MissionRosterContent);
		RefreshAssignedUnitCaptions();
		return true;
	}

	public static bool IsMissionPrepPresentationMember(RtsUnitMember _unit)
	{
		if (_unit == null)
			return false;

		// Отряд Mission Prep — это же боевые юниты сцены. Блокируем RTS только
		// пока открыт экран prep; после закрытия ими снова можно управлять.
		if (!IsMissionPrepInteractionLocked())
			return false;

		for (Transform t = _unit.transform; t != null; t = t.parent)
		{
			if (s_PresentationUnitRoots.Contains(t.gameObject))
				return true;
		}

		return false;
	}

	public static bool IsMissionPrepInteractionLocked()
	{
		MissionPrepScreenBindings bindings = MissionPrepScreenBindings.Instance;
		return bindings != null && bindings.IsMissionPrepOpen;
	}

	public void SpawnAndBind()
	{
		if (m_UnitList == null)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: assign Unit List.", this);
			return;
		}

		m_VehicleAssignments.Changed -= HandleVehicleAssignmentsChanged;
		m_VehicleAssignments.Changed += HandleVehicleAssignmentsChanged;

		if (!TryEnsureUnitCellsReady())
			return;

		PurgeNullSpawnedInstances();
		PurgeNullSpawnedVehicles();

		MissionPrepSharedPresetStore sharedStore = MissionPrepSharedPresetStore.GetOrCreate(this);
		MissionPrepRuntimePresetRegistry registry = MissionPrepRuntimePresetRegistry.GetOrCreate(this);

		if (HasLiveSquad())
		{
			if (sharedStore != null && registry != null)
			{
				PrepareBuiltInPresets(sharedStore, registry);
				ApplyPresetsToSpawnedUnits();
			}

			EnsureVehiclesSpawned();
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
		DestroySpawnedVehicles();
		m_VehicleAssignments.ClearAll();
		EnsureVehiclesSpawned();
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

		PrepareBuiltInPresets(sharedStore, registry);

		if (m_UnitOnlyCells.Count <= 0)
		{
			Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)} on {name}: no unit cells after setup.", this);
			return;
		}

		int slots = Mathf.Min(m_SquadSize, m_UnitOnlyCells.Count);
		Transform parent = ResolveActiveSpawnParent();

		for (int i = 0; i < slots; i++)
		{
			Vector3 position = GetSpawnPosition(i);
			Quaternion rotation = GetSpawnRotation(i);

			GameObject instance = Instantiate(m_UnitPrefab, position, rotation, parent);
			m_SpawnedInstances.Add(instance);
			s_PresentationUnitRoots.Add(instance);

			DisableStarterLoadout(instance);
			ApplyPlayerUnitRole(instance);
			int presetIndex = ResolvePresetIndexForUnit(i);
			ConfigureUnitForPreset(instance, presetIndex);
			UnitRosterDisplayState.GetOrCreate(instance)?.SetCallsign(ResolveCallsignForPreset(presetIndex));

			MissionPrepUnitCellView cell = m_UnitOnlyCells[i];
			if (cell != null)
			{
				UnitCellDisplayBinder.Apply(cell, instance);
				cell.SetInteractionEnabled(true);
			}
		}

		BindVehicleCells();
		RefreshSeatSlots();
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

	private void PrepareBuiltInPresets(MissionPrepSharedPresetStore _sharedStore, MissionPrepRuntimePresetRegistry _registry)
	{
		_registry.ClearAllUserPresets();

		int builtInCount = ResolveBuiltInPresetCount();
		_registry.ConfigureBuiltInPresetCount(builtInCount);
		_sharedStore.EnsurePresetSnapshots(builtInCount);

		_sharedStore.InitializeDefaultsFromCatalog(m_PresetCatalog, true);
	}

	private void ApplyPresetsToSpawnedUnits()
	{
		PurgeNullSpawnedInstances();

		for (int i = 0; i < m_SpawnedInstances.Count; i++)
		{
			GameObject instance = m_SpawnedInstances[i];
			if (instance != null)
				ConfigureUnitForPreset(instance, ResolvePresetIndexForUnit(i));
		}
	}

	private int ResolveBuiltInPresetCount()
	{
		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.PresetCount;

		return 1;
	}

	private int ResolvePresetIndexForUnit(int _unitIndex)
	{
		if (!m_AssignPresetByUnitIndex)
			return c_StandardPresetIndex;

		int presetCount = ResolveBuiltInPresetCount();
		if (presetCount <= 0)
			return c_StandardPresetIndex;

		return Mathf.Clamp(_unitIndex, 0, presetCount - 1);
	}

	private string ResolveCallsignForPreset(int _presetIndex)
	{
		if (m_AssignPresetByUnitIndex && m_PresetCatalog != null)
		{
			string presetLabel = m_PresetCatalog.GetPresetLabel(_presetIndex);
			if (!string.IsNullOrWhiteSpace(presetLabel))
				return presetLabel;
		}

		return GenerateRandomCallsign();
	}

	private void ConfigureUnitForPreset(GameObject _instance, int _presetIndex)
	{
		if (_instance == null)
			return;

		int presetCount = ResolveBuiltInPresetCount();
		int clampedPresetIndex = Mathf.Clamp(_presetIndex, 0, Mathf.Max(presetCount - 1, 0));

		MissionPrepUnitPresetState presetState = MissionPrepUnitPresetState.GetOrCreate(_instance, clampedPresetIndex);
		presetState.SetActivePresetIndex(clampedPresetIndex, presetCount);

		CharacterInventory inventory = _instance.GetComponentInChildren<CharacterInventory>(true);
		if (inventory != null)
		{
			if (TryApplyCatalogPresetDirectly(inventory, clampedPresetIndex))
				SyncSharedPresetFromInventory(clampedPresetIndex, inventory);
			else
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

	private bool TryApplyCatalogPresetDirectly(CharacterInventory _inventory, int _presetIndex)
	{
		if (!m_AssignPresetByUnitIndex || m_PresetCatalog == null || _inventory == null)
			return false;

		MissionPrepEquipmentPresetCatalog.PresetEntry entry = m_PresetCatalog.GetPresetEntry(_presetIndex);
		if (entry == null || !MissionPrepPresetDefaultLoadoutUtility.EntryDefinesInventory(entry, m_PresetCatalog.GrenadeThrowData, m_PresetCatalog.AlwaysIncludeBagItems))
			return false;

		MissionPrepPresetDefaultLoadoutUtility.ApplyPresetEntryToInventory(_inventory, entry, m_PresetCatalog.GrenadeThrowData, m_PresetCatalog.AlwaysIncludeBagItems);
		return true;
	}

	private void SyncSharedPresetFromInventory(int _presetIndex, CharacterInventory _inventory)
	{
		if (_inventory == null || m_PresetCatalog == null)
			return;

		MissionPrepSharedPresetStore sharedStore = MissionPrepSharedPresetStore.GetOrCreate(this);
		if (sharedStore == null)
			return;

		MissionPrepEquipmentPresetCatalog.PresetEntry entry = m_PresetCatalog.GetPresetEntry(_presetIndex);
		int armorIndex = entry != null
			? entry.DefaultArmorVisualIndex
			: MissionPrepUnitArmorVisualController.LightArmorIndex;
		sharedStore.SavePresetFromRuntime(_presetIndex, _inventory, armorIndex);
	}

	private void ConfigureUnitForStandardPreset(GameObject _instance)
	{
		ConfigureUnitForPreset(_instance, c_StandardPresetIndex);
	}

	private void RebindExistingSquad()
	{
		PurgeNullSpawnedInstances();
		PurgeNullSpawnedVehicles();
		BindVehicleCells();

		int slots = Mathf.Min(m_SquadSize, m_UnitOnlyCells.Count, m_SpawnedInstances.Count);
		for (int i = 0; i < slots; i++)
		{
			GameObject unitRoot = m_SpawnedInstances[i];
			if (unitRoot == null)
				continue;

			if (i >= m_UnitOnlyCells.Count)
				break;

			MissionPrepUnitCellView cell = m_UnitOnlyCells[i];
			if (cell == null)
				continue;

			UnitCellDisplayBinder.Apply(cell, unitRoot);
			cell.SetInteractionEnabled(true);
		}

		RefreshSeatSlots();
	}

	private void EnsureVehiclesSpawned()
	{
		PurgeNullSpawnedVehicles();
		if (m_VehicleCount <= 0 || m_VehiclePrefab == null)
			return;

		if (m_SpawnedVehicles.Count >= m_VehicleCount)
		{
			BindVehicleCells();
			RefreshSeatSlots();
			return;
		}

		Transform parent = ResolveActiveSpawnParent();
		Vector3 origin = (m_SpawnAnchor != null ? m_SpawnAnchor.position : transform.position) + m_VehicleSpawnOriginOffset;
		Quaternion rotation = m_SpawnAnchor != null ? m_SpawnAnchor.rotation : Quaternion.identity;

		s_SpawningPresentationVehicles = true;
		try
		{
			for (int i = m_SpawnedVehicles.Count; i < m_VehicleCount; i++)
			{
				Vector3 position = origin + m_VehicleSpawnPositionStep * i;
				GameObject instance = Instantiate(m_VehiclePrefab, position, rotation, parent);
				instance.name = $"{m_VehiclePrefab.name}_{i + 1}";
				// Префаб мог быть сохранён с выключенным корнем — иначе «спавн» невидимый.
				if (!instance.activeSelf)
					instance.SetActive(true);
				if (instance.GetComponent<MissionPrepPresentationVehicle>() == null)
					instance.AddComponent<MissionPrepPresentationVehicle>();

				VehicleController vehicle = instance.GetComponent<VehicleController>() ??
				                           instance.GetComponentInChildren<VehicleController>(true);
				if (vehicle == null)
				{
					Debug.LogWarning($"{nameof(MissionPrepSquadSpawner)}: vehicle prefab has no VehicleController.", instance);
					Destroy(instance);
					continue;
				}

				m_SpawnedVehicles.Add(vehicle);
			}
		}
		finally
		{
			s_SpawningPresentationVehicles = false;
		}

		// Seat rows depend on spawned vehicles' seat layouts.
		if (m_UnitCellPrefab != null &&
		    (m_CellsContentParent != null || m_VehicleCellsContentParent != null))
			BuildRuntimeUiCells();

		BindVehicleCells();
		RefreshSeatSlots();
	}

	private void BindVehicleCells()
	{
		int count = Mathf.Min(m_VehicleCells.Count, m_SpawnedVehicles.Count);
		for (int i = 0; i < count; i++)
		{
			MissionPrepUnitCellView cell = m_VehicleCells[i];
			VehicleController vehicle = m_SpawnedVehicles[i];
			if (cell == null || vehicle == null)
				continue;

			VehicleCellDisplayBinder.Apply(cell, vehicle);
			cell.SetInteractionEnabled(true);
		}

		RebindSeatSlotsToVehicles();
	}

	private void RebindSeatSlotsToVehicles()
	{
		int seatIndex = 0;
		for (int v = 0; v < m_SpawnedVehicles.Count; v++)
		{
			VehicleController vehicle = m_SpawnedVehicles[v];
			if (vehicle == null || vehicle.Seats == null)
				continue;

			vehicle.Seats.CollectConfiguredBoardingSeats(m_SeatBuffer);
			for (int s = 0; s < m_SeatBuffer.Count; s++)
			{
				if (seatIndex >= m_SeatSlots.Count)
					return;

				MissionPrepVehicleSeatSlotView slot = m_SeatSlots[seatIndex++];
				if (slot != null)
				{
					slot.Configure(
						vehicle,
						m_SeatBuffer[s].SeatId,
						m_VehicleAssignments,
						m_UnitCellPrefab,
						HandleOccupiedSeatUnitClicked);
				}
			}
		}
	}

	private void RefreshSeatSlots()
	{
		EnsureRosterDropZones();
		EnsureUnassignDropZones();
		SyncRosterListBindings();
		m_VehicleList?.RefreshSeatSlots();
		m_UnitList?.RefreshSeatSlots();
		RefreshAssignedUnitCaptions();
		if (BaseRosterContent != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(BaseRosterContent);
		if (MissionRosterContent != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(MissionRosterContent);
	}

	private void HandleVehicleAssignmentsChanged()
	{
		RefreshAssignedUnitCaptions();

		// Defer seat rebuild so EndDrag can finish before occupied cells are destroyed.
		if (m_DeferredSeatRefresh != null)
			StopCoroutine(m_DeferredSeatRefresh);
		m_DeferredSeatRefresh = StartCoroutine(DeferredRefreshSeatSlots());
	}

	private IEnumerator DeferredRefreshSeatSlots()
	{
		yield return null;
		m_DeferredSeatRefresh = null;
		RefreshSeatSlots();
	}

	private void EnsureUnassignDropZones()
	{
		EnsureUnassignDropZone(m_VehicleList, BaseRosterContent);
		EnsureUnassignDropZone(m_UnitList, MissionRosterContent);
	}

	private void EnsureUnassignDropZone(MissionPrepUnitListView _list, RectTransform _content)
	{
		if (_list == null)
			return;

		ScrollRect scroll = _list.GetComponent<ScrollRect>();
		if (scroll == null)
			scroll = _list.GetComponentInChildren<ScrollRect>(true);
		GameObject host = scroll != null ? scroll.gameObject : _list.gameObject;
		MissionPrepUnitUnassignDropZone zone = host.GetComponent<MissionPrepUnitUnassignDropZone>();
		if (zone == null)
			zone = host.AddComponent<MissionPrepUnitUnassignDropZone>();
		zone.Configure(m_VehicleAssignments, this, _content);

		if (_content == null)
			return;

		MissionPrepUnitUnassignDropZone contentZone =
			_content.GetComponent<MissionPrepUnitUnassignDropZone>();
		if (contentZone == null)
			contentZone = _content.gameObject.AddComponent<MissionPrepUnitUnassignDropZone>();
		contentZone.Configure(m_VehicleAssignments, this, _content);
	}

	private void EnsureRosterDropZones()
	{
		EnsureRosterDropZone(m_VehicleList, BaseRosterContent);
		EnsureRosterDropZone(m_UnitList, MissionRosterContent);
	}

	private void EnsureRosterDropZone(MissionPrepUnitListView _list, RectTransform _content)
	{
		if (_list == null || _content == null)
			return;

		ScrollRect scroll = _list.GetComponent<ScrollRect>();
		if (scroll == null)
			scroll = _list.GetComponentInChildren<ScrollRect>(true);
		GameObject host = scroll != null ? scroll.gameObject : _list.gameObject;
		MissionPrepRosterColumnDropZone zone = host.GetComponent<MissionPrepRosterColumnDropZone>();
		if (zone == null)
			zone = host.AddComponent<MissionPrepRosterColumnDropZone>();
		zone.Configure(this, _content);

		MissionPrepRosterColumnDropZone contentZone =
			_content.GetComponent<MissionPrepRosterColumnDropZone>();
		if (contentZone == null)
			contentZone = _content.gameObject.AddComponent<MissionPrepRosterColumnDropZone>();
		contentZone.Configure(this, _content);
	}

	private void HandleOccupiedSeatUnitClicked(MissionPrepUnitCellView _cell)
	{
		if (m_VehicleList != null)
			m_VehicleList.NotifyUnitCellSelected(_cell);
		else
			m_UnitList?.NotifyUnitCellSelected(_cell);
	}

	private void RefreshAssignedUnitCaptions()
	{
		for (int i = 0; i < m_UnitOnlyCells.Count; i++)
		{
			MissionPrepUnitCellView cell = m_UnitOnlyCells[i];
			if (cell == null || cell.IsInsideSeatSlot)
				continue;

			cell.SetVehicleAssignmentCaption(string.Empty);

			bool assigned = cell.BoundUnitRoot != null &&
			                m_VehicleAssignments.TryGetUnitAssignment(cell.BoundUnitRoot, out _, out _);
			if (cell.gameObject.activeSelf == assigned)
				cell.gameObject.SetActive(!assigned);
		}

		if (BaseRosterContent != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(BaseRosterContent);
		if (MissionRosterContent != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(MissionRosterContent);
	}

	public void PlaceRosterUnitCellInColumn(GameObject _unitRoot, RectTransform _content)
	{
		MissionPrepUnitCellView cell = FindRosterUnitCell(_unitRoot);
		if (cell == null)
			return;

		if (_content != null && cell.transform.parent != _content)
		{
			cell.transform.SetParent(_content, false);
			cell.transform.SetAsLastSibling();
		}

		cell.SetVehicleAssignmentCaption(string.Empty);
		if (!cell.gameObject.activeSelf)
			cell.gameObject.SetActive(true);

		SyncRosterListBindings();
		MissionPrepVehicleRosterBlock.SortVehiclesThenUnits(BaseRosterContent);
		MissionPrepVehicleRosterBlock.SortVehiclesThenUnits(MissionRosterContent);
		NormalizeUnitListContentLayout(BaseRosterContent);
		NormalizeUnitListContentLayout(MissionRosterContent);
	}

	private MissionPrepUnitCellView FindRosterUnitCell(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;

		for (int i = 0; i < m_UnitOnlyCells.Count; i++)
		{
			MissionPrepUnitCellView cell = m_UnitOnlyCells[i];
			if (cell == null || cell.IsInsideSeatSlot || cell.BoundUnitRoot != _unitRoot)
				continue;

			return cell;
		}

		return null;
	}

	private void DestroySpawnedVehicles()
	{
		for (int i = 0; i < m_SpawnedVehicles.Count; i++)
		{
			VehicleController vehicle = m_SpawnedVehicles[i];
			if (vehicle != null)
				Destroy(vehicle.gameObject);
		}

		m_SpawnedVehicles.Clear();
	}

	private void PurgeNullSpawnedVehicles()
	{
		for (int i = m_SpawnedVehicles.Count - 1; i >= 0; i--)
		{
			if (m_SpawnedVehicles[i] == null)
				m_SpawnedVehicles.RemoveAt(i);
		}
	}

	private bool HasLiveSquad()
	{
		PurgeNullSpawnedInstances();
		int cellSlots = m_UnitOnlyCells.Count > 0 ? m_UnitOnlyCells.Count : m_SquadSize;
		return m_SpawnedInstances.Count >= Mathf.Min(m_SquadSize, cellSlots);
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
		bool wantRuntimeCells = m_UnitCellPrefab != null && BaseRosterContent != null;
		if (!wantRuntimeCells)
		{
			if (m_UnitList != null && m_UnitList.UnitCellCount > 0)
			{
				RebuildCellCachesFromList();
				return true;
			}

			if (m_VehicleList != null && m_VehicleList.UnitCellCount > 0)
			{
				RebuildCellCachesFromList();
				return true;
			}

			Debug.LogWarning(
				$"{nameof(MissionPrepSquadSpawner)} on {name}: задайте префаб ячейки + Content колонки «на базе», либо заполните массив ячеек в MissionPrepUnitListView.",
				this);
			return false;
		}

		int expectedUnitCells = m_SquadSize;
		int expectedVehicleCells = m_VehicleCount > 0 && m_VehiclePrefab != null
			? Mathf.Max(0, m_VehicleCount)
			: 0;
		bool needsRebuild = m_UnitOnlyCells.Count != expectedUnitCells ||
		                    m_VehicleCells.Count != expectedVehicleCells ||
		                    m_RuntimeCellInstances.Count == 0 ||
		                    (expectedVehicleCells > 0 && m_RuntimeVehicleUiInstances.Count == 0);
		if (needsRebuild)
			BuildRuntimeUiCells();

		return m_UnitOnlyCells.Count > 0 || m_VehicleCells.Count > 0;
	}

	private void BuildRuntimeUiCells()
	{
		ClearRuntimeUiCells();

		m_VehicleCells.Clear();
		m_UnitOnlyCells.Clear();
		m_SeatSlots.Clear();

		RectTransform baseContent = BaseRosterContent;
		if (baseContent == null || m_UnitCellPrefab == null)
			return;

		NormalizeUnitListContentLayout(baseContent);
		NormalizeUnitListContentLayout(MissionRosterContent);

		int vehicleUiCount = m_VehicleCount > 0 && m_VehiclePrefab != null ? Mathf.Max(0, m_VehicleCount) : 0;
		for (int v = 0; v < vehicleUiCount; v++)
		{
			GameObject vehicleHeader = CreateListSectionHeader(
				baseContent,
				m_RuntimeVehicleUiInstances,
				$"SectionVehicle_{v}",
				"mission_prep.section.vehicle",
				"Машина");

			MissionPrepUnitCellView cell = Instantiate(m_UnitCellPrefab, baseContent);
			cell.gameObject.name = $"VehicleCell_{v}";
			m_VehicleCells.Add(cell);
			m_RuntimeVehicleUiInstances.Add(cell.gameObject);

			GameObject boardingHeader = CreateListSectionHeader(
				baseContent,
				m_RuntimeVehicleUiInstances,
				$"SectionBoarding_{v}",
				"mission_prep.section.boarding",
				"Посадочные места");

			MissionPrepVehicleRosterBlock group = cell.GetComponent<MissionPrepVehicleRosterBlock>();
			if (group == null)
				group = cell.gameObject.AddComponent<MissionPrepVehicleRosterBlock>();
			group.AddMember(vehicleHeader != null ? vehicleHeader.transform as RectTransform : null);
			group.AddMember(cell.transform as RectTransform);
			group.AddCollapsibleMember(boardingHeader != null ? boardingHeader.transform as RectTransform : null);

			VehicleController vehicle = v < m_SpawnedVehicles.Count ? m_SpawnedVehicles[v] : null;
			int seatCount = EstimateSeatCount(vehicle);
			for (int s = 0; s < seatCount; s++)
			{
				GameObject seatGo = new GameObject($"VehicleSeat_{v}_{s}", typeof(RectTransform));
				seatGo.transform.SetParent(baseContent, false);
				MissionPrepVehicleSeatSlotView seatView = seatGo.AddComponent<MissionPrepVehicleSeatSlotView>();
				m_SeatSlots.Add(seatView);
				m_RuntimeVehicleUiInstances.Add(seatGo);
				group.AddCollapsibleMember(seatGo.transform as RectTransform);
			}

			group.SetExpanded(false);
		}

		for (int i = 0; i < m_SquadSize; i++)
		{
			MissionPrepUnitCellView cell = Instantiate(m_UnitCellPrefab, baseContent);
			cell.gameObject.name = $"{m_UnitCellPrefab.name}_{i}";
			m_UnitOnlyCells.Add(cell);
			m_RuntimeCellInstances.Add(cell.gameObject);
		}

		EnsureRosterDropZones();
		EnsureUnassignDropZones();
		SyncRosterListBindings();
		RebindSeatSlotsToVehicles();
		RefreshAssignedUnitCaptions();
		MissionPrepVehicleRosterBlock.SortVehiclesThenUnits(baseContent);
		MissionPrepVehicleRosterBlock.SortVehiclesThenUnits(MissionRosterContent);
		NormalizeUnitListContentLayout(baseContent);
		NormalizeUnitListContentLayout(MissionRosterContent);
	}

	private static void NormalizeUnitListContentLayout(RectTransform _content)
	{
		if (_content == null)
			return;

		// Broken stretch + sizeDelta.y≈-1243 made Content shorter than Viewport → no scroll.
		InventoryUiScrollbarUtility.FixScrollContent(_content);

		ScrollRect scroll = _content.GetComponentInParent<ScrollRect>();
		if (scroll != null)
		{
			InventoryUiScrollbarUtility.ConfigureScrollRect(scroll);
			StripPrepListScrollFrame(scroll);
		}

		LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
	}

	private static void StripPrepListScrollFrame(ScrollRect _scroll)
	{
		if (_scroll == null)
			return;

		Image scrollImage = _scroll.GetComponent<Image>();
		if (scrollImage != null)
		{
			scrollImage.sprite = null;
			scrollImage.type = Image.Type.Simple;
			InventoryUiTheme.ApplyImageColor(scrollImage, InventoryUiTheme.ScrollInset);
		}

		if (_scroll.viewport != null && _scroll.viewport.TryGetComponent(out Mask mask))
		{
			mask.showMaskGraphic = false;
			mask.enabled = false;
			UnityEngine.Object.Destroy(mask);
		}
	}

	private static GameObject CreateListSectionHeader(
		RectTransform _parent,
		List<GameObject> _track,
		string _objectName,
		string _localizationKey,
		string _fallback)
	{
		if (_parent == null)
			return null;

		GameObject go = new GameObject(_objectName, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		InventoryPanelSectionHeader header = go.AddComponent<InventoryPanelSectionHeader>();
		header.Configure(_localizationKey, _fallback);
		if (_track != null)
			_track.Add(go);
		return go;
	}

	private void SyncRosterListBindings()
	{
		var baseCells = new List<MissionPrepUnitCellView>(m_VehicleCells.Count + m_UnitOnlyCells.Count);
		var missionCells = new List<MissionPrepUnitCellView>(m_VehicleCells.Count + m_UnitOnlyCells.Count);
		ClassifyRosterCells(m_VehicleCells, baseCells, missionCells);
		ClassifyRosterCells(m_UnitOnlyCells, baseCells, missionCells);

		var baseSeats = new List<MissionPrepVehicleSeatSlotView>(m_SeatSlots.Count);
		var missionSeats = new List<MissionPrepVehicleSeatSlotView>(m_SeatSlots.Count);
		RectTransform baseContent = BaseRosterContent;
		RectTransform missionContent = MissionRosterContent;
		for (int i = 0; i < m_SeatSlots.Count; i++)
		{
			MissionPrepVehicleSeatSlotView seat = m_SeatSlots[i];
			if (seat == null)
				continue;
			if (missionContent != null && seat.transform.IsChildOf(missionContent))
				missionSeats.Add(seat);
			else if (baseContent != null && seat.transform.IsChildOf(baseContent))
				baseSeats.Add(seat);
		}

		if (m_VehicleList != null)
		{
			m_VehicleList.SetUnitCells(baseCells.ToArray());
			m_VehicleList.SetSeatSlots(baseSeats);
		}

		if (m_UnitList != null)
		{
			m_UnitList.SetUnitCells(missionCells.ToArray());
			m_UnitList.SetSeatSlots(missionSeats);
		}
	}

	private void ClassifyRosterCells(
		List<MissionPrepUnitCellView> _source,
		List<MissionPrepUnitCellView> _baseCells,
		List<MissionPrepUnitCellView> _missionCells)
	{
		RectTransform missionContent = MissionRosterContent;
		for (int i = 0; i < _source.Count; i++)
		{
			MissionPrepUnitCellView cell = _source[i];
			if (cell == null)
				continue;

			if (missionContent != null && cell.transform.IsChildOf(missionContent))
				_missionCells.Add(cell);
			else
				_baseCells.Add(cell);
		}
	}

	private int EstimateSeatCount(VehicleController _vehicle)
	{
		if (_vehicle != null && _vehicle.Seats != null)
		{
			_vehicle.Seats.CollectConfiguredBoardingSeats(m_SeatBuffer);
			if (m_SeatBuffer.Count > 0)
				return m_SeatBuffer.Count;
		}

		// Light_Armored_Car typical boarding seats (без носилок) until vehicles are spawned.
		return 6;
	}

	private void RebuildCellCachesFromList()
	{
		m_VehicleCells.Clear();
		m_UnitOnlyCells.Clear();
		CollectCellsFromList(m_VehicleList);
		CollectCellsFromList(m_UnitList);
	}

	private void CollectCellsFromList(MissionPrepUnitListView _list)
	{
		if (_list == null)
			return;

		for (int i = 0; i < _list.UnitCellCount; i++)
		{
			MissionPrepUnitCellView cell = _list.GetUnitCell(i);
			if (cell == null)
				continue;

			if (cell.IsVehicleCell)
				m_VehicleCells.Add(cell);
			else
				m_UnitOnlyCells.Add(cell);
		}
	}

	private void ClearRuntimeUiCells()
	{
		for (int i = 0; i < m_RuntimeCellInstances.Count; i++)
		{
			GameObject cellObject = m_RuntimeCellInstances[i];
			if (cellObject != null)
				Destroy(cellObject);
		}

		for (int i = 0; i < m_RuntimeVehicleUiInstances.Count; i++)
		{
			GameObject cellObject = m_RuntimeVehicleUiInstances[i];
			if (cellObject != null)
				Destroy(cellObject);
		}

		m_RuntimeCellInstances.Clear();
		m_RuntimeVehicleUiInstances.Clear();
		m_VehicleCells.Clear();
		m_UnitOnlyCells.Clear();
		m_SeatSlots.Clear();
		if (m_UnitList != null)
		{
			m_UnitList.SetUnitCells(Array.Empty<MissionPrepUnitCellView>());
			m_UnitList.SetSeatSlots(null);
		}

		if (m_VehicleList != null)
		{
			m_VehicleList.SetUnitCells(Array.Empty<MissionPrepUnitCellView>());
			m_VehicleList.SetSeatSlots(null);
		}
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
