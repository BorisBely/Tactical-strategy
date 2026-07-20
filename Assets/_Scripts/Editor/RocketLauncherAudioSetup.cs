#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Переименовывает и переносит wav-файлы гранатомётов в Assets/Audio/Combat/RocketLaunchers и привязывает к RocketLauncherData.
/// </summary>
public static class RocketLauncherAudioSetup
{
	#region Constants
	private const string c_MarkerPath = "Assets/.rocket_launcher_audio_setup_done";
	private const string c_DataPath = "Assets/GameData/Combat/RocketLauncherData.asset";
	private const string c_AudioRoot = "Assets/Audio/Combat/RocketLaunchers";

	private const string c_SrcWhoosh = "Assets/FLYBY_Missile_02_Slow_mono.wav";
	private const string c_SrcFlyby = "Assets/FLYBY_Missile_01_Fast_mono.wav";
	private const string c_SrcDisposableAccent = "Assets/Pistol Shot_14.wav";
	private const string c_SrcRpgAccentPreferred = "Assets/Pistol Shot_15.wav";
	private const string c_SrcRpgAccentFallback = "Assets/Pistol Shot_13.wav";
	private const string c_SrcRpgReloadInsert = "Assets/Gun Reload 9_3.wav";
	private const string c_SrcRpgReloadInsertFallback =
		"Assets/SFX/0_Gun & Explosion Sounds/Gun Additional/Gun Reload 9_3.wav";

	private static readonly string[] c_SrcDisposableExplosions =
	{
		"Assets/Small Explosion 01.wav",
		"Assets/Small Explosion 02.wav",
		"Assets/Small Explosion 03.wav",
	};

	private static readonly string[] c_SrcRpgExplosions =
	{
		"Assets/Small Explosion 04.wav",
		"Assets/Small Explosion 05.wav",
	};

	private const string c_DstWhoosh = c_AudioRoot + "/Fire/rocket_launcher_fire_whoosh_01.wav";
	private const string c_DstFlyby = c_AudioRoot + "/Flyby/rocket_launcher_flyby_01.wav";
	private const string c_DstDisposableAccent = c_AudioRoot + "/Fire/rocket_launcher_disposable_fire_accent_01.wav";
	private const string c_DstRpgAccent = c_AudioRoot + "/Fire/rocket_launcher_rpg_fire_accent_01.wav";
	private const string c_DstRpgReloadInsert = c_AudioRoot + "/Reload/rocket_launcher_rpg_reload_insert_01.wav";
	#endregion

