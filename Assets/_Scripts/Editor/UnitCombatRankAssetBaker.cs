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

		CreateOrUpdate("Rank_Recruit", "combat.rank.recruit", "Recruit", 35f, 40f, 35f);
		CreateOrUpdate("Rank_Soldier", "combat.rank.soldier", "Soldier", 50f, 50f, 50f);
		CreateOrUpdate("Rank_Veteran", "combat.rank.veteran", "Corporal", 58f, 56f, 58f);
		CreateOrUpdate("Rank_Specialist", "combat.rank.specialist", "Veteran", 61f, 68f, 60f);
		CreateOrUpdate("Rank_Elite", "combat.rank.elite", "Elite", 65f, 63f, 66f);

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
		float _recoilControl)
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
