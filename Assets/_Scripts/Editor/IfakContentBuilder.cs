#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает IFAK: MedkitDefinition, ItemDefinition, визуал в руке и лут-префаб из объекта IFAK на сцене.
/// </summary>
public static class IfakContentBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_MedkitDefinitionPath = "Assets/GameData/Health/Medkit_IFAK.asset";
	private const string c_ItemPath = "Assets/GameData/Inventory/Medical/Item_IFAK.asset";
	private const string c_HandVisualPath = "Assets/Prefabs/World/Medical/Visual_IFAK.prefab";
	private const string c_LootPrefabPath = "Assets/Prefabs/World/Loot/Medical/Loot_IFAK.prefab";
	private const string c_FallbackVisualPath =
		"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Pouch_04.prefab";
	#endregion

	#region Hand Offsets
	private static readonly Vector3 s_HandLocalPosition = new Vector3(-0.045f, 0.028f, 0.072f);
	private static readonly Vector3 s_HandLocalEulerAngles = new Vector3(-68f, 198f, 286f);
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build IFAK Content")]
	public static void BuildIfakContent()
	{
		EnsureSceneLoaded();
		EnsureDirectory("Assets/GameData/Health");
		EnsureDirectory("Assets/GameData/Inventory/Medical");
		EnsureDirectory("Assets/Prefabs/World/Medical");
		EnsureDirectory("Assets/Prefabs/World/Loot/Medical");

		MedkitDefinition medkitDefinition = BuildMedkitDefinition();
		GameObject handVisual = BuildHandVisualPrefab();
		ItemDefinition item = BuildItemDefinition(medkitDefinition, handVisual);
		GameObject lootPrefab = BuildLootPrefab(item, handVisual);
		AssignItemReferences(item, medkitDefinition, handVisual, lootPrefab);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[IfakContentBuilder] IFAK content built.");
	}
	#endregion

	#region Build Steps
	private static MedkitDefinition BuildMedkitDefinition()
	{
		MedkitDefinition definition = AssetDatabase.LoadAssetAtPath<MedkitDefinition>(c_MedkitDefinitionPath);
		if (definition == null)
		{
			definition = ScriptableObject.CreateInstance<MedkitDefinition>();
			definition.name = "Medkit_IFAK";
			AssetDatabase.CreateAsset(definition, c_MedkitDefinitionPath);
		}

		SerializedObject so = new SerializedObject(definition);
		so.FindProperty("m_MaxResourcePoints").intValue = 300;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(definition);
		return definition;
	}

	private static ItemDefinition BuildItemDefinition(MedkitDefinition _medkitDefinition, GameObject _handVisual)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(c_ItemPath);
		if (item == null)
		{
			item = ScriptableObject.CreateInstance<ItemDefinition>();
			item.name = "Item_IFAK";
			AssetDatabase.CreateAsset(item, c_ItemPath);
		}

		SerializedObject so = new SerializedObject(item);
		so.FindProperty("m_LocalizationKey").stringValue = "item.medkit.ifak";
		so.FindProperty("m_Description").stringValue =
			"Individual First Aid Kit. Stabilizes wounds by spending medical resource; heavier injuries cost more.";
		so.FindProperty("m_BasePrice").intValue = 220;
		so.FindProperty("m_Category").enumValueIndex = (int)ItemCategory.General;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _handVisual;
		so.FindProperty("m_RightHandLocalPosition").vector3Value = s_HandLocalPosition;
		so.FindProperty("m_RightHandLocalEulerAngles").vector3Value = s_HandLocalEulerAngles;
		so.FindProperty("m_WeaponDefinition").objectReferenceValue = null;
		so.FindProperty("m_AmmoDefinition").objectReferenceValue = null;
		so.FindProperty("m_MagazineDefinition").objectReferenceValue = null;
		so.FindProperty("m_WeaponAttachmentDefinition").objectReferenceValue = null;
		so.FindProperty("m_GrenadeType").enumValueIndex = (int)GrenadeType.Unknown;
		so.FindProperty("m_AttachedBodyVisualPrefab").objectReferenceValue = null;
		so.FindProperty("m_MedkitDefinition").objectReferenceValue = _medkitDefinition;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
	}

	private static GameObject BuildHandVisualPrefab()
	{
		GameObject source = FindSceneObject("IFAK") ??
		                    AssetDatabase.LoadAssetAtPath<GameObject>(c_FallbackVisualPath);
		if (source == null)
			throw new InvalidOperationException($"Missing IFAK visual source: {c_FallbackVisualPath}");

		GameObject clone = CloneObject(source, null);
		try
		{
			clone.name = "Visual_IFAK";
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			clone.transform.localScale = Vector3.one;
			StripPhysicsAndPickup(clone);
			EnableRenderers(clone);
			return SaveAsPrefab(clone, c_HandVisualPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(clone);
		}
	}

	private static GameObject BuildLootPrefab(ItemDefinition _item, GameObject _handVisual)
	{
		GameObject root = new GameObject("Loot_IFAK");
		try
		{
			int lootLayer = LayerMask.NameToLayer("Loot");
			root.layer = lootLayer >= 0 ? lootLayer : root.layer;

			GameObject visual = PrefabUtility.InstantiatePrefab(_handVisual, root.transform) as GameObject;
			if (visual == null)
				throw new InvalidOperationException("Failed to instantiate IFAK hand visual for loot.");

			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			StripPhysicsAndPickup(visual);
			SetLayerRecursively(visual, root.layer);
			EnableRenderers(visual);

			BoxCollider collider = root.AddComponent<BoxCollider>();
			collider.center = new Vector3(0f, 0.02f, 0.015f);
			collider.size = new Vector3(0.14f, 0.08f, 0.1f);

			Rigidbody body = root.AddComponent<Rigidbody>();
			body.mass = 0.28f;
			body.linearDamping = 0.15f;
			body.angularDamping = 0.4f;
			body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

			WorldPickupItem pickup = root.AddComponent<WorldPickupItem>();
			SerializedObject pickupSo = new SerializedObject(pickup);
			pickupSo.FindProperty("m_Definition").objectReferenceValue = _item;
			pickupSo.ApplyModifiedPropertiesWithoutUndo();

			return SaveAsPrefab(root, c_LootPrefabPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static void AssignItemReferences(
		ItemDefinition _item,
		MedkitDefinition _medkitDefinition,
		GameObject _handVisual,
		GameObject _lootPrefab)
	{
		SerializedObject so = new SerializedObject(_item);
		so.FindProperty("m_MedkitDefinition").objectReferenceValue = _medkitDefinition;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _handVisual;
		so.FindProperty("m_DropWorldPrefab").objectReferenceValue = _lootPrefab;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
	}
	#endregion

	#region Helpers
	private static GameObject FindSceneObject(string _name)
	{
		if (string.IsNullOrEmpty(_name))
			return null;

		GameObject bestDuplicate = null;
		for (int s = 0; s < SceneManager.sceneCount; s++)
		{
			Scene scene = SceneManager.GetSceneAt(s);
			if (!scene.isLoaded)
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for (int r = 0; r < roots.Length; r++)
			{
				Transform[] children = roots[r].GetComponentsInChildren<Transform>(true);
				for (int c = 0; c < children.Length; c++)
				{
					if (children[c].name != _name)
						continue;

					if (!children[c].name.Contains("("))
						return children[c].gameObject;

					if (bestDuplicate == null)
						bestDuplicate = children[c].gameObject;
				}
			}
		}

		return bestDuplicate;
	}

	private static void EnsureSceneLoaded()
	{
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene scene = SceneManager.GetSceneAt(i);
			if (scene.isLoaded && scene.path == c_ScenePath)
				return;
		}

		EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
	}

	private static GameObject CloneObject(GameObject _source, Transform _parent)
	{
		GameObject clone = PrefabUtility.IsPartOfPrefabAsset(_source)
			? PrefabUtility.InstantiatePrefab(_source, _parent) as GameObject
			: UnityEngine.Object.Instantiate(_source, _parent);

		if (clone == null)
			throw new InvalidOperationException($"Failed to instantiate '{_source.name}'.");

		return clone;
	}

	private static GameObject SaveAsPrefab(GameObject _source, string _path)
	{
		EnsureDirectory(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_source, _path);
		if (prefab == null)
			throw new InvalidOperationException($"Failed to save prefab: {_path}");

		return prefab;
	}

	private static void StripPhysicsAndPickup(GameObject _root)
	{
		if (_root == null)
			return;

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			UnityEngine.Object.DestroyImmediate(colliders[i]);

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
			UnityEngine.Object.DestroyImmediate(bodies[i]);

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			UnityEngine.Object.DestroyImmediate(pickups[i]);
	}

	private static void EnableRenderers(GameObject _root)
	{
		Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].enabled = true;
	}

	private static void SetLayerRecursively(GameObject _root, int _layer)
	{
		if (_root == null)
			return;

		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
			children[i].gameObject.layer = _layer;
	}

	private static void EnsureDirectory(string _folderPath)
	{
		if (string.IsNullOrEmpty(_folderPath) || AssetDatabase.IsValidFolder(_folderPath))
			return;

		string parent = Path.GetDirectoryName(_folderPath)?.Replace('\\', '/');
		if (!string.IsNullOrEmpty(parent))
			EnsureDirectory(parent);

		AssetDatabase.CreateFolder(parent, Path.GetFileName(_folderPath));
	}
	#endregion
}
#endif
