using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns CQB-arena units onto <see cref="CombatTestSpawnMarker"/> pins.
/// Player = US/M-line kits, enemy = Mosin/SVD/PKM + AK series, neutral = unarmed civilian.
/// Unique weapon classes are guaranteed first; leftover pins pick similar series kits at random.
/// Does not use <see cref="UnitSceneSpawner"/> start-spawn (polygon / G-tests stay intact).
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatTestArenaSpawner : MonoBehaviour
{
	#region Constants
	public const float DefaultAutoSpawnInterval = 30f;
	public const float MinAutoSpawnInterval = 5f;
	public const float AutoSpawnIntervalStep = 15f;
	public const string CivilianPresetName = "Civilian-01";
	public const int GrenadesPerType = 2;
	public const int IfakCount = 2;
	private const float c_WaveOffset = 1.5f;
	private const float c_CenterAttackNavSample = 8f;
	public static readonly Vector3 ArenaCenterLocal = new Vector3(0f, 0f, 75f);
	#endregion

	#region Private Fields
	[SerializeField] private GameObject m_UnitPrefab;
	[SerializeField] private Transform m_SpawnedUnitsParent;
	[SerializeField] private bool m_SpawnOnStart = true;

	[Header("Shared gear")]
	[SerializeField] private ItemDefinition m_Backpack;
	[SerializeField] private ItemDefinition m_Ifak;
	[SerializeField] private ItemDefinition[] m_GrenadeTypes = Array.Empty<ItemDefinition>();
	[SerializeField] private ItemDefinition[] m_PlayerHelmets = Array.Empty<ItemDefinition>();

	[Header("Player kits — unique classes first, M-series fill")]
	[SerializeField] private CombatTestArenaWeaponKit[] m_PlayerUniqueKits = Array.Empty<CombatTestArenaWeaponKit>();
	[SerializeField] private CombatTestArenaWeaponKit[] m_PlayerFillKits = Array.Empty<CombatTestArenaWeaponKit>();

	[Header("Enemy kits — Mosin / SVD / PKM first, AK-series fill")]
	[SerializeField] private CombatTestArenaWeaponKit[] m_EnemyUniqueKits = Array.Empty<CombatTestArenaWeaponKit>();
	[SerializeField] private CombatTestArenaWeaponKit[] m_EnemyFillKits = Array.Empty<CombatTestArenaWeaponKit>();

	[Header("Neutral")]
	[SerializeField] private UnitSpawnConfig[] m_NeutralTemplates = Array.Empty<UnitSpawnConfig>();

	[SerializeField] private bool m_AutoSpawnEnabled;
	[SerializeField, Min(MinAutoSpawnInterval)] private float m_AutoSpawnInterval = DefaultAutoSpawnInterval;

	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(64);
	private readonly List<CombatTestSpawnMarker> m_Markers = new List<CombatTestSpawnMarker>(40);
	private float m_AutoSpawnTimer;
	#endregion

	#region Public Properties
	public bool AutoSpawnEnabled => m_AutoSpawnEnabled;
	public float AutoSpawnInterval => m_AutoSpawnInterval;
	public float AutoSpawnRemaining => m_AutoSpawnEnabled ? Mathf.Max(0f, m_AutoSpawnTimer) : 0f;
	public int PlayerUniqueKitCount => CountValid(m_PlayerUniqueKits);
	public int PlayerFillKitCount => CountValid(m_PlayerFillKits);
	public int EnemyUniqueKitCount => CountValid(m_EnemyUniqueKits);
	public int EnemyFillKitCount => CountValid(m_EnemyFillKits);
	public int GrenadeTypeCount => m_GrenadeTypes != null ? m_GrenadeTypes.Length : 0;
	public int PlayerHelmetCount => m_PlayerHelmets != null ? m_PlayerHelmets.Length : 0;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (ShouldSkipHarnessPlay())
			return;
		if (!m_SpawnOnStart)
			return;

		EnsureReady();
		SpawnInitial();
	}

	private void Update()
	{
		if (!m_AutoSpawnEnabled || ShouldSkipHarnessPlay())
			return;

		m_AutoSpawnTimer -= Time.deltaTime;
		if (m_AutoSpawnTimer > 0f)
			return;

		SpawnCombatWave();
		m_AutoSpawnTimer = m_AutoSpawnInterval;
	}

	private void OnDisable()
	{
		DestroySpawnedInstances();
	}
	#endregion

	#region Public Methods
	public int SpawnInitial()
	{
		int count = 0;
		count += SpawnSide(CombatTestSpawnMarker.MarkerSide.Player, false);
		count += SpawnSide(CombatTestSpawnMarker.MarkerSide.Enemy, false);
		count += SpawnSide(CombatTestSpawnMarker.MarkerSide.Neutral, false);
		RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
		return count;
	}

	public int SpawnSide(CombatTestSpawnMarker.MarkerSide _side, bool _offset)
	{
		EnsureReady();
		if (m_UnitPrefab == null)
		{
			Debug.LogWarning($"{nameof(CombatTestArenaSpawner)}: Unit prefab is missing.", this);
			return 0;
		}

		CollectMarkers();
		List<CombatTestSpawnMarker> sideMarkers = CollectSideMarkers(_side);
		if (sideMarkers.Count == 0)
			return 0;

		if (_side == CombatTestSpawnMarker.MarkerSide.Neutral)
			return SpawnNeutralSide(sideMarkers, _offset);

		CombatTestArenaWeaponKit[] kits = CombatTestArenaWeaponKit.PickForSlotCount(
			UniqueKitsFor(_side),
			FillKitsFor(_side),
			sideMarkers.Count);
		if (kits == null || kits.Length != sideMarkers.Count)
		{
			Debug.LogWarning($"{nameof(CombatTestArenaSpawner)}: no weapon kits for {_side}.", this);
			return 0;
		}

		int spawned = 0;
		for (int i = 0; i < sideMarkers.Count; i++)
		{
			UnitSpawnConfig config = BuildCombatConfig(_side, kits[i], i + 1);
			if (SpawnAtMarker(sideMarkers[i], config, _offset))
				spawned++;
		}

		if (spawned > 0 && _side == CombatTestSpawnMarker.MarkerSide.Player)
			RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();

		return spawned;
	}

	public int SpawnCombatWave()
	{
		int count = SpawnSide(CombatTestSpawnMarker.MarkerSide.Player, true);
		count += SpawnSide(CombatTestSpawnMarker.MarkerSide.Enemy, true);
		return count;
	}

	public void SetAutoSpawn(bool _enabled)
	{
		m_AutoSpawnEnabled = _enabled;
		m_AutoSpawnTimer = m_AutoSpawnInterval;
	}

	public void SetAutoSpawnInterval(float _seconds)
	{
		m_AutoSpawnInterval = Mathf.Max(MinAutoSpawnInterval, _seconds);
		if (m_AutoSpawnEnabled)
			m_AutoSpawnTimer = m_AutoSpawnInterval;
	}

	public void AdjustAutoSpawnInterval(float _delta)
	{
		SetAutoSpawnInterval(m_AutoSpawnInterval + _delta);
	}

	public void AssignFromEditor(
		GameObject _unitPrefab,
		Transform _spawnedParent,
		UnitSpawnConfig[] _neutralTemplates)
	{
		m_UnitPrefab = _unitPrefab;
		m_SpawnedUnitsParent = _spawnedParent;
		m_NeutralTemplates = _neutralTemplates ?? Array.Empty<UnitSpawnConfig>();
		m_SpawnOnStart = true;
		m_AutoSpawnEnabled = false;
		m_AutoSpawnInterval = DefaultAutoSpawnInterval;
	}

	public void AssignLoadoutCatalog(
		ItemDefinition _backpack,
		ItemDefinition _ifak,
		ItemDefinition[] _grenadeTypes,
		ItemDefinition[] _playerHelmets,
		CombatTestArenaWeaponKit[] _playerUniqueKits,
		CombatTestArenaWeaponKit[] _playerFillKits,
		CombatTestArenaWeaponKit[] _enemyUniqueKits,
		CombatTestArenaWeaponKit[] _enemyFillKits)
	{
		m_Backpack = _backpack;
		m_Ifak = _ifak;
		m_GrenadeTypes = _grenadeTypes ?? Array.Empty<ItemDefinition>();
		m_PlayerHelmets = _playerHelmets ?? Array.Empty<ItemDefinition>();
		m_PlayerUniqueKits = _playerUniqueKits ?? Array.Empty<CombatTestArenaWeaponKit>();
		m_PlayerFillKits = _playerFillKits ?? Array.Empty<CombatTestArenaWeaponKit>();
		m_EnemyUniqueKits = _enemyUniqueKits ?? Array.Empty<CombatTestArenaWeaponKit>();
		m_EnemyFillKits = _enemyFillKits ?? Array.Empty<CombatTestArenaWeaponKit>();
	}
	#endregion

	#region Private Methods
	private static bool ShouldSkipHarnessPlay()
	{
		return DetectionHarnessPlayMode.IsCalibrationPlay || DetectionHarnessPlayMode.IsGRegressionPlay;
	}

	private void EnsureReady()
	{
		if (m_UnitPrefab == null || m_NeutralTemplates == null || m_NeutralTemplates.Length == 0)
			TryCopyFromSceneSpawner();

		if (m_SpawnedUnitsParent == null)
			m_SpawnedUnitsParent = transform;
	}

	private void TryCopyFromSceneSpawner()
	{
		UnitSceneSpawner sceneSpawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (sceneSpawner == null)
			return;

		if (m_UnitPrefab == null)
			m_UnitPrefab = sceneSpawner.UnitPrefab;

		if (m_NeutralTemplates == null || m_NeutralTemplates.Length == 0)
			m_NeutralTemplates = CollectNamed(sceneSpawner.CivilianSpawns, CivilianPresetName);
	}

	public static UnitSpawnConfig[] CollectNamed(UnitSceneSpawnEntry[] _entries, params string[] _names)
	{
		if (_entries == null || _names == null || _names.Length == 0)
			return Array.Empty<UnitSpawnConfig>();

		List<UnitSpawnConfig> result = new List<UnitSpawnConfig>(_names.Length);
		for (int n = 0; n < _names.Length; n++)
		{
			string name = _names[n];
			for (int i = 0; i < _entries.Length; i++)
			{
				UnitSceneSpawnEntry entry = _entries[i];
				if (entry == null || entry.DisplayName != name)
					continue;
				result.Add(entry.ToConfig());
				break;
			}
		}

		return result.Count > 0 ? result.ToArray() : Array.Empty<UnitSpawnConfig>();
	}

	private CombatTestArenaWeaponKit[] UniqueKitsFor(CombatTestSpawnMarker.MarkerSide _side)
	{
		return _side == CombatTestSpawnMarker.MarkerSide.Enemy ? m_EnemyUniqueKits : m_PlayerUniqueKits;
	}

	private CombatTestArenaWeaponKit[] FillKitsFor(CombatTestSpawnMarker.MarkerSide _side)
	{
		return _side == CombatTestSpawnMarker.MarkerSide.Enemy ? m_EnemyFillKits : m_PlayerFillKits;
	}

	private void CollectMarkers()
	{
		m_Markers.Clear();
		CombatTestSpawnMarker[] found = FindObjectsByType<CombatTestSpawnMarker>(FindObjectsInactive.Exclude);
		for (int i = 0; i < found.Length; i++)
		{
			if (found[i] != null)
				m_Markers.Add(found[i]);
		}

		m_Markers.Sort(CompareMarkers);
	}

	private List<CombatTestSpawnMarker> CollectSideMarkers(CombatTestSpawnMarker.MarkerSide _side)
	{
		var sideMarkers = new List<CombatTestSpawnMarker>(16);
		for (int i = 0; i < m_Markers.Count; i++)
		{
			CombatTestSpawnMarker marker = m_Markers[i];
			if (marker != null && marker.Side == _side)
				sideMarkers.Add(marker);
		}

		return sideMarkers;
	}

	private static int CompareMarkers(CombatTestSpawnMarker _a, CombatTestSpawnMarker _b)
	{
		return string.CompareOrdinal(_a.name, _b.name);
	}

	private int SpawnNeutralSide(List<CombatTestSpawnMarker> _markers, bool _offset)
	{
		if (m_NeutralTemplates == null || m_NeutralTemplates.Length == 0)
		{
			Debug.LogWarning($"{nameof(CombatTestArenaSpawner)}: no templates for Neutral.", this);
			return 0;
		}

		int spawned = 0;
		for (int i = 0; i < _markers.Count; i++)
		{
			UnitSpawnConfig template = m_NeutralTemplates[i % m_NeutralTemplates.Length];
			string displayName = string.IsNullOrWhiteSpace(template.DisplayName)
				? "Neutral_" + (i + 1).ToString("00")
				: template.DisplayName + "_" + (i + 1).ToString("00");
			UnitSpawnConfig config = new UnitSpawnConfig(
				template.Team,
				template.Loadout,
				template.StartReady,
				displayName,
				template.ArmorVisualIndex,
				template.CamouflageVisualIndex,
				template.FemaleSpawnChance,
				template.BodyMeshArchetype,
				template.HasExplicitVisualAffiliation ? template.VisualAffiliation : (VisualAffiliation?)null);
			if (SpawnAtMarker(_markers[i], config, _offset))
				spawned++;
		}

		return spawned;
	}

	private UnitSpawnConfig BuildCombatConfig(
		CombatTestSpawnMarker.MarkerSide _side,
		CombatTestArenaWeaponKit _kit,
		int _index)
	{
		bool isPlayer = _side == CombatTestSpawnMarker.MarkerSide.Player;
		string kitName = _kit != null && !string.IsNullOrWhiteSpace(_kit.DisplayName)
			? _kit.DisplayName
			: _kit != null ? _kit.Role.ToString() : "Unit";
		string displayName = (isPlayer ? "Player_" : "Enemy_") + kitName + "_" + _index.ToString("00");

		ItemDefinition helmet = isPlayer ? PickPlayerHelmet() : null;
		int armorIndex = isPlayer
			? (UnityEngine.Random.value < 0.5f
				? MissionPrepUnitArmorVisualController.LightArmorIndex
				: MissionPrepUnitArmorVisualController.HeavyArmorIndex)
			: MissionPrepUnitArmorVisualController.LightArmorIndex;
		int camouflageIndex = isPlayer
			? (int)UnitCamouflagePattern.Desert
			: UnityEngine.Random.Range(0, UnitCamouflagePatternUtility.PatternCount);

		var loadout = new UnitSpawnLoadout(
			_kit != null ? _kit.Weapon : null,
			helmet,
			m_Backpack,
			_kit != null ? _kit.BuildBagItems(m_Ifak, IfakCount) : Array.Empty<ItemDefinition>(),
			BuildGrenadeItems(),
			_kit != null ? _kit.Ammo : null);

		return new UnitSpawnConfig(
			isPlayer ? UnitTeamId.Player : UnitTeamId.Enemy,
			loadout,
			false,
			displayName,
			armorIndex,
			camouflageIndex,
			UnitCharacterAppearance.DefaultFemaleSpawnChance,
			isPlayer ? UnitBodyMeshArchetype.Soldier : UnitBodyMeshArchetype.Insurgent);
	}

	private ItemDefinition PickPlayerHelmet()
	{
		if (m_PlayerHelmets == null || m_PlayerHelmets.Length == 0)
			return null;

		ItemDefinition picked = m_PlayerHelmets[UnityEngine.Random.Range(0, m_PlayerHelmets.Length)];
		return picked;
	}

	private ItemDefinition[] BuildGrenadeItems()
	{
		if (m_GrenadeTypes == null || m_GrenadeTypes.Length == 0)
			return Array.Empty<ItemDefinition>();

		var grenades = new List<ItemDefinition>(m_GrenadeTypes.Length * GrenadesPerType);
		for (int i = 0; i < m_GrenadeTypes.Length; i++)
		{
			ItemDefinition grenade = m_GrenadeTypes[i];
			if (grenade == null || !grenade.IsGrenade)
				continue;

			for (int copy = 0; copy < GrenadesPerType; copy++)
				grenades.Add(grenade);
		}

		return grenades.ToArray();
	}

	private bool SpawnAtMarker(
		CombatTestSpawnMarker _marker,
		UnitSpawnConfig _config,
		bool _offset)
	{
		if (_marker == null || _config == null)
			return false;

		Vector3 offset = _offset
			? new Vector3(UnityEngine.Random.Range(-c_WaveOffset, c_WaveOffset), 0f, UnityEngine.Random.Range(-c_WaveOffset, c_WaveOffset))
			: Vector3.zero;

		Transform parent = m_SpawnedUnitsParent != null ? m_SpawnedUnitsParent : transform;
		GameObject instance = Instantiate(
			m_UnitPrefab,
			_marker.transform.position + offset,
			_marker.transform.rotation,
			parent);

		if (!instance.TryGetComponent(out UnitFactionConfigurator configurator))
			configurator = instance.AddComponent<UnitFactionConfigurator>();

		configurator.Configure(_config);
		configurator.ApplyConfiguration();
		m_SpawnedInstances.Add(instance);
		QueueCenterAttack(instance);
		return true;
	}

	private void QueueCenterAttack(GameObject _instance)
	{
		if (_instance == null)
			return;
		if (!_instance.TryGetComponent(out UnitTeam team) || team.Team == UnitTeamId.Neutral)
			return;

		StartCoroutine(IssueCenterAttackNextFrame(_instance));
	}

	private IEnumerator IssueCenterAttackNextFrame(GameObject _instance)
	{
		yield return null;
		if (_instance == null)
			yield break;
		if (!_instance.TryGetComponent(out UnitTeam team) || team.Team == UnitTeamId.Neutral)
			yield break;

		if (!_instance.TryGetComponent(out UnitAIController ai) || ai == null)
			ai = _instance.AddComponent<UnitAIController>();
		if (ai == null)
			yield break;

		ai.DrawSearchHud = false;
		ai.TrySetUseOfForcePolicy(UseOfForceSideCommands.Peek(team.Team));
		ai.SetAttack(SpreadAround(ResolveCenterAttackPoint(), _instance), null);
	}

	private Vector3 ResolveCenterAttackPoint()
	{
		Vector3 point = transform.TransformPoint(ArenaCenterLocal);
		if (NavMesh.SamplePosition(point, out NavMeshHit hit, c_CenterAttackNavSample, NavMesh.AllAreas))
			return hit.position;
		return point;
	}

	private static Vector3 SpreadAround(Vector3 _center, GameObject _instance)
	{
		if (_instance == null)
			return _center;

		float angle = Mathf.Abs(_instance.GetEntityId().GetHashCode() * 0.6180339887f) % 1f * Mathf.PI * 2f;
		return _center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.35f;
	}

	private static int CountValid(CombatTestArenaWeaponKit[] _kits)
	{
		if (_kits == null)
			return 0;

		int count = 0;
		for (int i = 0; i < _kits.Length; i++)
		{
			if (_kits[i] != null && _kits[i].IsValid)
				count++;
		}

		return count;
	}

	private void DestroySpawnedInstances()
	{
		for (int i = 0; i < m_SpawnedInstances.Count; i++)
		{
			GameObject instance = m_SpawnedInstances[i];
			if (instance == null)
				continue;
			if (Application.isPlaying)
				Destroy(instance);
			else
				DestroyImmediate(instance);
		}

		m_SpawnedInstances.Clear();
	}
	#endregion
}
