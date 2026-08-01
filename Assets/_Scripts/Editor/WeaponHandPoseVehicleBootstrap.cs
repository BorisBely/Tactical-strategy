#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Seeds vehicle weapon pose / hand IK fields from standing on all weapon ItemDefinitions.
/// Runs automatically on editor load and via menu.
/// </summary>
[InitializeOnLoad]
public static class WeaponHandPoseVehicleBootstrap
{
	private const string c_MarkerPath = "Assets/.vehicle_hand_pose_bootstrap_done";

	static WeaponHandPoseVehicleBootstrap()
	{
		if (!System.IO.File.Exists(c_MarkerPath))
			EditorApplication.delayCall += CopyStandingHandPoseToVehicleForAllWeapons;
	}

	[MenuItem("Polygone/Weapons/Copy Standing Hand Pose To Vehicle (All Weapons)")]
	public static void CopyStandingHandPoseToVehicleForAllWeapons()
	{
		string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
		int updated = 0;
		int skipped = 0;

		AssetDatabase.StartAssetEditing();
		try
		{
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
				if (def == null || !def.IsEquipment || def.EquipmentKind != EquipmentKind.Weapon)
				{
					skipped++;
					continue;
				}

				if (def.HasVehicleWeaponPoseConfigured()
				    || def.HasVehicleRightHandIkConfigured()
				    || def.HasVehicleLeftHandIkConfigured())
				{
					skipped++;
					continue;
				}

				Undo.RecordObject(def, "Copy Standing Hand Pose To Vehicle");
				def.CopyStandingHandPoseToVehicle();
				EditorUtility.SetDirty(def);
				updated++;
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
		}

		AssetDatabase.SaveAssets();

		if (!System.IO.File.Exists(c_MarkerPath))
			System.IO.File.WriteAllText(c_MarkerPath, "done");

		Debug.Log($"[WeaponHandPoseVehicleBootstrap] Copied standing → vehicle on {updated} weapon ItemDefinitions ({skipped} skipped).");
	}
}
#endif
