#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// SVD + Sniper 7.62x51: звуки, слоты, дульные модули со сцены, совместимость оптики, веса.
/// </summary>
public static class SvdSniperContentBuilder
{
	#region Constants
	private const string c_SvdWeaponPath = "Assets/GameData/Shooting/Standalone/Weapon_SVD.asset";
	private const string c_SniperWeaponPath = "Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset";
	private const string c_SvdItemPath = "Assets/GameData/Inventory/Standalone/Item_Weapon_SVD.asset";
	private const string c_SniperItemPath = "Assets/GameData/Inventory/Standalone/Item_Weapon_Sniper762x51.asset";
	private const string c_SvdMagItemPath = "Assets/GameData/Inventory/Standalone/Item_Mag_SVD_762_54R_10.asset";
	private const string c_SniperMagItemPath = "Assets/GameData/Inventory/Standalone/Item_Mag_Sniper_762x51_10.asset";
	private const string c_Ammo762x54RItemPath = "Assets/GameData/Inventory/Standalone/Item_Loot_AmmoBox_762x54R.asset";
	private const string c_Ammo762x51ItemPath = "Assets/GameData/Inventory/Standalone/Item_Loot_AmmoBox_762x51.asset";
	private const string c_AkWeaponPath = "Assets/GameData/Shooting/AK/Weapon_AK47.asset";

	private const string c_SvdFireUnsuppressed = "Assets/Audio/Combat/Weapons/SVD/Fire/gun_svd_fire_01.wav";
	private const string c_SvdFireSuppressed = "Assets/Audio/Combat/Weapons/SVD/SuppressedFire/gun_svd_suppressed_fire_01.wav";
	private const string c_SniperFireUnsuppressed = "Assets/Audio/Combat/Weapons/Sniper762/Fire/gun_sniper762_fire_01.wav";
	private const string c_SniperFireSuppressed = "Assets/Audio/Combat/Weapons/Sniper762/SuppressedFire/gun_sniper762_suppressed_fire_01.wav";
	private const string c_Mk12WeaponPath = "Assets/GameData/Shooting/M4/Weapon_MK12.asset";
	private const string c_SniperSilencerAttachmentPath = "Assets/GameData/Shooting/Standalone/Attachment_Sniper762x51_Silencer.asset";
	private const string c_SvdSilencerAttachmentPath = "Assets/GameData/Shooting/Standalone/Attachment_SVD_Silencer.asset";

	private const string c_VisualRoot = "Assets/Prefabs/Weapons/Standalone/Visuals/Attachments";
	private const string c_ShootingRoot = "Assets/GameData/Shooting/Standalone";
	private const string c_InventoryRoot = "Assets/GameData/Inventory/Standalone";
	private const string c_LootRoot = "Assets/Prefabs/World/Loot/Standalone/Attachments";

	private const string c_SceneSvdSilencer = "SM_Wep_Mod_Silencer_02";
	private const string c_SceneSvdBrake = "SM_Wep_Mod_MuzzleBrake_04";
	private const string c_SceneSniperSilencer = "SM_Wep_Mod_Silencer_03";
	private const string c_SceneSniperBrake = "SM_Wep_Mod_MuzzleBrake_03";

	private static readonly string[] s_M4WeaponPaths =
	{
		"Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset",
		"Assets/GameData/Shooting/M4/Weapon_M4_ModA_2.asset",
		"Assets/GameData/Shooting/M4/Weapon_M16A_ModA_1.asset",
		"Assets/GameData/Shooting/M4/Weapon_M16A4_ModA_2.asset",
		"Assets/GameData/Shooting/M4/Weapon_MK12.asset",
		"Assets/GameData/Shooting/M4/Weapon_MK18.asset"
	};

	private static readonly string[] s_M4OpticPaths =
	{
		"Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Reddot2.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Reddot3.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_RDC.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Aimpoint.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_EOTech_G33.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_ACOG.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_ACOG_RMR.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_SUSAT.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Scope1_3x.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Scope4.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Scope5.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_ELCAN_SpecterDR.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Vortex_Razor.asset"
	};

	private static readonly string[] s_AkSideOpticPaths =
	{
		"Assets/GameData/Shooting/AK/Attachment_AK_Scope11.asset",
		"Assets/GameData/Shooting/AK/Attachment_AK_Reddot4_Rail.asset"
	};
	#endregion

