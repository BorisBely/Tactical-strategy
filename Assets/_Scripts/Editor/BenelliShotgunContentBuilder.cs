#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Аудио, гильза, VFX и CQB-оптика для Benelli M4.
/// </summary>
public static class BenelliShotgunContentBuilder
{
	private const string c_FireAudioRoot = "Assets/Audio/Combat/Weapons/Shotgun12G/Fire";
	private const string c_ShellInAudioRoot = "Assets/Audio/Combat/Weapons/Shotgun12G/Reload/ShellIn";
	private const string c_ShellImpactAudioRoot = "Assets/Audio/Combat/Ammo/ShellImpact_Shotgun";

	private const string c_FireSourceFolder = "Assets/SFX/М4";
	private const string c_ShellInSourceFolder = "Assets/SFX/m870";
	private const string c_ShellImpactSourceFolder = "Assets/SFX";

	private const string c_ShellCasingPrefabPath = "Assets/Prefabs/Weapons/Effects/ShellCasing_Shotgun_12Gauge.prefab";
	private const string c_ShellCasingTemplatePath = "Assets/Prefabs/Weapons/Effects/ShellCasing_PolygonMilitary_556.prefab";
	private const string c_ParticleTemplatePath = "Assets/Prefabs/FX/Shooting/ShellEjection/FX_ShellEjection_Particle.prefab";
	private const string c_ParticleShotgunPath = "Assets/Prefabs/FX/Shooting/ShellEjection/FX_ShellEjection_Particle_Shotgun.prefab";
	private const string c_VfxProfilePath = "Assets/GameData/Shooting/Standalone/WeaponVfxProfile_BenelliM4.asset";
	private const string c_VfxProfileTemplatePath = "Assets/GameData/Shooting/M4/WeaponVfxProfile_M4.asset";

	private const string c_Ammo12GaugePath = "Assets/GameData/Shooting/Ammo_12Gauge.asset";
	private const string c_MagazinePath = "Assets/GameData/Shooting/Standalone/Magazine_Benelli_12G_7.asset";
	private const string c_WeaponPath = "Assets/GameData/Shooting/Standalone/Weapon_BenelliM4.asset";

	private static readonly string[] s_CqbOpticPaths =
	{
		"Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Reddot2.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_Reddot3.asset",
		"Assets/GameData/Shooting/M4/Attachment_M4_RDC.asset"
	};

	private static readonly string[] s_LongRangeOpticPaths =
	{
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

	private static readonly string[] s_M4WeaponPaths =
	{
		"Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset",
		"Assets/GameData/Shooting/M4/Weapon_M4_ModA_2.asset",
		"Assets/GameData/Shooting/M4/Weapon_M16A_ModA_1.asset",
		"Assets/GameData/Shooting/M4/Weapon_M16A4_ModA_2.asset",
		"Assets/GameData/Shooting/M4/Weapon_MK12.asset",
		"Assets/GameData/Shooting/M4/Weapon_MK18.asset"
	};

	[MenuItem("Tools/Shotgun/Build Benelli Content")]
	public static void BuildAll()
	{
		EnsureAudioFolders();
		MoveAndRenameAudioAssets();
		BuildShellCasingPrefab();
		BuildShotgunParticlePrefab();
		BuildBenelliVfxProfile();
		WireAmmo12Gauge();
		WireMagazineRoundLoadSounds();
		WireBenelliFireAudio();
		WireBenelliFireModes();
		UpdateBenelliCqbOpticCompatibility();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[BenelliShotgunContentBuilder] Benelli content built.");
	}

	public static void BuildForStandalonePipeline()
	{
		EnsureAudioFolders();
		MoveAndRenameAudioAssets();
		BuildShellCasingPrefab();
		BuildShotgunParticlePrefab();
		BuildBenelliVfxProfile();
		WireAmmo12Gauge();
		WireMagazineRoundLoadSounds();
		WireBenelliFireAudio();
		WireBenelliFireModes();
	}

	public static void UpdateBenelliCqbOpticCompatibility()
	{
		WeaponDefinition benelli = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_WeaponPath);
		if (benelli == null)
		{
			Debug.LogWarning("[BenelliShotgunContentBuilder] Missing Weapon_BenelliM4.");
			return;
		}

		var m4Weapons = new List<WeaponDefinition>();
		for (int i = 0; i < s_M4WeaponPaths.Length; i++)
		{
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(s_M4WeaponPaths[i]);
			if (weapon != null)
				m4Weapons.Add(weapon);
		}

		var cqbWeapons = new List<WeaponDefinition>(m4Weapons) { benelli };

		for (int i = 0; i < s_CqbOpticPaths.Length; i++)
			SetExplicitCompatibleWeapons(s_CqbOpticPaths[i], cqbWeapons);

		for (int i = 0; i < s_LongRangeOpticPaths.Length; i++)
			SetExplicitCompatibleWeapons(s_LongRangeOpticPaths[i], m4Weapons);
	}

