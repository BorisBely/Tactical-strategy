#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch: adds <c>RightHandIkTarget</c> and <c>RightHandIkTarget_NotReady</c> to every Equipped_*.prefab
/// under Assets/Prefabs/Weapons. Local pose is taken from the matching ItemDefinition when found,
/// otherwise from Item_Weapon_M4_ModA_1 IK fields.
/// </summary>
public static class EquippedWeaponRightHandIkTargetsBatchSetup
{
	#region Constants
	private const string c_WeaponsRoot = "Assets/Prefabs/Weapons";
	private const string c_InventoryRoot = "Assets/GameData/Inventory";
	private const string c_FallbackItemPath = "Assets/GameData/Inventory/M4/Item_Weapon_M4_ModA_1.asset";
	private const string c_ReadyName = "RightHandIkTarget";
	private const string c_NotReadyName = "RightHandIkTarget_NotReady";
	#endregion

	#region Menu
	[MenuItem("Polygone/Weapons/Add RightHandIkTargets To All Equipped Prefabs")]
	public static void AddToAllEquippedPrefabs()
	{
		ItemDefinition fallbackItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(c_FallbackItemPath);
		Vector3 fallbackReadyPos = fallbackItem != null ? fallbackItem.RightHandIkReadyLocalPosition : Vector3.zero;
		Vector3 fallbackReadyEuler = fallbackItem != null ? fallbackItem.RightHandIkReadyLocalEulerAngles : Vector3.zero;
		Vector3 fallbackNotReadyPos = fallbackItem != null ? fallbackItem.RightHandIkNotReadyLocalPosition : Vector3.zero;
		Vector3 fallbackNotReadyEuler = fallbackItem != null ? fallbackItem.RightHandIkNotReadyLocalEulerAngles : Vector3.zero;

		Dictionary<string, ItemDefinition> prefabGuidToItem = BuildPrefabGuidToItemMap();

		string[] prefabGuids = AssetDatabase.FindAssets("Equipped_ t:Prefab", new[] { c_WeaponsRoot });
		int createdReady = 0;
		int createdNotReady = 0;
		int updatedPose = 0;
		int skipped = 0;
		var report = new StringBuilder();

		try
		{
			AssetDatabase.StartAssetEditing();

			for (int i = 0; i < prefabGuids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
					continue;

				string fileName = System.IO.Path.GetFileName(path);
				if (!fileName.StartsWith("Equipped_"))
					continue;

				EditorUtility.DisplayProgressBar(
					"RightHandIkTargets",
					fileName,
					(float)i / Mathf.Max(1, prefabGuids.Length));

				GameObject root = PrefabUtility.LoadPrefabContents(path);
				if (root == null)
				{
					skipped++;
					report.AppendLine($"SKIP load: {path}");
					continue;
				}

				try
				{
					ItemDefinition item = null;
					if (prefabGuidToItem.TryGetValue(prefabGuids[i], out ItemDefinition mapped))
						item = mapped;

					Vector3 readyPos = item != null ? item.RightHandIkReadyLocalPosition : fallbackReadyPos;
					Vector3 readyEuler = item != null ? item.RightHandIkReadyLocalEulerAngles : fallbackReadyEuler;
					Vector3 notReadyPos = item != null ? item.RightHandIkNotReadyLocalPosition : fallbackNotReadyPos;
					Vector3 notReadyEuler = item != null ? item.RightHandIkNotReadyLocalEulerAngles : fallbackNotReadyEuler;

					bool changed = false;
					if (EnsureIkTarget(root.transform, c_ReadyName, readyPos, readyEuler, out bool readyCreated))
					{
						changed = true;
						if (readyCreated)
							createdReady++;
						else
							updatedPose++;
					}

					if (EnsureIkTarget(root.transform, c_NotReadyName, notReadyPos, notReadyEuler, out bool notReadyCreated))
					{
						changed = true;
						if (notReadyCreated)
							createdNotReady++;
						else
							updatedPose++;
					}

					if (changed)
					{
						PrefabUtility.SaveAsPrefabAsset(root, path);
						string itemName = item != null ? item.name : "fallback M4_ModA_1";
						report.AppendLine($"OK  {fileName}  ← {itemName}");
					}
					else
					{
						skipped++;
						report.AppendLine($"OK  {fileName}  (already present, pose unchanged)");
					}
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
			EditorUtility.ClearProgressBar();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		string summary =
			$"RightHandIkTargets batch done.\n" +
			$"Created Ready: {createdReady}\n" +
			$"Created NotReady: {createdNotReady}\n" +
			$"Pose updates on existing: {updatedPose}\n" +
			$"Unchanged/skipped: {skipped}\n\n" +
			report;

		Debug.Log(summary);
		EditorUtility.DisplayDialog("RightHandIkTargets", summary, "OK");
	}
	#endregion

	#region Private Methods
	private static Dictionary<string, ItemDefinition> BuildPrefabGuidToItemMap()
	{
		var map = new Dictionary<string, ItemDefinition>();
		string[] itemGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { c_InventoryRoot });
		for (int i = 0; i < itemGuids.Length; i++)
		{
			string itemPath = AssetDatabase.GUIDToAssetPath(itemGuids[i]);
			ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
			if (item == null || item.EquippedVisualPrefab == null)
				continue;

			string prefabPath = AssetDatabase.GetAssetPath(item.EquippedVisualPrefab);
			if (string.IsNullOrEmpty(prefabPath))
				continue;

			string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
			if (string.IsNullOrEmpty(prefabGuid))
				continue;

			map[prefabGuid] = item;
		}

		return map;
	}

	/// <returns>True if transform was created or local pose was written.</returns>
	private static bool EnsureIkTarget(
		Transform _root,
		string _name,
		Vector3 _localPosition,
		Vector3 _localEuler,
		out bool _created)
	{
		_created = false;
		Transform existing = FindDirectOrDeepChild(_root, _name);
		if (existing == null)
		{
			GameObject go = new GameObject(_name);
			go.transform.SetParent(_root, false);
			existing = go.transform;
			_created = true;
		}
		else if (existing.parent != _root)
		{
			existing.SetParent(_root, true);
		}

		bool poseChanged =
			existing.localPosition != _localPosition ||
			existing.localEulerAngles != _localEuler ||
			existing.localScale != Vector3.one;

		existing.localPosition = _localPosition;
		existing.localRotation = Quaternion.Euler(_localEuler);
		existing.localScale = Vector3.one;

		return _created || poseChanged;
	}

	private static Transform FindDirectOrDeepChild(Transform _root, string _name)
	{
		Transform direct = _root.Find(_name);
		if (direct != null)
			return direct;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != _root && all[i].name == _name)
				return all[i];
		}

		return null;
	}
	#endregion
}
#endif
