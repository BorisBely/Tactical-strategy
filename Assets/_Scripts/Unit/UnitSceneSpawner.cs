using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Спавн юнитов (игрок/враг/гражданские) из одного универсального префаба по точкам.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitSceneSpawner : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private GameObject m_UnitPrefab;
	[SerializeField] private Transform m_SpawnedUnitsParent;
	[SerializeField] private bool m_SpawnOnStart = true;
	[SerializeField] private bool m_DestroySpawnedOnDisable = true;

	[Header("Player Spawns")]
	[SerializeField] private UnitSceneSpawnEntry[] m_PlayerSpawns = new UnitSceneSpawnEntry[0];

	[Header("Enemy Spawns")]
	[SerializeField] private bool m_SpawnEnemies = false;
	[SerializeField] private UnitSceneSpawnEntry[] m_EnemySpawns = new UnitSceneSpawnEntry[0];

	[Header("Civilian Spawns")]
	[SerializeField] private bool m_SpawnCivilians = false;
	[SerializeField] private UnitSceneSpawnEntry[] m_CivilianSpawns = new UnitSceneSpawnEntry[0];

	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(64);
	#endregion

	#region Public Properties
	public GameObject UnitPrefab => m_UnitPrefab;
	public UnitSceneSpawnEntry[] PlayerSpawns => m_PlayerSpawns;
	public UnitSceneSpawnEntry[] EnemySpawns => m_EnemySpawns;
	public UnitSceneSpawnEntry[] CivilianSpawns => m_CivilianSpawns;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (m_SpawnOnStart)
			SpawnUnits();
	}

	private void OnDisable()
	{
		if (m_DestroySpawnedOnDisable)
			DestroySpawnedInstances();
	}
	#endregion

	#region Public Methods
	[ContextMenu("Spawn Units")]
	public void SpawnUnits()
	{
		if (m_UnitPrefab == null)
		{
			Debug.LogWarning($"{nameof(UnitSceneSpawner)} on {name}: assign Unit Prefab.", this);
			return;
		}

		DestroySpawnedInstances();
		SpawnEntries(m_PlayerSpawns);
		if (m_SpawnEnemies) SpawnEntries(m_EnemySpawns);
		if (m_SpawnCivilians) SpawnEntries(m_CivilianSpawns);
		RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
	}

	/// <summary>
	/// Spawns only the first player + first enemy entry (ignores m_SpawnEnemies / other entries).
	/// Used by DetectionTestController for a clean 1v1 G1 harness.
	/// </summary>
	public bool TrySpawnDetectionTestPair(out GameObject _player, out GameObject _enemy)
	{
		_player = null;
		_enemy = null;

		if (m_UnitPrefab == null)
		{
			Debug.LogWarning($"{nameof(UnitSceneSpawner)} on {name}: assign Unit Prefab.", this);
			return false;
		}

		_player = SpawnFirstEntry(m_PlayerSpawns, "DetectionObserver");
		_enemy = SpawnFirstEntry(m_EnemySpawns, "DetectionTarget");
		if (_player == null || _enemy == null)
		{
			Debug.LogWarning(
				$"{nameof(UnitSceneSpawner)}: detection test pair spawn failed " +
				$"(player={_player != null}, enemy={_enemy != null}).",
				this);
			return false;
		}

		RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
		return true;
	}

	/// <summary>Spawns first player entry again (extra observer for dual-observer G2 tests).</summary>
	public GameObject SpawnAdditionalPlayer(string _displayName = "DetectionObserverB")
	{
		if (m_UnitPrefab == null)
			return null;
		return SpawnFirstEntry(m_PlayerSpawns, _displayName);
	}

	[ContextMenu("Toggle Enemy Spawn")]
	private void ToggleEnemySpawn()
	{
		m_SpawnEnemies = !m_SpawnEnemies;
		Debug.Log($"{name}: enemies spawn = {m_SpawnEnemies}", this);
	}

	[ContextMenu("Toggle Civilian Spawn")]
	private void ToggleCivilianSpawn()
	{
		m_SpawnCivilians = !m_SpawnCivilians;
		Debug.Log($"{name}: civilians spawn = {m_SpawnCivilians}", this);
	}
	#endregion

	#region Private Methods
	private void SpawnEntries(UnitSceneSpawnEntry[] _entries)
	{
		if (_entries == null)
			return;

		for (int i = 0; i < _entries.Length; i++)
		{
			UnitSceneSpawnEntry entry = _entries[i];
			if (entry == null)
				continue;

			SpawnEntry(entry);
		}
	}

	private void SpawnEntry(UnitSceneSpawnEntry _entry)
	{
		if (_entry == null || _entry.SpawnPoint == null)
		{
			Debug.LogWarning($"{nameof(UnitSceneSpawner)} on {name}: spawn entry or spawn point is missing.", this);
			return;
		}

		Transform parent = m_SpawnedUnitsParent != null ? m_SpawnedUnitsParent : transform;
		int count = _entry.SpawnCount;
		UnitSpawnConfig baseConfig = _entry.ToConfig();
		for (int i = 0; i < count; i++)
		{
			UnitSpawnConfig config = baseConfig;
			if (count > 1)
			{
				config = new UnitSpawnConfig(
					baseConfig.Team,
					baseConfig.Loadout,
					baseConfig.StartReady,
					$"{baseConfig.DisplayName}_{i + 1}",
					baseConfig.ArmorVisualIndex,
					baseConfig.CamouflageVisualIndex,
					_bodyMeshArchetype: baseConfig.BodyMeshArchetype,
					_visualAffiliation: baseConfig.ResolvedVisualAffiliation);
			}

			Vector3 offset = count > 1
				? new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f))
				: Vector3.zero;

			GameObject instance = Instantiate(
				m_UnitPrefab,
				_entry.SpawnPoint.position + offset,
				_entry.SpawnPoint.rotation,
				parent);

			if (!instance.TryGetComponent(out UnitFactionConfigurator configurator))
				configurator = instance.AddComponent<UnitFactionConfigurator>();

			configurator.Configure(config);
			configurator.ApplyConfiguration();

			m_SpawnedInstances.Add(instance);
		}
	}

	private GameObject SpawnFirstEntry(UnitSceneSpawnEntry[] _entries, string _fallbackDisplayName)
	{
		if (_entries == null || _entries.Length == 0)
			return null;

		UnitSceneSpawnEntry entry = _entries[0];
		if (entry == null || entry.SpawnPoint == null)
			return null;

		Transform parent = m_SpawnedUnitsParent != null ? m_SpawnedUnitsParent : transform;
		UnitSpawnConfig config = entry.ToConfig();
		if (string.IsNullOrWhiteSpace(config.DisplayName))
		{
			config = new UnitSpawnConfig(
				config.Team,
				config.Loadout,
				config.StartReady,
				_fallbackDisplayName,
				config.ArmorVisualIndex,
				config.CamouflageVisualIndex,
				_bodyMeshArchetype: config.BodyMeshArchetype,
				_visualAffiliation: config.ResolvedVisualAffiliation);
		}

		GameObject instance = Instantiate(
			m_UnitPrefab,
			entry.SpawnPoint.position,
			entry.SpawnPoint.rotation,
			parent);

		if (!instance.TryGetComponent(out UnitFactionConfigurator configurator))
			configurator = instance.AddComponent<UnitFactionConfigurator>();

		configurator.Configure(config);
		configurator.ApplyConfiguration();
		m_SpawnedInstances.Add(instance);
		return instance;
	}

	private void DestroySpawnedInstances()
	{
		for (int i = 0; i < m_SpawnedInstances.Count; i++)
		{
			GameObject instance = m_SpawnedInstances[i];
			if (instance != null)
				Destroy(instance);
		}

		m_SpawnedInstances.Clear();
	}
	#endregion
}
