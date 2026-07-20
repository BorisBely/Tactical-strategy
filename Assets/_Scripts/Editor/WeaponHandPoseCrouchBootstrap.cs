#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Seeds crouch weapon pose / hand IK fields from standing on all weapon ItemDefinitions.
/// </summary>
public static class WeaponHandPoseCrouchBootstrap
{
	[MenuItem("Polygone/Weapons/Copy Standing Hand Pose To Crouch (All Weapons)")]
	public static void CopyStandingHandPoseToCrouchForAllWeapons()
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

				Undo.RecordObject(def, "Copy Standing Hand Pose To Crouch");
				def.CopyStandingHandPoseToCrouch();
				EditorUtility.SetDirty(def);
				updated++;
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
		}

		AssetDatabase.SaveAssets();
		Debug.Log($"[WeaponHandPoseCrouchBootstrap] Copied standing → crouch on {updated} weapon ItemDefinitions ({skipped} skipped).");
	}
}
#endif
