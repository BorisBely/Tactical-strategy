#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Копирует smoke FX из PolygonMilitary, настраивает под дымовую гранату
/// и выкладывает все варианты на SampleScene для сравнения.
/// </summary>
[InitializeOnLoad]
internal static class GrenadeSmokeVfxComparisonBootstrap
{
	#region Constants
	private const string c_MarkerPath = "Assets/.grenade_smoke_vfx_polygon_setup_done";
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_ThrowDataPath = "Assets/GameData/Combat/GrenadeThrowData.asset";
	private const string c_OutputFolder = "Assets/Prefabs/FX/Grenades/Smoke";
	private const string c_MaterialFolder = "Assets/Prefabs/FX/Grenades/Materials";
	private const string c_SmokeSoftMaterialPath = "Assets/Prefabs/FX/Grenades/Materials/Mat_Grenade_Smoke_URP_Soft.mat";
	private const string c_SmokeDarkMaterialPath = "Assets/Prefabs/FX/Grenades/Materials/Mat_Grenade_Smoke_URP_Dark.mat";
	private const string c_SmokeTexturePath = "Assets/PolygonMilitary/Prefabs/FX/Materials/FX_Tex_Cicle.png";
	private const string c_LegacySoftMaterialPath = "Assets/PolygonMilitary/Prefabs/FX/Materials/FX_Soft.mat";
	private const string c_LegacyDarkMaterialPath = "Assets/PolygonMilitary/Prefabs/FX/Materials/FX_Dark.mat";
	private const string c_LegacyCircleMaterialPath = "Assets/PolygonMilitary/Prefabs/FX/Materials/FX_Circle.mat";
	private const string c_SceneRootName = "SmokeVfxComparison";
	private const float c_SpacingMeters = 16f;
	private const float c_OriginX = 55f;
	private const float c_OriginY = 0.05f;
	private const float c_OriginZ = -35f;

	private static readonly SmokeSource[] s_Sources =
	{
		new SmokeSource("FX_Grenade_Smoke_Small_01", "Assets/PolygonMilitary/Prefabs/FX/FX_Smoke_Small_01.prefab"),
		new SmokeSource("FX_Grenade_Smoke_01", "Assets/PolygonMilitary/Prefabs/FX/FX_Smoke_01.prefab"),
		new SmokeSource("FX_Grenade_Smoke_Medium_01", "Assets/PolygonMilitary/Prefabs/FX/FX_Smoke_Medium_01.prefab"),
		new SmokeSource("FX_Grenade_Smoke_Large_01", "Assets/PolygonMilitary/Prefabs/FX/FX_Smoke_Large_01.prefab"),
		new SmokeSource("FX_Grenade_Smoke_Huge_01", "Assets/PolygonMilitary/Prefabs/FX/FX_Smoke_Huge_01.prefab"),
		new SmokeSource("FX_Grenade_Smoke_Huge_02", "Assets/PolygonMilitary/Prefabs/FX/FX_Smoke_Huge_02.prefab"),
	};
	#endregion

	#region Types
	private readonly struct SmokeSource
	{
		public readonly string OutputName;
		public readonly string SourcePath;

		public SmokeSource(string _outputName, string _sourcePath)
		{
			OutputName = _outputName;
			SourcePath = _sourcePath;
		}
	}
	#endregion

	#region Bootstrap
	static GrenadeSmokeVfxComparisonBootstrap()
	{
		// Polygon smoke comparison is opt-in via menu only.
	}

	[MenuItem("Polygone/Combat/Setup Grenade Smoke VFX (Polygon)")]
	public static void RunFullSetup()
	{
		EnsureOutputFolder();
		EnsureUrpSmokeMaterials();
		GameObject[] tunedPrefabs = CopyAndTuneAllSmokePrefabs();
		WireThrowData(tunedPrefabs);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		File.WriteAllText(c_MarkerPath, "done");
		File.WriteAllText("Assets/.grenade_smoke_vfx_urp_materials_v1", "done");
		Debug.Log("[GrenadeSmokeVfxComparisonBootstrap] Polygon smoke prefabs copied and tuned. Scene layout is opt-in via menu.");
	}