	#region Menu
	[MenuItem("Tools/Standalone Weapons/Build SVD + Sniper Content", false, 120)]
	public static void BuildFromMenu()
	{
		try
		{
			BuildAll();
			EditorUtility.DisplayDialog("SVD + Sniper", "Готово: звуки, слоты, дульные модули, оптика, веса.", "OK");
		}
		catch (Exception ex)
		{
			Debug.LogException(ex);
			EditorUtility.DisplayDialog("SVD + Sniper", ex.Message, "OK");
		}
	}

	/// <summary>Batch: -executeMethod SvdSniperContentBuilder.BuildAll</summary>
	public static void BuildAll()
	{
		EnsureFolder(c_VisualRoot);
		EnsureFolder(c_ShootingRoot);
		EnsureFolder(c_InventoryRoot);
		EnsureFolder(c_LootRoot);

		WeaponDefinition svd = LoadRequired<WeaponDefinition>(c_SvdWeaponPath);
		WeaponDefinition sniper = LoadRequired<WeaponDefinition>(c_SniperWeaponPath);
		WeaponDefinition ak47 = LoadRequired<WeaponDefinition>(c_AkWeaponPath);

		ApplySvdBalanceAndSlots(svd);
		ApplySniperBalance(sniper);
		WireSvdAudio(svd, ak47);
		WireSniperAudio(sniper);
		ApplyWeights();

		GameObject svdSilencerVisual = BuildWrappedVisualFromScene(
			c_SceneSvdSilencer, $"{c_VisualRoot}/Attachment_Visual_SVD_Silencer.prefab", true, 0.234f);
		GameObject svdBrakeVisual = BuildWrappedVisualFromScene(
			c_SceneSvdBrake, $"{c_VisualRoot}/Attachment_Visual_SVD_MuzzleBrake.prefab", false, 0f);
		GameObject sniperSilencerVisual = BuildWrappedVisualFromScene(
			c_SceneSniperSilencer, $"{c_VisualRoot}/Attachment_Visual_Sniper762x51_Silencer.prefab", true, 0.24f);
		GameObject sniperBrakeVisual = BuildWrappedVisualFromScene(
			c_SceneSniperBrake, $"{c_VisualRoot}/Attachment_Visual_Sniper762x51_MuzzleBrake.prefab", false, 0f);

		AudioClip svdSuppressed = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SvdFireSuppressed);

		ItemDefinition svdSilencerItem = BuildMuzzleAttachment(
			"Attachment_SVD_Silencer",
			$"{c_ShootingRoot}/Attachment_SVD_Silencer.asset",
			"Item_Attachment_SVD_Silencer",
			$"{c_InventoryRoot}/Item_Attachment_SVD_Silencer.asset",
			"item.attachment.svd_silencer",
			"SVD suppressor (7.62x54R).",
			WeaponAttachmentType.Suppressor,
			svdSilencerVisual,
			new[] { svd },
			780,
			0.48f,
			1.05f,
			1.1f,
			1f,
			1f,
			1f,
			svdSuppressed);

		ItemDefinition svdBrakeItem = BuildMuzzleAttachment(
			"Attachment_SVD_MuzzleBrake",
			$"{c_ShootingRoot}/Attachment_SVD_MuzzleBrake.asset",
			"Item_Attachment_SVD_MuzzleBrake",
			$"{c_InventoryRoot}/Item_Attachment_SVD_MuzzleBrake.asset",
			"item.attachment.svd_muzzle_brake",
			"SVD compensator / flash hider (7.62x54R).",
			WeaponAttachmentType.Compensator,
			svdBrakeVisual,
			new[] { svd },
			440,
			0.18f,
			1f,
			1f,
			1f,
			0.9f,
			1.15f,
			null);

		ItemDefinition sniperSilencerItem = BuildMuzzleAttachment(
			"Attachment_Sniper762x51_Silencer",
			$"{c_ShootingRoot}/Attachment_Sniper762x51_Silencer.asset",
			"Item_Attachment_Sniper762x51_Silencer",
			$"{c_InventoryRoot}/Item_Attachment_Sniper762x51_Silencer.asset",
			"item.attachment.sniper762x51_silencer",
			"7.62x51 sniper rifle suppressor.",
			WeaponAttachmentType.Suppressor,
			sniperSilencerVisual,
			new[] { sniper },
			820,
			0.5f,
			1.05f,
			1.1f,
			1f,
			1f,
			1f,
			null);