	private static void EnsureAudioFolders()
	{
		EnsureFolder(c_FireAudioRoot);
		EnsureFolder(c_ShellInAudioRoot);
		EnsureFolder(c_ShellImpactAudioRoot);
	}

	private static void MoveAndRenameAudioAssets()
	{
		for (int i = 1; i <= 5; i++)
		{
			MoveAudioAsset(
				$"{c_FireSourceFolder}/Gun 10_{i}.wav",
				$"{c_FireAudioRoot}/gun_shotgun12g_fire_{i:00}.wav");
		}

		for (int i = 1; i <= 8; i++)
		{
			MoveAudioAsset(
				$"{c_ShellInSourceFolder}/gun_shotgun_load_bullet_{i:00}.wav",
				$"{c_ShellInAudioRoot}/gun_shotgun12g_shell_in_{i:00}.wav");
		}

		for (int i = 1; i <= 3; i++)
		{
			MoveAudioAsset(
				$"{c_ShellImpactSourceFolder}/CASING_Shotgun_Shell_Hard_Surface_RR{i}_mono.wav",
				$"{c_ShellImpactAudioRoot}/shell_impact_shotgun_{i:00}.wav");
		}
	}

	private static void MoveAudioAsset(string _sourcePath, string _targetPath)
	{
		if (!File.Exists(_sourcePath))
		{
			if (File.Exists(_targetPath))
				return;

			Debug.LogWarning($"[BenelliShotgunContentBuilder] Missing source audio: {_sourcePath}");
			return;
		}

		if (File.Exists(_targetPath))
			return;

		string error = AssetDatabase.MoveAsset(_sourcePath, _targetPath);
		if (!string.IsNullOrEmpty(error))
			Debug.LogWarning($"[BenelliShotgunContentBuilder] Move failed {_sourcePath} -> {_targetPath}: {error}");
	}

	private static void BuildShellCasingPrefab()
	{
		if (File.Exists(c_ShellCasingPrefabPath))
			return;

		GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(c_ShellCasingTemplatePath);
		if (template == null)
		{
			Debug.LogError("[BenelliShotgunContentBuilder] Missing shell casing template.");
			return;
		}

		EnsureFolder(Path.GetDirectoryName(c_ShellCasingPrefabPath)?.Replace('\\', '/'));
		GameObject instance = UnityEngine.Object.Instantiate(template);
		instance.name = "ShellCasing_Shotgun_12Gauge";

		Mesh shotgunMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/PolygonMilitary/Models/SM_Item_Bullet_Shotgun_01.fbx");
		if (shotgunMesh == null)
			shotgunMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Bullet_Shotgun_01.prefab");

		MeshFilter meshFilter = instance.GetComponent<MeshFilter>();
		if (meshFilter != null && shotgunMesh != null)
			meshFilter.sharedMesh = shotgunMesh;

		CapsuleCollider collider = instance.GetComponent<CapsuleCollider>();
		if (collider != null)
		{
			collider.radius = 0.012f;
			collider.height = 0.055f;
			collider.direction = 2;
		}

		Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
		if (rigidbody != null)
			rigidbody.mass = 0.012f;

		PrefabUtility.SaveAsPrefabAsset(instance, c_ShellCasingPrefabPath);
		UnityEngine.Object.DestroyImmediate(instance);
	}

