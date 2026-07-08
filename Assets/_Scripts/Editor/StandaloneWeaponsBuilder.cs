#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class StandaloneWeaponsBuilderBootstrap
{
	private const string c_MarkerPath = "Assets/.standalone_weapons_build_marker";

	static StandaloneWeaponsBuilderBootstrap()
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

			StandaloneWeaponsBuilder.BuildAll();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[StandaloneWeaponsBuilder] Auto-run failed: {exception}");
		}
	}
}

/// <summary>
/// Выпечка WeaponDefinition / ItemDefinition / loot / ammo / magazines для standalone-оружия
/// (Mosin, Benelli M4, M249, 7.62x51 sniper, PKM, SVD).
/// </summary>
public static class StandaloneWeaponsBuilder
{
	#region Constants
	private const string c_EquippedRoot = "Assets/Prefabs/Weapons/Standalone/Equipped";
	private const string c_VisualMagazinesRoot = "Assets/Prefabs/Weapons/Standalone/Visuals/Magazines";
	private const string c_ShootingRoot = "Assets/GameData/Shooting/Standalone";
	private const string c_InventoryRoot = "Assets/GameData/Inventory/Standalone";
	private const string c_LootWeaponsRoot = "Assets/Prefabs/World/Loot/Standalone/Weapons";
	private const string c_LootMagazinesRoot = "Assets/Prefabs/World/Loot/Standalone/Magazines";
	private const string c_LootAmmoRoot = "Assets/Prefabs/World/Loot/Ammo";

	private const string c_TemplateWeaponAkPath = "Assets/GameData/Shooting/AK/Weapon_AK47.asset";
	private const string c_TemplateWeaponRpk47Path = "Assets/GameData/Shooting/AK/Weapon_RPK47.asset";
	private const string c_TemplateWeaponRpk74Path = "Assets/GameData/Shooting/AK/Weapon_RPK74.asset";
	private const string c_TemplateWeaponMk12Path = "Assets/GameData/Shooting/M4/Weapon_MK12.asset";
	private const string c_TemplateWeaponMk18Path = "Assets/GameData/Shooting/M4/Weapon_MK18.asset";
	private const string c_TemplateItemAkPath = "Assets/GameData/Inventory/AK/Item_Weapon_AK47.asset";
	private const string c_TemplateItemMk12Path = "Assets/GameData/Inventory/M4/Item_Weapon_MK12.asset";

	private const string c_VfxProfileAkPath = "Assets/GameData/Shooting/AK/WeaponVfxProfile_AK47.asset";
	private const string c_VfxProfileM4Path = "Assets/GameData/Shooting/M4/WeaponVfxProfile_M4.asset";

	private const string c_Ammo762TemplatePath = "Assets/GameData/Shooting/Ammo_762x39mm.asset";
	private const string c_Ammo556TemplatePath = "Assets/GameData/Shooting/Ammo_556x45mmNATO.asset";
	private const string c_Ammo762LootTemplatePath = "Assets/Prefabs/World/Loot/Ammo/Loot_AmmoBox_762x39.prefab";

	private const string c_MosinScopeAttachmentPath = "Assets/GameData/Shooting/Mosin/Attachment_Mosin_Scope8.asset";

	private const string c_MagChildMosin = "SM_Wep_Rifle_Mag_01";
	private const string c_MagChildM249 = "SM_Wep_MachineGun_USA_Mag_01";
	private const string c_MagChildSniper = "SM_Wep_Sniper_Mag_01";
	private const string c_MagChildPkm = "SM_Wep_MachineGun_Bandit_Mag_01";
	private const string c_MagChildSvd = "SM_Wep_Preset_B_Sniper_01_Mag";

	private const string c_SourceMosinPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Rifle_01.prefab";
	private const string c_SourceBenelliPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Shotgun_01.prefab";
	private const string c_SourceM249Path = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_MachineGun_USA_01.prefab";
	private const string c_SourceSniperPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_Sniper_01.prefab";
	private const string c_SourcePkmPath = "Assets/PolygonMilitary/Prefabs/Weapons/SM_Wep_MachineGun_Bandit_01.prefab";
	private const string c_SourceSvdPath = "Assets/PolygonMilitary/Prefabs/Weapons/Modular_Presets/SM_Wep_Preset_B_Sniper_01.prefab";
	#endregion

	#region Slot Layouts
	private static readonly WeaponAttachmentSlotType[] s_OpticOnlySlots =
	{
		WeaponAttachmentSlotType.Optic
	};

	private static readonly WeaponAttachmentSlotType[] s_MuzzleOpticSlots =
	{
		WeaponAttachmentSlotType.Muzzle,
		WeaponAttachmentSlotType.Optic
	};

	private static readonly WeaponAttachmentSlotType[] s_StockAkSlots =
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

	private static readonly WeaponAttachmentSlotType[] s_TacticalFullSlots =
	{
		WeaponAttachmentSlotType.Muzzle,
		WeaponAttachmentSlotType.Optic,
		WeaponAttachmentSlotType.Stock,
		WeaponAttachmentSlotType.UnderBarrel,
		WeaponAttachmentSlotType.Rail,
		WeaponAttachmentSlotType.Rail,
		WeaponAttachmentSlotType.Rail
	};

