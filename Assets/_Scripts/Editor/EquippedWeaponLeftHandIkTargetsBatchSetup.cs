#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch: adds <c>LeftHandIkTarget_NotReady</c> to every Equipped_*.prefab.
/// Copies pose from existing <c>LeftHandIkTarget</c> as a starting point.
/// Seeds ItemDefinition left IK fields when they are still zero.
/// </summary>
public static class EquippedWeaponLeftHandIkTargetsBatchSetup
{
	#region Constants
	private const string c_WeaponsRoot = "Assets/Prefabs/Weapons";
	private const string c_InventoryRoot = "Assets/GameData/Inventory";
	private const string c_ReadyName = "LeftHandIkTarget";
	private const string c_NotReadyName = "LeftHandIkTarget_NotReady";
	#endregion

	#region Menu
	[MenuItem("Polygone/Weapons/Add LeftHandIkTarget_NotReady To All Equipped Prefabs")]
	public static void AddToAllEquippedPrefabs()
	{
		Dictionary<string, ItemDefinition> prefabGuidToItem = BuildPrefabGuidToItemMap();

		string[] prefabGuids = AssetDatabase.FindAssets("Equipped_ t:Prefab", new[] { c_WeaponsRoot });
		int createdNotReady = 0;
		int updatedItems = 0;
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
					"LeftHandIkTarget_NotReady",
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
					Transform ready = FindDirectOrDeepChild(root.transform, c_ReadyName);
					if (ready == null)
					{
						skipped++;
						report.AppendLine($"SKIP no {c_ReadyName}: {fileName}");
						continue;
					}

					Vector3 readyPos = ready.localPosition;
					Vector3 readyEuler = ready.localEulerAngles;

					bool changed = false;
					Transform notReady = FindDirectOrDeepChild(root.transform, c_NotReadyName);
					if (notReady == null)
					{
						GameObject go = new GameObject(c_NotReadyName);
						go.transform.SetParent(root.transform, false);
						notReady = go.transform;
						notReady.localPosition = readyPos;
						notReady.localRotation = Quaternion.Euler(readyEuler);
						notReady.localScale = Vector3.one;
						createdNotReady++;
						changed = true;
					}
					else if (notReady.parent != root.transform)
					{
						notReady.SetParent(root.transform, true);
						changed = true;
					}

					if (changed)
						PrefabUtility.SaveAsPrefabAsset(root, path);

					ItemDefinition item = null;
					if (prefabGuidToItem.TryGetValue(prefabGuids[i], out ItemDefinition mapped))
						item = mapped;

					if (item != null && SeedItemLeftIkFromPrefab(item, readyPos, readyEuler))
					{
						updatedItems++;
						report.AppendLine($"OK  {fileName}  + seeded {item.name}");
					}
					else
					{
						report.AppendLine(changed
							? $"OK  {fileName}  (created NotReady from Ready)"
							: $"OK  {fileName}  (already present)");
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
			$"LeftHandIkTarget_NotReady batch done.\n" +
			$"Created NotReady empties: {createdNotReady}\n" +
			$"Seeded ItemDefinition left IK: {updatedItems}\n" +
			$"Skipped: {skipped}\n\n" +
			report;

		Debug.Log(summary);
		EditorUtility.DisplayDialog("LeftHandIkTarget_NotReady", summary, "OK");
	}
	#endregion

	#region Private Methods
	private static bool SeedItemLeftIkFromPrefab(ItemDefinition _item, Vector3 _readyPos, Vector3 _readyEuler)
	{
		SerializedObject so = new SerializedObject(_item);
		SerializedProperty readyPos = so.FindProperty("m_LeftHandIkReadyLocalPosition");
		SerializedProperty readyEuler = so.FindProperty("m_LeftHandIkReadyLocalEulerAngles");
		SerializedProperty notReadyPos = so.FindProperty("m_LeftHandIkNotReadyLocalPosition");
		SerializedProperty notReadyEuler = so.FindProperty("m_LeftHandIkNotReadyLocalEulerAngles");
		SerializedProperty notReadyName = so.FindProperty("m_LeftHandIkTargetNotReadyChildName");

		if (readyPos == null || readyEuler == null || notReadyPos == null || notReadyEuler == null)
			return false;

		bool changed = false;
		if (readyPos.vector3Value == Vector3.zero && readyEuler.vector3Value == Vector3.zero)
		{
			readyPos.vector3Value = _readyPos;
			readyEuler.vector3Value = _readyEuler;
			changed = true;
		}

		if (notReadyPos.vector3Value == Vector3.zero && notReadyEuler.vector3Value == Vector3.zero)
		{
			notReadyPos.vector3Value = _readyPos;
			notReadyEuler.vector3Value = _readyEuler;
			changed = true;
		}

		if (notReadyName != null && string.IsNullOrWhiteSpace(notReadyName.stringValue))
		{
			notReadyName.stringValue = c_NotReadyName;
			changed = true;
		}

		if (!changed)
			return false;

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
		return true;
	}

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
