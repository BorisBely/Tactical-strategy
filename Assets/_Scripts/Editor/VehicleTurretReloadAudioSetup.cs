#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Переносит Gun Reload 4_5 в Audio/Combat/Turret/Reload и привязывает к перезарядке турели.
/// </summary>
public static class VehicleTurretReloadAudioSetup
{
	private const string c_VehiclePrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_SrcClip =
		"Assets/SFX/0_Gun & Explosion Sounds/Gun Additional/Gun Reload 4_5.wav";
	private const string c_DstClip = "Assets/Audio/Combat/Turret/Reload/turret_gunner_handle_pull_01.wav";

	[MenuItem("Polygone/Vehicles/Setup Turret Reload Handle Audio")]
	public static void RunSetup()
	{
		EnsureFolder("Assets/Audio/Combat/Turret/Reload");
		MoveOrCopy(c_SrcClip, c_DstClip);

		AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(c_DstClip);
		if (clip == null)
		{
			Debug.LogError($"[VehicleTurretReloadAudioSetup] Missing clip at {c_DstClip}");
			return;
		}

		GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(c_VehiclePrefabPath);
		if (prefabRoot == null)
		{
			Debug.LogError($"[VehicleTurretReloadAudioSetup] Missing prefab {c_VehiclePrefabPath}");
			return;
		}

		GameObject prefabContents = PrefabUtility.LoadPrefabContents(c_VehiclePrefabPath);
		try
		{
			if (!prefabContents.TryGetComponent(out VehicleTurretReloadController reload))
			{
				Debug.LogError("[VehicleTurretReloadAudioSetup] VehicleTurretReloadController not found on prefab.");
				return;
			}

			SerializedObject so = new SerializedObject(reload);
			so.FindProperty("m_HandlePullClip").objectReferenceValue = clip;
			so.FindProperty("m_HandlePullVolume").floatValue = 0.85f;
			so.FindProperty("m_HandlePullMaxDistance").floatValue = 35f;
			so.ApplyModifiedPropertiesWithoutUndo();

			EditorUtility.SetDirty(prefabContents);
			PrefabUtility.SaveAsPrefabAsset(prefabContents, c_VehiclePrefabPath);
			Debug.Log("[VehicleTurretReloadAudioSetup] Handle pull audio wired on Light_Armored_Car.");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabContents);
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	private static void MoveOrCopy(string _src, string _dst)
	{
		if (AssetDatabase.LoadAssetAtPath<AudioClip>(_dst) != null)
			return;

		if (AssetDatabase.LoadAssetAtPath<AudioClip>(_src) == null)
		{
			Debug.LogWarning($"[VehicleTurretReloadAudioSetup] Source missing (already moved?): {_src}");
			return;
		}

		string error = AssetDatabase.MoveAsset(_src, _dst);
		if (!string.IsNullOrEmpty(error) && !AssetDatabase.CopyAsset(_src, _dst))
			Debug.LogWarning($"[VehicleTurretReloadAudioSetup] Failed to move {_src} -> {_dst}: {error}");
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
}
#endif