	#region Bootstrap
	[InitializeOnLoadMethod]
	private static void AutoSetupOnce()
	{
		EditorApplication.delayCall += () =>
		{
			if (System.IO.File.Exists(c_MarkerPath))
				return;

			try
			{
				RunSetup();
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[RocketLauncherAudioSetup] Deferred: {ex.Message}");
			}
		};
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Setup Rocket Launcher Audio")]
	public static void RunSetup()
	{
		EnsureFolder(c_AudioRoot);
		EnsureFolder(c_AudioRoot + "/Fire");
		EnsureFolder(c_AudioRoot + "/Flyby");
		EnsureFolder(c_AudioRoot + "/Explosion/Disposable");
		EnsureFolder(c_AudioRoot + "/Explosion/Rpg");
		EnsureFolder(c_AudioRoot + "/Reload");

		MoveOrCopy(c_SrcWhoosh, c_DstWhoosh);
		MoveOrCopy(c_SrcFlyby, c_DstFlyby);
		MoveOrCopy(c_SrcDisposableAccent, c_DstDisposableAccent);

		string rpgAccentSrc = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SrcRpgAccentPreferred) != null
			? c_SrcRpgAccentPreferred
			: c_SrcRpgAccentFallback;
		if (rpgAccentSrc == c_SrcRpgAccentFallback)
			Debug.LogWarning("[RocketLauncherAudioSetup] Pistol Shot_15 not found — using Pistol Shot_13 for RPG fire accent.");

		MoveOrCopy(rpgAccentSrc, c_DstRpgAccent);

		string reloadInsertSrc = AssetDatabase.LoadAssetAtPath<AudioClip>(c_SrcRpgReloadInsert) != null
			? c_SrcRpgReloadInsert
			: c_SrcRpgReloadInsertFallback;
		MoveOrCopy(reloadInsertSrc, c_DstRpgReloadInsert);

		AudioClip[] disposableExplosions = MoveExplosionSet(
			c_SrcDisposableExplosions,
			c_AudioRoot + "/Explosion/Disposable",
			"rocket_launcher_disposable_explosion");
		AudioClip[] rpgExplosions = MoveExplosionSet(
			c_SrcRpgExplosions,
			c_AudioRoot + "/Explosion/Rpg",
			"rocket_launcher_rpg_explosion");

		RocketLauncherData data = AssetDatabase.LoadAssetAtPath<RocketLauncherData>(c_DataPath);
		if (data == null)
		{
			Debug.LogError($"[RocketLauncherAudioSetup] Missing {c_DataPath}");
			return;
		}

		SerializedObject so = new SerializedObject(data);
		SetClipSet(so, "m_FireWhooshClips", LoadClip(c_DstWhoosh));
		SetClipSet(so, "m_DisposableFireAccentClips", LoadClip(c_DstDisposableAccent));
		SetClipSet(so, "m_RpgFireAccentClips", LoadClip(c_DstRpgAccent));
		SetClipSet(so, "m_FlybyClips", LoadClip(c_DstFlyby));
		SetClipSet(so, "m_DisposableExplosionClips", disposableExplosions);
		SetClipSet(so, "m_RpgExplosionClips", rpgExplosions);
		SetClipSet(so, "m_RpgReloadInsertClips", LoadClip(c_DstRpgReloadInsert));
		SetFloat(so, "m_FireWhooshVolume", 1f);
		SetFloat(so, "m_FireAccentVolume", 0.95f);
		SetFloat(so, "m_FireAudioMaxDistance", 120f);
		SetFloat(so, "m_FlybyVolume", 1f);
		SetFloat(so, "m_FlybyRadiusMeters", 10f);
		SetFloat(so, "m_FlybyMinSpawnDistanceMeters", 2.5f);
		SetFloat(so, "m_ExplosionAudioVolume", 1f);
		SetFloat(so, "m_ExplosionAudioMaxDistance", 110f);
		SetFloat(so, "m_RpgReloadInsertVolume", 1f);
		SetFloat(so, "m_RpgReloadInsertMaxDistance", 45f);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(data);

		System.IO.File.WriteAllText(c_MarkerPath, System.DateTime.UtcNow.ToString("o"));
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[RocketLauncherAudioSetup] Rocket launcher audio moved, renamed, and wired to RocketLauncherData.");
	}
	#endregion

	#region Private
	private static AudioClip[] MoveExplosionSet(string[] _sources, string _folder, string _namePrefix)
	{
		AudioClip[] clips = new AudioClip[_sources.Length];
		for (int i = 0; i < _sources.Length; i++)
		{
			string dst = $"{_folder}/{_namePrefix}_{(i + 1):00}.wav";
			MoveOrCopy(_sources[i], dst);
			clips[i] = LoadClip(dst);
		}

		return clips;
	}

	private static void MoveOrCopy(string _src, string _dst)
	{
		if (AssetDatabase.LoadAssetAtPath<AudioClip>(_dst) != null)
			return;

		if (AssetDatabase.LoadAssetAtPath<AudioClip>(_src) == null)
		{
			Debug.LogWarning($"[RocketLauncherAudioSetup] Missing source clip: {_src}");
			return;
		}

		string error = AssetDatabase.MoveAsset(_src, _dst);
		if (!string.IsNullOrEmpty(error))
		{
			if (!AssetDatabase.CopyAsset(_src, _dst))
				Debug.LogWarning($"[RocketLauncherAudioSetup] Failed to move/copy {_src} -> {_dst}: {error}");
			else
				AssetDatabase.DeleteAsset(_src);
		}
	}

	private static AudioClip LoadClip(string _path) =>
		AssetDatabase.LoadAssetAtPath<AudioClip>(_path);

	private static void SetClipSet(SerializedObject _so, string _propertyName, params AudioClip[] _clips)
	{
		SerializedProperty set = _so.FindProperty(_propertyName);
		if (set == null)
			return;

		SerializedProperty clips = set.FindPropertyRelative("m_Clips");
		if (clips == null || !clips.isArray)
			return;

		clips.arraySize = _clips.Length;
		for (int i = 0; i < _clips.Length; i++)
			clips.GetArrayElementAtIndex(i).objectReferenceValue = _clips[i];
	}

	private static void SetFloat(SerializedObject _so, string _name, float _value)
	{
		SerializedProperty prop = _so.FindProperty(_name);
		if (prop != null)
			prop.floatValue = _value;
	}

	private static void EnsureFolder(string _path)
	{
		if (AssetDatabase.IsValidFolder(_path))
			return;

		string parent = System.IO.Path.GetDirectoryName(_path)?.Replace('\\', '/');
		string name = System.IO.Path.GetFileName(_path);
		if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, name);
	}
	#endregion
}
#endif
