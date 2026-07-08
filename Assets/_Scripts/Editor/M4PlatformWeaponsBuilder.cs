#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class M4PlatformWeaponsBuilderBootstrap
{
	private const string c_MarkerPath = "Assets/.m4_platform_weapons_build_marker";

	static M4PlatformWeaponsBuilderBootstrap()
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

			M4PlatformWeaponsBuilder.BuildAll();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[M4PlatformWeaponsBuilder] Auto-run failed: {exception}");
		}
	}
}

/// <summary>
/// Выпечка WeaponDefinition / ItemDefinition / loot для расширенных вариантов M4/AR-платформы.
/// </summary>
public static class M4PlatformWeaponsBuilder
{
	private const string c_TemplateWeaponModA1Path = "Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset";
	private const string c_TemplateWeaponModA2Path = "Assets/GameData/Shooting/M4/Weapon_M4_ModA_2.asset";
	private const string c_TemplateItemPath = "Assets/GameData/Inventory/M4/Item_Weapon_M4_ModA_2.asset";
	private const string c_EquippedRoot = "Assets/Prefabs/Weapons/M4/Equipped";
	private const string c_ShootingRoot = "Assets/GameData/Shooting/M4";
	private const string c_InventoryRoot = "Assets/GameData/Inventory/M4";
	private const string c_LootWeaponsRoot = "Assets/Prefabs/World/Loot/M4/Weapons";
	private const string c_SilencerAttachmentPath = "Assets/GameData/Shooting/M4/Attachment_M4_Silencer_556.asset";
	private const string c_MuzzleBrakeAttachmentPath = "Assets/GameData/Shooting/M4/Attachment_M4_MuzzleBrakeM4.asset";
	private const string c_Stock1AttachmentPath = "Assets/GameData/Shooting/M4/Attachment_M4_Stock1.asset";
	private const string c_Stock2AttachmentPath = "Assets/GameData/Shooting/M4/Attachment_M4_Stock2.asset";
	private const string c_ExistingWeaponModA1Path = "Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset";
	private const string c_ExistingWeaponModA2Path = "Assets/GameData/Shooting/M4/Weapon_M4_ModA_2.asset";

	private static readonly WeaponAttachmentSlotType[] s_BasicOpticSlots =
	{
		WeaponAttachmentSlotType.Muzzle,
		WeaponAttachmentSlotType.Optic
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

	[MenuItem("Tools/M4 Platform/Build Platform Expansion Weapons")]
	public static void BuildAllFromMenu()
	{
		try
		{
			BuildAll();
			EditorUtility.DisplayDialog("M4 Platform", "Готово: M16/MK12/MK18 и совместимость модулей обновлены.", "OK");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("M4 Platform", exception.Message, "OK");
		}
	}

	/// <summary>Batch: -executeMethod M4PlatformWeaponsBuilder.RunBatch</summary>
	public static void RunBatch()
	{
		try
		{
			BuildAll();
			Debug.Log("[M4PlatformWeaponsBuilder] Batch complete.");
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
		WeaponDefinition templateModA1 = LoadAsset<WeaponDefinition>(c_TemplateWeaponModA1Path);
		WeaponDefinition templateModA2 = LoadAsset<WeaponDefinition>(c_TemplateWeaponModA2Path);
		ItemDefinition templateItem = LoadAsset<ItemDefinition>(c_TemplateItemPath);
		if (templateModA1 == null || templateModA2 == null || templateItem == null)
			throw new InvalidOperationException("Missing M4 template weapon or item.");

		var builtWeapons = new List<WeaponDefinition>();
		foreach (WeaponBuildConfig config in GetWeaponConfigs())
		{
			GameObject equippedPrefab = LoadAsset<GameObject>($"{c_EquippedRoot}/{config.EquippedPrefabFileName}.prefab");
			if (equippedPrefab == null)
				throw new InvalidOperationException($"Missing equipped prefab: {config.EquippedPrefabFileName}");

			WeaponDefinition template = config.UseModA2Template ? templateModA2 : templateModA1;
			WeaponDefinition weapon = BuildWeaponDefinition(config, template);
			ItemDefinition item = BuildItemDefinition(config, equippedPrefab, weapon, templateItem);
			BuildLootForItem(item, $"{c_LootWeaponsRoot}/Loot_{config.ItemAssetName}.prefab", equippedPrefab);
			builtWeapons.Add(weapon);
		}

		UpdateM4AttachmentCompatibility(builtWeapons);
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"[M4PlatformWeaponsBuilder] Built {builtWeapons.Count} platform weapons.");
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
		so.FindProperty("m_WeaponClass").enumValueIndex = (int)_config.WeaponClass;
		so.FindProperty("m_SupportedCaliber").enumValueIndex = (int)CaliberType.Five56By45;
		so.FindProperty("m_SupportedMagazineType").enumValueIndex = (int)MagazineType.RifleStandard;
		so.FindProperty("m_AttachmentSlotProfile").enumValueIndex = (int)_config.SlotProfile;
		WriteAttachmentSlots(so.FindProperty("m_AttachmentSlots"), _config.SlotLayout);
		WriteFireModes(so.FindProperty("m_AvailableFireModes"), _config.FireModes);
		so.FindProperty("m_DefaultFireMode").enumValueIndex = (int)_config.DefaultFireMode;
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
				BuildDistanceCurve(curves.DispersionKeyframes);
			distanceAimProfile.FindPropertyRelative("m_AimTimeMultiplierByDistance").animationCurveValue =
				BuildDistanceCurve(curves.AimTimeKeyframes);
		}

		_weaponSo.FindProperty("m_AutoBurstSpreadMultiplierByShot").animationCurveValue =
			BuildAutoBurstSpreadCurve(curves.AutoBurstSpreadKeyframes);
	}

