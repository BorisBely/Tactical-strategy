#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class Ak47PreparedContentBuilderBootstrap
{
	private const string c_MarkerPath = "Assets/.ak47_prepared_content_build_marker";

	static Ak47PreparedContentBuilderBootstrap()
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

			Ak47PreparedContentBuilder.BuildPreparedContent();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[Ak47PreparedContentBuilder] Auto-run failed: {exception}");
		}
	}
}

public static class Ak47PreparedContentBuilder
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_Ak47SceneName = "AK47";

	private const string c_AkWeaponRoot = "Assets/Prefabs/Weapons/AK";
	private const string c_AkVisualAttachmentsRoot = "Assets/Prefabs/Weapons/AK/Visuals/Attachments";
	private const string c_AkVisualMagazinesRoot = "Assets/Prefabs/Weapons/AK/Visuals/Magazines";
	private const string c_AkEquippedRoot = "Assets/Prefabs/Weapons/AK/Equipped";
	private const string c_AkLootAttachmentsRoot = "Assets/Prefabs/World/Loot/AK/Attachments";
	private const string c_AkLootMagazinesRoot = "Assets/Prefabs/World/Loot/AK/Magazines";
	private const string c_AkLootWeaponsRoot = "Assets/Prefabs/World/Loot/AK/Weapons";
	private const string c_AkInventoryRoot = "Assets/GameData/Inventory/AK";
	private const string c_AkShootingRoot = "Assets/GameData/Shooting/AK";

	private const string c_M4VisualAttachmentsRoot = "Assets/Prefabs/Weapons/M4/Visuals/Attachments";
	private const string c_M4LootAttachmentsRoot = "Assets/Prefabs/World/Loot/M4/Attachments";
	private const string c_M4InventoryRoot = "Assets/GameData/Inventory/M4";
	private const string c_M4ShootingRoot = "Assets/GameData/Shooting/M4";

	private const string c_AmmoLootRoot = "Assets/Prefabs/World/Loot/Ammo";
	private const string c_Ammo556LootPath = "Assets/Prefabs/World/Loot/Ammo/Loot_AmmoBox_556NATO.prefab";
	private const string c_AkEquippedPath = "Assets/Prefabs/Weapons/AK/Equipped/Equipped_AK47.prefab";
	private const string c_WeaponAkPath = "Assets/GameData/Shooting/AK/Weapon_AK47.asset";
	private const string c_ItemWeaponAkPath = "Assets/GameData/Inventory/AK/Item_Weapon_AK47.asset";
	private const string c_MissionPrepSetPath = "Assets/GameData/Inventory/M4/MissionPrepM4AvailableEquipmentSet.asset";

	private static readonly string[] s_TemporaryAk47Children =
	{
		"SilencerAK",
		"MuzzleBrakeAK",
		"MuzzleBrakeM4",
		"MagazineAK5.45_30",
		"MagazineAK5.45_45",
		"MagazineAK7.62_75",
		"MagazineAK7.62_30",
		"MagazineAK7.62_30B",
		"MagazineAK7.62_30C",
		"Attachment_Visual_AK_Reddot4+Rail",
		"Attachment_Visual_AK_Scope11"
	};

	[MenuItem("Tools/AK47/Build Prepared Scene Content")]
	public static void BuildPreparedContentFromMenu()
	{
		try
		{
			BuildPreparedContent();
			EditorUtility.DisplayDialog("AK47", "Готово: префабы, лут, данные и стартовый loadout обновлены.", "OK");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("AK47", exception.Message, "OK");
		}
	}

	public static void BuildPreparedContent()
	{
		EnsureDirectories();

		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		GameObject ak47 = GameObject.Find(c_Ak47SceneName);
		if (ak47 == null)
			throw new InvalidOperationException($"GameObject '{c_Ak47SceneName}' not found in {c_ScenePath}.");

		BuildAmmo762();
		GameObject silencerAkVisual = SaveSceneObject("SilencerAK", $"{c_AkVisualAttachmentsRoot}/Attachment_Visual_AK_SilencerAK.prefab");
		GameObject muzzleBrakeAkVisual = SaveSceneObject("MuzzleBrakeAK", $"{c_AkVisualAttachmentsRoot}/Attachment_Visual_AK_MuzzleBrakeAK.prefab");
		GameObject muzzleBrakeM4Visual = SaveSceneObject("MuzzleBrakeM4", $"{c_M4VisualAttachmentsRoot}/Attachment_Visual_M4_MuzzleBrakeM4.prefab");

		ItemDefinition silencerAkItem = BuildAttachment(
			"Attachment_AK_SilencerAK",
			$"{c_AkShootingRoot}/Attachment_AK_SilencerAK.asset",
			"Item_Attachment_AK_SilencerAK",
			$"{c_AkInventoryRoot}/Item_Attachment_AK_SilencerAK.asset",
			"item.attachment.ak_silencer",
			"AK-series suppressor.",
			WeaponAttachmentType.Suppressor,
			WeaponAttachmentSlotType.Muzzle,
			silencerAkVisual,
			new[] { LoadAsset<WeaponDefinition>(c_WeaponAkPath) },
			760,
			_aimTimeModifier: 1.1f,
			_effectiveRangeModifier: 1.05f,
			_recoilModifier: 0.95f);

		ItemDefinition muzzleBrakeAkItem = BuildAttachment(
			"Attachment_AK_MuzzleBrakeAK",
			$"{c_AkShootingRoot}/Attachment_AK_MuzzleBrakeAK.asset",
			"Item_Attachment_AK_MuzzleBrakeAK",
			$"{c_AkInventoryRoot}/Item_Attachment_AK_MuzzleBrakeAK.asset",
			"item.attachment.ak_muzzle_brake",
			"AK-series muzzle brake.",
			WeaponAttachmentType.Compensator,
			WeaponAttachmentSlotType.Muzzle,
			muzzleBrakeAkVisual,
			new[] { LoadAsset<WeaponDefinition>(c_WeaponAkPath) },
			420,
			_aimTimeModifier: 1.02f,
			_effectiveRangeModifier: 1f,
			_recoilModifier: 0.82f);

		ItemDefinition muzzleBrakeM4Item = BuildAttachment(
			"Attachment_M4_MuzzleBrakeM4",
			$"{c_M4ShootingRoot}/Attachment_M4_MuzzleBrakeM4.asset",
			"Item_Attachment_M4_MuzzleBrakeM4",
			$"{c_M4InventoryRoot}/Item_Attachment_M4_MuzzleBrakeM4.asset",
			"item.attachment.m4_muzzle_brake",
			"M4 muzzle brake.",
			WeaponAttachmentType.Compensator,
			WeaponAttachmentSlotType.Muzzle,
			muzzleBrakeM4Visual,
			LoadM4Weapons(),
			420,
			_aimTimeModifier: 1.02f,
			_effectiveRangeModifier: 1f,
			_recoilModifier: 0.82f);

		BuildLootForItem(silencerAkItem, $"{c_AkLootAttachmentsRoot}/Loot_Att_AK_SilencerAK.prefab", silencerAkVisual);
		BuildLootForItem(muzzleBrakeAkItem, $"{c_AkLootAttachmentsRoot}/Loot_Att_AK_MuzzleBrakeAK.prefab", muzzleBrakeAkVisual);
		BuildLootForItem(muzzleBrakeM4Item, $"{c_M4LootAttachmentsRoot}/Loot_Att_M4_MuzzleBrakeM4.prefab", muzzleBrakeM4Visual);

		Dictionary<string, ItemDefinition> magazineItems = BuildMagazines();
		GameObject equippedAk = BuildEquippedAk47(ak47);
		ItemDefinition weaponItem = BuildAk47WeaponAndItem(equippedAk);
		BuildLootForItem(weaponItem, $"{c_AkLootWeaponsRoot}/Loot_Weapon_AK47.prefab", equippedAk);

		UpdateStarterLoadout(weaponItem, magazineItems, silencerAkItem, muzzleBrakeAkItem, muzzleBrakeM4Item);
		UpdateMissionPrepSet(weaponItem, magazineItems, silencerAkItem, muzzleBrakeAkItem, muzzleBrakeM4Item);

		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[Ak47PreparedContentBuilder] Build complete.");
	}

	private static void BuildAmmo762()
	{
		AmmoDefinition ammo = GetOrCreateAsset<AmmoDefinition>("Assets/GameData/Shooting/Ammo_762x39mm.asset", "Ammo_762x39mm");
		var so = new SerializedObject(ammo);
		so.FindProperty("m_Caliber").enumValueIndex = (int)CaliberType.Seven62By39;
		so.FindProperty("m_BaseDamage").floatValue = 38f;
		so.FindProperty("m_Penetration").floatValue = 16f;
		so.FindProperty("m_ArmorDamage").floatValue = 10f;
		so.FindProperty("m_ProjectileCount").intValue = 1;
		so.FindProperty("m_Velocity").floatValue = 715f;
		so.FindProperty("m_EffectiveRangeMeters").floatValue = 350f;
		so.FindProperty("m_ShellEjectSpeed").floatValue = 5.5f;
		so.FindProperty("m_ShellEjectSpeedVariance").floatValue = 0.75f;
		so.FindProperty("m_ShellEjectUpSpeed").floatValue = 1.2f;
		so.FindProperty("m_ShellAngularVelocity").floatValue = 18f;
		so.FindProperty("m_ShellImpactMinSpeed").floatValue = 0.35f;
		so.FindProperty("m_ShellImpactVolume").floatValue = 0.55f;
		so.FindProperty("m_ShellLifetimeAfterImpactSeconds").floatValue = 3f;
		so.FindProperty("m_ShellMaxAirborneSeconds").floatValue = 12f;
		so.FindProperty("m_SpreadModifier").floatValue = 1f;
		so.FindProperty("m_RecoilModifier").floatValue = 1.08f;
		so.FindProperty("m_WearPerShot").floatValue = 1f;
		so.FindProperty("m_FoulingPerShot").floatValue = 1f;
		so.FindProperty("m_JamRiskModifier").floatValue = 1f;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(ammo);

		ItemDefinition item = GetOrCreateAsset<ItemDefinition>("Assets/GameData/Inventory/Item_Loot_AmmoBox_762x39.asset", "Item_Loot_AmmoBox_762x39");
		SetItemBasics(item, "item.loot.ammo_box.762", "7.62x39 ammo box.", 140, null, null, null, ammo, 120);

		GameObject sourceLoot = AssetDatabase.LoadAssetAtPath<GameObject>(c_Ammo556LootPath);
		if (sourceLoot == null)
			throw new InvalidOperationException($"Missing source ammo loot prefab: {c_Ammo556LootPath}");

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceLoot);
		try
		{
			instance.name = "Loot_AmmoBox_762x39";
			WorldPickupItem pickup = instance.GetComponent<WorldPickupItem>();
			if (pickup != null)
			{
				var pickupSo = new SerializedObject(pickup);
				pickupSo.FindProperty("m_Definition").objectReferenceValue = item;
				pickupSo.ApplyModifiedPropertiesWithoutUndo();
			}

			GameObject lootPrefab = SaveAsPrefab(instance, $"{c_AmmoLootRoot}/Loot_AmmoBox_762x39.prefab");
			var itemSo = new SerializedObject(item);
			itemSo.FindProperty("m_DropWorldPrefab").objectReferenceValue = lootPrefab;
			itemSo.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(item);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(instance);
		}
	}

	private static Dictionary<string, ItemDefinition> BuildMagazines()
	{
		var result = new Dictionary<string, ItemDefinition>();
		BuildMagazine(result, "MagazineAK5.45_30", "Mag_Visual_AK_545_30", "Magazine_AK_545_30", "Item_Mag_AK_545_30", "item.mag.ak_545_30", "30-round 5.45x39 AK magazine.", CaliberType.Five45By39, 30, 120);
		BuildMagazine(result, "MagazineAK5.45_45", "Mag_Visual_AK_545_45", "Magazine_AK_545_45", "Item_Mag_AK_545_45", "item.mag.ak_545_45", "45-round 5.45x39 AK magazine.", CaliberType.Five45By39, 45, 180);
		BuildMagazine(result, "MagazineAK7.62_75", "Mag_Visual_AK_762_75", "Magazine_AK_762_75", "Item_Mag_AK_762_75", "item.mag.ak_762_75", "75-round 7.62x39 AK drum magazine.", CaliberType.Seven62By39, 75, 360, _reloadModifier: 1.2f, _jamModifier: 1.08f);
		BuildMagazine(result, "MagazineAK7.62_30", "Mag_Visual_AK_762_30", "Magazine_AK_762_30", "Item_Mag_AK_762_30", "item.mag.ak_762_30", "30-round 7.62x39 AK steel magazine.", CaliberType.Seven62By39, 30, 120);
		BuildMagazine(result, "MagazineAK7.62_30B", "Mag_Visual_AK_762_30B", "Magazine_AK_762_30B", "Item_Mag_AK_762_30B", "item.mag.ak_762_30b", "30-round 7.62x39 AK bakelite magazine.", CaliberType.Seven62By39, 30, 120);
		BuildMagazine(result, "MagazineAK7.62_30C", "Mag_Visual_AK_762_30C", "Magazine_AK_762_30C", "Item_Mag_AK_762_30C", "item.mag.ak_762_30c", "30-round 7.62x39 AK coupled magazine.", CaliberType.Seven62By39, 30, 120);
		return result;
	}

	private static void BuildMagazine(
		Dictionary<string, ItemDefinition> _items,
		string _sceneName,
		string _visualName,
		string _magazineName,
		string _itemName,
		string _localizationKey,
		string _description,
		CaliberType _caliber,
		int _capacity,
		int _price,
		float _reloadModifier = 1f,
		float _jamModifier = 1f)
	{
		GameObject visual = SaveSceneObject(_sceneName, $"{c_AkVisualMagazinesRoot}/{_visualName}.prefab");
		MagazineDefinition magazine = GetOrCreateAsset<MagazineDefinition>($"{c_AkShootingRoot}/{_magazineName}.asset", _magazineName);
		var magSo = new SerializedObject(magazine);
		magSo.FindProperty("m_MagazineType").enumValueIndex = (int)MagazineType.RifleStandard;
		magSo.FindProperty("m_SupportedCaliber").enumValueIndex = (int)_caliber;
		magSo.FindProperty("m_Capacity").intValue = _capacity;
		magSo.FindProperty("m_RoundLoadTimeSeconds").floatValue = 0.35f;
		magSo.FindProperty("m_ReloadTimeModifier").floatValue = _reloadModifier;
		magSo.FindProperty("m_JamRiskModifier").floatValue = _jamModifier;
		magSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(magazine);

		ItemDefinition item = GetOrCreateAsset<ItemDefinition>($"{c_AkInventoryRoot}/{_itemName}.asset", _itemName);
		SetItemBasics(item, _localizationKey, _description, _price, visual, null, magazine, null, 0);
		GameObject loot = BuildLootForItem(item, $"{c_AkLootMagazinesRoot}/Loot_{_itemName}.prefab", visual);
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_DropWorldPrefab").objectReferenceValue = loot;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		_items[_itemName] = item;
	}

	private static ItemDefinition BuildAttachment(
		string _attachmentName,
		string _attachmentPath,
		string _itemName,
		string _itemPath,
		string _localizationKey,
		string _description,
		WeaponAttachmentType _attachmentType,
		WeaponAttachmentSlotType _slot,
		GameObject _visual,
		WeaponDefinition[] _compatibleWeapons,
		int _price,
		float _aimTimeModifier,
		float _effectiveRangeModifier,
		float _recoilModifier)
	{
		WeaponAttachmentDefinition attachment = GetOrCreateAsset<WeaponAttachmentDefinition>(_attachmentPath, _attachmentName);
		var so = new SerializedObject(attachment);
		so.FindProperty("m_AttachmentType").enumValueIndex = (int)_attachmentType;
		so.FindProperty("m_RequiredSlot").enumValueIndex = (int)_slot;
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weapons = so.FindProperty("m_CompatibleWeapons");
		weapons.arraySize = _compatibleWeapons != null ? _compatibleWeapons.Length : 0;
		for (int i = 0; i < weapons.arraySize; i++)
			weapons.GetArrayElementAtIndex(i).objectReferenceValue = _compatibleWeapons[i];
		SerializedProperty slots = so.FindProperty("m_CompatibleSlots");
		slots.arraySize = 1;
		slots.GetArrayElementAtIndex(0).enumValueIndex = (int)_slot;
		so.FindProperty("m_AimTimeModifier").floatValue = _aimTimeModifier;
		so.FindProperty("m_EffectiveRangeModifier").floatValue = _effectiveRangeModifier;
		so.FindProperty("m_RecoilModifier").floatValue = _recoilModifier;
		so.FindProperty("m_ReloadTimeModifier").floatValue = 1f;
		so.FindProperty("m_WearPerShotMultiplier").floatValue = 1f;
		so.FindProperty("m_FoulingPerShotMultiplier").floatValue = _attachmentType == WeaponAttachmentType.Suppressor ? 1.2f : 1f;
		so.FindProperty("m_JamRiskModifier").floatValue = _attachmentType == WeaponAttachmentType.Suppressor ? 1.08f : 1f;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _visual;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);

		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(_itemPath, _itemName);
		SetItemBasics(item, _localizationKey, _description, _price, _visual, attachment, null, null, 0);
		return item;
	}

	private static GameObject BuildEquippedAk47(GameObject _sceneAk47)
	{
		GameObject clone = UnityEngine.Object.Instantiate(_sceneAk47);
		clone.name = "Equipped_AK47";
		try
		{
			foreach (string childName in s_TemporaryAk47Children)
				DestroyChildrenByName(clone.transform, childName);

			GameObject prefab = SaveAsPrefab(clone, c_AkEquippedPath);
			return prefab;
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(clone);
		}
	}

	private static ItemDefinition BuildAk47WeaponAndItem(GameObject _equippedPrefab)
	{
		WeaponDefinition weapon = GetOrCreateAsset<WeaponDefinition>(c_WeaponAkPath, "Weapon_AK47");
		var weaponSo = new SerializedObject(weapon);
		weaponSo.FindProperty("m_WeaponClass").enumValueIndex = (int)WeaponClassType.Rifle;
		weaponSo.FindProperty("m_SupportedCaliber").enumValueIndex = (int)CaliberType.Seven62By39;
		weaponSo.FindProperty("m_SupportedMagazineType").enumValueIndex = (int)MagazineType.RifleStandard;
		SerializedProperty slots = weaponSo.FindProperty("m_AttachmentSlots");
		WeaponAttachmentSlotType[] slotTypes =
		{
			WeaponAttachmentSlotType.Muzzle,
			WeaponAttachmentSlotType.Optic,
			WeaponAttachmentSlotType.SideRail,
			WeaponAttachmentSlotType.Stock,
			WeaponAttachmentSlotType.UnderBarrel,
			WeaponAttachmentSlotType.Rail,
			WeaponAttachmentSlotType.Rail,
			WeaponAttachmentSlotType.Rail
		};
		slots.arraySize = slotTypes.Length;
		for (int i = 0; i < slotTypes.Length; i++)
		{
			SerializedProperty slot = slots.GetArrayElementAtIndex(i);
			slot.FindPropertyRelative("SlotType").enumValueIndex = (int)slotTypes[i];
			slot.FindPropertyRelative("IsRequired").boolValue = false;
			slot.FindPropertyRelative("AnchorChildName").stringValue = string.Empty;
		}
		weaponSo.FindProperty("m_FireRateRpm").intValue = 600;
		weaponSo.FindProperty("m_AimTimeSeconds").floatValue = 0.32f;
		weaponSo.FindProperty("m_ReloadTimeSeconds").floatValue = 2.4f;
		weaponSo.FindProperty("m_EffectiveRangeMeters").floatValue = 350f;
		weaponSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);

		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(c_ItemWeaponAkPath, "Item_Weapon_AK47");
		SetItemBasics(item, "item.weapon.ak47", "AK-47 rifle chambered in 7.62x39.", 2400, _equippedPrefab, null, null, null, 0);
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_Category").enumValueIndex = 1;
		itemSo.FindProperty("m_WeaponDefinition").objectReferenceValue = weapon;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
	}

	private static GameObject BuildLootForItem(ItemDefinition _item, string _lootPath, GameObject _visualPrefab)
	{
		if (_item == null)
			throw new ArgumentNullException(nameof(_item));

		GameObject root = new GameObject(Path.GetFileNameWithoutExtension(_lootPath));
		try
		{
			root.layer = LayerMask.NameToLayer("Loot");
			if (_visualPrefab != null)
			{
				GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(_visualPrefab, root.transform);
				if (visual != null)
				{
					visual.transform.localPosition = Vector3.zero;
					visual.transform.localRotation = Quaternion.identity;
				}
			}

			BoxCollider box = root.AddComponent<BoxCollider>();
			box.center = new Vector3(0f, 0.03f, 0f);
			box.size = new Vector3(0.22f, 0.12f, 0.28f);
			Rigidbody rb = root.AddComponent<Rigidbody>();
			rb.mass = 0.25f;
			rb.linearDamping = 0.15f;
			rb.angularDamping = 0.4f;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			WorldPickupItem pickup = root.AddComponent<WorldPickupItem>();
			var pickupSo = new SerializedObject(pickup);
			pickupSo.FindProperty("m_Definition").objectReferenceValue = _item;
			pickupSo.ApplyModifiedPropertiesWithoutUndo();

			GameObject prefab = SaveAsPrefab(root, _lootPath);
			var itemSo = new SerializedObject(_item);
			itemSo.FindProperty("m_DropWorldPrefab").objectReferenceValue = prefab;
			itemSo.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(_item);
			return prefab;
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static void UpdateStarterLoadout(
		ItemDefinition _weaponItem,
		Dictionary<string, ItemDefinition> _magazineItems,
		params ItemDefinition[] _attachmentItems)
	{
		CharacterInventoryStarterLoadout starter = UnityEngine.Object.FindFirstObjectByType<CharacterInventoryStarterLoadout>(FindObjectsInactive.Include);
		if (starter == null)
			return;

		ItemDefinition mag762 = _magazineItems["Item_Mag_AK_762_30"];
		ItemDefinition ammo762Item = LoadAsset<ItemDefinition>("Assets/GameData/Inventory/Item_Loot_AmmoBox_762x39.asset");
		AmmoDefinition ammo762 = LoadAsset<AmmoDefinition>("Assets/GameData/Shooting/Ammo_762x39mm.asset");
		var so = new SerializedObject(starter);
		so.FindProperty("m_WeaponItem").objectReferenceValue = _weaponItem;
		so.FindProperty("m_MagazineItem").objectReferenceValue = mag762;
		so.FindProperty("m_AmmoForMagazines").objectReferenceValue = ammo762;
		so.FindProperty("m_SpareLoadedMagazinesInBag").intValue = 2;
		so.FindProperty("m_SpareEmptyMagazinesInBag").intValue = 1;

		SerializedProperty ammoBoxes = so.FindProperty("m_AmmoBoxItems");
		ammoBoxes.arraySize = 1;
		ammoBoxes.GetArrayElementAtIndex(0).objectReferenceValue = ammo762Item;

		SerializedProperty extraItems = so.FindProperty("m_ExtraItemsInBag");
		List<ItemDefinition> extras = new List<ItemDefinition>();
		foreach (KeyValuePair<string, ItemDefinition> pair in _magazineItems)
		{
			if (pair.Value != null && pair.Value != mag762)
				extras.Add(pair.Value);
		}
		if (ammo762Item != null)
			extras.Add(ammo762Item);
		extraItems.arraySize = extras.Count;
		for (int i = 0; i < extras.Count; i++)
			extraItems.GetArrayElementAtIndex(i).objectReferenceValue = extras[i];

		SerializedProperty attachments = so.FindProperty("m_AttachmentItemsInBag");
		attachments.arraySize = _attachmentItems != null ? _attachmentItems.Length : 0;
		for (int i = 0; i < attachments.arraySize; i++)
			attachments.GetArrayElementAtIndex(i).objectReferenceValue = _attachmentItems[i];

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(starter);
	}

	private static void UpdateMissionPrepSet(
		ItemDefinition _weaponItem,
		Dictionary<string, ItemDefinition> _magazineItems,
		params ItemDefinition[] _attachmentItems)
	{
		MissionPrepAvailableEquipmentItemSet set = AssetDatabase.LoadAssetAtPath<MissionPrepAvailableEquipmentItemSet>(c_MissionPrepSetPath);
		if (set == null)
			return;

		var items = new List<ItemDefinition>();
		var seen = new HashSet<ItemDefinition>();
		var so = new SerializedObject(set);
		SerializedProperty array = so.FindProperty("m_Items");
		for (int i = 0; i < array.arraySize; i++)
		{
			ItemDefinition item = array.GetArrayElementAtIndex(i).objectReferenceValue as ItemDefinition;
			if (item != null && seen.Add(item))
				items.Add(item);
		}

		AddUnique(items, seen, _weaponItem);
		foreach (KeyValuePair<string, ItemDefinition> pair in _magazineItems)
			AddUnique(items, seen, pair.Value);
		if (_attachmentItems != null)
		{
			for (int i = 0; i < _attachmentItems.Length; i++)
				AddUnique(items, seen, _attachmentItems[i]);
		}
		AddUnique(items, seen, LoadAsset<ItemDefinition>("Assets/GameData/Inventory/Item_Loot_AmmoBox_762x39.asset"));

		array.arraySize = items.Count;
		for (int i = 0; i < items.Count; i++)
			array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
		so.FindProperty("m_MagazineAmmo").objectReferenceValue = LoadAsset<AmmoDefinition>("Assets/GameData/Shooting/Ammo_762x39mm.asset");
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(set);
	}

	private static void SetItemBasics(
		ItemDefinition _item,
		string _localizationKey,
		string _description,
		int _basePrice,
		GameObject _equippedVisual,
		WeaponAttachmentDefinition _attachment,
		MagazineDefinition _magazine,
		AmmoDefinition _ammo,
		int _initialAmmoCount)
	{
		var so = new SerializedObject(_item);
		so.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		so.FindProperty("m_Description").stringValue = _description;
		so.FindProperty("m_BasePrice").intValue = _basePrice;
		so.FindProperty("m_Category").enumValueIndex = 0;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _equippedVisual;
		so.FindProperty("m_WeaponAttachmentDefinition").objectReferenceValue = _attachment;
		so.FindProperty("m_MagazineDefinition").objectReferenceValue = _magazine;
		so.FindProperty("m_AmmoDefinition").objectReferenceValue = _ammo;
		so.FindProperty("m_InitialAmmoCount").intValue = _initialAmmoCount;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
	}

	private static GameObject SaveSceneObject(string _sceneName, string _path)
	{
		GameObject source = FindInSceneIncludingInactive(_sceneName);
		if (source == null)
			throw new InvalidOperationException($"Scene object '{_sceneName}' not found.");

		return SaveAsPrefab(source, _path);
	}

	private static GameObject SaveAsPrefab(GameObject _source, string _path)
	{
		EnsureFolder(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_source, _path);
		if (prefab == null)
			throw new InvalidOperationException($"Failed to save prefab: {_path}");
		return prefab;
	}

	private static T GetOrCreateAsset<T>(string _path, string _name) where T : ScriptableObject
	{
		T asset = AssetDatabase.LoadAssetAtPath<T>(_path);
		if (asset != null)
			return asset;

		EnsureFolder(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		asset = ScriptableObject.CreateInstance<T>();
		asset.name = _name;
		AssetDatabase.CreateAsset(asset, _path);
		return asset;
	}

	private static T LoadAsset<T>(string _path) where T : UnityEngine.Object
	{
		return AssetDatabase.LoadAssetAtPath<T>(_path);
	}

	private static WeaponDefinition[] LoadM4Weapons()
	{
		var weapons = new List<WeaponDefinition>();
		AddIfNotNull(weapons, LoadAsset<WeaponDefinition>("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset"));
		AddIfNotNull(weapons, LoadAsset<WeaponDefinition>("Assets/GameData/Shooting/M4/Weapon_M4_ModA_2.asset"));
		return weapons.ToArray();
	}

	private static void AddIfNotNull<T>(List<T> _list, T _item) where T : class
	{
		if (_item != null)
			_list.Add(_item);
	}

	private static void AddUnique(List<ItemDefinition> _items, HashSet<ItemDefinition> _seen, ItemDefinition _item)
	{
		if (_item != null && _seen.Add(_item))
			_items.Add(_item);
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

	private static void DestroyChildrenByName(Transform _root, string _name)
	{
		for (int i = _root.childCount - 1; i >= 0; i--)
		{
			Transform child = _root.GetChild(i);
			if (child.name == _name)
			{
				UnityEngine.Object.DestroyImmediate(child.gameObject);
				continue;
			}

			DestroyChildrenByName(child, _name);
		}
	}

	private static void EnsureDirectories()
	{
		EnsureFolder(c_AkWeaponRoot);
		EnsureFolder(c_AkVisualAttachmentsRoot);
		EnsureFolder(c_AkVisualMagazinesRoot);
		EnsureFolder(c_AkEquippedRoot);
		EnsureFolder(c_AkLootAttachmentsRoot);
		EnsureFolder(c_AkLootMagazinesRoot);
		EnsureFolder(c_AkLootWeaponsRoot);
		EnsureFolder(c_AkInventoryRoot);
		EnsureFolder(c_AkShootingRoot);
		EnsureFolder(c_M4VisualAttachmentsRoot);
		EnsureFolder(c_M4LootAttachmentsRoot);
		EnsureFolder(c_AmmoLootRoot);
	}

	private static void EnsureFolder(string _path)
	{
		if (string.IsNullOrEmpty(_path) || AssetDatabase.IsValidFolder(_path))
			return;

		string parent = Path.GetDirectoryName(_path)?.Replace('\\', '/');
		string name = Path.GetFileName(_path);
		if (!AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		if (!AssetDatabase.IsValidFolder(_path))
			AssetDatabase.CreateFolder(parent, name);
	}
}
#endif
