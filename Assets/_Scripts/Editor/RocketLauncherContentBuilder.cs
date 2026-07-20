#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает лут/экип/снаряды гранатомётов из SampleScene.
/// Меши и материалы копируются в проект — без ссылок на PolygonMilitary/Synty.
/// Ракета одноразового гранатомёта — только projectile visual, не лут и не item.
/// </summary>
public static class RocketLauncherContentBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";

	private const string c_EquippedFolder = "Assets/Prefabs/Weapons/RocketLaunchers/Equipped";
	private const string c_ProjectileFolder = "Assets/Prefabs/Weapons/RocketLaunchers/Projectiles";
	private const string c_HandFolder = "Assets/Prefabs/Weapons/RocketLaunchers/Hand";
	private const string c_LootFolder = "Assets/Prefabs/World/Loot/RocketLaunchers";
	private const string c_MeshFolder = "Assets/Models/Weapons/RocketLaunchers";
	private const string c_MaterialFolder = "Assets/Materials/Weapons/RocketLaunchers";
	private const string c_ItemsFolder = "Assets/GameData/Inventory/RocketLaunchers";
	private const string c_DataPath = "Assets/GameData/Combat/RocketLauncherData.asset";

	private const string c_SceneRpg = "SM_Wep_RPG_01";
	private const string c_SceneRpgRocket = "SM_Wep_RPG_Rocket_Seperate_01";
	private const string c_SceneDisposable = "SM_Wep_RocketLauncher_01";
	private const string c_SceneMissile = "SM_Wep_RocketLauncher_MIssile_01";

	private const string c_OwnedRpgFbx = "Assets/Models/Weapons/RocketLaunchers/Rpg7.fbx";
	private const string c_OwnedRpgRocketFbx = "Assets/Models/Weapons/RocketLaunchers/RpgRocket.fbx";
	private const string c_OwnedDisposableFbx = "Assets/Models/Weapons/RocketLaunchers/DisposableLauncher.fbx";
	private const string c_OwnedMissileFbx = "Assets/Models/Weapons/RocketLaunchers/DisposableMissile.fbx";

	private const string c_EquippedRpg = c_EquippedFolder + "/Equipped_Rpg7.prefab";
	private const string c_EquippedDisposable = c_EquippedFolder + "/Equipped_DisposableLauncher.prefab";
	private const string c_ProjectileRpg = c_ProjectileFolder + "/Projectile_RpgRocket.prefab";
	private const string c_ProjectileDisposable = c_ProjectileFolder + "/Projectile_DisposableMissile.prefab";
	private const string c_HandRpgRocket = c_HandFolder + "/Hand_RpgRocket.prefab";

	private const string c_LootRpg = c_LootFolder + "/Loot_Item_Weapon_Rpg7.prefab";
	private const string c_LootDisposable = c_LootFolder + "/Loot_Item_Weapon_DisposableRocketLauncher.prefab";
	private const string c_LootRpgRocket = c_LootFolder + "/Loot_Item_Ammo_RpgRocket.prefab";

	private const string c_ItemRpg = c_ItemsFolder + "/Item_Weapon_Rpg7.asset";
	private const string c_ItemRocket = c_ItemsFolder + "/Item_Ammo_RpgRocket.asset";
	private const string c_ItemDisposable = c_ItemsFolder + "/Item_Weapon_DisposableRocketLauncher.asset";
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Rocket Launcher Content")]
	public static void BuildRocketLauncherContent()
	{
		try
		{
			EnsureSceneLoaded();
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[RocketLauncherContent] SampleScene not opened ({ex.Message}). Using owned FBX fallbacks.");
		}

		EnsureFolder(c_EquippedFolder);
		EnsureFolder(c_ProjectileFolder);
		EnsureFolder(c_HandFolder);
		EnsureFolder(c_LootFolder);
		EnsureFolder(c_MeshFolder);
		EnsureFolder(c_MaterialFolder);
		EnsureFolder(c_ItemsFolder);
		EnsureFolder("Assets/GameData/Combat");

		GameObject sceneRpg = ResolveSource(c_SceneRpg, c_OwnedRpgFbx);
		GameObject sceneRpgRocket = ResolveSource(c_SceneRpgRocket, c_OwnedRpgRocketFbx);
		GameObject sceneDisposable = ResolveSource(c_SceneDisposable, c_OwnedDisposableFbx);
		GameObject sceneMissile = ResolveSource(c_SceneMissile, c_OwnedMissileFbx);

		GameObject equippedRpg = BuildOwnedVisualPrefab(sceneRpg, c_EquippedRpg, "Equipped_Rpg7");
		GameObject equippedDisposable = BuildOwnedVisualPrefab(sceneDisposable, c_EquippedDisposable, "Equipped_DisposableLauncher");
		GameObject handRocket = BuildOwnedVisualPrefab(sceneRpgRocket, c_HandRpgRocket, "Hand_RpgRocket");
		GameObject projectileRpg = BuildOwnedProjectilePrefab(sceneRpgRocket, c_ProjectileRpg, "Projectile_RpgRocket");
		// Disposable missile: fire visual only — never loot / never ItemDefinition.
		GameObject projectileDisposable = BuildOwnedProjectilePrefab(sceneMissile, c_ProjectileDisposable, "Projectile_DisposableMissile");

		ItemDefinition rocketItem = CreateOrLoadItem(c_ItemRocket, "Item_Ammo_RpgRocket");
		ItemDefinition rpgItem = CreateOrLoadItem(c_ItemRpg, "Item_Weapon_Rpg7");
		ItemDefinition disposableItem = CreateOrLoadItem(c_ItemDisposable, "Item_Weapon_DisposableRocketLauncher");

		GameObject lootRpg = BuildLootPrefab(equippedRpg, rpgItem, c_LootRpg, "Loot_Item_Weapon_Rpg7",
			new Vector3(0f, 0.08f, 0f), new Vector3(0.22f, 0.22f, 0.95f), 6.3f);
		GameObject lootDisposable = BuildLootPrefab(equippedDisposable, disposableItem, c_LootDisposable,
			"Loot_Item_Weapon_DisposableRocketLauncher",
			new Vector3(0f, 0.07f, 0f), new Vector3(0.2f, 0.2f, 0.85f), 4.5f);
		GameObject lootRocket = BuildLootPrefab(handRocket, rocketItem, c_LootRpgRocket, "Loot_Item_Ammo_RpgRocket",
			new Vector3(0f, 0.04f, 0f), new Vector3(0.12f, 0.12f, 0.7f), 2.5f);

		ConfigureRocketAmmoItem(rocketItem, lootRocket, handRocket);
		ConfigureLauncherItem(
			rpgItem,
			RocketLauncherType.Rpg7,
			startsLoaded: false,
			handPrefab: equippedRpg,
			projectilePrefab: projectileRpg,
			rocketItem: rocketItem,
			rocketHandPrefab: handRocket,
			lootPrefab: lootRpg,
			locKey: "item.weapon.rpg7",
			description: "RPG-7 reusable rocket launcher.");
		ConfigureLauncherItem(
			disposableItem,
			RocketLauncherType.Disposable,
			startsLoaded: true,
			handPrefab: equippedDisposable,
			projectilePrefab: projectileDisposable,
			rocketItem: null,
			rocketHandPrefab: null,
			lootPrefab: lootDisposable,
			locKey: "item.weapon.disposable_rocket_launcher",
			description: "Single-use rocket launcher. Discarded after firing.");

		RocketLauncherData data = LoadOrCreateData();
		if (data == null)
		{
			Debug.LogError($"[RocketLauncherContent] Failed to load/create data at {c_DataPath}");
			return;
		}

		SerializedObject dataSo = new SerializedObject(data);
		dataSo.Update();
		SetObject(dataSo, "m_FallbackRpgHandPrefab", equippedRpg);
		SetObject(dataSo, "m_FallbackRpgProjectilePrefab", projectileRpg);
		SetObject(dataSo, "m_FallbackRpgRocketHandPrefab", handRocket);
		SetObject(dataSo, "m_FallbackDisposableHandPrefab", equippedDisposable);
		SetObject(dataSo, "m_FallbackDisposableProjectilePrefab", projectileDisposable);
		SetFloat(dataSo, "m_RpgMuzzleSpeed", 115f);
		SetFloat(dataSo, "m_DisposableMuzzleSpeed", 130f);
		SetFloat(dataSo, "m_ProjectileGravity", 9.81f);
		SetFloat(dataSo, "m_ProjectileLinearDamping", 0.02f);
		SetFloat(dataSo, "m_ProjectileLifetimeSeconds", 12f);
		SetFloat(dataSo, "m_DiscardedLauncherLifetimeSeconds", 30f);
		SetVector3(dataSo, "m_DiscardImpulseLocal", new Vector3(2.6f, 2.0f, 3.2f));
		SetFloat(dataSo, "m_DiscardTorque", 3.5f);

		GameObject explosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
			"Assets/Prefabs/FX/Grenades/FX_Grenade_Explosion_01.prefab");
		if (explosionPrefab != null)
			SetObject(dataSo, "m_ExplosionPrefab", explosionPrefab);

		SetFloat(dataSo, "m_ExplosionVfxScale", 2.15f);
		SetFloat(dataSo, "m_ExplosionVfxDurationSeconds", 5.5f);
		SetFloat(dataSo, "m_ExplosionMaxDistanceMeters", 95f);

		GameObject muzzleFlash = AssetDatabase.LoadAssetAtPath<GameObject>(
			"Assets/Prefabs/FX/Shooting/Muzzle/FX_MuzzleFlash_Smoke.prefab");
		if (muzzleFlash != null)
			SetObject(dataSo, "m_FireMuzzleFlashPrefab", muzzleFlash);
		SetBool(dataSo, "m_EnableFireMuzzleVfx", true);
		SetVector3(dataSo, "m_FireMuzzleVfxScale", new Vector3(2.4f, 2.4f, 4.2f));
		SetVector3(dataSo, "m_FireBackblastVfxScale", new Vector3(3.5f, 3.5f, 3.2f));
		SetFloat(dataSo, "m_FireMuzzleVfxLifetimeSeconds", 0.28f);
		SetFloat(dataSo, "m_FireMuzzleVfxMaxDistanceMeters", 70f);
		dataSo.ApplyModifiedPropertiesWithoutUndo();

		data.ApplyDefaultAimBalance();
		EditorUtility.SetDirty(data);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[RocketLauncherContent] Built owned loot/equip/projectile prefabs from SampleScene (no Synty asset paths).");
	}

	public static RocketLauncherData LoadOrCreateData()
	{
		RocketLauncherData data = AssetDatabase.LoadAssetAtPath<RocketLauncherData>(c_DataPath);
		if (data != null)
			return data;

		EnsureFolder("Assets/GameData/Combat");

		// Prefer not deleting an existing .asset (GUID is referenced by Unit.prefab).
		if (System.IO.File.Exists(c_DataPath))
		{
			AssetDatabase.ImportAsset(c_DataPath, ImportAssetOptions.ForceUpdate);
			data = AssetDatabase.LoadAssetAtPath<RocketLauncherData>(c_DataPath);
			if (data != null)
				return data;

			Debug.LogError(
				$"[RocketLauncherContent] '{c_DataPath}' exists but failed to load as RocketLauncherData. Check script GUID / compile errors.");
			return null;
		}

		data = ScriptableObject.CreateInstance<RocketLauncherData>();
		data.name = "RocketLauncherData";
		AssetDatabase.CreateAsset(data, c_DataPath);
		AssetDatabase.SaveAssets();
		return data;
	}

	public static string DataAssetPath => c_DataPath;
	#endregion

	#region Prefab builders
	private static GameObject BuildOwnedVisualPrefab(GameObject _sceneSource, string _destPath, string _rootName)
	{
		if (_sceneSource == null)
			return LoadExistingPrefabOrThrow(_destPath, _rootName);

		GameObject clone = UnityEngine.Object.Instantiate(_sceneSource);
		try
		{
			clone.name = _rootName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			StripPhysicsAndPickup(clone);
			OwnMeshesAndMaterials(clone, _rootName);
			return SaveAsPrefab(clone, _destPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(clone);
		}
	}

	private static GameObject BuildOwnedProjectilePrefab(GameObject _sceneSource, string _destPath, string _rootName)
	{
		if (_sceneSource == null)
			return LoadExistingPrefabOrThrow(_destPath, _rootName);

		GameObject clone = UnityEngine.Object.Instantiate(_sceneSource);
		try
		{
			clone.name = _rootName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			StripPhysicsAndPickup(clone);
			OwnMeshesAndMaterials(clone, _rootName);

			Rigidbody rb = clone.GetComponent<Rigidbody>();
			if (rb == null)
				rb = clone.AddComponent<Rigidbody>();
			rb.useGravity = false;
			rb.isKinematic = false;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			rb.interpolation = RigidbodyInterpolation.Interpolate;

			if (clone.GetComponent<Collider>() == null)
			{
				CapsuleCollider capsule = clone.AddComponent<CapsuleCollider>();
				capsule.direction = 2;
				capsule.radius = 0.06f;
				capsule.height = 0.45f;
				capsule.center = new Vector3(0f, 0f, 0.1f);
			}

			if (clone.GetComponent<RocketProjectile>() == null)
				clone.AddComponent<RocketProjectile>();

			return SaveAsPrefab(clone, _destPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(clone);
		}
	}

	private static GameObject LoadExistingPrefabOrThrow(string _destPath, string _rootName)
	{
		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(_destPath);
		if (existing != null)
		{
			Debug.LogWarning($"[RocketLauncherContent] Source missing for '{_rootName}', reusing existing prefab: {_destPath}");
			return existing;
		}

		throw new InvalidOperationException(
			$"[RocketLauncherContent] Cannot build '{_rootName}': source missing and prefab not found at {_destPath}");
	}

	private static GameObject BuildLootPrefab(
		GameObject _visualPrefab,
		ItemDefinition _item,
		string _destPath,
		string _rootName,
		Vector3 _colliderCenter,
		Vector3 _colliderSize,
		float _mass)
	{
		if (_visualPrefab == null)
			return LoadExistingPrefabOrThrow(_destPath, _rootName);

		GameObject root = new GameObject(_rootName);
		try
		{
			int lootLayer = LayerMask.NameToLayer("Loot");
			root.layer = lootLayer >= 0 ? lootLayer : 0;

			GameObject visual = PrefabUtility.InstantiatePrefab(_visualPrefab, root.transform) as GameObject;
			if (visual == null)
				visual = UnityEngine.Object.Instantiate(_visualPrefab, root.transform);

			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			StripPhysicsAndPickup(visual);
			SetLayerRecursively(visual, root.layer);

			BoxCollider collider = root.AddComponent<BoxCollider>();
			collider.center = _colliderCenter;
			collider.size = _colliderSize;

			Rigidbody body = root.AddComponent<Rigidbody>();
			body.mass = Mathf.Max(0.1f, _mass);
			body.linearDamping = 0.2f;
			body.angularDamping = 0.5f;
			body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

			WorldPickupItem pickup = root.AddComponent<WorldPickupItem>();
			SerializedObject pickupSo = new SerializedObject(pickup);
			pickupSo.FindProperty("m_Definition").objectReferenceValue = _item;
			pickupSo.ApplyModifiedPropertiesWithoutUndo();

			return SaveAsPrefab(root, _destPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}
	#endregion

	#region Mesh / material ownership
	private static void OwnMeshesAndMaterials(GameObject _root, string _assetPrefix)
	{
		Dictionary<Mesh, Mesh> meshMap = new Dictionary<Mesh, Mesh>();
		Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();

		MeshFilter[] filters = _root.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < filters.Length; i++)
		{
			MeshFilter filter = filters[i];
			if (filter == null || filter.sharedMesh == null)
				continue;

			filter.sharedMesh = GetOrCreateOwnedMesh(filter.sharedMesh, _assetPrefix, filter.name, meshMap);
		}

		MeshRenderer[] renderers = _root.GetComponentsInChildren<MeshRenderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			MeshRenderer renderer = renderers[i];
			if (renderer == null)
				continue;

			Material[] shared = renderer.sharedMaterials;
			if (shared == null || shared.Length == 0)
				continue;

			Material[] owned = new Material[shared.Length];
			for (int m = 0; m < shared.Length; m++)
			{
				if (shared[m] == null)
					continue;

				owned[m] = GetOrCreateOwnedMaterial(shared[m], materialMap);
			}

			renderer.sharedMaterials = owned;
		}
	}

	private static Mesh GetOrCreateOwnedMesh(Mesh _source, string _prefix, string _objectName, Dictionary<Mesh, Mesh> _cache)
	{
		if (_cache.TryGetValue(_source, out Mesh cached) && cached != null)
			return cached;

		string safeName = SanitizeFileName($"{_prefix}_{_objectName}_{_source.name}");
		string path = $"{c_MeshFolder}/{safeName}.asset";

		Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
		if (existing != null)
		{
			_cache[_source] = existing;
			return existing;
		}

		Mesh copy = UnityEngine.Object.Instantiate(_source);
		copy.name = safeName;
		AssetDatabase.CreateAsset(copy, path);
		_cache[_source] = copy;
		return copy;
	}

	private static Material GetOrCreateOwnedMaterial(Material _source, Dictionary<Material, Material> _cache)
	{
		if (_cache.TryGetValue(_source, out Material cached) && cached != null)
			return cached;

		string safeName = SanitizeFileName(_source.name);
		if (safeName.StartsWith("PolygonMilitary", StringComparison.OrdinalIgnoreCase))
			safeName = "RocketLauncher_" + safeName.Substring("PolygonMilitary".Length).TrimStart('_');

		string path = $"{c_MaterialFolder}/{safeName}.mat";

		Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (existing != null)
		{
			_cache[_source] = existing;
			return existing;
		}

		Material copy = new Material(_source)
		{
			name = safeName
		};
		AssetDatabase.CreateAsset(copy, path);
		_cache[_source] = copy;
		return copy;
	}

	private static string SanitizeFileName(string _name)
	{
		if (string.IsNullOrEmpty(_name))
			return "Mesh";

		char[] invalid = Path.GetInvalidFileNameChars();
		string result = _name;
		for (int i = 0; i < invalid.Length; i++)
			result = result.Replace(invalid[i], '_');

		return result.Replace(' ', '_');
	}
	#endregion

	#region Item config
	private static ItemDefinition CreateOrLoadItem(string _path, string _name)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_path);
		if (item != null)
			return item;

		item = ScriptableObject.CreateInstance<ItemDefinition>();
		item.name = _name;
		AssetDatabase.CreateAsset(item, _path);
		return item;
	}

	private static void ConfigureRocketAmmoItem(ItemDefinition _item, GameObject _lootPrefab, GameObject _handPrefab)
	{
		SerializedObject so = new SerializedObject(_item);
		so.FindProperty("m_LocalizationKey").stringValue = "item.ammo.rpg_rocket";
		so.FindProperty("m_Description").stringValue = "RPG-7 rocket. Load into RPG-7 via reload order.";
		so.FindProperty("m_Category").enumValueIndex = (int)ItemCategory.General;
		so.FindProperty("m_BasePrice").intValue = 400;
		so.FindProperty("m_WeightKg").floatValue = 2.5f;
		so.FindProperty("m_IsRpgRocketAmmo").boolValue = true;
		so.FindProperty("m_RocketLauncherType").enumValueIndex = (int)RocketLauncherType.None;
		so.FindProperty("m_GrenadeType").enumValueIndex = (int)GrenadeType.Unknown;
		so.FindProperty("m_DropWorldPrefab").objectReferenceValue = _lootPrefab;
		so.FindProperty("m_RpgRocketHandPrefab").objectReferenceValue = _handPrefab;
		so.FindProperty("m_RocketLauncherHandPrefab").objectReferenceValue = null;
		so.FindProperty("m_RocketProjectilePrefab").objectReferenceValue = null;
		so.FindProperty("m_RpgRocketItemDefinition").objectReferenceValue = null;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
	}

	private static void ConfigureLauncherItem(
		ItemDefinition _item,
		RocketLauncherType _type,
		bool startsLoaded,
		GameObject handPrefab,
		GameObject projectilePrefab,
		ItemDefinition rocketItem,
		GameObject rocketHandPrefab,
		GameObject lootPrefab,
		string locKey,
		string description)
	{
		SerializedObject so = new SerializedObject(_item);
		so.FindProperty("m_LocalizationKey").stringValue = locKey;
		so.FindProperty("m_Description").stringValue = description;
		so.FindProperty("m_Category").enumValueIndex = (int)ItemCategory.General;
		so.FindProperty("m_BasePrice").intValue = _type == RocketLauncherType.Rpg7 ? 2500 : 900;
		so.FindProperty("m_WeightKg").floatValue = _type == RocketLauncherType.Rpg7 ? 6.3f : 4.5f;
		so.FindProperty("m_RocketLauncherType").enumValueIndex = (int)_type;
		so.FindProperty("m_RocketLauncherStartsLoaded").boolValue = startsLoaded;
		so.FindProperty("m_RocketLauncherHandPrefab").objectReferenceValue = handPrefab;
		so.FindProperty("m_RocketProjectilePrefab").objectReferenceValue = projectilePrefab;
		so.FindProperty("m_RpgRocketItemDefinition").objectReferenceValue = rocketItem;
		so.FindProperty("m_RpgRocketHandPrefab").objectReferenceValue = rocketHandPrefab;
		so.FindProperty("m_DropWorldPrefab").objectReferenceValue = lootPrefab;
		so.FindProperty("m_IsRpgRocketAmmo").boolValue = false;
		so.FindProperty("m_GrenadeType").enumValueIndex = (int)GrenadeType.Unknown;
		so.FindProperty("m_RightHandLocalPosition").vector3Value = new Vector3(0.05f, 0.02f, 0.08f);
		so.FindProperty("m_RightHandLocalEulerAngles").vector3Value = new Vector3(-10f, 90f, 90f);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
	}
	#endregion

	#region Scene / IO helpers
	private static GameObject ResolveSource(string _sceneName, string _ownedFbxPath)
	{
		GameObject sceneObject = FindSceneObject(_sceneName);
		if (sceneObject != null)
			return sceneObject;

		GameObject owned = AssetDatabase.LoadAssetAtPath<GameObject>(_ownedFbxPath);
		if (owned != null)
		{
			Debug.LogWarning($"[RocketLauncherContent] Using owned FBX fallback for '{_sceneName}': {_ownedFbxPath}");
			return owned;
		}

		Debug.LogWarning(
			$"[RocketLauncherContent] Missing '{_sceneName}' in scene and owned FBX '{_ownedFbxPath}'. Will try existing prefabs.");
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

	private static GameObject SaveAsPrefab(GameObject _source, string _path)
	{
		EnsureFolder(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
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

		RocketProjectile[] rockets = _root.GetComponentsInChildren<RocketProjectile>(true);
		for (int i = 0; i < rockets.Length; i++)
			UnityEngine.Object.DestroyImmediate(rockets[i]);
	}

	private static void SetLayerRecursively(GameObject _root, int _layer)
	{
		if (_root == null)
			return;

		Transform[] transforms = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
			transforms[i].gameObject.layer = _layer;
	}

	private static void EnsureFolder(string _path)
	{
		if (string.IsNullOrEmpty(_path) || AssetDatabase.IsValidFolder(_path))
			return;

		string parent = Path.GetDirectoryName(_path)?.Replace('\\', '/');
		string name = Path.GetFileName(_path);
		if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, name);
	}

	private static void SetFloat(SerializedObject _so, string _propertyName, float _value)
	{
		if (_so == null)
			return;

		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property == null)
		{
			Debug.LogWarning($"[RocketLauncherContent] Missing float property '{_propertyName}' on {_so.targetObject}");
			return;
		}

		property.floatValue = _value;
	}

	private static void SetObject(SerializedObject _so, string _propertyName, UnityEngine.Object _value)
	{
		if (_so == null)
			return;

		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property == null)
		{
			Debug.LogWarning($"[RocketLauncherContent] Missing object property '{_propertyName}' on {_so.targetObject}");
			return;
		}

		property.objectReferenceValue = _value;
	}

	private static void SetBool(SerializedObject _so, string _propertyName, bool _value)
	{
		if (_so == null)
			return;

		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property == null)
		{
			Debug.LogWarning($"[RocketLauncherContent] Missing bool property '{_propertyName}' on {_so.targetObject}");
			return;
		}

		property.boolValue = _value;
	}

	private static void SetVector3(SerializedObject _so, string _propertyName, Vector3 _value)
	{
		if (_so == null)
			return;

		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property == null)
		{
			Debug.LogWarning($"[RocketLauncherContent] Missing Vector3 property '{_propertyName}' on {_so.targetObject}");
			return;
		}

		property.vector3Value = _value;
	}
	#endregion
}
#endif
