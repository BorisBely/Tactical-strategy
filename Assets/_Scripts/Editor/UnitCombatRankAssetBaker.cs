#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт пресеты рангов из CombatBalanceTables.md.
/// </summary>
public static class UnitCombatRankAssetBaker
{
	private const string c_OutputFolder = "Assets/GameData/Combat/Ranks";

	[MenuItem("Polygone/Combat Balance/Create Unit Combat Rank Assets")]
	public static void CreateRankAssets()
	{
		EnsureFolder(c_OutputFolder);

		// Reaction: current values are min; lower ranks have longer max.
		CreateOrUpdate("Rank_Recruit", "combat.rank.recruit", "Recruit", 35f, 40f, 35f, 0.38f, 0.65f, 0.65f, 0.9f);
		CreateOrUpdate("Rank_Soldier", "combat.rank.soldier", "Soldier", 50f, 50f, 50f, 0.32f, 0.50f, 0.45f, 0.6f);
		CreateOrUpdate("Rank_Veteran", "combat.rank.veteran", "Corporal", 58f, 56f, 58f, 0.27f, 0.40f, 0.28f, 0.42f);
		CreateOrUpdate("Rank_Specialist", "combat.rank.specialist", "Veteran", 61f, 68f, 60f, 0.23f, 0.32f, 0.22f, 0.35f);
		CreateOrUpdate("Rank_Elite", "combat.rank.elite", "Elite", 65f, 63f, 66f, 0.20f, 0.26f, 0.16f, 0.28f);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"Unit combat rank assets ensured in {c_OutputFolder}.");
	}

	private static void CreateOrUpdate(
		string _assetName,
		string _localizationKey,
		string _displayName,
		float _marksmanship,
		float _handling,
		float _recoilControl,
		float _reactionTimeMin,
		float _reactionTimeMax,
		float _visionScanMin,
		float _visionScanMax)
	{
		string path = $"{c_OutputFolder}/{_assetName}.asset";
		var rank = AssetDatabase.LoadAssetAtPath<UnitCombatRankDefinition>(path);
		if (rank == null)
		{
			rank = ScriptableObject.CreateInstance<UnitCombatRankDefinition>();
			AssetDatabase.CreateAsset(rank, path);
		}

		SerializedObject so = new SerializedObject(rank);
		so.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		so.FindProperty("m_DisplayName").stringValue = _displayName;
		so.FindProperty("m_Marksmanship").floatValue = _marksmanship;
		so.FindProperty("m_WeaponHandling").floatValue = _handling;
		so.FindProperty("m_RecoilControl").floatValue = _recoilControl;
		so.FindProperty("m_ReactionTimeMinSeconds").floatValue = _reactionTimeMin;
		so.FindProperty("m_ReactionTimeMaxSeconds").floatValue = _reactionTimeMax;
		so.FindProperty("m_VisionScanIntervalMinSeconds").floatValue = _visionScanMin;
		so.FindProperty("m_VisionScanIntervalMaxSeconds").floatValue = _visionScanMax;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(rank);
	}

	private static void EnsureFolder(string _folder)
	{
		if (AssetDatabase.IsValidFolder(_folder))
			return;

		string parent = Path.GetDirectoryName(_folder)?.Replace('\\', '/');
		string leaf = Path.GetFileName(_folder);
		if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, leaf);
	}
}
#endif
