#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class AkPlatformContentBuilderBootstrap
{
	private const string c_MarkerPath = "Assets/.ak_platform_build_marker";

	static AkPlatformContentBuilderBootstrap()
	{
		EditorApplication.delayCall += TryRunFromMarker;
	}

	private static void TryRunFromMarker()
	{
		if (!File.Exists(c_MarkerPath))
			return;

		try
		{
			File.Delete(c_MarkerPath);
			if (File.Exists(c_MarkerPath + ".meta"))
				File.Delete(c_MarkerPath + ".meta");

			AkPlatformContentBuilder.BuildOpticsAndLootOnly();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[AkPlatformContentBuilder] Auto-run failed: {exception.Message}");
		}
	}
}


/// <summary>
/// Миграция АК-прицелов с M4, выпечка префабов со сцены, WeaponDefinition и лут.
/// </summary>
public static class AkPlatformContentBuilder
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_Ak47SceneName = "AK47";
	private const string c_ReddotSceneVisualName = "Attachment_Visual_AK_Reddot4+Rail";
	private const string c_ScopeSceneVisualName = "Attachment_Visual_AK_Scope11";

	private const string c_AkWeaponsRoot = "Assets/Prefabs/Weapons/AK";
	private const string c_AkEquippedPath = "Assets/Prefabs/Weapons/AK/Equipped/Equipped_AK47.prefab";
	private const string c_AkVisualsRoot = "Assets/Prefabs/Weapons/AK/Visuals/Attachments";
	private const string c_AkReddotVisualPath = "Assets/Prefabs/Weapons/AK/Visuals/Attachments/Attachment_Visual_AK_Reddot4+Rail.prefab";
	private const string c_AkScopeVisualPath = "Assets/Prefabs/Weapons/AK/Visuals/Attachments/Attachment_Visual_AK_Scope11.prefab";

	private const string c_AkLootRoot = "Assets/Prefabs/World/Loot/AK/Attachments";
	private const string c_AkGameDataInventory = "Assets/GameData/Inventory/AK";
	private const string c_AkGameDataShooting = "Assets/GameData/Shooting/AK";

	private const string c_OldReddotVisualPath = "Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_AK_Reddot4.prefab";
	private const string c_OldScopeVisualPath = "Assets/Prefabs/Weapons/M4/Visuals/Attachments/Attachment_Visual_AK_Scope11.prefab";
	private const string c_OldKobraLootPath = "Assets/Prefabs/World/Loot/M4/Attachments/Loot_Att_AK_Kobra.prefab";
	private const string c_OldPsoLootPath = "Assets/Prefabs/World/Loot/M4/Attachments/Loot_Att_AK_PSO.prefab";

	private const string c_AttachmentKobraPath = "Assets/GameData/Shooting/M4/Attachment_AK_Kobra.asset";
	private const string c_AttachmentPsoPath = "Assets/GameData/Shooting/M4/Attachment_AK_PSO.asset";
	private const string c_ItemKobraPath = "Assets/GameData/Inventory/M4/Item_Attachment_AK_Kobra.asset";
	private const string c_ItemPsoPath = "Assets/GameData/Inventory/M4/Item_Attachment_AK_PSO.asset";

	private const string c_MovedAttachmentReddotPath = "Assets/GameData/Shooting/AK/Attachment_AK_Reddot4_Rail.asset";
	private const string c_MovedAttachmentScopePath = "Assets/GameData/Shooting/AK/Attachment_AK_Scope11.asset";
	private const string c_MovedItemReddotPath = "Assets/GameData/Inventory/AK/Item_Attachment_AK_Reddot4_Rail.asset";
	private const string c_MovedItemScopePath = "Assets/GameData/Inventory/AK/Item_Attachment_AK_Scope11.asset";
	private const string c_WeaponAkPath = "Assets/GameData/Shooting/AK/Weapon_AK47.asset";
	private const string c_ItemWeaponAkPath = "Assets/GameData/Inventory/AK/Item_Weapon_AK47.asset";

	private const string c_RailMeshPrefabPath = "Assets/PolygonMilitary/Prefabs/Weapons/Modular/Weapon_B/SM_Wep_Mod_B_Rail_02.prefab";

	[MenuItem("Tools/AK Platform/Build Optic Prefabs + Loot (From Scene)")]
	public static void BuildOpticsFromMenu()
	{
		try
		{
			BuildOpticsAndLootOnly();
			EditorUtility.DisplayDialog("AK Platform", "Готово: визуальные префабы АК, лут и данные. Старые M4 AK-ассеты удалены.", "OK");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("AK Platform", exception.Message, "OK");
		}
	}

	[MenuItem("Tools/AK Platform/Build All From Sample Scene")]
	public static void BuildAllFromMenu()
	{
		try
		{
			BuildAll();
			EditorUtility.DisplayDialog("AK Platform", "Готово: префабы АК, данные и очистка M4.", "OK");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("AK Platform", exception.Message, "OK");
		}
	}

	/// <summary>Batch: -executeMethod AkPlatformContentBuilder.RunBatch</summary>
	public static void RunBatch()
	{
		try
		{
			BuildAll();
			Debug.Log("[AkPlatformContentBuilder] Batch complete.");
			EditorApplication.Exit(0);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorApplication.Exit(1);
		}
	}

	private static void BuildAll()
	{
		EnsureDirectories();
		MigrateGameDataAssets();

		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		GameObject ak47 = GameObject.Find(c_Ak47SceneName);
		if (ak47 == null)
			throw new InvalidOperationException($"GameObject '{c_Ak47SceneName}' not found in {c_ScenePath}.");

		(GameObject reddotPrefab, GameObject scopePrefab) = BakeSceneOpticPrefabsAndLoot();
		RemoveSceneAttachmentPreviews(
			FindInSceneIncludingInactive(c_ReddotSceneVisualName),
			FindInSceneIncludingInactive(c_ScopeSceneVisualName));
		ConfigureAk47Equipped(ak47);

		GameObject equippedPrefab = SaveAsPrefab(ak47, c_AkEquippedPath);
		CreateOrUpdateWeaponAndItemDefinitions(equippedPrefab);
		DeleteLegacyM4AkAssets();
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	public static void BuildOpticsAndLootOnly()
	{
		EnsureDirectories();
		MigrateGameDataAssets();
		EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		BakeSceneOpticPrefabsAndLoot();
		DeleteLegacyM4AkAssets();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	private static (GameObject Reddot, GameObject Scope) BakeSceneOpticPrefabsAndLoot()
	{
		UpdateAttachmentDefinitionsForSideRail();

		GameObject reddotSource = FindInSceneIncludingInactive(c_ReddotSceneVisualName);
		GameObject scopeSource = FindInSceneIncludingInactive(c_ScopeSceneVisualName);
		if (reddotSource == null || scopeSource == null)
			throw new InvalidOperationException(
				"На AK47 под SideRailModuleVisualSocket должны быть Attachment_Visual_AK_Reddot4+Rail и Attachment_Visual_AK_Scope11.");

		Transform sideRailSocket = reddotSource.transform.parent;
		if (sideRailSocket == null || sideRailSocket.name != "SideRailModuleVisualSocket")
			Debug.LogWarning(
				"[AkPlatformContentBuilder] Превью прицелов не под SideRailModuleVisualSocket — префабы сохранятся как есть.");

		GameObject reddotPrefabRoot = SaveAsPrefab(reddotSource, c_AkReddotVisualPath);
		GameObject scopePrefabRoot = SaveAsPrefab(scopeSource, c_AkScopeVisualPath);
		UpdateAttachmentDefinitionsForSideRail();
		BuildLootPrefabs(reddotPrefabRoot, scopePrefabRoot);
		return (reddotPrefabRoot, scopePrefabRoot);
	}

	private static void EnsureDirectories()
	{
		EnsureFolder(c_AkWeaponsRoot);
		EnsureFolder($"{c_AkWeaponsRoot}/Equipped");
		EnsureFolder($"{c_AkWeaponsRoot}/Visuals");
		EnsureFolder(c_AkVisualsRoot);
		EnsureFolder("Assets/Prefabs/World/Loot/AK");
		EnsureFolder(c_AkLootRoot);
		EnsureFolder(c_AkGameDataInventory);
		EnsureFolder(c_AkGameDataShooting);
	}

	private static void EnsureFolder(string _path)
	{
		if (AssetDatabase.IsValidFolder(_path))
			return;

		string parent = Path.GetDirectoryName(_path)?.Replace('\\', '/');
		string name = Path.GetFileName(_path);
		if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
			return;

		if (!AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, name);
	}

	private static void MigrateGameDataAssets()
	{
		MoveAssetIfExists(c_AttachmentKobraPath, c_MovedAttachmentReddotPath);
		MoveAssetIfExists(c_AttachmentPsoPath, c_MovedAttachmentScopePath);
		MoveAssetIfExists(c_ItemKobraPath, c_MovedItemReddotPath);
		MoveAssetIfExists(c_ItemPsoPath, c_MovedItemScopePath);
	}

	private static void UpdateAttachmentDefinitionsForSideRail()
	{
		SetAttachmentSideRail(c_MovedAttachmentReddotPath, "Attachment_AK_Reddot4_Rail", c_AkReddotVisualPath);
		SetAttachmentSideRail(c_MovedAttachmentScopePath, "Attachment_AK_Scope11", c_AkScopeVisualPath);

		SetItemAttachment(c_MovedItemReddotPath, "Item_Attachment_AK_Reddot4_Rail", c_MovedAttachmentReddotPath, c_AkReddotVisualPath,
			"item.attachment.ak_reddot4_rail", "Side-rail red dot (Kobra-style) for AK platform.");
		SetItemAttachment(c_MovedItemScopePath, "Item_Attachment_AK_Scope11", c_MovedAttachmentScopePath, c_AkScopeVisualPath,
			"item.attachment.ak_scope11", "4x side-rail optical sight for AK platform.");
	}

	private static void SetAttachmentSideRail(string _assetPath, string _assetName, string _visualPrefabPath)
	{
		var attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_assetPath);
		if (attachment == null)
			throw new InvalidOperationException($"Missing attachment asset: {_assetPath}");

		var so = new SerializedObject(attachment);
		attachment.name = _assetName;
		so.FindProperty("m_RequiredSlot").enumValueIndex = (int)WeaponAttachmentSlotType.SideRail;
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		WeaponDefinition akWeapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_WeaponAkPath);
		SerializedProperty weapons = so.FindProperty("m_CompatibleWeapons");
		weapons.arraySize = akWeapon != null ? 1 : 0;
		if (akWeapon != null)
			weapons.GetArrayElementAtIndex(0).objectReferenceValue = akWeapon;
		so.FindProperty("m_CompatibleSlots").arraySize = 1;
		so.FindProperty("m_CompatibleSlots").GetArrayElementAtIndex(0).enumValueIndex = (int)WeaponAttachmentSlotType.SideRail;
		var visual = AssetDatabase.LoadAssetAtPath<GameObject>(_visualPrefabPath);
		if (visual != null)
			so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = visual;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);
	}

	private static void SetItemAttachment(
		string _itemPath,
		string _itemName,
		string _attachmentPath,
		string _visualPrefabPath,
		string _localizationKey,
		string _description)
	{
		var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_itemPath);
		if (item == null)
			throw new InvalidOperationException($"Missing item asset: {_itemPath}");

		var attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_attachmentPath);
		var visual = AssetDatabase.LoadAssetAtPath<GameObject>(_visualPrefabPath);
		var so = new SerializedObject(item);
		item.name = _itemName;
		so.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		so.FindProperty("m_Description").stringValue = _description;
		so.FindProperty("m_WeaponAttachmentDefinition").objectReferenceValue = attachment;
		if (visual != null)
			so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = visual;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
	}

	private static void ConfigureAk47Equipped(GameObject _ak47)
	{
		Transform sideRailSocket = _ak47.transform.Find("SideRailModuleVisualSocket");
		if (sideRailSocket == null)
			throw new InvalidOperationException("AK47 must have SideRailModuleVisualSocket (scene-tuned, zero local on children).");

		var sideRailMounts = new System.Collections.Generic.List<GameObject>();
		Transform visualRoot = _ak47.transform.Find("Visual") ?? _ak47.transform;
		for (int i = 0; i < visualRoot.childCount; i++)
		{
			Transform child = visualRoot.GetChild(i);
			if (child.name.IndexOf("Rail", StringComparison.OrdinalIgnoreCase) >= 0 &&
			    child.GetComponentInChildren<MeshRenderer>() != null)
				sideRailMounts.Add(child.gameObject);
		}

		EquippedWeapon weapon = _ak47.GetComponent<EquippedWeapon>();
		if (weapon == null)
			weapon = Undo.AddComponent<EquippedWeapon>(_ak47);

		var so = new SerializedObject(weapon);
		so.FindProperty("m_SideRailModuleVisualSocket").objectReferenceValue = sideRailSocket;
		so.FindProperty("m_SideRailMountVisuals").arraySize = sideRailMounts.Count;
		for (int i = 0; i < sideRailMounts.Count; i++)
			so.FindProperty("m_SideRailMountVisuals").GetArrayElementAtIndex(i).objectReferenceValue = sideRailMounts[i];
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
	}

	private static void CreateOrUpdateWeaponAndItemDefinitions(GameObject _equippedPrefab)
	{
		WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_WeaponAkPath);
		if (weapon == null)
		{
			weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
			weapon.name = "Weapon_AK47";
			AssetDatabase.CreateAsset(weapon, c_WeaponAkPath);
		}

		var weaponSo = new SerializedObject(weapon);
		weaponSo.FindProperty("m_WeaponClass").enumValueIndex = (int)WeaponClassType.Rifle;
		weaponSo.FindProperty("m_SupportedCaliber").enumValueIndex = (int)CaliberType.Five45By39;
		weaponSo.FindProperty("m_SupportedMagazineType").enumValueIndex = (int)MagazineType.RifleStandard;
		var slots = weaponSo.FindProperty("m_AttachmentSlots");
		slots.arraySize = 3;
		slots.GetArrayElementAtIndex(0).FindPropertyRelative("SlotType").enumValueIndex = (int)WeaponAttachmentSlotType.Optic;
		slots.GetArrayElementAtIndex(1).FindPropertyRelative("SlotType").enumValueIndex = (int)WeaponAttachmentSlotType.SideRail;
		slots.GetArrayElementAtIndex(2).FindPropertyRelative("SlotType").enumValueIndex = (int)WeaponAttachmentSlotType.Stock;
		weaponSo.FindProperty("m_FireRateRpm").intValue = 600;
		weaponSo.FindProperty("m_AimTimeSeconds").floatValue = 0.32f;
		weaponSo.FindProperty("m_ReloadTimeSeconds").floatValue = 2.4f;
		weaponSo.FindProperty("m_EffectiveRangeMeters").floatValue = 140f;
		weaponSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);

		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(c_ItemWeaponAkPath);
		if (item == null)
		{
			item = ScriptableObject.CreateInstance<ItemDefinition>();
			item.name = "Item_Weapon_AK47";
			AssetDatabase.CreateAsset(item, c_ItemWeaponAkPath);
		}

		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_LocalizationKey").stringValue = "item.weapon.ak47";
		itemSo.FindProperty("m_Description").stringValue = "AK platform rifle with Picatinny optic rail and side rail.";
		itemSo.FindProperty("m_Category").enumValueIndex = 1;
		itemSo.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _equippedPrefab;
		itemSo.FindProperty("m_WeaponDefinition").objectReferenceValue = weapon;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
	}

	private static void BuildLootPrefabs(GameObject _reddotVisual, GameObject _scopeVisual)
	{
		BuildLootForItem(c_MovedItemReddotPath, $"{c_AkLootRoot}/Loot_Att_AK_Reddot4_Rail.prefab", _reddotVisual);
		BuildLootForItem(c_MovedItemScopePath, $"{c_AkLootRoot}/Loot_Att_AK_Scope11.prefab", _scopeVisual);
	}

	private static void BuildLootForItem(string _itemPath, string _lootPath, GameObject _visualPrefab)
	{
		var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_itemPath);
		if (item == null)
			return;

		GameObject root = new GameObject(Path.GetFileNameWithoutExtension(_lootPath));
		try
		{
			root.layer = LayerMask.NameToLayer("Loot");
			if (_visualPrefab != null)
			{
				GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(_visualPrefab, root.transform);
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = Quaternion.identity;
			}

			var box = root.AddComponent<BoxCollider>();
			box.center = Vector3.zero;
			box.size = new Vector3(0.2f, 0.12f, 0.25f);
			var rb = root.AddComponent<Rigidbody>();
			rb.isKinematic = false;
			var pickup = root.AddComponent<WorldPickupItem>();
			var so = new SerializedObject(pickup);
			so.FindProperty("m_Definition").objectReferenceValue = item;
			so.ApplyModifiedPropertiesWithoutUndo();

			EnsureFolder(Path.GetDirectoryName(_lootPath)?.Replace('\\', '/'));
			SaveAsPrefab(root, _lootPath);

			var itemSo = new SerializedObject(item);
			itemSo.FindProperty("m_DropWorldPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(_lootPath);
			itemSo.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(item);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static void DeleteLegacyM4AkAssets()
	{
		DeleteAssetIfExists(c_OldReddotVisualPath);
		DeleteAssetIfExists(c_OldScopeVisualPath);
		DeleteAssetIfExists(c_OldKobraLootPath);
		DeleteAssetIfExists(c_OldPsoLootPath);
	}

	private static void RemoveSceneAttachmentPreviews(GameObject _reddot, GameObject _scope)
	{
		UnityEngine.Object.DestroyImmediate(_reddot);
		UnityEngine.Object.DestroyImmediate(_scope);
	}

	private static GameObject SaveAsPrefab(GameObject _source, string _path)
	{
		EnsureFolder(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_source, _path);
		if (prefab == null)
			throw new InvalidOperationException($"Failed to save prefab: {_path}");
		return prefab;
	}

	private static GameObject FindInSceneIncludingInactive(string _name)
	{
		foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
		{
			Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				if (transforms[i].name == _name)
					return transforms[i].gameObject;
			}
		}

		return null;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t.name == _name)
				return t;
		}

		return null;
	}

	private static void MoveAssetIfExists(string _from, string _to)
	{
		if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_from))
			return;

		EnsureFolder(Path.GetDirectoryName(_to)?.Replace('\\', '/'));
		string error = AssetDatabase.MoveAsset(_from, _to);
		if (!string.IsNullOrEmpty(error))
			throw new InvalidOperationException($"MoveAsset failed: {_from} -> {_to}: {error}");
	}

	private static void DeleteAssetIfExists(string _path)
	{
		if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_path) == null)
			return;

		AssetDatabase.DeleteAsset(_path);
	}
}
#endif
