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
	[SerializeField] private UnitSceneSpawnEntry[] m_EnemySpawns = new UnitSceneSpawnEntry[0];

	[Header("Civilian Spawns")]
	[SerializeField] private UnitSceneSpawnEntry[] m_CivilianSpawns = new UnitSceneSpawnEntry[0];

	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(64);
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
		SpawnEntries(m_EnemySpawns);
		SpawnEntries(m_CivilianSpawns);
		RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
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
					_bodyMeshArchetype: baseConfig.BodyMeshArchetype);
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