	private static void BuildShotgunParticlePrefab()
	{
		if (File.Exists(c_ParticleShotgunPath))
			return;

		GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(c_ParticleTemplatePath);
		if (template == null)
		{
			Debug.LogError("[BenelliShotgunContentBuilder] Missing particle template.");
			return;
		}

		EnsureFolder(Path.GetDirectoryName(c_ParticleShotgunPath)?.Replace('\\', '/'));
		GameObject instance = UnityEngine.Object.Instantiate(template);
		instance.name = "FX_ShellEjection_Particle_Shotgun";

		Mesh shotgunMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/PolygonMilitary/Models/SM_Item_Bullet_Shotgun_01.fbx");
		ParticleSystemRenderer[] renderers = instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			if (shotgunMesh != null)
				renderers[i].mesh = shotgunMesh;
		}

		PrefabUtility.SaveAsPrefabAsset(instance, c_ParticleShotgunPath);
		UnityEngine.Object.DestroyImmediate(instance);
	}

	private static void BuildBenelliVfxProfile()
	{
		WeaponVfxProfile template = AssetDatabase.LoadAssetAtPath<WeaponVfxProfile>(c_VfxProfileTemplatePath);
		if (template == null)
		{
			Debug.LogError("[BenelliShotgunContentBuilder] Missing M4 VFX profile template.");
			return;
		}

		WeaponVfxProfile profile = AssetDatabase.LoadAssetAtPath<WeaponVfxProfile>(c_VfxProfilePath);
		if (profile == null)
		{
			EnsureFolder(Path.GetDirectoryName(c_VfxProfilePath)?.Replace('\\', '/'));
			profile = ScriptableObject.CreateInstance<WeaponVfxProfile>();
			profile.name = "WeaponVfxProfile_BenelliM4";
			AssetDatabase.CreateAsset(profile, c_VfxProfilePath);
		}

		EditorUtility.CopySerialized(template, profile);
		profile.name = "WeaponVfxProfile_BenelliM4";

		var so = new SerializedObject(profile);
		so.FindProperty("m_ShellEjectionMode").enumValueIndex = 2;
		so.FindProperty("m_ShellParticlePrefab").objectReferenceValue =
			AssetDatabase.LoadAssetAtPath<GameObject>(c_ParticleShotgunPath);
		so.FindProperty("m_ShellParticleScale").floatValue = 2.2f;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(profile);

		WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_WeaponPath);
		if (weapon != null)
		{
			var weaponSo = new SerializedObject(weapon);
			weaponSo.FindProperty("m_VfxProfile").objectReferenceValue = profile;
			weaponSo.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(weapon);
		}
	}

	private static void WireAmmo12Gauge()
	{
		AmmoDefinition ammo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(c_Ammo12GaugePath);
		GameObject shellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(c_ShellCasingPrefabPath);
		if (ammo == null || shellPrefab == null)
			return;

		AudioClip[] impactClips = LoadAudioClips(c_ShellImpactAudioRoot, "shell_impact_shotgun_", 3);
		var so = new SerializedObject(ammo);
		so.FindProperty("m_ShellPrefab").objectReferenceValue = shellPrefab;
		so.FindProperty("m_ShellEjectSpeed").floatValue = 4f;
		so.FindProperty("m_ShellEjectSpeedVariance").floatValue = 0.6f;
		so.FindProperty("m_ShellEjectUpSpeed").floatValue = 1.5f;
		so.FindProperty("m_ShellAngularVelocity").floatValue = 12f;
		WriteAudioClipArray(so.FindProperty("m_ShellImpactSounds"), impactClips);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(ammo);
	}

	private static void WireMagazineRoundLoadSounds()
	{
		MagazineDefinition magazine = AssetDatabase.LoadAssetAtPath<MagazineDefinition>(c_MagazinePath);
		if (magazine == null)
			return;

		AudioClip[] clips = LoadAudioClips(c_ShellInAudioRoot, "gun_shotgun12g_shell_in_", 8);
		var so = new SerializedObject(magazine);
		WriteAudioClipArray(so.FindProperty("m_RoundLoadSounds"), clips);
		so.FindProperty("m_RoundLoadSoundsVolume").floatValue = 0.85f;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(magazine);
	}

	public static void WireBenelliFireAudio()
	{
		WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_WeaponPath);
		if (weapon == null)
			return;

		AudioClip[] clips = LoadAudioClips(c_FireAudioRoot, "gun_shotgun12g_fire_", 5);
		var so = new SerializedObject(weapon);
		SerializedProperty fireProfile = so.FindProperty("m_FireSoundProfile");
		if (fireProfile == null)
			return;

		WriteAudioClipArray(fireProfile.FindPropertyRelative("m_FireClips"), clips);
		fireProfile.FindPropertyRelative("m_MaxAudibleDistanceMeters").floatValue = 625f;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
	}

	public static void WireBenelliFireModes()
	{
		WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(c_WeaponPath);
		if (weapon == null)
			return;

		var so = new SerializedObject(weapon);
		SerializedProperty fireModes = so.FindProperty("m_AvailableFireModes");
		fireModes.arraySize = 1;
		fireModes.GetArrayElementAtIndex(0).enumValueIndex = (int)WeaponFireMode.SemiAuto;
		so.FindProperty("m_DefaultFireMode").enumValueIndex = (int)WeaponFireMode.SemiAuto;
		so.FindProperty("m_FireRateRpm").floatValue = 180f;
		so.FindProperty("m_SemiAutoFireRateRpm").floatValue = 180f;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(weapon);
	}

	private static void SetExplicitCompatibleWeapons(string _attachmentPath, List<WeaponDefinition> _weapons)
	{
		WeaponAttachmentDefinition attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_attachmentPath);
		if (attachment == null)
		{
			Debug.LogWarning($"[BenelliShotgunContentBuilder] Missing attachment: {_attachmentPath}");
			return;
		}

		var so = new SerializedObject(attachment);
		so.FindProperty("m_UseExplicitWeaponCompatibility").boolValue = true;
		SerializedProperty weaponsProp = so.FindProperty("m_CompatibleWeapons");
		weaponsProp.arraySize = _weapons.Count;
		for (int i = 0; i < _weapons.Count; i++)
			weaponsProp.GetArrayElementAtIndex(i).objectReferenceValue = _weapons[i];

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(attachment);
	}

	private static AudioClip[] LoadAudioClips(string _folder, string _prefix, int _count)
	{
		var clips = new List<AudioClip>(_count);
		for (int i = 1; i <= _count; i++)
		{
			string path = $"{_folder}/{_prefix}{i:00}.wav";
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
			if (clip != null)
				clips.Add(clip);
		}

		return clips.ToArray();
	}

	private static void WriteAudioClipArray(SerializedProperty _array, AudioClip[] _clips)
	{
		if (_array == null)
			return;

		_array.arraySize = _clips != null ? _clips.Length : 0;
		for (int i = 0; i < _array.arraySize; i++)
			_array.GetArrayElementAtIndex(i).objectReferenceValue = _clips[i];
	}

	private static void EnsureFolder(string _folder)
	{
		if (string.IsNullOrEmpty(_folder))
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
}
#endif
