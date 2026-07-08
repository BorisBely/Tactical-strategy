#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class AkPlatformWeaponsBuilderBootstrap
{
	private const string c_MarkerPath = "Assets/.ak_platform_weapons_build_marker";

	static AkPlatformWeaponsBuilderBootstrap()
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

			AkPlatformWeaponsBuilder.BuildAll();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[AkPlatformWeaponsBuilder] Auto-run failed: {exception}");
		}
	}
}

/// <summary>
/// Выпечка WeaponDefinition / ItemDefinition / loot для всех вариантов АК-платформы из готовых Equipped-префабов.
/// </summary>
public static class AkPlatformWeaponsBuilder
{
	private const string c_TemplateWeaponPath = "Assets/GameData/Shooting/AK/Weapon_AK47.asset";
	private const string c_TemplateItemPath = "Assets/GameData/Inventory/AK/Item_Weapon_AK47.asset";
	private const string c_EquippedRoot = "Assets/Prefabs/Weapons/AK/Equipped";
	private const string c_ShootingRoot = "Assets/GameData/Shooting/AK";
	private const string c_InventoryRoot = "Assets/GameData/Inventory/AK";
	private const string c_LootWeaponsRoot = "Assets/Prefabs/World/Loot/AK/Weapons";
	private const string c_Ammo545Path = "Assets/GameData/Shooting/Ammo_545x39mm.asset";
	private const string c_Ammo545ItemPath = "Assets/GameData/Inventory/Item_Loot_AmmoBox_545x39.asset";
	private const string c_Ammo545LootPath = "Assets/Prefabs/World/Loot/Ammo/Loot_AmmoBox_545x39.prefab";
	private const string c_Ammo762LootPath = "Assets/Prefabs/World/Loot/Ammo/Loot_AmmoBox_762x39.prefab";
	private const string c_LootAttachmentsRoot = "Assets/Prefabs/World/Loot/AK/Attachments";
	private const string c_Silencer762AttachmentPath = "Assets/GameData/Shooting/AK/Attachment_AK_SilencerAK.asset";
	private const string c_MuzzleBrake762AttachmentPath = "Assets/GameData/Shooting/AK/Attachment_AK_MuzzleBrakeAK.asset";
	private const string c_Silencer545AttachmentPath = "Assets/GameData/Shooting/AK/Attachment_AK_SilencerAK_545.asset";
	private const string c_MuzzleBrake545AttachmentPath = "Assets/GameData/Shooting/AK/Attachment_AK_MuzzleBrakeAK_545.asset";

	private static readonly WeaponAttachmentSlotType[] s_FullAttachmentSlots =
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

	[MenuItem("Tools/AK Platform/Build All Platform Weapons")]
	public static void BuildAllFromMenu()
	{
		try
		{
			BuildAll();
			EditorUtility.DisplayDialog("AK Platform", "Готово: оружия, патрон 5.45 и совместимость модулей обновлены.", "OK");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("AK Platform", exception.Message, "OK");
		}
	}

	/// <summary>Batch: -executeMethod AkPlatformWeaponsBuilder.RunBatch</summary>
	public static void RunBatch()
	{
		try
		{
			BuildAll();
			Debug.Log("[AkPlatformWeaponsBuilder] Batch complete.");
			EditorApplication.Exit(0);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorApplication.Exit(1);
		}
	}

