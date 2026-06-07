using UnityEngine;

/// <summary>
/// Находит <see cref="UnitCombatStats"/> активного игрока: сначала выделенный RTS-юнит, иначе первый Player в сцене.
/// </summary>
public static class UnitCombatStatsLookup
{
	#region Public Methods
	public static bool TryGetActivePlayerCombatStats(out UnitCombatStats _combatStats)
	{
		_combatStats = null;

		RtsUnitSelectionManager selection = RtsUnitSelectionManager.Instance;
		if (selection != null && selection.TryGetFirstSelectedPlayerCombatStats(out _combatStats))
			return true;

		return TryGetFirstPlayerCombatStatsInScene(out _combatStats);
	}

	public static bool TryGetFirstPlayerCombatStatsInScene(out UnitCombatStats _combatStats)
	{
		_combatStats = null;

#if UNITY_2023_1_OR_NEWER
		UnitCombatStats[] allStats = Object.FindObjectsByType<UnitCombatStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
		UnitCombatStats[] allStats = Object.FindObjectsOfType<UnitCombatStats>();
#endif
		for (int i = 0; i < allStats.Length; i++)
		{
			UnitCombatStats stats = allStats[i];
			if (stats == null || !IsPlayerUnit(stats))
				continue;

			_combatStats = stats;
			return true;
		}

		return false;
	}

	public static UnitCombatStats ResolveOnUnit(Component _unitComponent)
	{
		if (_unitComponent == null)
			return null;

		UnitCombatStats stats = _unitComponent.GetComponent<UnitCombatStats>();
		if (stats != null)
			return stats;

		return _unitComponent.GetComponentInParent<UnitCombatStats>();
	}
	#endregion

	#region Private Methods
	private static bool IsPlayerUnit(UnitCombatStats _stats)
	{
		UnitTeam team = _stats.GetComponent<UnitTeam>();
		if (team == null)
			team = _stats.GetComponentInParent<UnitTeam>();

		return team != null && team.Team == UnitTeamId.Player;
	}
	#endregion
}
