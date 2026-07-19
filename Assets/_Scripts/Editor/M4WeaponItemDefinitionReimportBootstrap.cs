#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class M4WeaponItemDefinitionReimportBootstrap
{
	private const string c_MarkerPath = "Assets/.reimport_m4_weapon_items_marker";

	private static readonly string[] s_M4WeaponItemPaths =
	{
		"Assets/GameData/Inventory/M4/Item_Weapon_M4_ModA_1.asset",
		"Assets/GameData/Inventory/M4/Item_Weapon_M4_ModA_2.asset",
		"Assets/GameData/Inventory/M4/Item_Weapon_M16A_ModA_1.asset",
		"Assets/GameData/Inventory/M4/Item_Weapon_M16A4_ModA_2.asset",
		"Assets/GameData/Inventory/M4/Item_Weapon_MK12.asset",
		"Assets/GameData/Inventory/M4/Item_Weapon_MK18.asset"
	};

	static M4WeaponItemDefinitionReimportBootstrap()
	{
		EditorApplication.delayCall += TryRunFromMarker;
	}

	[MenuItem("Polygone/Weapons/Reimport M4 Weapon Item Definitions")]
	public static void ReimportM4WeaponItems()
	{
		ReimportAll();
	}

	private static void TryRunFromMarker()
	{
		if (!File.Exists(c_MarkerPath))
			return;

		try
		{
			File.Delete(c_MarkerPath);
			if (File.Exists(c_MarkerPath + ".meta"))
				File.Delete(c_MarkerPath + ".meta");

			ReimportAll();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[M4WeaponItemDefinitionReimport] Auto-run failed: {exception}");
		}
	}

	private static void ReimportAll()
	{
		AssetDatabase.StartAssetEditing();
		try
		{
			foreach (string path in s_M4WeaponItemPaths)
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
		}

		AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
		Debug.Log("[M4WeaponItemDefinitionReimport] Force-reimported M4 weapon ItemDefinition assets.");
	}
}
#endif