	public static void BuildAll()
	{
		EnsureFolders();
		BuildAmmo545();
		WeaponDefinition template = LoadAsset<WeaponDefinition>(c_TemplateWeaponPath);
		ItemDefinition templateItem = LoadAsset<ItemDefinition>(c_TemplateItemPath);
		if (template == null || templateItem == null)
			throw new InvalidOperationException("Missing AK47 template weapon or item.");

		var builtWeapons = new List<WeaponDefinition>();
		foreach (WeaponBuildConfig config in GetWeaponConfigs())
		{
			GameObject equippedPrefab = LoadAsset<GameObject>($"{c_EquippedRoot}/{config.EquippedPrefabFileName}.prefab");
			if (equippedPrefab == null)
				throw new InvalidOperationException($"Missing equipped prefab: {config.EquippedPrefabFileName}");

			WeaponDefinition weapon = BuildWeaponDefinition(config, template);
			ItemDefinition item = BuildItemDefinition(config, equippedPrefab, weapon, templateItem);
			BuildLootForItem(item, $"{c_LootWeaponsRoot}/Loot_{config.ItemAssetName}.prefab", equippedPrefab);
			builtWeapons.Add(weapon);
		}

		UpdateAkAttachmentCompatibility(builtWeapons);
		BuildAk545MuzzleAttachments(builtWeapons);
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[AkPlatformWeaponsBuilder] Built {builtWeapons.Count} platform weapons.");
	}

	private static WeaponDefinition BuildWeaponDefinition(WeaponBuildConfig _config, WeaponDefinition _template)
	{
		string path = $"{c_ShootingRoot}/{_config.WeaponAssetName}.asset";
		bool isNew = !File.Exists(path);
		WeaponDefinition weapon = GetOrCreateAsset<WeaponDefinition>(path, _config.WeaponAssetName);
		if (isNew)
			EditorUtility.CopySerialized(_template, weapon);

		weapon.name = _config.WeaponAssetName;
		var so = new SerializedObject(weapon);
		so.FindProperty("m_SupportedCaliber").enumValueIndex = (int)_config.Caliber;
		so.FindProperty("m_SupportedMagazineType").enumValueIndex = (int)MagazineType.RifleStandard;
		so.FindProperty("m_AttachmentSlotProfile").enumValueIndex = (int)_config.SlotProfile;
		WriteAttachmentSlots(so.FindProperty("m_AttachmentSlots"));
		so.FindProperty("m_FireRateRpm").floatValue = _config.FireRateRpm;
		so.FindProperty("m_AimTimeSeconds").floatValue = _config.AimTimeSeconds;
		so.FindProperty("m_ReloadTimeSeconds").floatValue = _config.ReloadTimeSeconds;
		so.FindProperty("m_EffectiveRangeMeters").floatValue = _config.EffectiveRangeMeters;
		so.FindProperty("m_BaseShotDispersion").floatValue = _config.BaseShotDispersion;
		so.FindProperty("m_RecoilPerShot").floatValue = _config.RecoilPerShot;
		so.FindProperty("m_SemiAutoRecoilMultiplier").floatValue = _config.SemiAutoRecoilMultiplier;
		so.FindProperty("m_AutoRecoilMultiplier").floatValue = _config.AutoRecoilMultiplier;
		so.FindProperty("m_RecoilRecoveryPerSecond").floatValue = _config.RecoilRecoveryPerSecond;
		so.FindProperty("m_Reliability").floatValue = _config.Reliability;
		ApplyBalanceCurves(so, _config.CurveKind);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
		return weapon;
	}

	private static void ApplyBalanceCurves(SerializedObject _weaponSo, WeaponDistanceCurveLibrary.WeaponBalanceKind _kind)
	{
		WeaponDistanceCurveLibrary.WeaponBalanceCurves curves = WeaponDistanceCurveLibrary.GetCurves(_kind);
		SerializedProperty distanceAimProfile = _weaponSo.FindProperty("m_DistanceAimProfile");
		if (distanceAimProfile != null)
		{
			distanceAimProfile.FindPropertyRelative("m_DispersionMultiplierByDistance").animationCurveValue =
				OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes);
			distanceAimProfile.FindPropertyRelative("m_AimTimeMultiplierByDistance").animationCurveValue =
				OpticDistanceCurveLibrary.BuildCurve(curves.AimTimeKeyframes);
		}

