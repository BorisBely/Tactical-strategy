#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CombatVehicleSystem.Editor
{
	[InitializeOnLoad]
	public static class CombatVehicleAutoBuild
	{
		private const string c_MarkerPrefab = "Assets/CombatVehicleSystem/Prefabs/Vehicles/Desert/Stryker.prefab";
		private const string c_RequestFile = "Assets/CombatVehicleSystem/Editor/BUILD_REQUEST.txt";

		static CombatVehicleAutoBuild()
		{
			EditorApplication.delayCall += TryAutoBuild;
		}

		private static void TryAutoBuild()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				EditorApplication.delayCall += TryAutoBuild;
				return;
			}
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;

			bool hasRequest = File.Exists(c_RequestFile);
			if (!hasRequest)
				return;

			Debug.Log("[CombatVehicleSystem] Building package prefabs...");
			try
			{
				CombatVehiclePrefabBuilder.BuildFullPackage();
				if (File.Exists(c_RequestFile))
					AssetDatabase.DeleteAsset(c_RequestFile);
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[CombatVehicleSystem] Auto-build failed: {ex}");
			}
		}
	}
}
#endif