	[MenuItem("Polygone/Combat/Retune Grenade Smoke Prefabs")]
	public static void RetuneSmokePrefabsOnly()
	{
		EnsureOutputFolder();
		EnsureUrpSmokeMaterials();
		GameObject[] tunedPrefabs = CopyAndTuneAllSmokePrefabs();
		WireThrowData(tunedPrefabs);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[GrenadeSmokeVfxComparisonBootstrap] Grenade smoke prefabs retuned. Use 'Layout Smoke VFX Comparison On Scene' only if you want a scene preview.");
	}

	[MenuItem("Polygone/Combat/Layout Smoke VFX Comparison On Scene")]
	public static void LayoutOnly()
	{
		GameObject[] prefabs = LoadTunedPrefabs();
		if (prefabs.Length == 0)
		{
			Debug.LogWarning("[GrenadeSmokeVfxComparisonBootstrap] No tuned smoke prefabs found. Run full setup first.");
			return;
		}

		LayoutComparisonOnScene(prefabs);
		AssetDatabase.SaveAssets();
	}
	#endregion

	#region Prefab Pipeline
	private static void EnsureOutputFolder()
	{
		if (!AssetDatabase.IsValidFolder("Assets/Prefabs/FX"))
			AssetDatabase.CreateFolder("Assets/Prefabs/FX", "Grenades");
		else if (!AssetDatabase.IsValidFolder("Assets/Prefabs/FX/Grenades"))
			AssetDatabase.CreateFolder("Assets/Prefabs/FX", "Grenades");

		if (!AssetDatabase.IsValidFolder(c_OutputFolder))
			AssetDatabase.CreateFolder("Assets/Prefabs/FX/Grenades", "Smoke");

		if (!AssetDatabase.IsValidFolder(c_MaterialFolder))
			AssetDatabase.CreateFolder("Assets/Prefabs/FX/Grenades", "Materials");
	}

	private static void EnsureUrpSmokeMaterials()
	{
		Material soft = CreateOrUpdateUrpSmokeMaterial(
			c_SmokeSoftMaterialPath,
			new Color(0.82f, 0.82f, 0.8f, 0.55f));
		Material dark = CreateOrUpdateUrpSmokeMaterial(
			c_SmokeDarkMaterialPath,
			new Color(0.45f, 0.45f, 0.43f, 0.72f));

		if (soft == null || dark == null)
			Debug.LogError("[GrenadeSmokeVfxComparisonBootstrap] Failed to create URP smoke materials. Particle FX will stay invisible.");
	}

	private static Material CreateOrUpdateUrpSmokeMaterial(string _path, Color _baseColor)
	{
		Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
		if (shader == null)
			shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
		if (shader == null)
			shader = Shader.Find("Particles/Standard Unlit");

		if (shader == null)
		{
			Debug.LogError("[GrenadeSmokeVfxComparisonBootstrap] No compatible particle shader found for URP smoke materials.");
			return null;
		}

		Material material = AssetDatabase.LoadAssetAtPath<Material>(_path);
		bool isNew = material == null;
		if (isNew)
			material = new Material(shader);

		material.shader = shader;
		material.name = Path.GetFileNameWithoutExtension(_path);

		Texture smokeTexture = AssetDatabase.LoadAssetAtPath<Texture>(c_SmokeTexturePath);
		if (smokeTexture != null)
		{
			if (material.HasProperty("_BaseMap"))
				material.SetTexture("_BaseMap", smokeTexture);
			if (material.HasProperty("_MainTex"))
				material.SetTexture("_MainTex", smokeTexture);
		}

		if (material.HasProperty("_BaseColor"))
			material.SetColor("_BaseColor", _baseColor);
		if (material.HasProperty("_Color"))
			material.SetColor("_Color", _baseColor);
		if (material.HasProperty("_Surface"))
			material.SetFloat("_Surface", 1f);
		if (material.HasProperty("_Blend"))
			material.SetFloat("_Blend", 0f);
		if (material.HasProperty("_SrcBlend"))
			material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
		if (material.HasProperty("_DstBlend"))
			material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		if (material.HasProperty("_ZWrite"))
			material.SetFloat("_ZWrite", 0f);

		material.renderQueue = 3000;
		material.enableInstancing = true;

		if (isNew)
			AssetDatabase.CreateAsset(material, _path);
		else
			EditorUtility.SetDirty(material);

		return material;
	}

