using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads existing Player/Enemy spawn pins only. Does not create ThreatDirection scene objects.
/// </summary>
public static class ThreatDirectionSpawnQuery
{
	#region Private Fields
	private static bool s_Resolved;
	private static bool s_HasPlayer;
	private static bool s_HasEnemy;
	private static Vector3 s_PlayerCenter;
	private static Vector3 s_EnemyCenter;
	#endregion

	#region Public Methods
	public static void Invalidate()
	{
		s_Resolved = false;
		s_HasPlayer = false;
		s_HasEnemy = false;
		s_PlayerCenter = Vector3.zero;
		s_EnemyCenter = Vector3.zero;
	}

	public static bool TryGetCenters(UnitTeamId _team, out Vector3 _ownCenter, out Vector3 _enemyCenter)
	{
		_ownCenter = Vector3.zero;
		_enemyCenter = Vector3.zero;
		EnsureResolved();
		if (!s_HasPlayer || !s_HasEnemy)
			return false;

		if (_team == UnitTeamId.Player)
		{
			_ownCenter = s_PlayerCenter;
			_enemyCenter = s_EnemyCenter;
			return true;
		}

		if (_team == UnitTeamId.Enemy)
		{
			_ownCenter = s_EnemyCenter;
			_enemyCenter = s_PlayerCenter;
			return true;
		}

		return false;
	}

	public static bool TryGetPlayerAndEnemyCenters(out Vector3 _playerCenter, out Vector3 _enemyCenter)
	{
		EnsureResolved();
		_playerCenter = s_PlayerCenter;
		_enemyCenter = s_EnemyCenter;
		return s_HasPlayer && s_HasEnemy;
	}
	#endregion

	#region Private Methods
	private static void EnsureResolved()
	{
		if (s_Resolved)
			return;

		s_Resolved = true;
		s_HasPlayer = false;
		s_HasEnemy = false;

		if (TryCollectMarkerCenters(out Vector3 markerPlayer, out Vector3 markerEnemy))
		{
			s_PlayerCenter = markerPlayer;
			s_EnemyCenter = markerEnemy;
			s_HasPlayer = true;
			s_HasEnemy = true;
			return;
		}

		if (!TryCollectSpawnerCenters(out Vector3 spawnerPlayer, out Vector3 spawnerEnemy))
			return;

		s_PlayerCenter = spawnerPlayer;
		s_EnemyCenter = spawnerEnemy;
		s_HasPlayer = true;
		s_HasEnemy = true;
	}

	private static bool TryCollectMarkerCenters(out Vector3 _player, out Vector3 _enemy)
	{
		_player = Vector3.zero;
		_enemy = Vector3.zero;
		CombatTestSpawnMarker[] markers =
			Object.FindObjectsByType<CombatTestSpawnMarker>(FindObjectsInactive.Exclude);
		if (markers == null || markers.Length == 0)
			return false;

		var player = new List<Vector3>(16);
		var enemy = new List<Vector3>(16);
		for (int i = 0; i < markers.Length; i++)
		{
			CombatTestSpawnMarker marker = markers[i];
			if (marker == null)
				continue;
			if (marker.Side == CombatTestSpawnMarker.MarkerSide.Player)
				player.Add(marker.transform.position);
			else if (marker.Side == CombatTestSpawnMarker.MarkerSide.Enemy)
				enemy.Add(marker.transform.position);
		}

		return ThreatDirectionEstimator.TryAverage(player, out _player) &&
		       ThreatDirectionEstimator.TryAverage(enemy, out _enemy);
	}

	private static bool TryCollectSpawnerCenters(out Vector3 _player, out Vector3 _enemy)
	{
		_player = Vector3.zero;
		_enemy = Vector3.zero;
		UnitSceneSpawner spawner = Object.FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner == null)
			return false;

		return TryAverageEntries(spawner.PlayerSpawns, out _player) &&
		       TryAverageEntries(spawner.EnemySpawns, out _enemy);
	}

	private static bool TryAverageEntries(UnitSceneSpawnEntry[] _entries, out Vector3 _center)
	{
		_center = Vector3.zero;
		if (_entries == null || _entries.Length == 0)
			return false;

		var points = new List<Vector3>(_entries.Length);
		for (int i = 0; i < _entries.Length; i++)
		{
			UnitSceneSpawnEntry entry = _entries[i];
			if (entry == null || entry.SpawnPoint == null)
				continue;
			points.Add(entry.SpawnPoint.position);
		}

		return ThreatDirectionEstimator.TryAverage(points, out _center);
	}
	#endregion
}