		ItemDefinition sniperBrakeItem = BuildMuzzleAttachment(
			"Attachment_Sniper762x51_MuzzleBrake",
			$"{c_ShootingRoot}/Attachment_Sniper762x51_MuzzleBrake.asset",
			"Item_Attachment_Sniper762x51_MuzzleBrake",
			$"{c_InventoryRoot}/Item_Attachment_Sniper762x51_MuzzleBrake.asset",
			"item.attachment.sniper762x51_muzzle_brake",
			"7.62x51 sniper rifle muzzle brake.",
			WeaponAttachmentType.Compensator,
			sniperBrakeVisual,
			new[] { sniper },
			460,
			0.2f,
			1f,
			1f,
			1f,
			0.88f,
			1.12f,
			null);

		BuildLootForItem(svdSilencerItem, $"{c_LootRoot}/Loot_Att_SVD_Silencer.prefab", svdSilencerVisual);
		BuildLootForItem(svdBrakeItem, $"{c_LootRoot}/Loot_Att_SVD_MuzzleBrake.prefab", svdBrakeVisual);
		BuildLootForItem(sniperSilencerItem, $"{c_LootRoot}/Loot_Att_Sniper762x51_Silencer.prefab", sniperSilencerVisual);
		BuildLootForItem(sniperBrakeItem, $"{c_LootRoot}/Loot_Att_Sniper762x51_MuzzleBrake.prefab", sniperBrakeVisual);

