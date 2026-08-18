#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-migrates equipped weapon / foregrip prefabs to WeaponGripRig architecture.
/// Seeds grip transforms from Ready IK asset data or existing IK dummy empties.
/// </summary>
[InitializeOnLoad]
public static class WeaponGripRigMigration
{
	private const string c_PendingFlagRelative = "Assets/.grip_rig_migrate_pending";

	private static readonly string[] c_EquippedRoots =
	{
		"Assets/Prefabs/Weapons/M4/Equipped",
		"Assets/Prefabs/Weapons/AK/Equipped",
		"Assets/Prefabs/Weapons/Standalone/Equipped",
		"Assets/Prefabs/Weapons/RocketLaunchers/Equipped"
	};

	private static readonly string[] c_ForeGripVisualRoots =
	{
		"Assets/Prefabs/Weapons/M4/Visuals/Attachments"
	};

	static WeaponGripRigMigration()
	{
		EditorApplication.delayCall += TryRunPendingMigration;
	}

	private static void TryRunPendingMigration()
	{
		if (!File.Exists(c_PendingFlagRelative))
			return;

		try
		{
			int count = MigrateAll();
			Debug.Log($"[WeaponGripRigMigration] Pending flag migration finished: {count} prefabs.");
		}
		finally
		{
			AssetDatabase.DeleteAsset(c_PendingFlagRelative);
			if (File.Exists(c_PendingFlagRelative))
				File.Delete(c_PendingFlagRelative);
		}
	}

	[MenuItem("Polygone/Weapons/GripRig/Migrate All Equipped Weapons")]
	public static void MigrateAllMenu()
	{
		int count = MigrateAll();
		EditorUtility.DisplayDialog("GripRig Migration", $"Migrated {count} prefabs.", "OK");
	}

	[MenuItem("Polygone/Weapons/GripRig/Queue Migrate On Next Domain Reload")]
	public static void QueuePendingMigration()
	{
		File.WriteAllText(c_PendingFlagRelative, "1");
		AssetDatabase.Refresh();
		Debug.Log("[WeaponGripRigMigration] Queued. Will run after next script reload / recompile.");
	}

	/// <summary>Batch: Unity -batchmode -executeMethod WeaponGripRigMigration.MigrateAllBatch</summary>
	public static void MigrateAllBatch()
	{
		int count = MigrateAll();
		Debug.Log($"[WeaponGripRigMigration] Migrated {count} prefabs.");
		EditorApplication.Exit(0);
	}

	public static int MigrateAll()
	{
		Dictionary<string, ItemDefinition> prefabGuidToItem = BuildEquippedPrefabGuidMap();
		int migrated = 0;

		foreach (string root in c_EquippedRoots)
		{
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (MigrateEquippedWeaponPrefab(path, prefabGuidToItem))
					migrated++;
			}
		}

		foreach (string root in c_ForeGripVisualRoots)
		{
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.Contains("ForeGrip"))
					continue;
				if (MigrateForeGripPrefab(path, prefabGuidToItem))
					migrated++;
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		return migrated;
	}

	private static Dictionary<string, ItemDefinition> BuildEquippedPrefabGuidMap()
	{
		var map = new Dictionary<string, ItemDefinition>();
		string[] itemGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/GameData/Inventory" });
		foreach (string guid in itemGuids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
			if (item == null || item.EquippedVisualPrefab == null)
				continue;

			string prefabPath = AssetDatabase.GetAssetPath(item.EquippedVisualPrefab);
			if (string.IsNullOrEmpty(prefabPath))
				continue;

			string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
			if (!string.IsNullOrEmpty(prefabGuid))
				map[prefabGuid] = item;
		}

		return map;
	}

	private static bool MigrateEquippedWeaponPrefab(string _path, Dictionary<string, ItemDefinition> _prefabGuidToItem)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(_path);
		if (root == null)
			return false;

		bool changed;
		try
		{
			WeaponGripRig gripRig = root.GetComponent<WeaponGripRig>();
			if (gripRig == null)
				gripRig = root.AddComponent<WeaponGripRig>();

			Transform gripRoot = EnsureChild(root.transform, WeaponGripRig.GripRigChildName);
			Transform rightGrip = EnsureChild(gripRoot, WeaponGripRig.RightHandGripName);
			Transform leftGrip = EnsureChild(gripRoot, WeaponGripRig.LeftHandGripName);
			gripRig.SetGrips(rightGrip, leftGrip);

			string prefabGuid = AssetDatabase.AssetPathToGUID(_path);
			_prefabGuidToItem.TryGetValue(prefabGuid, out ItemDefinition item);

			SeedGripFromSources(root.transform, rightGrip, leftGrip, item);
			changed = true;
			PrefabUtility.SaveAsPrefabAsset(root, _path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}

		Debug.Log($"[WeaponGripRigMigration] Equipped: {_path}", AssetDatabase.LoadAssetAtPath<Object>(_path));
		return changed;
	}

	private static bool MigrateForeGripPrefab(string _path, Dictionary<string, ItemDefinition> _prefabGuidToItem)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(_path);
		if (root == null)
			return false;

		try
		{
			WeaponForeGrip foreGrip = root.GetComponent<WeaponForeGrip>();
			if (foreGrip == null)
				foreGrip = root.AddComponent<WeaponForeGrip>();

			Transform leftGrip = EnsureChild(root.transform, WeaponForeGrip.LeftHandGripName);
			foreGrip.SetLeftHandGrip(leftGrip);

			Vector3 pos = Vector3.zero;
			Vector3 euler = Vector3.zero;
			bool seeded = false;

			Transform legacy = FindChildRecursive(root.transform, "LeftHandIkTarget");
			if (legacy != null)
			{
				pos = legacy.localPosition;
				euler = legacy.localEulerAngles;
				seeded = true;
			}

			if (seeded)
			{
				leftGrip.localPosition = pos;
				leftGrip.localRotation = Quaternion.Euler(euler);
			}

			PrefabUtility.SaveAsPrefabAsset(root, _path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}

		Debug.Log($"[WeaponGripRigMigration] ForeGrip: {_path}", AssetDatabase.LoadAssetAtPath<Object>(_path));
		return true;
	}

	private static void SeedGripFromSources(
		Transform _weaponRoot,
		Transform _rightGrip,
		Transform _leftGrip,
		ItemDefinition _)
	{
		Transform legacyRight = FindChildRecursive(_weaponRoot, "RightHandIkTarget");
		Transform legacyLeft = FindChildRecursive(_weaponRoot, "LeftHandIkTarget");

		if (legacyRight != null)
		{
			_rightGrip.localPosition = _weaponRoot.InverseTransformPoint(legacyRight.position);
			_rightGrip.localRotation = Quaternion.Inverse(_weaponRoot.rotation) * legacyRight.rotation;
		}

		if (legacyLeft != null)
		{
			_leftGrip.localPosition = _weaponRoot.InverseTransformPoint(legacyLeft.position);
			_leftGrip.localRotation = Quaternion.Inverse(_weaponRoot.rotation) * legacyLeft.rotation;
		}
	}

	private static Transform EnsureChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;

		var go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return t;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}
}
#endif
