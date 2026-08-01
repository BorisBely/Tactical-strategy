#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CopyAk47PoseToAllWeapons
{
	[MenuItem("Polygone/Weapons/Copy AK47 Vehicle Pose + IK To All Weapons")]
	public static void Execute()
	{
		string ak47Path = "Assets/GameData/Inventory/AK/Item_Weapon_AK47.asset";
		ItemDefinition source = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ak47Path);
		if (source == null)
		{
			Debug.LogError("AK47 source not found at " + ak47Path);
			return;
		}

		string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
		int updated = 0;
		int skipped = 0;

		var excludeNames = new HashSet<string>
		{
			"Item_Weapon_AK47",
			"Item_Weapon_Rpg7",
			"Item_Weapon_DisposableRocketLauncher",
			"Item_Weapon_M2Browning",
			"Item_Weapon_MK19"
		};

		AssetDatabase.StartAssetEditing();
		try
		{
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/GameData/Inventory"))
					continue;

				ItemDefinition target = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
				if (target == null || !target.IsEquipment || target.EquipmentKind != EquipmentKind.Weapon)
				{
					skipped++;
					continue;
				}

				if (excludeNames.Contains(target.name))
				{
					skipped++;
					continue;
				}

				Undo.RecordObject(target, "Copy AK47 Vehicle Pose To Weapon");

				SerializedObject so = new SerializedObject(target);

				CopyField(so, "m_VehicleRightHandLocalPosition", source.VehicleRightHandLocalPosition);
				CopyField(so, "m_VehicleRightHandLocalEulerAngles", source.VehicleRightHandLocalEulerAngles);
				CopyField(so, "m_VehicleRightHandReadyLocalPosition", source.VehicleRightHandReadyLocalPosition);
				CopyField(so, "m_VehicleRightHandReadyLocalEulerAngles", source.VehicleRightHandReadyLocalEulerAngles);

				CopyField(so, "m_VehicleRightHandIkNotReadyLocalPosition", source.VehicleRightHandIkNotReadyLocalPosition);
				CopyField(so, "m_VehicleRightHandIkNotReadyLocalEulerAngles", source.VehicleRightHandIkNotReadyLocalEulerAngles);
				CopyField(so, "m_VehicleRightHandIkReadyLocalPosition", source.VehicleRightHandIkReadyLocalPosition);
				CopyField(so, "m_VehicleRightHandIkReadyLocalEulerAngles", source.VehicleRightHandIkReadyLocalEulerAngles);

				CopyField(so, "m_VehicleLeftHandIkNotReadyLocalPosition", source.VehicleLeftHandIkNotReadyLocalPosition);
				CopyField(so, "m_VehicleLeftHandIkNotReadyLocalEulerAngles", source.VehicleLeftHandIkNotReadyLocalEulerAngles);
				CopyField(so, "m_VehicleLeftHandIkReadyLocalPosition", source.VehicleLeftHandIkReadyLocalPosition);
				CopyField(so, "m_VehicleLeftHandIkReadyLocalEulerAngles", source.VehicleLeftHandIkReadyLocalEulerAngles);

				so.ApplyModifiedPropertiesWithoutUndo();
				EditorUtility.SetDirty(target);
				updated++;
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
		}

		AssetDatabase.SaveAssets();
		Debug.Log($"[CopyAk47VehiclePose] Copied AK47 vehicle pose + IK to {updated} weapons ({skipped} skipped).");
	}

	private static void CopyField(SerializedObject so, string name, Vector3 value)
	{
		SerializedProperty prop = so.FindProperty(name);
		if (prop != null)
			prop.vector3Value = value;
	}
}
#endif