		UpdateOpticCompatibility(svd, sniper);
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();
		AssetDatabase.SaveAssets();
		Debug.Log("[SvdSniperContentBuilder] SVD + Sniper content built.");
	}
	#endregion

	#region Weapon Data
	private static void ApplySvdBalanceAndSlots(WeaponDefinition _weapon)
	{
		var so = new SerializedObject(_weapon);
		SerializedProperty slots = so.FindProperty("m_AttachmentSlots");
		WeaponAttachmentSlotType[] slotTypes =
		{
			WeaponAttachmentSlotType.Muzzle,
			WeaponAttachmentSlotType.Optic,
			WeaponAttachmentSlotType.SideRail
		};
		slots.arraySize = slotTypes.Length;
		for (int i = 0; i < slotTypes.Length; i++)
		{
			SerializedProperty slot = slots.GetArrayElementAtIndex(i);
			slot.FindPropertyRelative("SlotType").enumValueIndex = (int)slotTypes[i];
			slot.FindPropertyRelative("IsRequired").boolValue = false;
			slot.FindPropertyRelative("AnchorChildName").stringValue = string.Empty;
		}

		so.FindProperty("m_AttachmentSlotProfile").enumValueIndex = (int)WeaponAttachmentSlotProfile.Full;
		so.FindProperty("m_FireRateRpm").floatValue = 150f;
		so.FindProperty("m_SemiAutoFireRateRpm").floatValue = 150f;
		so.FindProperty("m_AimTimeSeconds").floatValue = 0.48f;
		so.FindProperty("m_ReloadTimeSeconds").floatValue = 2.5f;
		so.FindProperty("m_EffectiveRangeMeters").floatValue = 320f;
		so.FindProperty("m_BaseShotDispersion").floatValue = 0.48f;
		so.FindProperty("m_RecoilPerShot").floatValue = 0.46f;
		so.FindProperty("m_SemiAutoRecoilMultiplier").floatValue = 0.82f;
		so.FindProperty("m_AutoRecoilMultiplier").floatValue = 1f;
		so.FindProperty("m_RecoilRecoveryPerSecond").floatValue = 4.2f;
		WeaponRecoilAssetDefaults.Write(so, _weapon != null ? _weapon.name : "Weapon_SVD");
		so.FindProperty("m_Reliability").floatValue = 0.88f;
		so.FindProperty("m_HasBoltHoldOpenDelay").boolValue = false;

		WeaponDistanceCurveLibrary.WeaponBalanceCurves curves =
			WeaponDistanceCurveLibrary.GetCurves(WeaponDistanceCurveLibrary.WeaponBalanceKind.Marksman);
		SerializedProperty distanceAimProfile = so.FindProperty("m_DistanceAimProfile");
		distanceAimProfile.FindPropertyRelative("m_DispersionMultiplierByDistance").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes);
		distanceAimProfile.FindPropertyRelative("m_AimTimeMultiplierByDistance").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.AimTimeKeyframes);
		so.FindProperty("m_AutoBurstSpreadMultiplierByShot").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.AutoBurstSpreadKeyframes);

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_weapon);
	}

	private static void ApplySniperBalance(WeaponDefinition _weapon)
	{
		var so = new SerializedObject(_weapon);
		so.FindProperty("m_EffectiveRangeMeters").floatValue = 380f;
		so.FindProperty("m_AimTimeSeconds").floatValue = 0.55f;
		so.FindProperty("m_BaseShotDispersion").floatValue = 0.40f;
		so.FindProperty("m_RecoilPerShot").floatValue = 0.42f;
		so.FindProperty("m_SemiAutoRecoilMultiplier").floatValue = 0.74f;
		so.FindProperty("m_RecoilRecoveryPerSecond").floatValue = 4.3f;
		WeaponRecoilAssetDefaults.Write(so, _weapon != null ? _weapon.name : "Weapon_Sniper762x51");
		so.FindProperty("m_Reliability").floatValue = 0.91f;

		WeaponDistanceCurveLibrary.WeaponBalanceCurves curves =
			WeaponDistanceCurveLibrary.GetCurves(WeaponDistanceCurveLibrary.WeaponBalanceKind.Dmr);
		SerializedProperty distanceAimProfile = so.FindProperty("m_DistanceAimProfile");
		distanceAimProfile.FindPropertyRelative("m_DispersionMultiplierByDistance").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes);
		distanceAimProfile.FindPropertyRelative("m_AimTimeMultiplierByDistance").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.AimTimeKeyframes);
		so.FindProperty("m_AutoBurstSpreadMultiplierByShot").animationCurveValue =
			OpticDistanceCurveLibrary.BuildCurve(curves.AutoBurstSpreadKeyframes);

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_weapon);
	}

	private static void WireSvdAudio(WeaponDefinition _svd, WeaponDefinition _akTemplate)
	{
		AudioClip fire = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SvdFireUnsuppressed);
		AudioClip suppressed = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SvdFireSuppressed);
		if (fire == null)
			Debug.LogWarning($"[SvdSniperContentBuilder] Missing fire clip: {c_SvdFireUnsuppressed}");
		if (suppressed == null)
			Debug.LogWarning($"[SvdSniperContentBuilder] Missing suppressed clip: {c_SvdFireSuppressed}");

		var svdSo = new SerializedObject(_svd);
		var akSo = new SerializedObject(_akTemplate);

		WriteAudioClipArray(
			svdSo.FindProperty("m_FireSoundProfile").FindPropertyRelative("m_FireClips"),
			fire != null ? new[] { fire } : Array.Empty<AudioClip>());
		svdSo.FindProperty("m_FireSoundProfile").FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue = 625f;

		WriteAudioClipArray(
			svdSo.FindProperty("m_SuppressedFireSoundProfile").FindPropertyRelative("m_FireClips"),
			suppressed != null ? new[] { suppressed } : Array.Empty<AudioClip>());
		svdSo.FindProperty("m_SuppressedFireSoundProfile").FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue = 220f;

		CopyClipArray(akSo, svdSo, "m_FireModeSwitchSounds", "m_Clips");
		CopyClipArray(akSo, svdSo, "m_ReloadMagOutSounds", "m_Clips");
		CopyClipArray(akSo, svdSo, "m_ReloadMagInSounds", "m_Clips");
		CopyClipArray(akSo, svdSo, "m_BoltCycleSounds", "m_Clips");
		CopyClipArray(akSo, svdSo, "m_MalfunctionClickSounds", "m_Clips");
		svdSo.FindProperty("m_ReloadSoundsVolume").floatValue =
			akSo.FindProperty("m_ReloadSoundsVolume").floatValue;
		svdSo.FindProperty("m_FireModeSwitchSoundVolume").floatValue =
			akSo.FindProperty("m_FireModeSwitchSoundVolume").floatValue;
		svdSo.FindProperty("m_MalfunctionClickSoundVolume").floatValue =
			akSo.FindProperty("m_MalfunctionClickSoundVolume").floatValue;
		svdSo.FindProperty("m_HasBoltHoldOpenDelay").boolValue = false;
		svdSo.FindProperty("m_AnimationPlatform").enumValueIndex = (int)WeaponAnimationPlatform.Svd;
		WriteAudioClipArray(svdSo.FindProperty("m_ReloadBoltHoldOpenDelaySounds").FindPropertyRelative("m_Clips"), Array.Empty<AudioClip>());

		svdSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_svd);

		WireSilencerSuppressedClip(c_SvdSilencerAttachmentPath, suppressed, 220f, 1f);
	}

	private static void WireSniperAudio(WeaponDefinition _sniper)
	{
		AudioClip fire = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SniperFireUnsuppressed);
		AudioClip suppressed = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SniperFireSuppressed);
		WeaponDefinition mk12 = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_Mk12WeaponPath);
		if (fire == null)
			Debug.LogWarning($"[SvdSniperContentBuilder] Missing fire clip: {c_SniperFireUnsuppressed}");
		if (suppressed == null)
			Debug.LogWarning($"[SvdSniperContentBuilder] Missing suppressed clip: {c_SniperFireSuppressed}");
		if (mk12 == null)
		{
			Debug.LogWarning("[SvdSniperContentBuilder] Missing Weapon_MK12 for reload audio template.");
			return;
		}

		var sniperSo = new SerializedObject(_sniper);
		var mk12So = new SerializedObject(mk12);

		WriteAudioClipArray(
			sniperSo.FindProperty("m_FireSoundProfile").FindPropertyRelative("m_FireClips"),
			fire != null ? new[] { fire } : Array.Empty<AudioClip>());
		sniperSo.FindProperty("m_FireSoundProfile").FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue = 650f;

		WriteAudioClipArray(
			sniperSo.FindProperty("m_SuppressedFireSoundProfile").FindPropertyRelative("m_FireClips"),
			suppressed != null ? new[] { suppressed } : Array.Empty<AudioClip>());
		sniperSo.FindProperty("m_SuppressedFireSoundProfile").FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue = 200f;

		// Mag reload / bolt / firemode / dryfire — M4 family.
		CopyClipArray(mk12So, sniperSo, "m_FireModeSwitchSounds", "m_Clips");
		CopyClipArray(mk12So, sniperSo, "m_ReloadMagOutSounds", "m_Clips");
		CopyClipArray(mk12So, sniperSo, "m_ReloadMagInSounds", "m_Clips");
		CopyClipArray(mk12So, sniperSo, "m_BoltCycleSounds", "m_Clips");
		CopyClipArray(mk12So, sniperSo, "m_ReloadBoltHoldOpenDelaySounds", "m_Clips");
		CopyClipArray(mk12So, sniperSo, "m_MalfunctionClickSounds", "m_Clips");
		sniperSo.FindProperty("m_ReloadSoundsVolume").floatValue =
			mk12So.FindProperty("m_ReloadSoundsVolume").floatValue;
		sniperSo.FindProperty("m_FireModeSwitchSoundVolume").floatValue =
			mk12So.FindProperty("m_FireModeSwitchSoundVolume").floatValue;
		sniperSo.FindProperty("m_MalfunctionClickSoundVolume").floatValue =
			mk12So.FindProperty("m_MalfunctionClickSoundVolume").floatValue;
		sniperSo.FindProperty("m_HasBoltHoldOpenDelay").boolValue = false;
		SerializedProperty requiresManualBolt = sniperSo.FindProperty("m_RequiresManualBoltCycle");
		if (requiresManualBolt != null)
			requiresManualBolt.boolValue = true;

		sniperSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_sniper);

		// Clip already attenuated to ~70% amplitude; keep attachment multiplier at 1.
		WireSilencerSuppressedClip(c_SniperSilencerAttachmentPath, suppressed, 200f, 1f);
	}

	private static void WireSilencerSuppressedClip(
		string _attachmentPath,
		AudioClip _clip,
		float _maxDistance,
		float _volumeMultiplier)
	{
		WeaponAttachmentDefinition attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_attachmentPath);
		if (attachment == null)
			return;

		var so = new SerializedObject(attachment);
		SerializedProperty profile = so.FindProperty("m_SuppressedFireSoundProfile");
		WriteAudioClipArray(
			profile.FindPropertyRelative("m_FireClips"),
			_clip != null ? new[] { _clip } : Array.Empty<AudioClip>());
		profile.FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue = _clip != null ? _maxDistance : 0f;
		so.FindProperty("m_SuppressedFireVolumeMultiplier").floatValue = _volumeMultiplier;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);
	}

	private static void ApplyWeights()
	{
		SetItemWeight(c_SvdItemPath, 4.3f);
		SetItemWeight(c_SniperItemPath, 5.8f);
		SetItemWeight(c_SvdMagItemPath, 0.4f);
		SetItemWeight(c_SniperMagItemPath, 0.5f);
		SetItemWeight(c_Ammo762x54RItemPath, 0.65f);
		SetItemWeight(c_Ammo762x51ItemPath, 0.65f);
		SetItemWeight($"{c_InventoryRoot}/Item_Attachment_SVD_Silencer.asset", 0.48f);
		SetItemWeight($"{c_InventoryRoot}/Item_Attachment_SVD_MuzzleBrake.asset", 0.18f);
		SetItemWeight($"{c_InventoryRoot}/Item_Attachment_Sniper762x51_Silencer.asset", 0.5f);
		SetItemWeight($"{c_InventoryRoot}/Item_Attachment_Sniper762x51_MuzzleBrake.asset", 0.2f);
	}

	private static void SetItemWeight(string _path, float _weightKg)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_path);
		if (item == null)
		{
			Debug.LogWarning($"[SvdSniperContentBuilder] Missing item for weight: {_path}");
			return;
		}

		var so = new SerializedObject(item);
		so.FindProperty("m_WeightKg").floatValue = _weightKg;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
	}
	#endregion

	#region Attachments
	private static ItemDefinition BuildMuzzleAttachment(
		string _attachmentName,
		string _attachmentPath,
		string _itemName,
		string _itemPath,
		string _localizationKey,
		string _description,
		WeaponAttachmentType _type,
		GameObject _visual,
		WeaponDefinition[] _compatibleWeapons,
		int _price,
		float _weightKg,
		float _aimTimeModifier,
		float _effectiveRangeModifier,
		float _recoilModifier,
		float _semiRecoilModifier,
		float _autoRecoilModifier,
		AudioClip _suppressedClip)
	{
		WeaponAttachmentDefinition attachment = GetOrCreateAsset<WeaponAttachmentDefinition>(_attachmentPath, _attachmentName);
		var so = new SerializedObject(attachment);
		so.FindProperty("m_AttachmentType").enumValueIndex = (int)_type;
		so.FindProperty("m_RequiredSlot").enumValueIndex = (int)WeaponAttachmentSlotType.Muzzle;
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weapons = so.FindProperty("m_CompatibleWeapons");
		weapons.arraySize = _compatibleWeapons.Length;
		for (int i = 0; i < _compatibleWeapons.Length; i++)
			weapons.GetArrayElementAtIndex(i).objectReferenceValue = _compatibleWeapons[i];

		SerializedProperty slots = so.FindProperty("m_CompatibleSlots");
		slots.arraySize = 1;
		slots.GetArrayElementAtIndex(0).enumValueIndex = (int)WeaponAttachmentSlotType.Muzzle;
		so.FindProperty("m_AimTimeModifier").floatValue = _aimTimeModifier;
		so.FindProperty("m_EffectiveRangeModifier").floatValue = _effectiveRangeModifier;
		so.FindProperty("m_RecoilModifier").floatValue = _recoilModifier;
		so.FindProperty("m_SemiAutoRecoilModifier").floatValue = _semiRecoilModifier;
		so.FindProperty("m_AutomaticRecoilModifier").floatValue = _autoRecoilModifier;
		so.FindProperty("m_ReloadTimeModifier").floatValue = 1f;
		so.FindProperty("m_WearPerShotMultiplier").floatValue = 1f;
		so.FindProperty("m_FoulingPerShotMultiplier").floatValue = _type == WeaponAttachmentType.Suppressor ? 1.15f : 1f;
		so.FindProperty("m_JamRiskModifier").floatValue = _type == WeaponAttachmentType.Suppressor ? 1.06f : 1f;
		so.FindProperty("m_SuppressedFireVolumeMultiplier").floatValue = 0.35f;

		SerializedProperty suppressed = so.FindProperty("m_SuppressedFireSoundProfile");
		WriteAudioClipArray(
			suppressed.FindPropertyRelative("m_FireClips"),
			_suppressedClip != null ? new[] { _suppressedClip } : Array.Empty<AudioClip>());
		suppressed.FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue =
			_suppressedClip != null ? 220f : 0f;

		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _visual;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);

		ItemDefinition item = GetOrCreateAsset<ItemDefinition>(_itemPath, _itemName);
		var itemSo = new SerializedObject(item);
		itemSo.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		itemSo.FindProperty("m_Description").stringValue = _description;
		itemSo.FindProperty("m_BasePrice").intValue = _price;
		itemSo.FindProperty("m_WeightKg").floatValue = _weightKg;
		itemSo.FindProperty("m_Category").enumValueIndex = 0;
		itemSo.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _visual;
		itemSo.FindProperty("m_WeaponAttachmentDefinition").objectReferenceValue = attachment;
		itemSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
	}

	private static void UpdateOpticCompatibility(WeaponDefinition _svd, WeaponDefinition _sniper)
	{
		var m4Family = new List<WeaponDefinition>();
		for (int i = 0; i < s_M4WeaponPaths.Length; i++)
		{
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(s_M4WeaponPaths[i]);
			if (weapon != null)
				m4Family.Add(weapon);
		}

		WeaponDefinition benelli = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
			"Assets/GameData/Shooting/Standalone/Weapon_BenelliM4.asset");

		for (int i = 0; i < s_M4OpticPaths.Length; i++)
		{
			var list = new List<WeaponDefinition>(m4Family) { _svd, _sniper };
			string path = s_M4OpticPaths[i];
			bool isCqb = path.Contains("Reddot") || path.Contains("RDC");
			if (isCqb && benelli != null)
				list.Add(benelli);
			AddCompatibleWeapons(path, list);
		}

		for (int i = 0; i < s_AkSideOpticPaths.Length; i++)
			AppendCompatibleWeapon(s_AkSideOpticPaths[i], _svd);
	}
	#endregion

	#region Visual / Loot
	private static GameObject BuildWrappedVisualFromScene(
		string _sceneObjectName,
		string _prefabPath,
		bool _ensureMuzzleExit,
		float _muzzleExitZ)
	{
		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
		GameObject sceneObject = FindInSceneIncludingInactive(_sceneObjectName);
		if (sceneObject == null)
		{
			if (existing != null)
			{
				Debug.LogWarning($"[SvdSniperContentBuilder] Scene '{_sceneObjectName}' missing — keeping existing prefab.");
				return existing;
			}

			throw new InvalidOperationException($"Scene object '{_sceneObjectName}' not found and no prefab at {_prefabPath}.");
		}

		GameObject root = new GameObject(Path.GetFileNameWithoutExtension(_prefabPath));
		try
		{
			GameObject meshClone = UnityEngine.Object.Instantiate(sceneObject);
			meshClone.name = _sceneObjectName;
			meshClone.transform.SetParent(root.transform, false);
			meshClone.transform.localPosition = Vector3.zero;
			meshClone.transform.localRotation = Quaternion.identity;
			meshClone.transform.localScale = Vector3.one;

			if (_ensureMuzzleExit)
			{
				Transform muzzleExit = meshClone.transform.Find("MuzzleExit");
				if (muzzleExit == null)
				{
					GameObject exitGo = new GameObject("MuzzleExit");
					exitGo.transform.SetParent(root.transform, false);
					exitGo.transform.localPosition = new Vector3(0f, 0f, _muzzleExitZ);
				}
				else if (muzzleExit.parent != root.transform)
				{
					muzzleExit.SetParent(root.transform, true);
				}
			}

			EnsureFolder(Path.GetDirectoryName(_prefabPath)?.Replace('\\', '/'));
			GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, _prefabPath);
			return prefab;
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
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
	#endregion

	#region Helpers
	private static void AddCompatibleWeapons(string _attachmentPath, List<WeaponDefinition> _weapons)
	{
		WeaponAttachmentDefinition attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_attachmentPath);
		if (attachment == null)
		{
			Debug.LogWarning($"[SvdSniperContentBuilder] Missing optic: {_attachmentPath}");
			return;
		}

		var unique = new List<WeaponDefinition>();
		var seen = new HashSet<WeaponDefinition>();
		for (int i = 0; i < _weapons.Count; i++)
		{
			if (_weapons[i] != null && seen.Add(_weapons[i]))
				unique.Add(_weapons[i]);
		}

		var so = new SerializedObject(attachment);
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weaponsProp = so.FindProperty("m_CompatibleWeapons");
		weaponsProp.arraySize = unique.Count;
		for (int i = 0; i < unique.Count; i++)
			weaponsProp.GetArrayElementAtIndex(i).objectReferenceValue = unique[i];
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);
	}

	private static void AppendCompatibleWeapon(string _attachmentPath, WeaponDefinition _weapon)
	{
		WeaponAttachmentDefinition attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_attachmentPath);
		if (attachment == null || _weapon == null)
			return;

		var so = new SerializedObject(attachment);
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weaponsProp = so.FindProperty("m_CompatibleWeapons");
		for (int i = 0; i < weaponsProp.arraySize; i++)
		{
			if (weaponsProp.GetArrayElementAtIndex(i).objectReferenceValue == _weapon)
			{
				so.ApplyModifiedPropertiesWithoutUndo();
				return;
			}
		}

		int index = weaponsProp.arraySize;
		weaponsProp.arraySize = index + 1;
		weaponsProp.GetArrayElementAtIndex(index).objectReferenceValue = _weapon;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);
	}

	private static void CopyClipArray(SerializedObject _from, SerializedObject _to, string _profileName, string _clipsField)
	{
		SerializedProperty fromProfile = _from.FindProperty(_profileName);
		SerializedProperty toProfile = _to.FindProperty(_profileName);
		if (fromProfile == null || toProfile == null)
			return;

		SerializedProperty fromClips = fromProfile.FindPropertyRelative(_clipsField);
		SerializedProperty toClips = toProfile.FindPropertyRelative(_clipsField);
		if (fromClips == null || toClips == null)
			return;

		toClips.arraySize = fromClips.arraySize;
		for (int i = 0; i < fromClips.arraySize; i++)
			toClips.GetArrayElementAtIndex(i).objectReferenceValue = fromClips.GetArrayElementAtIndex(i).objectReferenceValue;
	}

	private static void WriteAudioClipArray(SerializedProperty _array, AudioClip[] _clips)
	{
		if (_array == null)
			return;

		_array.arraySize = _clips != null ? _clips.Length : 0;
		for (int i = 0; i < _array.arraySize; i++)
			_array.GetArrayElementAtIndex(i).objectReferenceValue = _clips[i];
	}

	private static T LoadRequired<T>(string _path) where T : UnityEngine.Object
	{
		T asset = AssetDatabase.LoadAssetAtPath<T>(_path);
		if (asset == null)
			throw new InvalidOperationException($"Missing asset: {_path}");
		return asset;
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

	private static void EnsureFolder(string _folder)
	{
		if (string.IsNullOrEmpty(_folder) || AssetDatabase.IsValidFolder(_folder))
			return;

		string[] parts = _folder.Split('/');
		string current = parts[0];
		for (int i = 1; i < parts.Length; i++)
		{
			string next = $"{current}/{parts[i]}";
			if (!AssetDatabase.IsValidFolder(next))
				AssetDatabase.CreateFolder(current, parts[i]);
			current = next;
		}
	}

	private static GameObject FindInSceneIncludingInactive(string _name)
	{
		Scene scene = SceneManager.GetActiveScene();
		if (!scene.IsValid())
			return null;

		GameObject[] roots = scene.GetRootGameObjects();
		for (int r = 0; r < roots.Length; r++)
		{
			Transform[] transforms = roots[r].GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				if (transforms[i].name == _name)
					return transforms[i].gameObject;
			}
		}

		return null;
	}
	#endregion
}
#endif
