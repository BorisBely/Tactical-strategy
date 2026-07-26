#if UNITY_EDITOR
using CombatVehicleSystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires CombatVehicleSystem physics drive onto Light_Armored_Car without touching seat/door transforms.
/// </summary>
public static class LightArmoredCarPhysicsDriveSetup
{
	private const string c_PrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_TuningPath = "Assets/CombatVehicleSystem/Data/Tunings/Tuning_LightUtility_Humvee.asset";

	[MenuItem("Polygone/Vehicles/Setup Light Armored Car Physics Drive")]
	public static void SetupPrefab()
	{
		VehicleTuning tuning = AssetDatabase.LoadAssetAtPath<VehicleTuning>(c_TuningPath);
		if (tuning == null)
		{
			tuning = ScriptableObject.CreateInstance<VehicleTuning>();
			tuning.ConfigureAsLightUtilityHumvee();
			AssetDatabase.CreateAsset(tuning, c_TuningPath);
			AssetDatabase.SaveAssets();
		}
		else
		{
			tuning.ConfigureAsLightUtilityHumvee();
			EditorUtility.SetDirty(tuning);
		}

		GameObject root = PrefabUtility.LoadPrefabContents(c_PrefabPath);
		if (root == null)
		{
			Debug.LogError($"Missing prefab at {c_PrefabPath}");
			return;
		}

		try
		{
			VehicleController vehicle = root.GetComponent<VehicleController>();
			if (vehicle == null)
			{
				Debug.LogError("Light_Armored_Car prefab has no VehicleController.");
				return;
			}

			vehicle.EnsureComponents();
			VehicleHierarchyBinder.EnsureBound(vehicle);

			SerializedObject so = new SerializedObject(vehicle);
			so.FindProperty("m_Tuning").objectReferenceValue = tuning;
			so.ApplyModifiedPropertiesWithoutUndo();

			vehicle.EnsurePhysicsDrive();
			VehicleHierarchyBinder.EnsureBound(vehicle);

			PrefabUtility.SaveAsPrefabAsset(root, c_PrefabPath);
			Debug.Log("Light_Armored_Car physics drive setup complete (Humvee tuning, WheelColliders, VehicleBrain).");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}
}
#endif