	private static readonly WeaponAttachmentSlotType[] s_MuzzleOnlySlots =
	{
		WeaponAttachmentSlotType.Muzzle
	};
	#endregion

	[MenuItem("Tools/Standalone Weapons/Build All Standalone Weapons")]
	public static void BuildAllFromMenu()
	{
		try
		{
			BuildAll();
			EditorUtility.DisplayDialog("Standalone Weapons", "Готово: Mosin, Benelli M4, M249, Sniper 7.62x51, PKM, SVD.", "OK");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("Standalone Weapons", exception.Message, "OK");
		}
	}

	/// <summary>Batch: -executeMethod StandaloneWeaponsBuilder.RunBatch</summary>
	public static void RunBatch()
	{
		try
		{
			BuildAll();
			Debug.Log("[StandaloneWeaponsBuilder] Batch complete.");
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

		WeaponDefinition templateAk = LoadAsset<WeaponDefinition>(c_TemplateWeaponAkPath);
		WeaponDefinition templateRpk47 = LoadAsset<WeaponDefinition>(c_TemplateWeaponRpk47Path);
		WeaponDefinition templateRpk74 = LoadAsset<WeaponDefinition>(c_TemplateWeaponRpk74Path);
		WeaponDefinition templateMk12 = LoadAsset<WeaponDefinition>(c_TemplateWeaponMk12Path);
		WeaponDefinition templateMk18 = LoadAsset<WeaponDefinition>(c_TemplateWeaponMk18Path);
		ItemDefinition templateItemAk = LoadAsset<ItemDefinition>(c_TemplateItemAkPath);
		ItemDefinition templateItemMk12 = LoadAsset<ItemDefinition>(c_TemplateItemMk12Path);

		if (templateAk == null || templateRpk47 == null || templateRpk74 == null ||
		    templateMk12 == null || templateMk18 == null || templateItemAk == null || templateItemMk12 == null)
			throw new InvalidOperationException("Missing template weapon or item assets.");

		BuildAmmoTypes();

		var builtWeapons = new List<WeaponDefinition>();
		foreach (WeaponBuildConfig config in GetWeaponConfigs())
		{
			GameObject equippedPrefab = BuildEquippedPrefab(config);
			WeaponDefinition template = ResolveTemplate(config.TemplateKind, templateAk, templateRpk47, templateRpk74, templateMk12, templateMk18);
			ItemDefinition templateItem = config.UseMk12ItemTemplate ? templateItemMk12 : templateItemAk;

			WeaponDefinition weapon = BuildWeaponDefinition(config, template);
			ItemDefinition item = BuildItemDefinition(config, equippedPrefab, weapon, templateItem);
			BuildLootForItem(item, $"{c_LootWeaponsRoot}/Loot_{config.ItemAssetName}.prefab", equippedPrefab);
			builtWeapons.Add(weapon);
		}

		BuildMagazines(builtWeapons);
		WireBenelliBuiltInMagazine();
		UpdateMosinScopeCompatibility(builtWeapons);
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[StandaloneWeaponsBuilder] Built {builtWeapons.Count} standalone weapons.");
	}

	#region Ammo
	private static void BuildAmmoTypes()
	{
		BuildAmmoFromTemplate(
			"Ammo_12Gauge",
			c_Ammo762TemplatePath,
			CaliberType.TwelveGauge,
			12f, 4f, 3f,
			9, 400f, 40f,
			1.35f, 1.20f);

		BuildAmmoFromTemplate(
			"Ammo_762x51mmNATO",
			c_Ammo556TemplatePath,
			CaliberType.Seven62By51,
			55f, 28f, 14f,
			1, 850f, 500f,
			0.92f, 1.15f);

		BuildAmmoFromTemplate(
			"Ammo_762x54mmR",
			c_Ammo762TemplatePath,
			CaliberType.Seven62By54R,
			58f, 26f, 13f,
			1, 830f, 500f,
			0.95f, 1.18f);

		BuildAmmoBox(
			"Ammo_12Gauge",
			"Item_Loot_AmmoBox_12Gauge",
			"item.loot.ammo_box.12g",
			"12 gauge buckshot ammo box.",
			"Loot_AmmoBox_12Gauge",
			80, 40);

		BuildAmmoBox(
			"Ammo_762x51mmNATO",
			"Item_Loot_AmmoBox_762x51",
			"item.loot.ammo_box.762x51",
			"7.62x51 NATO ammo box.",
			"Loot_AmmoBox_762x51",
			100, 40);

		BuildAmmoBox(
			"Ammo_762x54mmR",
			"Item_Loot_AmmoBox_762x54R",
			"item.loot.ammo_box.762x54r",
			"7.62x54R ammo box.",
			"Loot_AmmoBox_762x54R",
			100, 40);
	}

	private static void BuildAmmoFromTemplate(
		string _assetName,
		string _templatePath,
		CaliberType _caliber,
		float _damage,
		float _penetration,
		float _armorDamage,
		int _projectileCount,
		float _velocity,
		float _effectiveRange,
		float _spreadModifier,
		float _recoilModifier)
	{
		AmmoDefinition template = LoadAsset<AmmoDefinition>(_templatePath);
		if (template == null)
			throw new InvalidOperationException($"Missing ammo template: {_templatePath}");

		string path = $"Assets/GameData/Shooting/{_assetName}.asset";
		bool isNew = !File.Exists(path);
		AmmoDefinition ammo = GetOrCreateAsset<AmmoDefinition>(path, _assetName);
		if (isNew)
			EditorUtility.CopySerialized(template, ammo);

		var so = new SerializedObject(ammo);
		so.FindProperty("m_Caliber").enumValueIndex = (int)_caliber;
		so.FindProperty("m_BaseDamage").floatValue = _damage;
		so.FindProperty("m_Penetration").floatValue = _penetration;
		so.FindProperty("m_ArmorDamage").floatValue = _armorDamage;
		so.FindProperty("m_ProjectileCount").intValue = _projectileCount;
		so.FindProperty("m_Velocity").floatValue = _velocity;
		so.FindProperty("m_EffectiveRangeMeters").floatValue = _effectiveRange;
		so.FindProperty("m_SpreadModifier").floatValue = _spreadModifier;
		so.FindProperty("m_RecoilModifier").floatValue = _recoilModifier;
		so.ApplyModifiedPropertiesWithoutUndo();
		ammo.name = _assetName;
		EditorUtility.SetDirty(ammo);
	}

	private static void BuildAmmoBox(
		string _ammoAssetName,
		string _itemAssetName,
		string _localizationKey,
		string _description,
		string _lootPrefabName,
		int _initialAmmoCount,
		int _price)
	{
		AmmoDefinition ammo = LoadAsset<AmmoDefinition>($"Assets/GameData/Shooting/{_ammoAssetName}.asset");
		if (ammo == null)
			throw new InvalidOperationException($"Missing ammo asset: {_ammoAssetName}");

		string itemPath = $"{c_InventoryRoot}/{_itemAssetName}.asset";
		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(itemPath, _itemAssetName);
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		itemSo.FindProperty("m_Description").stringValue = _description;
		itemSo.FindProperty("m_BasePrice").intValue = _price;
		itemSo.FindProperty("m_Category").enumValueIndex = 0;
		itemSo.FindProperty("m_AmmoDefinition").objectReferenceValue = ammo;
		itemSo.FindProperty("m_InitialAmmoCount").intValue = _initialAmmoCount;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);

		GameObject sourceLoot = LoadAsset<GameObject>(c_Ammo762LootTemplatePath);
		if (sourceLoot == null)
			throw new InvalidOperationException($"Missing ammo loot template: {c_Ammo762LootTemplatePath}");

		string lootPath = $"{c_LootAmmoRoot}/{_lootPrefabName}.prefab";
		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceLoot);
		try
		{
			instance.name = _lootPrefabName;
			WorldPickupItem pickup = instance.GetComponent<WorldPickupItem>();
			if (pickup != null)
			{
				var pickupSo = new SerializedObject(pickup);
				pickupSo.FindProperty("m_Definition").objectReferenceValue = item;
				pickupSo.ApplyModifiedPropertiesWithoutUndo();
			}

			GameObject lootPrefab = PrefabUtility.SaveAsPrefabAsset(instance, lootPath);
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
	#endregion

	#region Magazines
	private static void BuildMagazines(IReadOnlyList<WeaponDefinition> _builtWeapons)
	{
		BuildMagazine(
			"Mag_Visual_Mosin_Internal_5",
			"Magazine_Mosin_762_54R_5",
			"Item_Mag_Mosin_762_54R_5",
			"item.mag.mosin_762_54r_5",
			"5-round internal magazine for Mosin rifle.",
			CaliberType.Seven62By54R,
			MagazineType.Internal,
			5, 60, 1.15f, c_SourceMosinPath, c_MagChildMosin);

		BuildMagazine(
			"Mag_Visual_M249_556_200",
			"Magazine_M249_556_200",
			"Item_Mag_M249_556_200",
			"item.mag.m249_556_200",
			"200-round belt box for M249 SAW.",
			CaliberType.Five56By45,
			MagazineType.Drum,
			200, 420, 1.35f, c_SourceM249Path, c_MagChildM249);

		BuildMagazine(
			"Mag_Visual_Sniper_762x51_10",
			"Magazine_Sniper_762x51_10",
			"Item_Mag_Sniper_762x51_10",
			"item.mag.sniper_762x51_10",
			"10-round 7.62x51 NATO magazine.",
			CaliberType.Seven62By51,
			MagazineType.RifleStandard,
			10, 140, 1.08f, c_SourceSniperPath, c_MagChildSniper);

		BuildMagazine(
			"Mag_Visual_PKM_762_54R_100",
			"Magazine_PKM_762_54R_100",
			"Item_Mag_PKM_762_54R_100",
			"item.mag.pkm_762_54r_100",
			"100-round belt box for PKM.",
			CaliberType.Seven62By54R,
			MagazineType.Drum,
			100, 380, 1.30f, c_SourcePkmPath, c_MagChildPkm);

		BuildMagazine(
			"Mag_Visual_SVD_762_54R_10",
			"Magazine_SVD_762_54R_10",
			"Item_Mag_SVD_762_54R_10",
			"item.mag.svd_762_54r_10",
			"10-round 7.62x54R SVD magazine.",
			CaliberType.Seven62By54R,
			MagazineType.RifleStandard,
			10, 120, 1.05f, c_SourceSvdPath, c_MagChildSvd);
	}

	private static void BuildMagazine(
		string _visualPrefabName,
		string _magazineAssetName,
		string _itemAssetName,
		string _localizationKey,
		string _description,
		CaliberType _caliber,
		MagazineType _magazineType,
		int _capacity,
		int _price,
		float _reloadModifier,
		string _weaponSourcePath,
		string _magChildName,
		bool _isNonRemovable = false,
		bool _buildLoot = true)
	{
		GameObject visual = BuildMagazineVisualFromWeaponMesh(_visualPrefabName, _weaponSourcePath, _magChildName);

		string magazinePath = $"{c_ShootingRoot}/{_magazineAssetName}.asset";
		MagazineDefinition magazine = GetOrCreateAsset<MagazineDefinition>(magazinePath, _magazineAssetName);
		var magSo = new SerializedObject(magazine);
		magSo.FindProperty("m_MagazineType").enumValueIndex = (int)_magazineType;
		magSo.FindProperty("m_SupportedCaliber").enumValueIndex = (int)_caliber;
		magSo.FindProperty("m_Capacity").intValue = _capacity;
		magSo.FindProperty("m_RoundLoadTimeSeconds").floatValue = 0.35f;
		magSo.FindProperty("m_ReloadTimeModifier").floatValue = _reloadModifier;
		magSo.FindProperty("m_JamRiskModifier").floatValue = _magazineType == MagazineType.Drum ? 1.08f : 1f;
		magSo.FindProperty("m_IsNonRemovable").boolValue = _isNonRemovable;
		magSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(magazine);

		string itemPath = $"{c_InventoryRoot}/{_itemAssetName}.asset";
		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(itemPath, _itemAssetName);
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		itemSo.FindProperty("m_Description").stringValue = _description;
		itemSo.FindProperty("m_BasePrice").intValue = _price;
		itemSo.FindProperty("m_Category").enumValueIndex = 0;
		itemSo.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _isNonRemovable ? null : visual;
		itemSo.FindProperty("m_MagazineDefinition").objectReferenceValue = magazine;
		if (!_buildLoot)
			itemSo.FindProperty("m_DropWorldPrefab").objectReferenceValue = null;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);

		if (_buildLoot)
			BuildLootForItem(item, $"{c_LootMagazinesRoot}/Loot_{_itemAssetName}.prefab", visual);
	}

