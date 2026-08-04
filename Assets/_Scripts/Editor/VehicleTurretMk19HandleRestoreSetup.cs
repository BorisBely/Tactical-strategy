#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Копирует GameObjectBolt + Bolt mesh + IK с <c>Assets/Light_Armored_Car1.prefab</c>
/// в <c>Assets/Prefabs/Vehicles/Light_Armored_Car.prefab</c> под MK19_1.
/// </summary>
public static class VehicleTurretMk19HandleRestoreSetup
{
	private const string c_SourcePrefabPath = "Assets/Light_Armored_Car1.prefab";
	private const string c_TargetPrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_Mk19InnerMeshName = "MK19_1";
	private const string c_HandleName = "GameObjectBolt";

	[MenuItem("Polygone/Vehicles/Restore MK19 Reload Handle From Light_Armored_Car1")]
	public static void RestoreMk19ReloadHandleFromCar1()
	{
		GameObject sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(c_SourcePrefabPath);
		if (sourceRoot == null)
		{
			EditorUtility.DisplayDialog(
				"MK19 handle",
				$"Source prefab not found:\n{c_SourcePrefabPath}",
				"OK");
			return;
		}

		GameObject sourceContents = PrefabUtility.LoadPrefabContents(c_SourcePrefabPath);
		GameObject targetContents = PrefabUtility.LoadPrefabContents(c_TargetPrefabPath);
		try
		{
			Transform sourceHandle = FindDeepChild(sourceContents.transform, c_HandleName);
			Transform sourceParent = sourceHandle != null ? sourceHandle.parent : null;
			Transform targetParent = FindDeepChild(targetContents.transform, c_Mk19InnerMeshName)
			                         ?? FindDeepChild(targetContents.transform, "MK19");

			if (sourceHandle == null || sourceParent == null)
			{
				EditorUtility.DisplayDialog("MK19 handle", "GameObjectBolt not found in source prefab.", "OK");
				return;
			}

			if (targetParent == null)
			{
				EditorUtility.DisplayDialog("MK19 handle", "MK19_1 / MK19 not found in target prefab.", "OK");
				return;
			}

			Transform existing = FindDeepChild(targetParent, c_HandleName);
			if (existing != null)
				Object.DestroyImmediate(existing.gameObject);

			GameObject copy = Object.Instantiate(sourceHandle.gameObject, targetParent);
			copy.name = c_HandleName;
			Transform copyTransform = copy.transform;
			copyTransform.localPosition = sourceHandle.localPosition;
			copyTransform.localRotation = sourceHandle.localRotation;
			copyTransform.localScale = sourceHandle.localScale;

			EditorUtility.SetDirty(targetContents);
			PrefabUtility.SaveAsPrefabAsset(targetContents, c_TargetPrefabPath);
			Debug.Log(
				$"[{nameof(VehicleTurretMk19HandleRestoreSetup)}] Copied '{c_HandleName}' " +
				$"from '{sourceParent.name}' -> '{targetParent.name}' " +
				$"(Bolt + {VehicleTurretReloadController.LeftHandIkNotReadyHandleName} + " +
				$"{VehicleTurretReloadController.RightHandIkNotReadyHandleName}).",
				sourceRoot);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(sourceContents);
			PrefabUtility.UnloadPrefabContents(targetContents);
		}
	}

	private static Transform FindDeepChild(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name)
				return all[i];
		}

		return null;
	}
}
#endif

/// <summary>Batch: -executeMethod VehicleTurretMk19HandleRestoreSetupRunner.Run</summary>
public static class VehicleTurretMk19HandleRestoreSetupRunner
{
	public static void Run()
	{
		VehicleTurretMk19HandleRestoreSetup.RestoreMk19ReloadHandleFromCar1();
		EditorApplication.Exit(0);
	}
}
