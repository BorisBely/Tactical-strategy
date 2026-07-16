#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает предметы гранат, лут-префабы и визуалы крепления на теле.
/// </summary>
public static class GrenadeContentBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	#endregion

	#region Serializable Types
	private sealed class GrenadeBuildSpec
	{
		public string ItemAssetName;
		public string ItemPath;
		public string LootPrefabPath;
		public string AttachedVisualPath;
		public string LocalizationKey;
		public string Description;
		public int BasePrice;
		public GrenadeType GrenadeType;
		public string LootPrefabRootName;
		public string AttachedPrefabRootName;
		public string[] SceneLootObjectNames = Array.Empty<string>();
		public string FallbackLootPrefabPath;
		public string[] SceneAttachedObjectNames = Array.Empty<string>();
		public string FallbackAttachedPrefabPath;
	}
	#endregion

	#region Specs
	private static readonly GrenadeBuildSpec[] s_GrenadeSpecs =
	{
		new GrenadeBuildSpec
		{
			ItemAssetName = "Item_Grenade_Frag_01",
			ItemPath = "Assets/GameData/Inventory/Grenades/Item_Grenade_Frag_01.asset",
			LootPrefabPath = "Assets/Prefabs/World/Loot/Grenades/Loot_Grenade_Frag_01.prefab",
			AttachedVisualPath = "Assets/Prefabs/Characters/BodyDecorations/Attach_Grenade_Frag_01.prefab",
			LocalizationKey = "item.grenade.frag_01",
			Description = "Fragmentation grenade.",
			BasePrice = 180,
			GrenadeType = GrenadeType.Fragmentation,
			LootPrefabRootName = "Loot_Grenade_Frag_01",
			AttachedPrefabRootName = "Attach_Grenade_Frag_01",
			SceneLootObjectNames = new[] { "Grenade_01", "SM_Wep_Grenade_01" },
			FallbackLootPrefabPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Grenade_01.prefab",
			SceneAttachedObjectNames = new[] { "SM_Chr_Attach_Grenade_01" },
			FallbackAttachedPrefabPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Grenade_01.prefab"
		},
		new GrenadeBuildSpec
		{
			ItemAssetName = "Item_Grenade_RGD5",
			ItemPath = "Assets/GameData/Inventory/Grenades/Item_Grenade_RGD5.asset",
			LootPrefabPath = "Assets/Prefabs/World/Loot/Grenades/Loot_Grenade_RGD5.prefab",
			AttachedVisualPath = "Assets/Prefabs/Characters/BodyDecorations/Attach_Grenade_RGD5.prefab",
			LocalizationKey = "item.grenade.rgd5",
			Description = "RGD-5 fragmentation grenade.",
			BasePrice = 200,
			GrenadeType = GrenadeType.Fragmentation,
			LootPrefabRootName = "Loot_Grenade_RGD5",
			AttachedPrefabRootName = "Attach_Grenade_RGD5",
			SceneLootObjectNames = new[] { "GrenadeRGD5" },
			FallbackLootPrefabPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Grenade_01.prefab",
			SceneAttachedObjectNames = new[] { "GrenadeRGD5" },
			FallbackAttachedPrefabPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Grenade_01.prefab"
		},
		new GrenadeBuildSpec
		{
			ItemAssetName = "Item_Grenade_F1",
			ItemPath = "Assets/GameData/Inventory/Grenades/Item_Grenade_F1.asset",
			LootPrefabPath = "Assets/Prefabs/World/Loot/Grenades/Loot_Grenade_F1.prefab",
			AttachedVisualPath = "Assets/Prefabs/Characters/BodyDecorations/Attach_Grenade_F1.prefab",
			LocalizationKey = "item.grenade.f1",
			Description = "F-1 fragmentation grenade.",
			BasePrice = 220,
			GrenadeType = GrenadeType.Fragmentation,
			LootPrefabRootName = "Loot_Grenade_F1",
			AttachedPrefabRootName = "Attach_Grenade_F1",
			SceneLootObjectNames = new[] { "GrenadeF1" },
			FallbackLootPrefabPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Grenade_01.prefab",
			SceneAttachedObjectNames = new[] { "GrenadeF1" },
			FallbackAttachedPrefabPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Grenade_01.prefab"
		},
		new GrenadeBuildSpec
		{
			ItemAssetName = "Item_Grenade_Flash_01",
			ItemPath = "Assets/GameData/Inventory/Grenades/Item_Grenade_Flash_01.asset",
			LootPrefabPath = "Assets/Prefabs/World/Loot/Grenades/Loot_Grenade_Flash_01.prefab",
			AttachedVisualPath = "Assets/Prefabs/Characters/BodyDecorations/Attach_Grenade_Flash_01.prefab",
			LocalizationKey = "item.grenade.flash_01",
			Description = "Flashbang grenade.",
			BasePrice = 160,
			GrenadeType = GrenadeType.Flash,
			LootPrefabRootName = "Loot_Grenade_Flash_01",
			AttachedPrefabRootName = "Attach_Grenade_Flash_01",
			SceneLootObjectNames = new[] { "Grenade_Flash_01", "SM_Wep_Flashbang_01" },
			FallbackLootPrefabPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Flashbang_01.prefab",
			SceneAttachedObjectNames = new[] { "SM_Chr_Attach_Grenade_Flash_01" },
			FallbackAttachedPrefabPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Grenade_Flash_01.prefab"
		},
		new GrenadeBuildSpec
		{
			ItemAssetName = "Item_Grenade_Smoke_01",
			ItemPath = "Assets/GameData/Inventory/Grenades/Item_Grenade_Smoke_01.asset",
			LootPrefabPath = "Assets/Prefabs/World/Loot/Grenades/Loot_Grenade_Smoke_01.prefab",
			AttachedVisualPath = "Assets/Prefabs/Characters/BodyDecorations/Attach_Grenade_Smoke_01.prefab",
			LocalizationKey = "item.grenade.smoke_01",
			Description = "Smoke grenade.",
			BasePrice = 150,
			GrenadeType = GrenadeType.Smoke,
			LootPrefabRootName = "Loot_Grenade_Smoke_01",
			AttachedPrefabRootName = "Attach_Grenade_Smoke_01",
			SceneLootObjectNames = new[] { "Grenade_Smoke_01", "SM_Wep_Grenade_Smoke_01" },
			FallbackLootPrefabPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Grenade_Smoke_01.prefab",
			SceneAttachedObjectNames = new[] { "SM_Chr_Attach_Grenade_Smoke_01" },
			FallbackAttachedPrefabPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Grenade_Smoke_01.prefab"
		}
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Grenade Content")]
	public static void BuildGrenadeContent() => BuildAllGrenades();

	public static void BuildAllGrenades()
	{
		EnsureSceneLoaded();
		EnsureDirectory("Assets/GameData/Inventory/Grenades");
		EnsureDirectory("Assets/Prefabs/World/Loot/Grenades");
		EnsureDirectory("Assets/Prefabs/Characters/BodyDecorations");

		for (int i = 0; i < s_GrenadeSpecs.Length; i++)
			BuildGrenade(s_GrenadeSpecs[i]);

		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[GrenadeContentBuilder] Built {s_GrenadeSpecs.Length} grenade item(s).");
	}
	#endregion

	#region Build Steps
	private static void BuildGrenade(GrenadeBuildSpec _spec)
	{
		GameObject attachedVisual = BuildAttachedVisualPrefab(_spec);
		ItemDefinition item = BuildItem(_spec, attachedVisual);
		GameObject lootPrefab = BuildLootPrefab(_spec, item);
		AssignItemPrefabReferences(item, lootPrefab, attachedVisual);
	}

	private static ItemDefinition BuildItem(GrenadeBuildSpec _spec, GameObject _attachedVisual)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_spec.ItemPath);
		if (item == null)
		{
			item = ScriptableObject.CreateInstance<ItemDefinition>();
			item.name = _spec.ItemAssetName;
			AssetDatabase.CreateAsset(item, _spec.ItemPath);
		}

		SerializedObject so = new SerializedObject(item);
		so.FindProperty("m_LocalizationKey").stringValue = _spec.LocalizationKey;
		so.FindProperty("m_Description").stringValue = _spec.Description;
		so.FindProperty("m_BasePrice").intValue = _spec.BasePrice;
		so.FindProperty("m_Category").enumValueIndex = (int)ItemCategory.General;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = null;
		so.FindProperty("m_WeaponDefinition").objectReferenceValue = null;
		so.FindProperty("m_AmmoDefinition").objectReferenceValue = null;
		so.FindProperty("m_MagazineDefinition").objectReferenceValue = null;
		so.FindProperty("m_WeaponAttachmentDefinition").objectReferenceValue = null;
		so.FindProperty("m_GrenadeType").enumValueIndex = (int)_spec.GrenadeType;
		so.FindProperty("m_AttachedBodyVisualPrefab").objectReferenceValue = _attachedVisual;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
	}

	private static GameObject BuildLootPrefab(GrenadeBuildSpec _spec, ItemDefinition _item)
	{
		GameObject source = FindSceneObject(_spec.SceneLootObjectNames) ??
		                    AssetDatabase.LoadAssetAtPath<GameObject>(_spec.FallbackLootPrefabPath);
		if (source == null)
			throw new InvalidOperationException(
				$"Missing loot source for {_spec.ItemAssetName}: {_spec.FallbackLootPrefabPath}");

		GameObject root = new GameObject(_spec.LootPrefabRootName);
		try
		{
			int lootLayer = LayerMask.NameToLayer("Loot");
			root.layer = lootLayer >= 0 ? lootLayer : root.layer;

			GameObject visual = CloneObject(source, root.transform);
			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			StripPhysicsAndPickup(visual);
			SetLayerRecursively(visual, root.layer);
			EnableRenderers(visual);

			BoxCollider collider = root.AddComponent<BoxCollider>();
			collider.center = new Vector3(0f, 0.03f, 0f);
			collider.size = new Vector3(0.16f, 0.12f, 0.16f);

			Rigidbody body = root.AddComponent<Rigidbody>();
			body.mass = 0.35f;
			body.linearDamping = 0.15f;
			body.angularDamping = 0.4f;
			body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

			WorldPickupItem pickup = root.AddComponent<WorldPickupItem>();
			SerializedObject pickupSo = new SerializedObject(pickup);
			pickupSo.FindProperty("m_Definition").objectReferenceValue = _item;
			pickupSo.ApplyModifiedPropertiesWithoutUndo();

			return SaveAsPrefab(root, _spec.LootPrefabPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static GameObject BuildAttachedVisualPrefab(GrenadeBuildSpec _spec)
	{
		GameObject source = FindSceneObject(_spec.SceneAttachedObjectNames) ??
		                    AssetDatabase.LoadAssetAtPath<GameObject>(_spec.FallbackAttachedPrefabPath);
		if (source == null)
			throw new InvalidOperationException(
				$"Missing attached visual source for {_spec.ItemAssetName}: {_spec.FallbackAttachedPrefabPath}");

		GameObject clone = CloneObject(source, null);
		try
		{
			clone.name = _spec.AttachedPrefabRootName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			clone.transform.localScale = source.transform.localScale;
			StripPhysicsAndPickup(clone);
			EnableRenderers(clone);
			return SaveAsPrefab(clone, _spec.AttachedVisualPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(clone);
		}
	}

	private static void AssignItemPrefabReferences(
		ItemDefinition _item,
		GameObject _lootPrefab,
		GameObject _attachedVisual)
	{
		SerializedObject so = new SerializedObject(_item);
		so.FindProperty("m_DropWorldPrefab").objectReferenceValue = _lootPrefab;
		so.FindProperty("m_AttachedBodyVisualPrefab").objectReferenceValue = _attachedVisual;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
	}
	#endregion

	#region Helpers
	private static GameObject FindSceneObject(string[] _names)
	{
		if (_names == null)
			return null;

		for (int i = 0; i < _names.Length; i++)
		{
			GameObject found = FindSceneObject(_names[i]);
			if (found != null)
				return found;
		}

		return null;
	}

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
					string childName = children[c].name;
					if (childName != _name)
						continue;

					if (!childName.Contains("("))
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
