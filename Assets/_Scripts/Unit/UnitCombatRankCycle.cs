using UnityEngine;

/// <summary>
/// Порядок прокачки: Recruit → Soldier → Corporal → Veteran → Elite.
/// </summary>
public static class UnitCombatRankCycle
{
	#region Constants
	public static readonly string[] RankAssetNamesInOrder =
	{
		"Rank_Recruit",
		"Rank_Soldier",
		"Rank_Veteran",
		"Rank_Specialist",
		"Rank_Elite"
	};
	#endregion

	#region Public Methods
	public static int GetRankIndex(UnitCombatRankDefinition _rank, UnitCombatRankDefinition[] _orderedRanks)
	{
		if (_rank == null || _orderedRanks == null || _orderedRanks.Length == 0)
			return -1;

		for (int i = 0; i < _orderedRanks.Length; i++)
		{
			if (_orderedRanks[i] == _rank)
				return i;
		}

		return -1;
	}

	public static UnitCombatRankDefinition GetNextRank(
		UnitCombatRankDefinition _currentRank,
		UnitCombatRankDefinition[] _orderedRanks)
	{
		if (_orderedRanks == null || _orderedRanks.Length == 0)
			return null;

		int index = GetRankIndex(_currentRank, _orderedRanks);
		int nextIndex = index < 0 ? 0 : (index + 1) % _orderedRanks.Length;
		return _orderedRanks[nextIndex];
	}

	public static string ResolveRankLabel(UnitCombatRankDefinition _rank)
	{
		if (_rank == null)
			return "—";

		string localized = _rank.GetLocalizedDisplayName();
		return string.IsNullOrWhiteSpace(localized) ? _rank.name : localized;
	}
	#endregion
}