	private static GameObject[] CopyAndTuneAllSmokePrefabs()
	{
		GameObject[] results = new GameObject[s_Sources.Length];
		for (int i = 0; i < s_Sources.Length; i++)
			results[i] = CopyAndTuneSmokePrefab(s_Sources[i]);

		return results;
	}

	private static GameObject CopyAndTuneSmokePrefab(SmokeSource _source)
	{
		string destPath = $"{c_OutputFolder}/{_source.OutputName}.prefab";
		if (!File.Exists(_source.SourcePath))
		{
			Debug.LogError($"[GrenadeSmokeVfxComparisonBootstrap] Missing source prefab: {_source.SourcePath}");
			return null;
		}

		if (!File.Exists(destPath))
		{
			if (!AssetDatabase.CopyAsset(_source.SourcePath, destPath))
			{
				Debug.LogError($"[GrenadeSmokeVfxComparisonBootstrap] Failed to copy {_source.SourcePath}");
				return null;
			}
		}

		GameObject root = PrefabUtility.LoadPrefabContents(destPath);
		try
		{
			root.name = _source.OutputName;
			SetChildActiveRecursive(root.transform, "FX_Smoke_Huge_Fire_01", false);
			TuneSmokeParticleSystems(root);

			PrefabUtility.SaveAsPrefabAsset(root, destPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}

		return AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
	}

	private static void TuneSmokeParticleSystems(GameObject _root)
	{
		Material softMaterial = AssetDatabase.LoadAssetAtPath<Material>(c_SmokeSoftMaterialPath);
		Material darkMaterial = AssetDatabase.LoadAssetAtPath<Material>(c_SmokeDarkMaterialPath);
		Material legacySoft = AssetDatabase.LoadAssetAtPath<Material>(c_LegacySoftMaterialPath);
		Material legacyDark = AssetDatabase.LoadAssetAtPath<Material>(c_LegacyDarkMaterialPath);
		Material legacyCircle = AssetDatabase.LoadAssetAtPath<Material>(c_LegacyCircleMaterialPath);

		ParticleSystem[] systems = _root.GetComponentsInChildren<ParticleSystem>(true);
		for (int i = 0; i < systems.Length; i++)
		{
			ParticleSystem system = systems[i];
			if (system == null)
				continue;

			ParticleSystem.MainModule main = system.main;
			main.playOnAwake = true;
			main.loop = false;
			main.prewarm = false;
			main.duration = 36f;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.scalingMode = ParticleSystemScalingMode.Hierarchy;
			main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

			ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
			if (renderer == null)
				continue;

			renderer.enabled = true;
			Material mappedMaterial = MapSmokeMaterial(
				renderer.sharedMaterial,
				softMaterial,
				darkMaterial,
				legacySoft,
				legacyDark,
				legacyCircle);

			if (mappedMaterial != null)
				renderer.sharedMaterial = mappedMaterial;

			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			renderer.maxParticleSize = 8f;
			renderer.sortingFudge = 12f;
		}
	}

	private static Material MapSmokeMaterial(
		Material _current,
		Material _soft,
		Material _dark,
		Material _legacySoft,
		Material _legacyDark,
		Material _legacyCircle)
	{
		if (_current == null)
			return _dark ?? _soft;

		if (_legacyDark != null && _current == _legacyDark)
			return _dark ?? _soft;
		if (_legacySoft != null && _current == _legacySoft)
			return _soft ?? _dark;
		if (_legacyCircle != null && _current == _legacyCircle)
			return _soft ?? _dark;

		string name = _current.name.ToLowerInvariant();
		if (name.Contains("dark"))
			return _dark ?? _soft;

		return _soft ?? _dark;
	}

	private static GameObject[] LoadTunedPrefabs()
	{
		GameObject[] results = new GameObject[s_Sources.Length];
		for (int i = 0; i < s_Sources.Length; i++)
		{
			string path = $"{c_OutputFolder}/{s_Sources[i].OutputName}.prefab";
			results[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}

		return results;
	}
	#endregion

	#region Scene Layout
	private static void LayoutComparisonOnScene(GameObject[] _prefabs)
	{
		EnsureSceneLoaded();

		GameObject existing = GameObject.Find(c_SceneRootName);
		if (existing != null)
			Object.DestroyImmediate(existing);

		GameObject root = new GameObject(c_SceneRootName);
		root.transform.position = new Vector3(c_OriginX, c_OriginY, c_OriginZ);
		TryAddSmokeComparisonPlayer(root);

		for (int i = 0; i < _prefabs.Length; i++)
		{
			if (_prefabs[i] == null)
				continue;

			string label = _prefabs[i].name;
			GameObject slot = new GameObject(label);
			slot.transform.SetParent(root.transform, false);
			slot.transform.localPosition = new Vector3(i * c_SpacingMeters, 0f, 0f);

			GameObject instance = PrefabUtility.InstantiatePrefab(_prefabs[i], slot.transform) as GameObject;
			if (instance != null)
			{
				instance.transform.localPosition = Vector3.zero;
				instance.transform.localRotation = Quaternion.identity;
				instance.transform.localScale = Vector3.one;

				if (Application.isPlaying)
					WeaponVfxUtility.PlayParticleSystems(instance);
			}

			CreateLabel(slot.transform, label, i * c_SpacingMeters);
		}

		EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
		EditorSceneManager.SaveOpenScenes();
	}

	private static void CreateLabel(Transform _parent, string _text, float _localX)
	{
		GameObject labelGo = new GameObject($"{_text}_Label");
		labelGo.transform.SetParent(_parent.parent, false);
		labelGo.transform.localPosition = new Vector3(_localX, 2.5f, -2f);
		labelGo.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);

		TextMesh textMesh = labelGo.AddComponent<TextMesh>();
		textMesh.text = _text.Replace("FX_Grenade_", string.Empty);
		textMesh.characterSize = 0.12f;
		textMesh.fontSize = 48;
		textMesh.anchor = TextAnchor.MiddleCenter;
		textMesh.alignment = TextAlignment.Center;
		textMesh.color = new Color(0.92f, 0.92f, 0.88f, 1f);
	}

	private static void EnsureSceneLoaded()
	{
		Scene active = SceneManager.GetActiveScene();
		if (active.path == c_ScenePath)
			return;

		EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
	}
	#endregion

	#region Data Wiring
	private static void WireThrowData(GameObject[] _prefabs)
	{
		GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrowDataPath);
		if (data == null)
		{
			Debug.LogError($"[GrenadeSmokeVfxComparisonBootstrap] Missing {c_ThrowDataPath}");
			return;
		}

		SerializedObject so = new SerializedObject(data);

		SerializedProperty smokeScale = so.FindProperty("m_SmokeScale");
		if (smokeScale != null)
			smokeScale.floatValue = 1.15f;

		SerializedProperty smokeLifetime = so.FindProperty("m_SmokeLifetimeSeconds");
		if (smokeLifetime != null)
			smokeLifetime.floatValue = 42f;

		SerializedProperty smokeMaxDistance = so.FindProperty("m_SmokeMaxDistanceMeters");
		if (smokeMaxDistance != null)
			smokeMaxDistance.floatValue = 110f;

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(data);
	}

	private static GameObject FindPrefab(GameObject[] _prefabs, string _name)
	{
		for (int i = 0; i < _prefabs.Length; i++)
		{
			if (_prefabs[i] != null && _prefabs[i].name == _name)
				return _prefabs[i];
		}

		return null;
	}
	#endregion

	#region Helpers
	private static void TryAddSmokeComparisonPlayer(GameObject _root)
	{
		System.Type playerType = System.Type.GetType("SmokeVfxComparisonPlayer, Assembly-CSharp");
		if (playerType == null)
		{
			Debug.LogWarning("[GrenadeSmokeVfxComparisonBootstrap] SmokeVfxComparisonPlayer not found; layout created without auto-play helper.");
			return;
		}

		_root.AddComponent(playerType);
	}

	private static void SetChildActiveRecursive(Transform _root, string _name, bool _active)
	{
		if (_root.name == _name)
			_root.gameObject.SetActive(_active);

		for (int i = 0; i < _root.childCount; i++)
			SetChildActiveRecursive(_root.GetChild(i), _name, _active);
	}
	#endregion
}
#endif