	private static GameObject BuildMagazineVisualFromWeaponMesh(
		string _visualPrefabName,
		string _weaponSourcePath,
		string _magChildName)
	{
		string path = $"{c_VisualMagazinesRoot}/{_visualPrefabName}.prefab";
		GameObject root = new GameObject(_visualPrefabName);
		try
		{
			if (!string.IsNullOrEmpty(_weaponSourcePath) && !string.IsNullOrEmpty(_magChildName))
			{
				GameObject weaponSource = LoadAsset<GameObject>(_weaponSourcePath);
				if (weaponSource == null)
					throw new InvalidOperationException($"Missing weapon source for magazine visual: {_weaponSourcePath}");

				GameObject weaponInstance = InstantiateUnpackedPrefab(weaponSource);
				try
				{
					Transform magTransform = FindChildTransformByName(weaponInstance.transform, _magChildName);
					if (magTransform == null)
						throw new InvalidOperationException($"Mag child '{_magChildName}' not found in '{_weaponSourcePath}'.");

					GameObject magClone = UnityEngine.Object.Instantiate(magTransform.gameObject);
					magClone.name = _magChildName;
					magClone.transform.SetParent(root.transform, false);
					magClone.transform.localPosition = Vector3.zero;
					magClone.transform.localRotation = Quaternion.identity;
					magClone.transform.localScale = Vector3.one;
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(weaponInstance);
				}
			}

			EnsureFolder(c_VisualMagazinesRoot);
			return PrefabUtility.SaveAsPrefabAsset(root, path);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}
	#endregion

	#region Equipped Prefabs
	private static GameObject BuildEquippedPrefab(WeaponBuildConfig _config)
	{
		string equippedPath = $"{c_EquippedRoot}/{_config.EquippedPrefabFileName}.prefab";

		GameObject source = LoadAsset<GameObject>(_config.SourcePrefabPath);
		if (source == null)
			throw new InvalidOperationException($"Missing source model prefab: {_config.SourcePrefabPath}");

		GameObject instance = InstantiateUnpackedPrefab(source);
		try
		{
			instance.name = _config.EquippedPrefabFileName;
			EquippedWeaponHierarchySetup.ApplyToRoot(instance);
			AlignMagazineSocketFromBuiltInMag(instance, _config.SourceMagChildName);
			EnsureFolder(c_EquippedRoot);
			return PrefabUtility.SaveAsPrefabAsset(instance, equippedPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(instance);
		}
	}
	#endregion

	#region Weapon / Item
	private static WeaponDefinition BuildWeaponDefinition(WeaponBuildConfig _config, WeaponDefinition _template)
	{
		string path = $"{c_ShootingRoot}/{_config.WeaponAssetName}.asset";
		bool isNew = !File.Exists(path);
		WeaponDefinition weapon = GetOrCreateAsset<WeaponDefinition>(path, _config.WeaponAssetName);
		if (isNew)
			EditorUtility.CopySerialized(_template, weapon);

		weapon.name = _config.WeaponAssetName;
		var so = new SerializedObject(weapon);
		so.FindProperty("m_WeaponClass").enumValueIndex = (int)_config.WeaponClass;
		so.FindProperty("m_SupportedCaliber").enumValueIndex = (int)_config.Caliber;
		so.FindProperty("m_SupportedMagazineType").enumValueIndex = (int)_config.MagazineType;
		so.FindProperty("m_AttachmentSlotProfile").enumValueIndex = (int)_config.SlotProfile;
		WriteAttachmentSlots(so.FindProperty("m_AttachmentSlots"), _config.SlotLayout);
		WriteFireModes(so.FindProperty("m_AvailableFireModes"), _config.FireModes);
		so.FindProperty("m_DefaultFireMode").enumValueIndex = (int)_config.DefaultFireMode;
		so.FindProperty("m_FireRateRpm").floatValue = _config.FireRateRpm;
		so.FindProperty("m_SemiAutoFireRateRpm").floatValue = _config.SemiAutoFireRateRpm;
		so.FindProperty("m_AimTimeSeconds").floatValue = _config.AimTimeSeconds;
		so.FindProperty("m_ReloadTimeSeconds").floatValue = _config.ReloadTimeSeconds;
		so.FindProperty("m_EffectiveRangeMeters").floatValue = _config.EffectiveRangeMeters;
		so.FindProperty("m_BaseShotDispersion").floatValue = _config.BaseShotDispersion;
		so.FindProperty("m_RecoilPerShot").floatValue = _config.RecoilPerShot;
		so.FindProperty("m_SemiAutoRecoilMultiplier").floatValue = _config.SemiAutoRecoilMultiplier;
		so.FindProperty("m_AutoRecoilMultiplier").floatValue = _config.AutoRecoilMultiplier;
		so.FindProperty("m_RecoilRecoveryPerSecond").floatValue = _config.RecoilRecoveryPerSecond;
		so.FindProperty("m_Reliability").floatValue = _config.Reliability;
		so.FindProperty("m_HasBoltHoldOpenDelay").boolValue = _config.HasBoltHoldOpenDelay;
		so.FindProperty("m_VfxProfile").objectReferenceValue = LoadAsset<WeaponVfxProfile>(_config.VfxProfilePath);

		if (_config.TemplateKind == WeaponTemplateKind.Mosin)
			ApplyMosinFireAudio(so);

		ApplyBalanceCurves(so, _config.CurveKind);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
		return weapon;
	}

	private static void ApplyMosinFireAudio(SerializedObject _weaponSo)
	{
		AudioClip[] clips =
		{
			LoadAsset<AudioClip>("Assets/SFX/mosin/Gun 08/Gun 8_1.wav"),
			LoadAsset<AudioClip>("Assets/SFX/mosin/Gun 08/Gun 8_2.wav"),
			LoadAsset<AudioClip>("Assets/SFX/mosin/Gun 08/Gun 8_3.wav"),
			LoadAsset<AudioClip>("Assets/SFX/mosin/Gun 08/Gun 8_4.wav"),
			LoadAsset<AudioClip>("Assets/SFX/mosin/Gun 08/Gun 8_5.wav")
		};

		SerializedProperty fireProfile = _weaponSo.FindProperty("m_FireSoundProfile");
		if (fireProfile == null)
			return;

		SerializedProperty clipsProp = fireProfile.FindPropertyRelative("m_FireClips");
		if (clipsProp == null)
			return;
		int validCount = 0;
		for (int i = 0; i < clips.Length; i++)
		{
			if (clips[i] != null)
				validCount++;
		}

		clipsProp.arraySize = validCount;
		int writeIndex = 0;
		for (int i = 0; i < clips.Length; i++)
		{
			if (clips[i] == null)
				continue;

			clipsProp.GetArrayElementAtIndex(writeIndex).objectReferenceValue = clips[i];
			writeIndex++;
		}
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
		so.FindProperty("m_WeightKg").floatValue = _config.WeightKg;
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

	private static void WireBenelliBuiltInMagazine()
	{
		WeaponDefinition weapon = LoadAsset<WeaponDefinition>($"{c_ShootingRoot}/Weapon_BenelliM4.asset");
		AmmoDefinition ammo12Gauge = LoadAsset<AmmoDefinition>($"{c_ShootingRoot}/Ammo_12Gauge.asset");
		if (weapon == null)
			return;

		string magazinePath = $"{c_ShootingRoot}/Magazine_Benelli_12G_7.asset";
		MagazineDefinition magazine = GetOrCreateAsset<MagazineDefinition>(magazinePath, "Magazine_Benelli_12G_7");
		var magSo = new SerializedObject(magazine);
		magSo.FindProperty("m_MagazineType").enumValueIndex = (int)MagazineType.Internal;
		magSo.FindProperty("m_SupportedCaliber").enumValueIndex = (int)CaliberType.TwelveGauge;
		magSo.FindProperty("m_Capacity").intValue = 7;
		magSo.FindProperty("m_RoundLoadTimeSeconds").floatValue = 0.35f;
		magSo.FindProperty("m_ReloadTimeModifier").floatValue = 1.05f;
		magSo.FindProperty("m_JamRiskModifier").floatValue = 1f;
		magSo.FindProperty("m_IsNonRemovable").boolValue = true;
		magSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(magazine);

		var so = new SerializedObject(weapon);
		so.FindProperty("m_UsesShellByShellReload").boolValue = true;
		so.FindProperty("m_BuiltInMagazineDefinition").objectReferenceValue = magazine;
		so.FindProperty("m_BuiltInMagazineDefaultAmmo").objectReferenceValue = ammo12Gauge;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
	}

	private static void UpdateMosinScopeCompatibility(IReadOnlyList<WeaponDefinition> _builtWeapons)
	{
		WeaponDefinition mosin = null;
		for (int i = 0; i < _builtWeapons.Count; i++)
		{
			if (_builtWeapons[i] != null && _builtWeapons[i].name == "Weapon_Mosin")
			{
				mosin = _builtWeapons[i];
				break;
			}
		}

		if (mosin == null)
			return;

		WeaponAttachmentDefinition scope = LoadAsset<WeaponAttachmentDefinition>(c_MosinScopeAttachmentPath);
		if (scope == null)
		{
			Debug.LogWarning("[StandaloneWeaponsBuilder] Missing Mosin scope attachment.");
			return;
		}

		var so = new SerializedObject(scope);
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weapons = so.FindProperty("m_CompatibleWeapons");
		weapons.arraySize = 1;
		weapons.GetArrayElementAtIndex(0).objectReferenceValue = mosin;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(scope);
	}
	#endregion

	#region Weapon Configs
	private static IEnumerable<WeaponBuildConfig> GetWeaponConfigs()
	{
		WeaponFireMode[] semiOnly = { WeaponFireMode.SemiAuto };
		WeaponFireMode[] fullAutoOnly = { WeaponFireMode.FullAuto, WeaponFireMode.SemiAuto };

		yield return Config(
			"Equipped_Mosin", "Weapon_Mosin", "Item_Weapon_Mosin",
			c_SourceMosinPath, c_MagChildMosin, WeaponTemplateKind.Mosin,
			"item.weapon.mosin", "Mosin-Nagant bolt-action rifle chambered in 7.62x54R.",
			CaliberType.Seven62By54R, MagazineType.Internal, WeaponClassType.Rifle,
			SlotLayout.OpticOnly, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Dmr,
			semiOnly, WeaponFireMode.SemiAuto,
			50f, 0.55f, 3.20f, 200f, 0.48f,
			0.42f, 0.78f, 1.00f, 4.2f, 0.92f,
			1800, 4.5f, c_VfxProfileAkPath, false);

		yield return Config(
			"Equipped_BenelliM4", "Weapon_BenelliM4", "Item_Weapon_BenelliM4",
			c_SourceBenelliPath, null, WeaponTemplateKind.Mk18,
			"item.weapon.benelli_m4", "Benelli M4 semi-automatic 12 gauge shotgun.",
			CaliberType.TwelveGauge, MagazineType.Internal, WeaponClassType.Shotgun,
			SlotLayout.OpticOnly, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbShort,
			semiOnly, WeaponFireMode.SemiAuto,
			120f, 0.30f, 0.85f, 35f, 2.80f,
			0.72f, 0.90f, 1.00f, 3.5f, 0.88f,
			3200, 3.6f, c_VfxProfileM4Path, false);

		yield return Config(
			"Equipped_M249", "Weapon_M249", "Item_Weapon_M249",
			c_SourceM249Path, c_MagChildM249, WeaponTemplateKind.Rpk74,
			"item.weapon.m249", "M249 SAW light machine gun chambered in 5.56x45 NATO.",
			CaliberType.Five56By45, MagazineType.Drum, WeaponClassType.LightMachineGun,
			SlotLayout.TacticalFull, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support545,
			fullAutoOnly, WeaponFireMode.FullAuto,
			750f, 0.48f, 4.50f, 140f, 1.05f,
			0.50f, 0.86f, 1.10f, 4.5f, 0.82f,
			4800, 7.5f, c_VfxProfileM4Path, true);

		yield return Config(
			"Equipped_Sniper762x51", "Weapon_Sniper762x51", "Item_Weapon_Sniper762x51",
			c_SourceSniperPath, c_MagChildSniper, WeaponTemplateKind.Mk12,
			"item.weapon.sniper_762x51", "Bolt-action precision rifle chambered in 7.62x51 NATO.",
			CaliberType.Seven62By51, MagazineType.RifleStandard, WeaponClassType.Rifle,
			SlotLayout.MuzzleOptic, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Dmr,
			semiOnly, WeaponFireMode.SemiAuto,
			40f, 0.58f, 3.00f, 220f, 0.42f,
			0.40f, 0.76f, 1.00f, 4.5f, 0.90f,
			4200, 5.8f, c_VfxProfileM4Path, true, true);

		yield return Config(
			"Equipped_PKM", "Weapon_PKM", "Item_Weapon_PKM",
			c_SourcePkmPath, c_MagChildPkm, WeaponTemplateKind.Rpk47,
			"item.weapon.pkm", "PKM general-purpose machine gun chambered in 7.62x54R.",
			CaliberType.Seven62By54R, MagazineType.Drum, WeaponClassType.LightMachineGun,
			SlotLayout.StockAk, WeaponAttachmentSlotProfile.StockAk,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support762,
			fullAutoOnly, WeaponFireMode.FullAuto,
			650f, 0.50f, 5.00f, 150f, 1.10f,
			0.52f, 0.88f, 1.12f, 4.2f, 0.80f,
			4500, 8.2f, c_VfxProfileAkPath, false);

		yield return Config(
			"Equipped_SVD", "Weapon_SVD", "Item_Weapon_SVD",
			c_SourceSvdPath, c_MagChildSvd, WeaponTemplateKind.Mk12,
			"item.weapon.svd", "SVD semi-automatic marksman rifle chambered in 7.62x54R.",
			CaliberType.Seven62By54R, MagazineType.RifleStandard, WeaponClassType.Rifle,
			SlotLayout.TacticalFull, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Marksman,
			semiOnly, WeaponFireMode.SemiAuto,
			30f, 0.45f, 2.80f, 180f, 0.52f,
			0.44f, 0.80f, 1.00f, 4.4f, 0.86f,
			3900, 4.3f, c_VfxProfileAkPath, false, true);
	}

	private static WeaponBuildConfig Config(
		string _equippedPrefab,
		string _weaponAsset,
		string _itemAsset,
		string _sourcePrefabPath,
		string _sourceMagChildName,
		WeaponTemplateKind _templateKind,
		string _localizationKey,
		string _descriptionEn,
		CaliberType _caliber,
		MagazineType _magazineType,
		WeaponClassType _weaponClass,
		SlotLayout _slotLayout,
		WeaponAttachmentSlotProfile _slotProfile,
		WeaponDistanceCurveLibrary.WeaponBalanceKind _curveKind,
		WeaponFireMode[] _fireModes,
		WeaponFireMode _defaultFireMode,
		float _semiAutoFireRateRpm,
		float _aimTime,
		float _reloadTime,
		float _effectiveRange,
		float _dispersion,
		float _recoilPerShot,
		float _semiRecoil,
		float _autoRecoil,
		float _recoilRecovery,
		float _reliability,
		int _price,
		float _weightKg,
		string _vfxProfilePath,
		bool _hasBoltHoldOpenDelay,
		bool _useMk12ItemTemplate = false)
	{
		return new WeaponBuildConfig
		{
			EquippedPrefabFileName = _equippedPrefab,
			WeaponAssetName = _weaponAsset,
			ItemAssetName = _itemAsset,
			SourcePrefabPath = _sourcePrefabPath,
			SourceMagChildName = _sourceMagChildName,
			TemplateKind = _templateKind,
			LocalizationKey = _localizationKey,
			DescriptionEn = _descriptionEn,
			Caliber = _caliber,
			MagazineType = _magazineType,
			WeaponClass = _weaponClass,
			SlotLayout = _slotLayout,
			SlotProfile = _slotProfile,
			CurveKind = _curveKind,
			FireModes = _fireModes,
			DefaultFireMode = _defaultFireMode,
			FireRateRpm = _semiAutoFireRateRpm > 0f ? _semiAutoFireRateRpm : 600f,
			SemiAutoFireRateRpm = _semiAutoFireRateRpm,
			AimTimeSeconds = _aimTime,
			ReloadTimeSeconds = _reloadTime,
			EffectiveRangeMeters = _effectiveRange,
			BaseShotDispersion = _dispersion,
			RecoilPerShot = _recoilPerShot,
			SemiAutoRecoilMultiplier = _semiRecoil,
			AutoRecoilMultiplier = _autoRecoil,
			RecoilRecoveryPerSecond = _recoilRecovery,
			Reliability = _reliability,
			BasePrice = _price,
			WeightKg = _weightKg,
			VfxProfilePath = _vfxProfilePath,
			HasBoltHoldOpenDelay = _hasBoltHoldOpenDelay,
			UseMk12ItemTemplate = _useMk12ItemTemplate
		};
	}
	#endregion

	#region Helpers
	private static WeaponDefinition ResolveTemplate(
		WeaponTemplateKind _kind,
		WeaponDefinition _ak,
		WeaponDefinition _rpk47,
		WeaponDefinition _rpk74,
		WeaponDefinition _mk12,
		WeaponDefinition _mk18)
	{
		return _kind switch
		{
			WeaponTemplateKind.Mosin => _ak,
			WeaponTemplateKind.Rpk47 => _rpk47,
			WeaponTemplateKind.Rpk74 => _rpk74,
			WeaponTemplateKind.Mk12 => _mk12,
			WeaponTemplateKind.Mk18 => _mk18,
			_ => _ak
		};
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

	private static void WriteAttachmentSlots(SerializedProperty _slots, SlotLayout _layout)
	{
		WeaponAttachmentSlotType[] slotTypes = _layout switch
		{
			SlotLayout.OpticOnly => s_OpticOnlySlots,
			SlotLayout.MuzzleOptic => s_MuzzleOpticSlots,
			SlotLayout.StockAk => s_StockAkSlots,
			SlotLayout.TacticalFull => s_TacticalFullSlots,
			SlotLayout.MuzzleOnly => s_MuzzleOnlySlots,
			_ => s_TacticalFullSlots
		};

		_slots.arraySize = slotTypes.Length;
		for (int i = 0; i < slotTypes.Length; i++)
		{
			SerializedProperty slot = _slots.GetArrayElementAtIndex(i);
			slot.FindPropertyRelative("SlotType").enumValueIndex = (int)slotTypes[i];
			slot.FindPropertyRelative("IsRequired").boolValue = false;
			slot.FindPropertyRelative("AnchorChildName").stringValue = string.Empty;
		}
	}

	private static void WriteFireModes(SerializedProperty _fireModes, WeaponFireMode[] _modes)
	{
		_fireModes.arraySize = _modes != null ? _modes.Length : 0;
		for (int i = 0; i < _fireModes.arraySize; i++)
			_fireModes.GetArrayElementAtIndex(i).enumValueIndex = (int)_modes[i];
	}

	private static GameObject BuildLootForItem(ItemDefinition _item, string _lootPath, GameObject _visualPrefab)
	{
		GameObject root = new GameObject(Path.GetFileNameWithoutExtension(_lootPath));
		try
		{
			root.layer = LayerMask.NameToLayer("Loot");
			if (_visualPrefab != null)
			{
				GameObject visual = InstantiateUnpackedPrefab(_visualPrefab);
				visual.transform.SetParent(root.transform, false);
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = Quaternion.identity;
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

	private static void EnsureFolders()
	{
		EnsureFolder(c_EquippedRoot);
		EnsureFolder(c_VisualMagazinesRoot);
		EnsureFolder(c_ShootingRoot);
		EnsureFolder(c_InventoryRoot);
		EnsureFolder(c_LootWeaponsRoot);
		EnsureFolder(c_LootMagazinesRoot);
		EnsureFolder(c_LootAmmoRoot);
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

	/// <summary>
	/// Клонирует префаб и полностью отвязывает иерархию от исходного prefab asset
	/// (без nested PrefabInstance / m_SourcePrefab).
	/// </summary>
	private static GameObject InstantiateUnpackedPrefab(GameObject _prefab)
	{
		if (_prefab == null)
			return null;

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(_prefab);
		UnpackPrefabHierarchyCompletely(instance);
		return instance;
	}

	private static void UnpackPrefabHierarchyCompletely(GameObject _root)
	{
		if (_root == null)
			return;

		Transform[] transforms = _root.GetComponentsInChildren<Transform>(true);
		for (int i = transforms.Length - 1; i >= 0; i--)
		{
			GameObject go = transforms[i].gameObject;
			if (!PrefabUtility.IsPartOfPrefabInstance(go))
				continue;

			GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
			if (instanceRoot != go)
				continue;

			PrefabUtility.UnpackPrefabInstance(
				go,
				PrefabUnpackMode.Completely,
				InteractionMode.AutomatedAction);
		}
	}

	private static Transform FindChildTransformByName(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i].name == _name)
				return children[i];
		}

		return null;
	}

	private static void AlignMagazineSocketFromBuiltInMag(GameObject _weaponRoot, string _magChildName)
	{
		if (_weaponRoot == null || string.IsNullOrEmpty(_magChildName))
			return;

		Transform magazineSocket = _weaponRoot.transform.Find("MagazineSocket");
		if (magazineSocket == null)
		{
			Debug.LogWarning($"[StandaloneWeaponsBuilder] MagazineSocket missing on '{_weaponRoot.name}'.");
			return;
		}

		Transform visualRoot = _weaponRoot.transform.Find("Visual");
		Transform magTransform = visualRoot != null
			? FindChildTransformByName(visualRoot, _magChildName)
			: FindChildTransformByName(_weaponRoot.transform, _magChildName);
		if (magTransform == null)
		{
			Debug.LogWarning($"[StandaloneWeaponsBuilder] Built-in mag '{_magChildName}' not found on '{_weaponRoot.name}'.");
			return;
		}

		Transform weaponTransform = _weaponRoot.transform;
		magazineSocket.SetPositionAndRotation(magTransform.position, magTransform.rotation);
		magazineSocket.localScale = Vector3.one;
		magTransform.gameObject.SetActive(false);
	}
	#endregion

	#region Nested Types
	private enum WeaponTemplateKind
	{
		Mosin,
		Rpk47,
		Rpk74,
		Mk12,
		Mk18
	}

	private enum SlotLayout
	{
		OpticOnly,
		MuzzleOptic,
		StockAk,
		TacticalFull,
		MuzzleOnly
	}

	private struct WeaponBuildConfig
	{
		public string EquippedPrefabFileName;
		public string WeaponAssetName;
		public string ItemAssetName;
		public string SourcePrefabPath;
		public string SourceMagChildName;
		public WeaponTemplateKind TemplateKind;
		public string LocalizationKey;
		public string DescriptionEn;
		public CaliberType Caliber;
		public MagazineType MagazineType;
		public WeaponClassType WeaponClass;
		public SlotLayout SlotLayout;
		public WeaponAttachmentSlotProfile SlotProfile;
		public WeaponDistanceCurveLibrary.WeaponBalanceKind CurveKind;
		public WeaponFireMode[] FireModes;
		public WeaponFireMode DefaultFireMode;
		public float FireRateRpm;
		public float SemiAutoFireRateRpm;
		public float AimTimeSeconds;
		public float ReloadTimeSeconds;
		public float EffectiveRangeMeters;
		public float BaseShotDispersion;
		public float RecoilPerShot;
		public float SemiAutoRecoilMultiplier;
		public float AutoRecoilMultiplier;
		public float RecoilRecoveryPerSecond;
		public float Reliability;
		public int BasePrice;
		public float WeightKg;
		public string VfxProfilePath;
		public bool HasBoltHoldOpenDelay;
		public bool UseMk12ItemTemplate;
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
	#endregion
}
#endif