		_weaponSo.FindProperty("m_AutoBurstSpreadMultiplierByShot").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.AutoBurstSpreadKeyframes);
	}

	private static ItemDefinition BuildItemDefinition(
		WeaponBuildConfig _config,
		GameObject _equippedPrefab,
		WeaponDefinition _weapon,
		ItemDefinition _templateItem)
	{
		string path = $"{c_InventoryRoot}/{_config.ItemAssetName}.asset";
		bool isNew = !File.Exists(path);
		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(path, _config.ItemAssetName);
		if (isNew)
			EditorUtility.CopySerialized(_templateItem, item);

		item.name = _config.ItemAssetName;
		var so = new SerializedObject(item);
		var templateSo = new SerializedObject(_templateItem);
		so.FindProperty("m_LocalizationKey").stringValue = _config.LocalizationKey;
		so.FindProperty("m_Description").stringValue = _config.DescriptionEn;
		so.FindProperty("m_BasePrice").intValue = _config.BasePrice;
		so.FindProperty("m_Category").enumValueIndex = 1;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _equippedPrefab;
		so.FindProperty("m_WeaponDefinition").objectReferenceValue = _weapon;
		so.FindProperty("m_RightHandLocalPosition").vector3Value =
			templateSo.FindProperty("m_RightHandLocalPosition").vector3Value;
		so.FindProperty("m_RightHandLocalEulerAngles").vector3Value =
			templateSo.FindProperty("m_RightHandLocalEulerAngles").vector3Value;
		CopyReadyHandPoseFields(so, templateSo);
		CopyRightHandIkFields(so, templateSo);
		so.FindProperty("m_LeftHandIkTargetChildName").stringValue =
			templateSo.FindProperty("m_LeftHandIkTargetChildName").stringValue;
		so.FindProperty("m_RightHandIkTargetChildName").stringValue =
			templateSo.FindProperty("m_RightHandIkTargetChildName").stringValue;
		so.FindProperty("m_RightHandIkTargetNotReadyChildName").stringValue =
			templateSo.FindProperty("m_RightHandIkTargetNotReadyChildName").stringValue;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
	}

	private static void BuildAmmo545()
	{
		AmmoDefinition ammo = GetOrCreateAsset<AmmoDefinition>(c_Ammo545Path, "Ammo_545x39mm");
		var ammoSo = new SerializedObject(ammo);
		ammoSo.FindProperty("m_Caliber").enumValueIndex = (int)CaliberType.Five45By39;
		ammoSo.FindProperty("m_BaseDamage").floatValue = 33f;
		ammoSo.FindProperty("m_Penetration").floatValue = 17f;
		ammoSo.FindProperty("m_ArmorDamage").floatValue = 9f;
		ammoSo.FindProperty("m_ProjectileCount").intValue = 1;
		ammoSo.FindProperty("m_Velocity").floatValue = 900f;
		ammoSo.FindProperty("m_EffectiveRangeMeters").floatValue = 100f;
		ammoSo.FindProperty("m_SpreadModifier").floatValue = 1f;
		ammoSo.FindProperty("m_RecoilModifier").floatValue = 0.96f;
		ammoSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(ammo);

		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(c_Ammo545ItemPath, "Item_Loot_AmmoBox_545x39");
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_LocalizationKey").stringValue = "item.loot.ammo_box.545";
		itemSo.FindProperty("m_Description").stringValue = "5.45x39 ammo box.";
		itemSo.FindProperty("m_BasePrice").intValue = 130;
		itemSo.FindProperty("m_Category").enumValueIndex = 0;
		itemSo.FindProperty("m_AmmoDefinition").objectReferenceValue = ammo;
		itemSo.FindProperty("m_InitialAmmoCount").intValue = 120;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);

		GameObject sourceLoot = AssetDatabase.LoadAssetAtPath<GameObject>(c_Ammo762LootPath);
		if (sourceLoot == null)
			throw new InvalidOperationException($"Missing source ammo loot prefab: {c_Ammo762LootPath}");

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceLoot);
		try
		{
			instance.name = "Loot_AmmoBox_545x39";
			WorldPickupItem pickup = instance.GetComponent<WorldPickupItem>();
			if (pickup != null)
			{
				var pickupSo = new SerializedObject(pickup);
				pickupSo.FindProperty("m_Definition").objectReferenceValue = item;
				pickupSo.ApplyModifiedPropertiesWithoutUndo();
			}

			EnsureFolder(Path.GetDirectoryName(c_Ammo545LootPath)?.Replace('\\', '/'));
			GameObject lootPrefab = PrefabUtility.SaveAsPrefabAsset(instance, c_Ammo545LootPath);
			itemSo = new SerializedObject(item);
			itemSo.FindProperty("m_DropWorldPrefab").objectReferenceValue = lootPrefab;
			itemSo.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(item);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(instance);
		}
	}

	private static void UpdateAkAttachmentCompatibility(IReadOnlyList<WeaponDefinition> _builtWeapons)
	{
		var weapons762 = new List<WeaponDefinition>();
		var weapons545 = new List<WeaponDefinition>();
		var weaponsSideRail = new List<WeaponDefinition>();
		for (int i = 0; i < _builtWeapons.Count; i++)
		{
			WeaponDefinition weapon = _builtWeapons[i];
			if (weapon == null)
				continue;

			if (weapon.SupportedCaliber == CaliberType.Seven62By39)
				weapons762.Add(weapon);
			else if (weapon.SupportedCaliber == CaliberType.Five45By39)
				weapons545.Add(weapon);

			if (WeaponAttachmentSlotPolicy.IsSlotTypeEnabled(weapon, WeaponAttachmentSlotType.SideRail))
				weaponsSideRail.Add(weapon);
		}

		SetAttachmentCompatibleWeapons("Assets/GameData/Shooting/AK/Attachment_AK_SilencerAK.asset", weapons762.ToArray());
		SetAttachmentCompatibleWeapons("Assets/GameData/Shooting/AK/Attachment_AK_MuzzleBrakeAK.asset", weapons762.ToArray());
		SetAttachmentCompatibleWeapons(c_Silencer545AttachmentPath, weapons545.ToArray());
		SetAttachmentCompatibleWeapons(c_MuzzleBrake545AttachmentPath, weapons545.ToArray());
		SetAttachmentCompatibleWeapons("Assets/GameData/Shooting/AK/Attachment_AK_Reddot4_Rail.asset", weaponsSideRail.ToArray());
		SetAttachmentCompatibleWeapons("Assets/GameData/Shooting/AK/Attachment_AK_Scope11.asset", weaponsSideRail.ToArray());
	}

	private static void BuildAk545MuzzleAttachments(IReadOnlyList<WeaponDefinition> _builtWeapons)
	{
		var weapons545 = new List<WeaponDefinition>();
		for (int i = 0; i < _builtWeapons.Count; i++)
		{
			WeaponDefinition weapon = _builtWeapons[i];
			if (weapon != null && weapon.SupportedCaliber == CaliberType.Five45By39)
				weapons545.Add(weapon);
		}

		if (weapons545.Count == 0)
			return;

		WeaponAttachmentDefinition silencerTemplate = LoadAsset<WeaponAttachmentDefinition>(c_Silencer762AttachmentPath);
		WeaponAttachmentDefinition brakeTemplate = LoadAsset<WeaponAttachmentDefinition>(c_MuzzleBrake762AttachmentPath);
		ItemDefinition silencerItemTemplate = LoadAsset<ItemDefinition>($"{c_InventoryRoot}/Item_Attachment_AK_SilencerAK.asset");
		ItemDefinition brakeItemTemplate = LoadAsset<ItemDefinition>($"{c_InventoryRoot}/Item_Attachment_AK_MuzzleBrakeAK.asset");
		if (silencerTemplate == null || brakeTemplate == null || silencerItemTemplate == null || brakeItemTemplate == null)
		{
			Debug.LogWarning("[AkPlatformWeaponsBuilder] Missing 7.62 AK muzzle attachment templates for 5.45 duplicates.");
			return;
		}

		BuildDuplicateMuzzleAttachment(
			silencerTemplate,
			c_Silencer545AttachmentPath,
			"Attachment_AK_SilencerAK_545",
			silencerItemTemplate,
			"Item_Attachment_AK_SilencerAK_545",
			"item.attachment.ak_silencer_545",
			"5.45x39 AK-series suppressor.",
			760,
			weapons545.ToArray());

		BuildDuplicateMuzzleAttachment(
			brakeTemplate,
			c_MuzzleBrake545AttachmentPath,
			"Attachment_AK_MuzzleBrakeAK_545",
			brakeItemTemplate,
			"Item_Attachment_AK_MuzzleBrakeAK_545",
			"item.attachment.ak_muzzle_brake_545",
			"5.45x39 AK-series muzzle brake.",
			420,
			weapons545.ToArray());
	}

	private static void BuildDuplicateMuzzleAttachment(
		WeaponAttachmentDefinition _templateAttachment,
		string _attachmentPath,
		string _attachmentName,
		ItemDefinition _templateItem,
		string _itemName,
		string _localizationKey,
		string _description,
		int _price,
		WeaponDefinition[] _compatibleWeapons)
	{
		WeaponAttachmentDefinition attachment = GetOrCreateAsset<WeaponAttachmentDefinition>(_attachmentPath, _attachmentName);
		EditorUtility.CopySerialized(_templateAttachment, attachment);
		attachment.name = _attachmentName;

		var attachmentSo = new SerializedObject(attachment);
		attachmentSo.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weapons = attachmentSo.FindProperty("m_CompatibleWeapons");
		weapons.arraySize = _compatibleWeapons.Length;
		for (int i = 0; i < _compatibleWeapons.Length; i++)
			weapons.GetArrayElementAtIndex(i).objectReferenceValue = _compatibleWeapons[i];
		attachmentSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);

		string itemPath = $"{c_InventoryRoot}/{_itemName}.asset";
		bool isNewItem = !File.Exists(itemPath);
		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(itemPath, _itemName);
		if (isNewItem)
			EditorUtility.CopySerialized(_templateItem, item);

		item.name = _itemName;
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		itemSo.FindProperty("m_Description").stringValue = _description;
		itemSo.FindProperty("m_BasePrice").intValue = _price;
		itemSo.FindProperty("m_WeaponAttachmentDefinition").objectReferenceValue = attachment;
		itemSo.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = attachment.EquippedVisualPrefab;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);

		GameObject visual = attachment.EquippedVisualPrefab;
		BuildLootForItem(item, $"{c_LootAttachmentsRoot}/Loot_{_itemName}.prefab", visual);
	}

	private static void SetAttachmentCompatibleWeapons(string _assetPath, WeaponDefinition[] _weapons)
	{
		WeaponAttachmentDefinition attachment = LoadAsset<WeaponAttachmentDefinition>(_assetPath);
		if (attachment == null)
		{
			Debug.LogWarning($"[AkPlatformWeaponsBuilder] Missing attachment: {_assetPath}");
			return;
		}

		var so = new SerializedObject(attachment);
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weapons = so.FindProperty("m_CompatibleWeapons");
		weapons.arraySize = _weapons != null ? _weapons.Length : 0;
		for (int i = 0; i < weapons.arraySize; i++)
			weapons.GetArrayElementAtIndex(i).objectReferenceValue = _weapons[i];
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);
	}

	private static void WriteAttachmentSlots(SerializedProperty _slots)
	{
		_slots.arraySize = s_FullAttachmentSlots.Length;
		for (int i = 0; i < s_FullAttachmentSlots.Length; i++)
		{
			SerializedProperty slot = _slots.GetArrayElementAtIndex(i);
			slot.FindPropertyRelative("SlotType").enumValueIndex = (int)s_FullAttachmentSlots[i];
			slot.FindPropertyRelative("IsRequired").boolValue = false;
			slot.FindPropertyRelative("AnchorChildName").stringValue = string.Empty;
		}
	}

	private static GameObject BuildLootForItem(ItemDefinition _item, string _lootPath, GameObject _visualPrefab)
	{
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

			EnsureFolder(Path.GetDirectoryName(_lootPath)?.Replace('\\', '/'));
			GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, _lootPath);
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

	private static IEnumerable<WeaponBuildConfig> GetWeaponConfigs()
	{
		yield return Config(
			"Equipped_AK47_0", "Weapon_AK47", "Item_Weapon_AK47",
			"item.weapon.ak47", "AK-47 rifle chambered in 7.62x39.",
			CaliberType.Seven62By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.BattleRifle762Default,
			600f, 0.33f, 2.45f, 95f, 1.18f, 0.54f, 0.86f, 1.34f, 3.2f, 0.86f, 2400);

		yield return Config(
			"Equipped_AK47_1", "Weapon_AK47_1", "Item_Weapon_AK47_1",
			"item.weapon.ak47_1", "AK-47 with wooden handguard, chambered in 7.62x39.",
			CaliberType.Seven62By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.BattleRifle762WoodHandguard,
			600f, 0.35f, 2.45f, 105f, 1.10f, 0.50f, 0.84f, 1.24f, 3.6f, 0.87f, 2500);

		yield return Config(
			"Equipped_AK47MOD1", "Weapon_AK47MOD1", "Item_Weapon_AK47MOD1",
			"item.weapon.ak47mod1", "Tactical AK-47 Mod.1 with M4-style optic rails and side mount, 7.62x39.",
			CaliberType.Seven62By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.Mod1Ak,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.BattleRifle762Mod1,
			600f, 0.37f, 2.45f, 110f, 1.08f, 0.49f, 0.84f, 1.20f, 3.8f, 0.84f, 2800);

		yield return Config(
			"Equipped_AK47S", "Weapon_AK47S", "Item_Weapon_AK47S",
			"item.weapon.ak47s", "AK-47S with folding stock, chambered in 7.62x39.",
			CaliberType.Seven62By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbControlled,
			600f, 0.26f, 2.20f, 75f, 1.25f, 0.56f, 0.90f, 1.45f, 3.0f, 0.83f, 2300);

		yield return Config(
			"Equipped_AK74", "Weapon_AK74", "Item_Weapon_AK74",
			"item.weapon.ak74", "AK-74 rifle chambered in 5.45x39.",
			CaliberType.Five45By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Intermediate545,
			650f, 0.30f, 2.30f, 105f, 1.00f, 0.42f, 0.84f, 1.12f, 4.1f, 0.86f, 2600);

		yield return Config(
			"Equipped_AK74MOD1", "Weapon_AK74MOD1", "Item_Weapon_AK74MOD1",
			"item.weapon.ak74mod1", "Tactical AK-74 Mod.1 with rails and side mount, 5.45x39.",
			CaliberType.Five45By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.Mod1Ak,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Intermediate545,
			650f, 0.33f, 2.30f, 115f, 0.96f, 0.40f, 0.83f, 1.08f, 4.3f, 0.84f, 2900);

		yield return Config(
			"Equipped_AK74U", "Weapon_AK74U", "Item_Weapon_AK74U",
			"item.weapon.ak74u", "Compact AK-74U chambered in 5.45x39.",
			CaliberType.Five45By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbShort,
			650f, 0.26f, 1.95f, 55f, 1.45f, 0.58f, 0.92f, 1.55f, 2.8f, 0.81f, 2200);

		yield return Config(
			"Equipped_AK74UMOD1", "Weapon_AK74UMOD1", "Item_Weapon_AK74UMOD1",
			"item.weapon.ak74umod1", "Tactical AK-74U Mod.1 with rails and side mount, 5.45x39.",
			CaliberType.Five45By39, WeaponClassType.Rifle, WeaponAttachmentSlotProfile.Mod1Ak,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbControlled,
			650f, 0.28f, 2.00f, 65f, 1.35f, 0.55f, 0.90f, 1.45f, 3.1f, 0.80f, 2500);

		yield return Config(
			"Equipped_RPK47", "Weapon_RPK47", "Item_Weapon_RPK47",
			"item.weapon.rpk47", "RPK-47 light machine gun chambered in 7.62x39.",
			CaliberType.Seven62By39, WeaponClassType.LightMachineGun, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support762,
			600f, 0.44f, 2.95f, 125f, 1.02f, 0.46f, 0.86f, 1.08f, 4.7f, 0.88f, 3200);

		yield return Config(
			"Equipped_RPK47MOD1", "Weapon_RPK47MOD1", "Item_Weapon_RPK47MOD1",
			"item.weapon.rpk47mod1", "Tactical RPK-47 Mod.1 with rails and side mount, 7.62x39.",
			CaliberType.Seven62By39, WeaponClassType.LightMachineGun, WeaponAttachmentSlotProfile.Mod1Ak,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support762,
			600f, 0.47f, 2.95f, 135f, 0.98f, 0.45f, 0.85f, 1.04f, 5.0f, 0.86f, 3500);

		yield return Config(
			"Equipped_RPK74", "Weapon_RPK74", "Item_Weapon_RPK74",
			"item.weapon.rpk74", "RPK-74 light machine gun chambered in 5.45x39.",
			CaliberType.Five45By39, WeaponClassType.LightMachineGun, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support545,
			650f, 0.41f, 2.85f, 135f, 0.92f, 0.39f, 0.84f, 1.00f, 5.2f, 0.88f, 3400);

		yield return Config(
			"Equipped_RPK74MOD1", "Weapon_RPK74MOD1", "Item_Weapon_RPK74MOD1",
			"item.weapon.rpk74mod1", "Tactical RPK-74 Mod.1 with rails and side mount, 5.45x39.",
			CaliberType.Five45By39, WeaponClassType.LightMachineGun, WeaponAttachmentSlotProfile.Mod1Ak,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support545,
			650f, 0.44f, 2.85f, 145f, 0.88f, 0.38f, 0.83f, 0.96f, 5.5f, 0.86f, 3700);
	}

	private static WeaponBuildConfig Config(
		string _equippedPrefab,
		string _weaponAsset,
		string _itemAsset,
		string _localizationKey,
		string _descriptionEn,
		CaliberType _caliber,
		WeaponClassType _weaponClass,
		WeaponAttachmentSlotProfile _slotProfile,
		WeaponDistanceCurveLibrary.WeaponBalanceKind _curveKind,
		float _fireRateRpm,
		float _aimTime,
		float _reloadTime,
		float _effectiveRange,
		float _dispersion,
		float _recoilPerShot,
		float _semiRecoil,
		float _autoRecoil,
		float _recoilRecovery,
		float _reliability,
		int _price)
	{
		return new WeaponBuildConfig
		{
			EquippedPrefabFileName = _equippedPrefab,
			WeaponAssetName = _weaponAsset,
			ItemAssetName = _itemAsset,
			LocalizationKey = _localizationKey,
			DescriptionEn = _descriptionEn,
			Caliber = _caliber,
			WeaponClass = _weaponClass,
			SlotProfile = _slotProfile,
			CurveKind = _curveKind,
			FireRateRpm = _fireRateRpm,
			AimTimeSeconds = _aimTime,
			ReloadTimeSeconds = _reloadTime,
			EffectiveRangeMeters = _effectiveRange,
			BaseShotDispersion = _dispersion,
			RecoilPerShot = _recoilPerShot,
			SemiAutoRecoilMultiplier = _semiRecoil,
			AutoRecoilMultiplier = _autoRecoil,
			RecoilRecoveryPerSecond = _recoilRecovery,
			Reliability = _reliability,
			BasePrice = _price
		};
	}

	private static void EnsureFolders()
	{
		EnsureFolder(c_ShootingRoot);
		EnsureFolder(c_InventoryRoot);
		EnsureFolder(c_LootWeaponsRoot);
		EnsureFolder(c_LootAttachmentsRoot);
		EnsureFolder("Assets/Prefabs/World/Loot/Ammo");
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

	private static T LoadAsset<T>(string _path) where T : UnityEngine.Object =>
		AssetDatabase.LoadAssetAtPath<T>(_path);

	private struct WeaponBuildConfig
	{
		public string EquippedPrefabFileName;
		public string WeaponAssetName;
		public string ItemAssetName;
		public string LocalizationKey;
		public string DescriptionEn;
		public CaliberType Caliber;
		public WeaponClassType WeaponClass;
		public WeaponAttachmentSlotProfile SlotProfile;
		public float FireRateRpm;
		public float AimTimeSeconds;
		public float ReloadTimeSeconds;
		public float EffectiveRangeMeters;
		public float BaseShotDispersion;
		public float RecoilPerShot;
		public float SemiAutoRecoilMultiplier;
		public float AutoRecoilMultiplier;
		public float RecoilRecoveryPerSecond;
		public float Reliability;
		public WeaponDistanceCurveLibrary.WeaponBalanceKind CurveKind;
		public int BasePrice;
	}

	private static void CopyReadyHandPoseFields(SerializedObject _so, SerializedObject _templateSo)
	{
		SerializedProperty readyPos = _templateSo.FindProperty("m_RightHandReadyLocalPosition");
		SerializedProperty readyEuler = _templateSo.FindProperty("m_RightHandReadyLocalEulerAngles");
		bool hasReadyPose = readyPos != null && readyEuler != null
		                    && (readyPos.vector3Value != Vector3.zero || readyEuler.vector3Value != Vector3.zero);

		if (hasReadyPose)
		{
			_so.FindProperty("m_RightHandReadyLocalPosition").vector3Value = readyPos.vector3Value;
			_so.FindProperty("m_RightHandReadyLocalEulerAngles").vector3Value = readyEuler.vector3Value;
		}
		else
		{
			_so.FindProperty("m_RightHandReadyLocalPosition").vector3Value =
				_templateSo.FindProperty("m_RightHandLocalPosition").vector3Value;
			_so.FindProperty("m_RightHandReadyLocalEulerAngles").vector3Value =
				_templateSo.FindProperty("m_RightHandLocalEulerAngles").vector3Value;
		}
	}

	private static void CopyRightHandIkFields(SerializedObject _so, SerializedObject _templateSo)
	{
		_so.FindProperty("m_RightHandIkNotReadyLocalPosition").vector3Value =
			_templateSo.FindProperty("m_RightHandIkNotReadyLocalPosition").vector3Value;
		_so.FindProperty("m_RightHandIkNotReadyLocalEulerAngles").vector3Value =
			_templateSo.FindProperty("m_RightHandIkNotReadyLocalEulerAngles").vector3Value;
		_so.FindProperty("m_RightHandIkReadyLocalPosition").vector3Value =
			_templateSo.FindProperty("m_RightHandIkReadyLocalPosition").vector3Value;
		_so.FindProperty("m_RightHandIkReadyLocalEulerAngles").vector3Value =
			_templateSo.FindProperty("m_RightHandIkReadyLocalEulerAngles").vector3Value;
	}

}
#endif
