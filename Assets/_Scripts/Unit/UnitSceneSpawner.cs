using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Спавн игрока и врага из одного универсального префаба с inline-параметрами в инспекторе.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitSceneSpawner : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private GameObject m_UnitPrefab;
	[SerializeField] private Transform m_SpawnedUnitsParent;
	[SerializeField] private bool m_SpawnOnStart = true;
	[SerializeField] private bool m_DestroySpawnedOnDisable = true;

	[Header("Spawn Entries")]
	[SerializeField] private UnitSceneSpawnEntry m_PlayerSpawn = new UnitSceneSpawnEntry();
	[SerializeField] private UnitSceneSpawnEntry m_EnemySpawn = new UnitSceneSpawnEntry();

	private readonly List<GameObject> m_SpawnedInstances = new List<GameObject>(8);
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
		SpawnEntry(m_PlayerSpawn);
		SpawnEntry(m_EnemySpawn);
		RtsUnitSelectionManager.Instance?.EnsurePlayerUnitSelected();
	}
	#endregion

	#region Private Methods
	private void SpawnEntry(UnitSceneSpawnEntry _entry)
	{
		if (_entry == null || _entry.SpawnPoint == null)
		{
			Debug.LogWarning($"{nameof(UnitSceneSpawner)} on {name}: spawn entry or spawn point is missing.", this);
			return;
		}

		Transform parent = m_SpawnedUnitsParent != null ? m_SpawnedUnitsParent : transform;
		GameObject instance = Instantiate(
			m_UnitPrefab,
			_entry.SpawnPoint.position,
			_entry.SpawnPoint.rotation,
			parent);

		if (!instance.TryGetComponent(out UnitFactionConfigurator configurator))
			configurator = instance.AddComponent<UnitFactionConfigurator>();

		configurator.Configure(_entry.ToConfig());
		configurator.ApplyConfiguration();

		m_SpawnedInstances.Add(instance);
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
