#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Подключает боевые компоненты к префабу юнита и назначает ранг по умолчанию.
/// </summary>
public static class CombatBalanceSetupEditor
{
	private const string c_PlayerUnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_DefaultRankPath = "Assets/GameData/Combat/Ranks/Rank_Soldier.asset";

	[MenuItem("Polygone/Combat Balance/Setup Unit Combat Components")]
	public static void SetupPlayerUnitCombatComponents()
	{
		var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c_PlayerUnitPrefabPath);
		if (prefab == null)
		{
			Debug.LogError($"Missing prefab: {c_PlayerUnitPrefabPath}");
			return;
		}

		var rank = AssetDatabase.LoadAssetAtPath<UnitCombatRankDefinition>(c_DefaultRankPath);
		GameObject root = PrefabUtility.LoadPrefabContents(c_PlayerUnitPrefabPath);
		bool changed = false;

		if (EnsureComponent<UnitCombatStats>(root, out UnitCombatStats stats))
			changed = true;

		if (EnsureComponent<UnitCombatCondition>(root, out _))
			changed = true;

		if (stats != null && rank != null)
		{
			SerializedObject so = new SerializedObject(stats);
			so.FindProperty("m_RankPreset").objectReferenceValue = rank;
			so.FindProperty("m_ApplyRankPresetOnAwake").boolValue = true;
			so.ApplyModifiedPropertiesWithoutUndo();
			changed = true;
		}

		if (changed)
		{
			PrefabUtility.SaveAsPrefabAsset(root, c_PlayerUnitPrefabPath);
			Debug.Log("Unit prefab updated with UnitCombatStats and UnitCombatCondition.");
		}
		else
		{
			Debug.Log("Unit prefab already has combat components.");
		}

		PrefabUtility.UnloadPrefabContents(root);
	}

	[MenuItem("Polygone/Combat Balance/Bake All Combat Balance Data")]
	public static void BakeAllCombatBalanceData()
	{
		UnitCombatRankAssetBaker.CreateRankAssets();
		WeaponDistanceProfileBaker.BakeAllWeaponProfiles();
		OpticDistanceProfileBaker.BakeAllOpticProfiles();
		CombatBalanceSetupEditor.SetupPlayerUnitCombatComponents();
		Debug.Log("All combat balance data baked.");
	}

	private static bool EnsureComponent<T>(GameObject _root, out T _component) where T : Component
	{
		_component = _root.GetComponent<T>();
		if (_component != null)
			return false;

		_component = _root.AddComponent<T>();
		return true;
	}
}
#endif
