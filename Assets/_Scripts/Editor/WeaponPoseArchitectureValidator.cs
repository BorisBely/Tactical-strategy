#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Editor validation for GripRig + WeaponPoseDefinition integrity (errors, not silent fallback).</summary>
public static class WeaponPoseArchitectureValidator
{
	private static readonly string[] c_ItemFolders =
	{
		"Assets/GameData/Inventory/AK",
		"Assets/GameData/Inventory/M4",
		"Assets/GameData/Inventory/Standalone",
	};

	private static readonly string[] c_ForeGripPaths =
	{
		"Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_M4_ForeGrip1.prefab",
		"Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_M4_ForeGrip2.prefab",
		"Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_M4_ForeGrip3.prefab",
		"Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_M4_ForeGrip4.prefab",
		"Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_M4_ForeGrip5.prefab",
	};

	[MenuItem("Polygone/Weapons/Architecture/Validate GripRig + Pose Definitions", false, 21)]
	public static void ValidateAll()
	{
		var sb = new StringBuilder();
		int errors = 0;
		int warnings = 0;

		foreach (string folder in c_ItemFolders)
		{
			string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { folder });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
				if (item == null || !IsHoldWeaponItem(item))
					continue;
				if (item.EquippedVisualPrefab == null)
				{
					sb.AppendLine($"ERROR {item.name}: no EquippedVisualPrefab");
					errors++;
					continue;
				}

				if (item.WeaponPoseDefinition == null)
				{
					sb.AppendLine($"ERROR {item.name}: missing WeaponPoseDefinition");
					errors++;
				}
				else if (!item.WeaponPoseDefinition.TryGetPose(WeaponStance.Standing, WeaponPoseState.PointAim, out _))
				{
					sb.AppendLine($"ERROR {item.name}: WeaponPoseDefinition missing Standing/Ready");
					errors++;
				}
				else if (!item.WeaponPoseDefinition.TryGetPose(WeaponStance.Standing, WeaponPoseState.HighReady, out _))
				{
					sb.AppendLine($"WARN {item.name}: Standing/HighReady missing (fallback Aiming until authored)");
					warnings++;
				}

				GameObject prefab = item.EquippedVisualPrefab;
				WeaponGripRig grip = prefab.GetComponentInChildren<WeaponGripRig>(true);
				if (grip == null)
				{
					sb.AppendLine($"ERROR {item.name}: prefab has no WeaponGripRig");
					errors++;
					continue;
				}

				if (grip.LeftHandGrip == null)
				{
					sb.AppendLine($"ERROR {item.name}: no LeftHandGrip");
					errors++;
				}

				if (!grip.HasRightHandIkTargets)
				{
					sb.AppendLine($"ERROR {item.name}: missing RightHand Standing Ready/NotReady targets");
					errors++;
				}
			}
		}

		foreach (string fgPath in c_ForeGripPaths)
		{
			GameObject fg = AssetDatabase.LoadAssetAtPath<GameObject>(fgPath);
			if (fg == null)
			{
				sb.AppendLine($"ERROR missing foregrip prefab {fgPath}");
				errors++;
				continue;
			}

			WeaponForeGrip comp = fg.GetComponentInChildren<WeaponForeGrip>(true);
			if (comp == null || comp.LeftHandGrip == null)
			{
				sb.AppendLine($"ERROR {fg.name}: need WeaponForeGrip + LeftHandGrip");
				errors++;
			}
		}

		string report = errors == 0 && warnings == 0
			? "OK — all weapons/foregrips pass validation."
			: sb.ToString();
		Debug.Log($"[WeaponPoseArchitectureValidator] errors={errors} warnings={warnings}\n{report}");
		EditorUtility.DisplayDialog("Architecture Validation", report.Length > 1500 ? report.Substring(0, 1500) + "…" : report, "OK");
	}

	/// <summary>
	/// Only infantry hold weapons need PoseDefinition/GripRig.
	/// Attachments/mags/loot often default EquipmentKind=Weapon (0) — exclude them.
	/// </summary>
	private static bool IsHoldWeaponItem(ItemDefinition _item)
	{
		if (_item.Category != ItemCategory.Equipment)
			return false;
		if (_item.EquipmentKind != EquipmentKind.Weapon)
			return false;
		if (_item.WeaponAttachmentDefinition != null)
			return false;
		if (_item.MagazineDefinition != null)
			return false;
		if (_item.AmmoDefinition != null)
			return false;
		if (_item.WeaponDefinition == null)
			return false;
		if (!_item.name.StartsWith("Item_Weapon_"))
			return false;
		return true;
	}
}
#endif
