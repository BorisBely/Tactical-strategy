#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Migrates ItemDefinition flat Hand_R poses → WeaponPoseDefinition SO,
/// and seeds GripRig/RightHand/{Stance}/{Ready|NotReady} from orphaned RightHandIk YAML.
/// </summary>
public static class WeaponPoseArchitectureMigration
{
	private const string c_PoseFolder = "Assets/GameData/WeaponPoses";
	private static readonly string[] c_ItemFolders =
	{
		"Assets/GameData/Inventory/AK",
		"Assets/GameData/Inventory/M4",
		"Assets/GameData/Inventory/Standalone",
	};

	[MenuItem("Polygone/Weapons/Architecture/Migrate Pose Definitions + RightHand Targets")]
	public static void MigrateAllMenu()
	{
		int n = MigrateAll();
		EditorUtility.DisplayDialog("Weapon Pose Architecture", $"Migrated {n} weapons.", "OK");
	}

	public static int MigrateAll()
	{
		if (!AssetDatabase.IsValidFolder(c_PoseFolder))
		{
			Directory.CreateDirectory(c_PoseFolder.Replace("Assets/", Application.dataPath + "/"));
			AssetDatabase.Refresh();
		}

		int count = 0;
		foreach (string folder in c_ItemFolders)
		{
			string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { folder });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
				if (item == null || item.Category != ItemCategory.Equipment
				    || item.EquipmentKind != EquipmentKind.Weapon
				    || item.WeaponDefinition == null
				    || item.WeaponAttachmentDefinition != null
				    || item.MagazineDefinition != null
				    || !item.name.StartsWith("Item_Weapon_"))
					continue;
				if (item.EquippedVisualPrefab == null)
					continue;
				if (MigrateOne(item, path))
					count++;
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		return count;
	}

	private static bool MigrateOne(ItemDefinition _item, string _itemPath)
	{
		string posePath = $"{c_PoseFolder}/WeaponPose_{_item.name.Replace("Item_Weapon_", "")}.asset";
		WeaponPoseDefinition pose = AssetDatabase.LoadAssetAtPath<WeaponPoseDefinition>(posePath);
		if (pose == null)
		{
			pose = ScriptableObject.CreateInstance<WeaponPoseDefinition>();
			AssetDatabase.CreateAsset(pose, posePath);
		}

		pose.ImportFromFlatFields(
			_item.RightHandLocalPosition,
			_item.RightHandLocalEulerAngles,
			_item.RightHandReadyLocalPosition,
			_item.RightHandReadyLocalEulerAngles,
			_item.CrouchRightHandLocalPosition,
			_item.CrouchRightHandLocalEulerAngles,
			_item.CrouchRightHandReadyLocalPosition,
			_item.CrouchRightHandReadyLocalEulerAngles,
			_item.VehicleRightHandLocalPosition,
			_item.VehicleRightHandLocalEulerAngles,
			_item.VehicleRightHandReadyLocalPosition,
			_item.VehicleRightHandReadyLocalEulerAngles);
		EditorUtility.SetDirty(pose);

		SerializedObject so = new SerializedObject(_item);
		so.FindProperty("m_WeaponPoseDefinition").objectReferenceValue = pose;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);

		string prefabPath = AssetDatabase.GetAssetPath(_item.EquippedVisualPrefab);
		if (string.IsNullOrEmpty(prefabPath))
			return true;

		GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
		try
		{
			WeaponGripRig grip = root.GetComponentInChildren<WeaponGripRig>(true);
			if (grip == null)
			{
				grip = root.AddComponent<WeaponGripRig>();
			}

			Transform gripRoot = EnsureChild(root.transform, WeaponGripRig.GripRigChildName);
			Transform left = gripRoot.Find(WeaponGripRig.LeftHandIkName)
			                 ?? gripRoot.Find(WeaponGripRig.LeftHandGripName);
			if (left == null)
				left = EnsureChild(gripRoot, WeaponGripRig.LeftHandIkName);
			Transform legacyRight = EnsureChild(gripRoot, WeaponGripRig.RightHandGripName);
			Transform rightRoot = gripRoot.Find(WeaponGripRig.RightHandIkRootName)
			                      ?? gripRoot.Find(WeaponGripRig.RightHandRootName);
			if (rightRoot == null)
				rightRoot = EnsureChild(gripRoot, WeaponGripRig.RightHandIkRootName);

			Dictionary<string, Vector3> ik = ParseOrphanedRightIk(File.ReadAllText(_itemPath));
			SeedRightTarget(rightRoot, WeaponGripRig.StandingName, WeaponGripRig.ReadyName,
				ik, "m_RightHandIkReadyLocalPosition", "m_RightHandIkReadyLocalEulerAngles");
			SeedRightTarget(rightRoot, WeaponGripRig.StandingName, WeaponGripRig.NotReadyName,
				ik, "m_RightHandIkNotReadyLocalPosition", "m_RightHandIkNotReadyLocalEulerAngles");
			SeedRightTarget(rightRoot, WeaponGripRig.CrouchName, WeaponGripRig.ReadyName,
				ik, "m_CrouchRightHandIkReadyLocalPosition", "m_CrouchRightHandIkReadyLocalEulerAngles",
				"m_RightHandIkReadyLocalPosition", "m_RightHandIkReadyLocalEulerAngles");
			SeedRightTarget(rightRoot, WeaponGripRig.CrouchName, WeaponGripRig.NotReadyName,
				ik, "m_CrouchRightHandIkNotReadyLocalPosition", "m_CrouchRightHandIkNotReadyLocalEulerAngles",
				"m_RightHandIkNotReadyLocalPosition", "m_RightHandIkNotReadyLocalEulerAngles");
			SeedRightTarget(rightRoot, WeaponGripRig.VehicleName, WeaponGripRig.ReadyName,
				ik, "m_VehicleRightHandIkReadyLocalPosition", "m_VehicleRightHandIkReadyLocalEulerAngles",
				"m_RightHandIkReadyLocalPosition", "m_RightHandIkReadyLocalEulerAngles");
			SeedRightTarget(rightRoot, WeaponGripRig.VehicleName, WeaponGripRig.NotReadyName,
				ik, "m_VehicleRightHandIkNotReadyLocalPosition", "m_VehicleRightHandIkNotReadyLocalEulerAngles",
				"m_RightHandIkNotReadyLocalPosition", "m_RightHandIkNotReadyLocalEulerAngles");

			// Left from Ready Left IK YAML if present
			if (TryParseVec(File.ReadAllText(_itemPath), "m_LeftHandIkReadyLocalPosition", out Vector3 lpos) &&
			    TryParseVec(File.ReadAllText(_itemPath), "m_LeftHandIkReadyLocalEulerAngles", out Vector3 leu))
			{
				left.localPosition = lpos;
				left.localEulerAngles = leu;
			}

			grip.SetGrips(legacyRight, left);
			grip.SetRightHandPoseTargets(
				FindChildPath(rightRoot, WeaponGripRig.StandingName, WeaponGripRig.ReadyName),
				FindChildPath(rightRoot, WeaponGripRig.StandingName, WeaponGripRig.NotReadyName),
				FindChildPath(rightRoot, WeaponGripRig.CrouchName, WeaponGripRig.ReadyName),
				FindChildPath(rightRoot, WeaponGripRig.CrouchName, WeaponGripRig.NotReadyName),
				FindChildPath(rightRoot, WeaponGripRig.VehicleName, WeaponGripRig.ReadyName),
				FindChildPath(rightRoot, WeaponGripRig.VehicleName, WeaponGripRig.NotReadyName));

			PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}