	private static AnimationCurve BuildDistanceCurve(OpticDistanceCurveLibrary.DistanceKeyframe[] _keyframes)
	{
		var keys = new Keyframe[_keyframes.Length];
		for (int i = 0; i < _keyframes.Length; i++)
			keys[i] = new Keyframe(_keyframes[i].DistanceMeters, _keyframes[i].Value);

		return new AnimationCurve(keys);
	}

	private static AnimationCurve BuildAutoBurstSpreadCurve(OpticDistanceCurveLibrary.DistanceKeyframe[] _keyframes)
	{
		var keys = new Keyframe[_keyframes.Length];
		for (int i = 0; i < _keyframes.Length; i++)
			keys[i] = new Keyframe(_keyframes[i].DistanceMeters, _keyframes[i].Value);

		return new AnimationCurve(keys);
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

	private static void UpdateM4AttachmentCompatibility(IReadOnlyList<WeaponDefinition> _builtWeapons)
	{
		var compatibleWeapons = new List<WeaponDefinition>();
		WeaponDefinition modA1 = LoadAsset<WeaponDefinition>(c_ExistingWeaponModA1Path);
		WeaponDefinition modA2 = LoadAsset<WeaponDefinition>(c_ExistingWeaponModA2Path);
		if (modA1 != null)
			compatibleWeapons.Add(modA1);
		if (modA2 != null)
			compatibleWeapons.Add(modA2);

		for (int i = 0; i < _builtWeapons.Count; i++)
		{
			if (_builtWeapons[i] != null)
				compatibleWeapons.Add(_builtWeapons[i]);
		}

		SetAttachmentCompatibleWeapons(c_SilencerAttachmentPath, compatibleWeapons.ToArray());
		SetAttachmentCompatibleWeapons(c_MuzzleBrakeAttachmentPath, compatibleWeapons.ToArray());

		var stockCompatibleWeapons = new List<WeaponDefinition>();
		if (modA2 != null)
			stockCompatibleWeapons.Add(modA2);

		for (int i = 0; i < _builtWeapons.Count; i++)
		{
			WeaponDefinition weapon = _builtWeapons[i];
			if (weapon == null)
				continue;

			if (weapon.name == "Weapon_MK12" || weapon.name == "Weapon_MK18")
				stockCompatibleWeapons.Add(weapon);
		}

		WeaponDefinition[] stockWeapons = stockCompatibleWeapons.ToArray();
		SetAttachmentCompatibleWeapons(c_Stock1AttachmentPath, stockWeapons);
		SetAttachmentCompatibleWeapons(c_Stock2AttachmentPath, stockWeapons);
	}

	private static void SetAttachmentCompatibleWeapons(string _assetPath, WeaponDefinition[] _weapons)
	{
		WeaponAttachmentDefinition attachment = LoadAsset<WeaponAttachmentDefinition>(_assetPath);
		if (attachment == null)
		{
			Debug.LogWarning($"[M4PlatformWeaponsBuilder] Missing attachment: {_assetPath}");
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

	private static void WriteAttachmentSlots(SerializedProperty _slots, SlotLayout _layout)
	{
		WeaponAttachmentSlotType[] slotTypes = _layout == SlotLayout.BasicOptic ? s_BasicOpticSlots : s_TacticalFullSlots;
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
		WeaponFireMode[] standardModes =
		{
			WeaponFireMode.SemiAuto,
			WeaponFireMode.FullAuto,
			WeaponFireMode.Burst,
			WeaponFireMode.Auto
		};

		WeaponFireMode[] marksmanModes =
		{
			WeaponFireMode.SemiAuto,
			WeaponFireMode.Burst
		};

		yield return Config(
			"Equipped_M16AModA_1", "Weapon_M16A_ModA_1", "Item_Weapon_M16A_ModA_1",
			"item.weapon.m16a_moda_1", "M16A rifle with carry handle optic. Longer barrel than M4, no stock slot.",
			false, SlotLayout.BasicOptic, WeaponAttachmentSlotProfile.M4BasicOpticNoStock,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.MidRifle,
			standardModes, WeaponFireMode.SemiAuto,
			600f, 0.35f, 2.30f, 125f, 0.80f, 0.46f, 0.83f, 1.12f, 3.9f, 0.84f, 3000);

		yield return Config(
			"Equipped_M16A4ModA_2", "Weapon_M16A4_ModA_2", "Item_Weapon_M16A4_ModA_2",
			"item.weapon.m16a4_moda_2", "M16A4 marksman rifle with railed handguard. Full M4 accessory layout except stock.",
			true, SlotLayout.TacticalFull, WeaponAttachmentSlotProfile.M4TacticalNoStock,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Marksman,
			standardModes, WeaponFireMode.SemiAuto,
			600f, 0.39f, 2.35f, 140f, 0.72f, 0.43f, 0.82f, 1.06f, 4.3f, 0.84f, 3100);

		yield return Config(
			"Equipped_MK12", "Weapon_MK12", "Item_Weapon_MK12",
			"item.weapon.mk12", "MK12 Mod 1 DMR. Long-barrel 5.56 marksman rifle with full accessory layout.",
			true, SlotLayout.TacticalFull, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Dmr,
			marksmanModes, WeaponFireMode.SemiAuto,
			450f, 0.50f, 2.50f, 160f, 0.56f, 0.38f, 0.80f, 1.00f, 4.8f, 0.86f, 3600);

		yield return Config(
			"Equipped_MK18", "Weapon_MK18", "Item_Weapon_MK18",
			"item.weapon.mk18", "MK18 Mod 1 CQB carbine. Short 5.56 rifle with full M4 tactical slots.",
			true, SlotLayout.TacticalFull, WeaponAttachmentSlotProfile.Full,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbShort,
			standardModes, WeaponFireMode.FullAuto,
			700f, 0.26f, 1.95f, 60f, 1.18f, 0.60f, 0.88f, 1.50f, 3.0f, 0.82f, 2750);
	}

	private static WeaponBuildConfig Config(
		string _equippedPrefab,
		string _weaponAsset,
		string _itemAsset,
		string _localizationKey,
		string _descriptionEn,
		bool _useModA2Template,
		SlotLayout _slotLayout,
		WeaponAttachmentSlotProfile _slotProfile,
		WeaponDistanceCurveLibrary.WeaponBalanceKind _curveKind,
		WeaponFireMode[] _fireModes,
		WeaponFireMode _defaultFireMode,
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
			UseModA2Template = _useModA2Template,
			SlotLayout = _slotLayout,
			SlotProfile = _slotProfile,
			CurveKind = _curveKind,
			FireModes = _fireModes,
			DefaultFireMode = _defaultFireMode,
			WeaponClass = WeaponClassType.Rifle,
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
		public bool UseModA2Template;
		public SlotLayout SlotLayout;
		public WeaponAttachmentSlotProfile SlotProfile;
		public WeaponDistanceCurveLibrary.WeaponBalanceKind CurveKind;
		public WeaponFireMode[] FireModes;
		public WeaponFireMode DefaultFireMode;
		public WeaponClassType WeaponClass;
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
		public int BasePrice;
	}

	private enum SlotLayout
	{
		BasicOptic,
		TacticalFull
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
