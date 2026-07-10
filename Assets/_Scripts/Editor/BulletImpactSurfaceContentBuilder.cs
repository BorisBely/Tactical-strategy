#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Прошивает звуки/декали попадания по поверхностям в WeaponVfxProfile
/// и назначает тестовые материалы мишеням полигона.
/// </summary>
public static class BulletImpactSurfaceContentBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_ImpactAudioRoot = "Assets/Audio/Combat/Bullet/Impact";
	private const string c_DecalRoot = "Assets/PolygonMilitary/Prefabs/FX/Decals";
	private const string c_PhysicsMaterialRoot = "Assets/GameData/Audio/FootstepSurfaces";
	private const string c_VisualMaterialRoot = "Assets/GameData/Audio/FootstepSurfaces/VisualMaterials";
	private const int c_TargetLayerBit = 1 << 8; // Target
	#endregion

	#region Surface Specs
	private static readonly string[] s_Surfaces = { "Concrete", "Metal", "Wood", "Glass" };

	private static readonly string[] s_VfxProfilePaths =
	{
		"Assets/GameData/Shooting/M4/WeaponVfxProfile_M4.asset",
		"Assets/GameData/Shooting/AK/WeaponVfxProfile_AK47.asset",
		"Assets/GameData/Shooting/Standalone/WeaponVfxProfile_BenelliM4.asset"
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Audio/Build Bullet Impact Surface Content")]
	public static void BuildBulletImpactSurfaceContent()
	{
		AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

		Dictionary<string, PhysicsMaterial> physicsMaterials = LoadPhysicsMaterials();
		Dictionary<string, Material> visualMaterials = LoadVisualMaterials();
		Dictionary<string, AudioClip[]> clipsBySurface = LoadImpactClipsBySurface();
		Dictionary<string, GameObject[]> decalsBySurface = LoadDecalsBySurface();

		for (int i = 0; i < s_VfxProfilePaths.Length; i++)
			WireVfxProfile(s_VfxProfilePaths[i], physicsMaterials, clipsBySurface, decalsBySurface);

		EnsureSceneLoaded();
		WireShootingRangeManager(physicsMaterials, visualMaterials);
		ApplySurfacesToRangeTargets(physicsMaterials, visualMaterials);

		EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
		EditorSceneManager.SaveOpenScenes();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Debug.Log("[BulletImpactSurfaceContentBuilder] Impact surfaces, VFX profiles and range targets are ready.");
	}
	#endregion

	#region Load
	private static Dictionary<string, PhysicsMaterial> LoadPhysicsMaterials()
	{
		var result = new Dictionary<string, PhysicsMaterial>(StringComparer.Ordinal);
		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i];
			string path = $"{c_PhysicsMaterialRoot}/FootstepSurface_{surface}.physicMaterial";
			PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
			if (material == null)
				Debug.LogWarning($"[BulletImpactSurfaceContentBuilder] Missing physics material: {path}");
			else
				result[surface] = material;
		}

		return result;
	}

	private static Dictionary<string, Material> LoadVisualMaterials()
	{
		var result = new Dictionary<string, Material>(StringComparer.Ordinal);
		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i];
			string path = $"{c_VisualMaterialRoot}/FootstepVisual_{surface}.mat";
			Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material == null)
				Debug.LogWarning($"[BulletImpactSurfaceContentBuilder] Missing visual material: {path}");
			else
				result[surface] = material;
		}

		return result;
	}

	private static Dictionary<string, AudioClip[]> LoadImpactClipsBySurface()
	{
		var result = new Dictionary<string, AudioClip[]>(StringComparer.Ordinal);
		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i];
			string folder = $"{c_ImpactAudioRoot}/{surface}";
			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
			var clips = new List<AudioClip>(guids.Length);
			for (int g = 0; g < guids.Length; g++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[g]);
				AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
				if (clip != null)
					clips.Add(clip);
			}

			clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			result[surface] = clips.ToArray();
			Debug.Log($"[BulletImpactSurfaceContentBuilder] {surface}: {clips.Count} impact clips.");
		}

		return result;
	}

	private static Dictionary<string, GameObject[]> LoadDecalsBySurface()
	{
		var result = new Dictionary<string, GameObject[]>(StringComparer.Ordinal);
		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i];
			var prefabs = new List<GameObject>(3);
			for (int variant = 1; variant <= 3; variant++)
			{
				string path = $"{c_DecalRoot}/FX_Decal_Bullet_{surface}_{variant:00}.prefab";
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab != null)
					prefabs.Add(prefab);
			}

			result[surface] = prefabs.ToArray();
			if (prefabs.Count == 0)
				Debug.LogWarning($"[BulletImpactSurfaceContentBuilder] No decals for {surface}.");
		}

		return result;
	}
	#endregion

	#region VFX Profiles
	private static void WireVfxProfile(
		string _profilePath,
		Dictionary<string, PhysicsMaterial> _physicsMaterials,
		Dictionary<string, AudioClip[]> _clipsBySurface,
		Dictionary<string, GameObject[]> _decalsBySurface)
	{
		WeaponVfxProfile profile = AssetDatabase.LoadAssetAtPath<WeaponVfxProfile>(_profilePath);
		if (profile == null)
		{
			Debug.LogWarning($"[BulletImpactSurfaceContentBuilder] Missing VFX profile: {_profilePath}");
			return;
		}

		var so = new SerializedObject(profile);
		so.FindProperty("m_EnableImpactDecals").boolValue = true;
		so.FindProperty("m_EnableImpactAudio").boolValue = true;
		so.FindProperty("m_ImpactSurfaceLayers").intValue = c_TargetLayerBit;
		so.FindProperty("m_DefaultSurfaceName").stringValue = "Concrete";
		so.FindProperty("m_DecalSurfaceOffset").floatValue = 0.012f;
		so.FindProperty("m_DecalScale").floatValue = 0.45f;
		so.FindProperty("m_DecalLifetimeSeconds").floatValue = 20f;
		so.FindProperty("m_ImpactAudioMaxDistance").floatValue = 45f;

		SerializedProperty surfacesProp = so.FindProperty("m_ImpactSurfaces");
		surfacesProp.arraySize = s_Surfaces.Length;
		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i];
			SerializedProperty setProp = surfacesProp.GetArrayElementAtIndex(i);
			setProp.FindPropertyRelative("SurfaceName").stringValue = surface;
			setProp.FindPropertyRelative("PhysicsMaterial").objectReferenceValue =
				_physicsMaterials.TryGetValue(surface, out PhysicsMaterial physicsMaterial) ? physicsMaterial : null;
			setProp.FindPropertyRelative("ImpactVolume").floatValue =
				surface == "Glass" ? 1f : 0.85f;

			WriteObjectArray(
				setProp.FindPropertyRelative("DecalPrefabs"),
				_decalsBySurface.TryGetValue(surface, out GameObject[] decals) ? decals : Array.Empty<GameObject>());
			WriteObjectArray(
				setProp.FindPropertyRelative("ImpactSounds"),
				_clipsBySurface.TryGetValue(surface, out AudioClip[] clips) ? clips : Array.Empty<AudioClip>());
		}

		// Clear legacy concrete-only fields if they still exist on older serialized assets.
		SerializedProperty legacyDecals = so.FindProperty("m_ConcreteImpactDecalPrefabs");
		if (legacyDecals != null)
			legacyDecals.arraySize = 0;

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(profile);
	}

	private static void WriteObjectArray(SerializedProperty _array, UnityEngine.Object[] _items)
	{
		if (_array == null)
			return;

		_array.arraySize = _items != null ? _items.Length : 0;
		for (int i = 0; i < _array.arraySize; i++)
			_array.GetArrayElementAtIndex(i).objectReferenceValue = _items[i];
	}
	#endregion

	#region Scene
	private static void EnsureSceneLoaded()
	{
		Scene active = SceneManager.GetActiveScene();
		if (active.path == c_ScenePath)
			return;

		EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
	}

	private static void WireShootingRangeManager(
		Dictionary<string, PhysicsMaterial> _physicsMaterials,
		Dictionary<string, Material> _visualMaterials)
	{
#if UNITY_2023_1_OR_NEWER
		ShootingRangeManager manager = UnityEngine.Object.FindAnyObjectByType<ShootingRangeManager>(FindObjectsInactive.Exclude);
#else
		ShootingRangeManager manager = UnityEngine.Object.FindObjectOfType<ShootingRangeManager>();
#endif
		if (manager == null)
		{
			Debug.LogWarning("[BulletImpactSurfaceContentBuilder] ShootingRangeManager not found in scene.");
			return;
		}

		var so = new SerializedObject(manager);
		so.FindProperty("m_AssignImpactTestSurfaces").boolValue = true;
		so.FindProperty("m_SurfaceConcrete").objectReferenceValue = GetOrNull(_physicsMaterials, "Concrete");
		so.FindProperty("m_SurfaceMetal").objectReferenceValue = GetOrNull(_physicsMaterials, "Metal");
		so.FindProperty("m_SurfaceWood").objectReferenceValue = GetOrNull(_physicsMaterials, "Wood");
		so.FindProperty("m_SurfaceGlass").objectReferenceValue = GetOrNull(_physicsMaterials, "Glass");
		so.FindProperty("m_VisualConcrete").objectReferenceValue = GetOrNull(_visualMaterials, "Concrete");
		so.FindProperty("m_VisualMetal").objectReferenceValue = GetOrNull(_visualMaterials, "Metal");
		so.FindProperty("m_VisualWood").objectReferenceValue = GetOrNull(_visualMaterials, "Wood");
		so.FindProperty("m_VisualGlass").objectReferenceValue = GetOrNull(_visualMaterials, "Glass");
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(manager);
	}

	private static void ApplySurfacesToRangeTargets(
		Dictionary<string, PhysicsMaterial> _physicsMaterials,
		Dictionary<string, Material> _visualMaterials)
	{
		string[] targetNames =
		{
			"Sphere50", "Sphere100", "Sphere150", "Sphere200", "Sphere250",
			"Sphere300", "Sphere350", "Sphere400", "Sphere450", "Sphere500"
		};

		Color[] intactColors =
		{
			new Color(0.55f, 0.55f, 0.55f, 1f),
			new Color(0.70f, 0.72f, 0.76f, 1f),
			new Color(0.58f, 0.36f, 0.18f, 1f),
			new Color(0.55f, 0.78f, 0.92f, 1f)
		};
		Color disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.25f);

		for (int i = 0; i < targetNames.Length; i++)
		{
			GameObject go = GameObject.Find(targetNames[i]);
			if (go == null)
				continue;

			string surface = s_Surfaces[i % s_Surfaces.Length];
			if (!_physicsMaterials.TryGetValue(surface, out PhysicsMaterial physicsMaterial))
				continue;

			if (go.TryGetComponent(out Collider collider))
				collider.sharedMaterial = physicsMaterial;

			if (_visualMaterials.TryGetValue(surface, out Material visualMaterial) &&
			    go.TryGetComponent(out Renderer renderer))
			{
				renderer.sharedMaterial = visualMaterial;
			}

			if (go.TryGetComponent(out ShootingRangeTarget target))
				target.ConfigureSurfaceVisual(intactColors[i % intactColors.Length], disabledColor);

			EditorUtility.SetDirty(go);
		}
	}

	private static T GetOrNull<T>(Dictionary<string, T> _map, string _key) where T : class
	{
		return _map.TryGetValue(_key, out T value) ? value : null;
	}
	#endregion
}
#endif