		return true;
	}

	private static void SeedRightTarget(
		Transform _rightRoot,
		string _stance,
		string _ready,
		Dictionary<string, Vector3> _ik,
		string _posKey,
		string _euKey,
		string _fallbackPosKey = null,
		string _fallbackEuKey = null)
	{
		Transform stance = EnsureChild(_rightRoot, _stance);
		Transform t = EnsureChild(stance, _ready);
		Vector3 pos = GetIk(_ik, _posKey, _fallbackPosKey);
		Vector3 eu = GetIk(_ik, _euKey, _fallbackEuKey);
		t.localPosition = pos;
		t.localEulerAngles = eu;
	}

	private static Vector3 GetIk(Dictionary<string, Vector3> _ik, string _key, string _fallback)
	{
		if (_ik.TryGetValue(_key, out Vector3 v) && v != Vector3.zero)
			return v;
		if (!string.IsNullOrEmpty(_fallback) && _ik.TryGetValue(_fallback, out v))
			return v;
		return Vector3.zero;
	}

	private static Transform EnsureChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;
		var go = new GameObject(_name);
		go.transform.SetParent(_parent, false);
		return go.transform;
	}

	private static Transform FindChildPath(Transform _root, string _a, string _b)
	{
		Transform a = _root.Find(_a);
		return a != null ? a.Find(_b) : null;
	}

	private static Dictionary<string, Vector3> ParseOrphanedRightIk(string _yaml)
	{
		var dict = new Dictionary<string, Vector3>();
		foreach (Match m in Regex.Matches(
			         _yaml,
			         @"(m_(?:Crouch|Vehicle)?RightHandIk(?:Ready|NotReady)?Local(?:Position|EulerAngles)): \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}"))
		{
			dict[m.Groups[1].Value] = new Vector3(
				float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
				float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
				float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture));
		}

		return dict;
	}

	private static bool TryParseVec(string _yaml, string _key, out Vector3 _v)
	{
		_v = Vector3.zero;
		Match m = Regex.Match(
			_yaml,
			Regex.Escape(_key) + @": \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE]+)\}");
		if (!m.Success)
			return false;
		_v = new Vector3(
			float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
			float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
			float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
		return true;
	}

	[MenuItem("Polygone/Weapons/Architecture/Rename GripRig Nodes (RightHandIK + LeftHandIK)", false, 22)]
	public static void RenameGripRigNodesMenu()
	{
		int n = RenameGripRigNodesOnAllWeaponPrefabs();
		EditorUtility.DisplayDialog("GripRig Rename", $"Updated {n} weapon prefabs.", "OK");
	}

	public static int RenameGripRigNodesOnAllWeaponPrefabs()
	{
		int count = 0;
		string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" });
		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				WeaponGripRig grip = root.GetComponentInChildren<WeaponGripRig>(true);
				if (grip == null)
					continue;

				Transform gripRoot = root.transform.Find(WeaponGripRig.GripRigChildName);
				if (gripRoot == null)
					continue;

				bool changed = false;
				Transform right = gripRoot.Find(WeaponGripRig.RightHandRootName);
				if (right != null && gripRoot.Find(WeaponGripRig.RightHandIkRootName) == null)
				{
					right.name = WeaponGripRig.RightHandIkRootName;
					changed = true;
				}

				Transform left = gripRoot.Find(WeaponGripRig.LeftHandGripName);
				if (left != null && gripRoot.Find(WeaponGripRig.LeftHandIkName) == null)
				{
					left.name = WeaponGripRig.LeftHandIkName;
					changed = true;
				}

				if (!changed)
					continue;

				PrefabUtility.SaveAsPrefabAsset(root, path);
				count++;
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		return count;
	}
}
#endif
