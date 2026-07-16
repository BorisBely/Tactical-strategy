using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public static class EquipToLootVisualSync
{
	private enum Result { Updated, Skipped, Error }

	[MenuItem("Tools/Sync Equip Visuals To Loot")]
	private static void Sync()
	{
		var pairs = CollectWeaponPairs();
		int updated = 0;
		int skipped = 0;
		int errors = 0;

		try
		{
			AssetDatabase.StartAssetEditing();

			foreach (var (equipPath, lootPath) in pairs)
			{
				Result result = SyncPair(equipPath, lootPath);
				switch (result)
				{
					case Result.Updated: updated++; break;
					case Result.Skipped: skipped++; break;
					case Result.Error: errors++; break;
				}
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Debug.Log($"[EquipToLootVisualSync] Done. Updated: {updated}, Skipped: {skipped}, Errors: {errors}, Total: {pairs.Count}");
	}

	private static List<(string equipPath, string lootPath)> CollectWeaponPairs()
	{
		var pairs = new List<(string, string)>();
		string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");

		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			var itemDef = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

			if (itemDef == null) continue;
			if (itemDef.WeaponDefinition == null) continue;
			if (itemDef.EquippedVisualPrefab == null || itemDef.DropWorldPrefab == null) continue;

			string equipPath = AssetDatabase.GetAssetPath(itemDef.EquippedVisualPrefab);
			string lootPath = AssetDatabase.GetAssetPath(itemDef.DropWorldPrefab);

			if (!string.IsNullOrEmpty(equipPath) && !string.IsNullOrEmpty(lootPath))
				pairs.Add((equipPath, lootPath));
		}

		return pairs;
	}

	private static Result SyncPair(string equipPath, string lootPath)
	{
		var equipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(equipPath);
		var lootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lootPath);

		if (equipPrefab == null || lootPrefab == null)
			return Result.Error;

		GameObject equipRoot = PrefabUtility.LoadPrefabContents(equipPath);
		GameObject lootRoot = PrefabUtility.LoadPrefabContents(lootPath);

		try
		{
			var equipChildNames = new HashSet<string>();
			for (int i = 0; i < equipRoot.transform.childCount; i++)
				equipChildNames.Add(equipRoot.transform.GetChild(i).name);

			Transform equipInstance = FindEquipInstance(lootRoot.transform, equipChildNames);
			if (equipInstance == null)
			{
				Debug.LogWarning($"[EquipToLootVisualSync] No equip instance in '{lootPath}' for '{equipPrefab.name}'");
				return Result.Error;
			}

			var equipMap = BuildRelativeTransformMap(equipRoot);
			var lootMap = BuildRelativeTransformMap(equipInstance.gameObject);

			// Process shallow paths first so parents exist before children
			var sortedPaths = equipMap.Keys
				.OrderBy(p => p.Count(c => c == '/'))
				.ToList();

			bool changed = false;
			int added = 0;
			int fixedTransforms = 0;
			int filledComponents = 0;

			foreach (string relPath in sortedPaths)
			{
				Transform equipChild = equipMap[relPath];

				if (lootMap.TryGetValue(relPath, out Transform lootChild))
				{
					if (!TransformMatches(equipChild, lootChild))
					{
						lootChild.localPosition = equipChild.localPosition;
						lootChild.localRotation = equipChild.localRotation;
						lootChild.localScale = equipChild.localScale;
						fixedTransforms++;
						changed = true;
					}

					if (FillMissingComponents(equipChild, lootChild))
					{
						filledComponents++;
						changed = true;
					}
				}
				else
				{
					string parentPath = GetParentPath(relPath);
					Transform parent = string.IsNullOrEmpty(parentPath)
						? equipInstance
						: FindByRelativePath(equipInstance, parentPath);

					if (parent != null)
					{
						CloneChildWithComponents(equipChild, parent, equipChild.name);
						added++;
						changed = true;
					}
				}
			}

			if (changed)
			{
				PrefabUtility.SaveAsPrefabAsset(lootRoot, lootPath);
				Debug.Log($"[EquipToLootVisualSync] {equipPrefab.name} -> {lootPrefab.name}: +{added} added, ~{fixedTransforms} transforms, *{filledComponents} components");
			}

			return changed ? Result.Updated : Result.Skipped;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(equipRoot);
			PrefabUtility.UnloadPrefabContents(lootRoot);
		}
	}

	private static Transform FindEquipInstance(Transform lootRoot, HashSet<string> equipChildNames)
	{
		Transform bestMatch = null;
		int bestScore = 0;

		for (int i = 0; i < lootRoot.childCount; i++)
		{
			Transform candidate = lootRoot.GetChild(i);
			int score = 0;
			for (int j = 0; j < candidate.childCount; j++)
				if (equipChildNames.Contains(candidate.GetChild(j).name))
					score++;

			if (score > bestScore)
			{
				bestScore = score;
				bestMatch = candidate;
			}
		}

		if (bestScore >= 3)
			return bestMatch;

		for (int i = 0; i < lootRoot.childCount; i++)
		{
			Transform child = lootRoot.GetChild(i);
			for (int j = 0; j < child.childCount; j++)
			{
				Transform grandChild = child.GetChild(j);
				int score = 0;
				for (int k = 0; k < grandChild.childCount; k++)
					if (equipChildNames.Contains(grandChild.GetChild(k).name))
						score++;

				if (score > bestScore)
				{
					bestScore = score;
					bestMatch = grandChild;
				}
			}
		}

		return bestScore >= 3 ? bestMatch : null;
	}

	private static Dictionary<string, Transform> BuildRelativeTransformMap(GameObject root)
	{
		var map = new Dictionary<string, Transform>();

		for (int i = 0; i < root.transform.childCount; i++)
			BuildRelativeTransformMapRecursive(root.transform.GetChild(i), "", map);

		return map;
	}

	private static void BuildRelativeTransformMapRecursive(Transform t, string parentPath, Dictionary<string, Transform> map)
	{
		string path = string.IsNullOrEmpty(parentPath) ? t.name : parentPath + "/" + t.name;
		map[path] = t;

		for (int i = 0; i < t.childCount; i++)
			BuildRelativeTransformMapRecursive(t.GetChild(i), path, map);
	}

	private static Transform FindByRelativePath(Transform root, string relativePath)
	{
		string[] parts = relativePath.Split('/');
		Transform current = root;

		for (int i = 0; i < parts.Length; i++)
		{
			current = current.Find(parts[i]);
			if (current == null)
				return null;
		}

		return current;
	}

	private static string GetParentPath(string path)
	{
		int lastSlash = path.LastIndexOf('/');
		return lastSlash >= 0 ? path.Substring(0, lastSlash) : "";
	}

	private static bool TransformMatches(Transform a, Transform b)
	{
		return a.localPosition == b.localPosition
			&& a.localRotation == b.localRotation
			&& a.localScale == b.localScale;
	}

	private static void CloneChildWithComponents(Transform source, Transform parent, string name)
	{
		GameObject clone = new GameObject(name);
		clone.transform.SetParent(parent, false);
		clone.transform.localPosition = source.localPosition;
		clone.transform.localRotation = source.localRotation;
		clone.transform.localScale = source.localScale;

		Component[] sourceComponents = source.GetComponents<Component>();
		for (int i = 0; i < sourceComponents.Length; i++)
		{
			Component sourceComp = sourceComponents[i];
			if (sourceComp is Transform)
				continue;

			System.Type compType = sourceComp.GetType();
			Component newComp = clone.AddComponent(compType);
			EditorUtility.CopySerialized(sourceComp, newComp);
		}
	}

	private static bool FillMissingComponents(Transform source, Transform target)
	{
		bool added = false;
		Component[] sourceComponents = source.GetComponents<Component>();

		for (int i = 0; i < sourceComponents.Length; i++)
		{
			Component sourceComp = sourceComponents[i];
			if (sourceComp is Transform)
				continue;

			System.Type compType = sourceComp.GetType();
			Component targetComp = target.GetComponent(compType);

			if (targetComp == null)
			{
				targetComp = target.gameObject.AddComponent(compType);
				EditorUtility.CopySerialized(sourceComp, targetComp);
				added = true;
			}
		}

		return added;
	}
}
