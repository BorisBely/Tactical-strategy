#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт Thrown-префабы для всех гранат и собирает GrenadeThrowData asset с маппингами.
/// Запускать после Polygone/Equipment/Build Grenade Content.
/// </summary>
public static class GrenadeThrowContentBuilder
{
	#region Constants
	private const string c_GrenadesFolder = "Assets/GameData/Inventory/Grenades";
	private const string c_ThrownFolder = "Assets/Prefabs/Combat/Grenades";
	private const string c_ThrownDataPath = "Assets/GameData/Combat/GrenadeThrowData.asset";
	private const string c_LootFolder = "Assets/Prefabs/World/Loot/Grenades";
	private const string c_AttachFolder = "Assets/Prefabs/Characters/BodyDecorations";
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Grenade Throw Content")]
	public static void BuildGrenadeThrowContent()
	{
		EnsureDirectory(c_ThrownFolder);
		EnsureDirectory("Assets/GameData/Combat");

		ItemDefinition[] grenades = LoadAllGrenadeItems();
		if (grenades.Length == 0)
		{
			Debug.LogError("[GrenadeThrowContentBuilder] Не найдены ItemDefinition гранат в " + c_GrenadesFolder);
			return;
		}

		GrenadeThrowData data = LoadOrCreateThrowData();

		for (int i = 0; i < grenades.Length; i++)
		{
			ItemDefinition item = grenades[i];
			if (item == null || !item.IsGrenade)
				continue;

			GameObject thrownPrefab = BuildThrownPrefab(item);
			GameObject handPrefab = GetHandPrefab(item);

			data.AddMapping(item, thrownPrefab, handPrefab);
		}

		SetTypeDefaultsFromFirstItem(data, grenades);

		EditorUtility.SetDirty(data);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[GrenadeThrowContentBuilder] Built throw content for {grenades.Length} grenade(s).");
	}
	#endregion

	#region Build Steps
	private static GameObject BuildThrownPrefab(ItemDefinition _item)
	{
		string safeName = _item.name.Replace("Item_", "Thrown_");
		string path = $"{c_ThrownFolder}/{safeName}.prefab";

		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
		if (existing != null)
		{
			UpdateThrownPrefabVisual(existing, _item);
			return existing;
		}

		GameObject lootPrefab = _item.DropWorldPrefab;
		if (lootPrefab == null)
		{
			Debug.LogWarning($"[GrenadeThrowContentBuilder] {_item.name}: нет DropWorldPrefab, пропускаю.");
			return null;
		}

		GameObject root = new GameObject(safeName);
		try
		{
			root.layer = LayerMask.NameToLayer("Default");

			GameObject visual = UnityEngine.Object.Instantiate(lootPrefab, root.transform);
			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			StripLootComponents(visual);

			CapsuleCollider cc = root.AddComponent<CapsuleCollider>();
			cc.radius = 0.03f;
			cc.height = 0.1f;
			cc.center = new Vector3(0f, 0.05f, 0f);
			cc.direction = 1;

			PhysicsMaterial pm = new PhysicsMaterial("GrenadePhysic")
			{
				bounciness = 0.3f,
				bounceCombine = PhysicsMaterialCombine.Average,
				dynamicFriction = 0.4f,
				staticFriction = 0.4f,
				frictionCombine = PhysicsMaterialCombine.Average
			};
			cc.material = pm;

			Rigidbody rb = root.AddComponent<Rigidbody>();
			rb.mass = 0.35f;
			rb.linearDamping = 0.1f;
			rb.angularDamping = 0.3f;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			rb.interpolation = RigidbodyInterpolation.Interpolate;

			GrenadeProjectile gp = root.AddComponent<GrenadeProjectile>();

			GameObject prefab = SaveAsPrefab(root, path);
			return prefab;
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static void UpdateThrownPrefabVisual(GameObject _prefab, ItemDefinition _item)
	{
		GameObject lootPrefab = _item.DropWorldPrefab;
		if (lootPrefab == null)
			return;

		string prefabPath = AssetDatabase.GetAssetPath(_prefab);
		if (string.IsNullOrEmpty(prefabPath))
			return;

		GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
		try
		{
			Transform existingVisual = root.transform.Find("Visual");
			if (existingVisual != null)
				UnityEngine.Object.DestroyImmediate(existingVisual.gameObject);

			GameObject visual = UnityEngine.Object.Instantiate(lootPrefab, root.transform);
			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			StripLootComponents(visual);

			PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static GameObject GetHandPrefab(ItemDefinition _item)
	{
		return _item.AttachedBodyVisualPrefab;
	}

	private static GrenadeThrowData LoadOrCreateThrowData()
	{
		GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrownDataPath);
		if (data != null)
			return data;

		data = ScriptableObject.CreateInstance<GrenadeThrowData>();
		data.name = "GrenadeThrowData";
		AssetDatabase.CreateAsset(data, c_ThrownDataPath);
		return data;
	}

	private static void SetTypeDefaultsFromFirstItem(GrenadeThrowData _data, ItemDefinition[] _grenades)
	{
		GameObject defaultFrag = null;
		GameObject defaultFlash = null;
		GameObject defaultSmoke = null;
		GameObject handFrag = null;
		GameObject handFlash = null;
		GameObject handSmoke = null;

		for (int i = 0; i < _grenades.Length; i++)
		{
			ItemDefinition item = _grenades[i];
			if (item == null || !item.IsGrenade)
				continue;

			string safeName = item.name.Replace("Item_", "Thrown_");
			string path = $"{c_ThrownFolder}/{safeName}.prefab";
			GameObject thrown = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			GameObject hand = item.AttachedBodyVisualPrefab;

			switch (item.GrenadeType)
			{
				case GrenadeType.Fragmentation when defaultFrag == null:
					defaultFrag = thrown;
					handFrag = hand;
					break;
				case GrenadeType.Flash when defaultFlash == null:
					defaultFlash = thrown;
					handFlash = hand;
					break;
				case GrenadeType.Smoke when defaultSmoke == null:
					defaultSmoke = thrown;
					handSmoke = hand;
					break;
			}
		}

		_data.SetTypeDefaults(defaultFrag, defaultFlash, defaultSmoke, handFrag, handFlash, handSmoke);
	}

	private static ItemDefinition[] LoadAllGrenadeItems()
	{
		string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { c_GrenadesFolder });
		ItemDefinition[] result = new ItemDefinition[guids.Length];
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			result[i] = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
		}

		return result;
	}
	#endregion

	#region Helpers
	private static void StripLootComponents(GameObject _root)
	{
		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			UnityEngine.Object.DestroyImmediate(pickups[i]);

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			UnityEngine.Object.DestroyImmediate(colliders[i]);

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
			UnityEngine.Object.DestroyImmediate(bodies[i]);
	}

	private static GameObject SaveAsPrefab(GameObject _source, string _path)
	{
		EnsureDirectory(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_source, _path);
		if (prefab == null)
			throw new InvalidOperationException($"Failed to save prefab: {_path}");

		return prefab;
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
