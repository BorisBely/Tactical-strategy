#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Создаёт PhysicsMaterial для шагов, заполняет <see cref="UnitFootsteps"/>,
/// добавляет тестовую дорожку поверхностей и чинит locomotion Animation Events.
/// </summary>
public static class FootstepSurfaceContentBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_FootstepsRoot = "Assets/Audio/Combat/Footsteps";
	private const string c_SurfaceRoot = "Assets/GameData/Audio/FootstepSurfaces";
	private const string c_TestStripRootName = "FootstepSurfaceTestStrip";
	private const string c_PlaneObjectName = "Plane";
	#endregion

	#region Surface Specs
	private static readonly (string Surface, Color Color)[] s_Surfaces =
	{
		("Concrete", new Color(0.55f, 0.55f, 0.55f)),
		("Dirt", new Color(0.45f, 0.32f, 0.18f)),
		("Glass", new Color(0.55f, 0.78f, 0.92f)),
		("Gravel", new Color(0.62f, 0.60f, 0.58f)),
		("Metal", new Color(0.70f, 0.72f, 0.76f)),
		("Sand", new Color(0.86f, 0.76f, 0.48f)),
		("Wood", new Color(0.58f, 0.36f, 0.18f))
	};

	private static readonly (string ClipPath, float EventA, float EventB)[] s_LocomotionFootstepFixes =
	{
		("Assets/Animations/Rifle/Stand/Walk_F_Loop.anim", 0.42f, 1.02f),
		("Assets/Animations/Rifle/Stand/Run_F.anim", 0.23333333f, 0.53333336f),
		("Assets/Animations/Rifle/Stand/SprintFwdLoop.anim", 0.2f, 0.53333336f),
		("Assets/Animations/Rifle/Crouch/CrouchWalk.anim", 0.16666667f, 0.6666667f),
		("Assets/Animations/heal/WalkDrag_Aim_B_Loop.anim", 0.016666668f, -1f)
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Audio/Repair Footstep Audio Import")]
	public static void RepairFootstepAudioImport()
	{
		string[] assetPaths = CollectFootstepWavAssetPaths();
		int guidRemapped = 0;
		int registeredClips = 0;

		AssetDatabase.StartAssetEditing();
		try
		{
			for (int i = 0; i < assetPaths.Length; i++)
			{
				string assetPath = assetPaths[i];
				string metaPath = assetPath + ".meta";
				if (!File.Exists(metaPath))
				{
					Debug.LogWarning($"[FootstepSurfaceContentBuilder] Missing meta for {assetPath}");
					continue;
				}

				string metaGuid = ReadMetaGuid(metaPath);
				if (string.IsNullOrEmpty(metaGuid))
				{
					metaGuid = Guid.NewGuid().ToString("N");
					WriteMetaGuid(metaPath, metaGuid);
					guidRemapped++;
				}
				else
				{
					string mappedPath = AssetDatabase.GUIDToAssetPath(metaGuid);
					if (!string.IsNullOrEmpty(mappedPath)
					    && !string.Equals(mappedPath, assetPath, StringComparison.OrdinalIgnoreCase))
					{
						WriteMetaGuid(metaPath, Guid.NewGuid().ToString("N"));
						guidRemapped++;
						Debug.LogWarning(
							$"[FootstepSurfaceContentBuilder] GUID {metaGuid} was mapped to '{mappedPath}', remapped for '{assetPath}'.");
					}
				}

				AssetDatabase.ImportAsset(
					assetPath,
					ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

				if (AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath) != null)
					registeredClips++;
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
		}

		AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

		Dictionary<string, PhysicsMaterial> physicsMaterials = CreateOrLoadPhysicsMaterials();
		Dictionary<string, AudioClip[]> walkClipsBySurface = LoadWalkClipsBySurface();
		ConfigureUnitPrefab(physicsMaterials, walkClipsBySurface);
		AssetDatabase.SaveAssets();

		Debug.Log(
			$"[FootstepSurfaceContentBuilder] Footstep audio repair complete. Wav on disk: {assetPaths.Length}, registered clips: {registeredClips}, remapped GUIDs: {guidRemapped}.");
	}

	[MenuItem("Polygone/Audio/Build Footstep Surface Content")]
	public static void BuildFootstepSurfaceContent()
	{
		RepairFootstepAudioImport();
		EnsureSceneLoaded();
		EnsureDirectory(c_SurfaceRoot);
		EnsureDirectory($"{c_SurfaceRoot}/VisualMaterials");

		Dictionary<string, PhysicsMaterial> physicsMaterials = CreateOrLoadPhysicsMaterials();
		Dictionary<string, AudioClip[]> walkClipsBySurface = LoadWalkClipsBySurface();
		ConfigureUnitPrefab(physicsMaterials, walkClipsBySurface);
		BuildSceneTestStrip(physicsMaterials);
		FixLocomotionAnimationEvents();
		FixAnimatorForwardClipReferences();

		EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[FootstepSurfaceContentBuilder] Footstep surfaces, unit wiring, test strip and locomotion events are ready.");
	}
	#endregion

	#region Physics Materials
	private static Dictionary<string, PhysicsMaterial> CreateOrLoadPhysicsMaterials()
	{
		var result = new Dictionary<string, PhysicsMaterial>(StringComparer.Ordinal);

		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i].Surface;
			string assetPath = $"{c_SurfaceRoot}/FootstepSurface_{surface}.physicMaterial";
			PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(assetPath);
			if (material == null)
			{
				material = new PhysicsMaterial($"FootstepSurface_{surface}")
				{
					dynamicFriction = 0.6f,
					staticFriction = 0.6f,
					bounciness = 0f,
					frictionCombine = PhysicsMaterialCombine.Average,
					bounceCombine = PhysicsMaterialCombine.Average
				};
				AssetDatabase.CreateAsset(material, assetPath);
			}

			result[surface] = material;
		}

		return result;
	}
	#endregion

	#region Clips
	private static Dictionary<string, AudioClip[]> LoadWalkClipsBySurface()
	{
		var result = new Dictionary<string, AudioClip[]>(StringComparer.Ordinal);

		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i].Surface;
			string folder = $"{c_FootstepsRoot}/{surface}";
			string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
			var clips = new List<AudioClip>(guids.Length);

			for (int g = 0; g < guids.Length; g++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[g]);
				string fileName = Path.GetFileName(path);
				if (!IsWalkClip(fileName))
					continue;

				AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
				if (clip != null)
					clips.Add(clip);
			}

			clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			result[surface] = clips.ToArray();
		}

		return result;
	}

	private static bool IsWalkClip(string _fileName)
	{
		if (string.IsNullOrEmpty(_fileName))
			return false;

		return _fileName.IndexOf("_stop_", StringComparison.OrdinalIgnoreCase) < 0
		       && _fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
	}
	#endregion

	#region Unit Prefab
	private static void ConfigureUnitPrefab(
		Dictionary<string, PhysicsMaterial> _physicsMaterials,
		Dictionary<string, AudioClip[]> _walkClipsBySurface)
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		try
		{
			if (!prefabRoot.TryGetComponent(out UnitFootsteps footsteps))
			{
				Debug.LogError("[FootstepSurfaceContentBuilder] UnitFootsteps not found on unit prefab.");
				return;
			}

			SerializedObject serializedFootsteps = new SerializedObject(footsteps);
			SerializedProperty rulesProperty = serializedFootsteps.FindProperty("m_SurfaceRules");
			SerializedProperty defaultClipsProperty = serializedFootsteps.FindProperty("m_FootstepClips");
			SerializedProperty baseVolumeProperty = serializedFootsteps.FindProperty("m_FootstepBaseVolume");

			rulesProperty.arraySize = s_Surfaces.Length;
			for (int i = 0; i < s_Surfaces.Length; i++)
			{
				string surface = s_Surfaces[i].Surface;
				SerializedProperty ruleProperty = rulesProperty.GetArrayElementAtIndex(i);
				ruleProperty.FindPropertyRelative("Layers").intValue = 0;
				ruleProperty.FindPropertyRelative("PhysicsMaterial").objectReferenceValue = _physicsMaterials[surface];
				SetClipArray(ruleProperty.FindPropertyRelative("Clips"), _walkClipsBySurface[surface]);
			}

			AudioClip[] concreteClips = _walkClipsBySurface.TryGetValue("Concrete", out AudioClip[] clips)
				? clips
				: Array.Empty<AudioClip>();
			SetClipArray(defaultClipsProperty, concreteClips);
			baseVolumeProperty.floatValue = 0.6f;
			serializedFootsteps.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset(prefabRoot, c_UnitPrefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}

	private static void SetClipArray(SerializedProperty _property, AudioClip[] _clips)
	{
		_property.arraySize = _clips.Length;
		for (int i = 0; i < _clips.Length; i++)
			_property.GetArrayElementAtIndex(i).objectReferenceValue = _clips[i];
	}
	#endregion

	#region Scene Test Strip
	private static void BuildSceneTestStrip(Dictionary<string, PhysicsMaterial> _physicsMaterials)
	{
		Transform plane = FindSceneTransformByName(c_PlaneObjectName);
		if (plane == null)
		{
			Debug.LogWarning("[FootstepSurfaceContentBuilder] Plane not found in scene; test strip will be placed at origin.");
		}

		Transform existingStrip = FindSceneTransformByName(c_TestStripRootName);
		if (existingStrip != null)
			UnityEngine.Object.DestroyImmediate(existingStrip.gameObject);

		GameObject stripRoot = new GameObject(c_TestStripRootName);
		stripRoot.transform.SetParent(plane, false);
		const float padWidth = 1.4f;
		const float padDepth = 1.4f;
		const float padHeight = 0.05f;
		const float gap = 0.08f;
		const float spawnLocalX = -1.26f;
		const float spawnLocalZ = -3.95f;
		float totalWidth = s_Surfaces.Length * padWidth + (s_Surfaces.Length - 1) * gap;
		stripRoot.transform.localPosition = new Vector3(spawnLocalX - totalWidth * 0.5f, 0.03f, spawnLocalZ);
		stripRoot.transform.localRotation = Quaternion.identity;
		stripRoot.transform.localScale = Vector3.one;

		for (int i = 0; i < s_Surfaces.Length; i++)
		{
			string surface = s_Surfaces[i].Surface;
			Color color = s_Surfaces[i].Color;
			float x = i * (padWidth + gap);

			GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
			pad.name = $"FootstepTest_{surface}";
			pad.transform.SetParent(stripRoot.transform, false);
			pad.transform.localPosition = new Vector3(x + padWidth * 0.5f, padHeight * 0.5f, 0f);
			pad.transform.localRotation = Quaternion.identity;
			pad.transform.localScale = new Vector3(padWidth, padHeight, padDepth);
			pad.layer = plane != null ? plane.gameObject.layer : 0;

			if (pad.TryGetComponent(out Collider collider))
				collider.sharedMaterial = _physicsMaterials[surface];

			if (pad.TryGetComponent(out MeshRenderer renderer))
				renderer.sharedMaterial = CreateOrLoadVisualMaterial(surface, color);
		}
	}

	private static Material CreateOrLoadVisualMaterial(string _surface, Color _color)
	{
		string path = $"{c_SurfaceRoot}/VisualMaterials/FootstepVisual_{_surface}.mat";
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (material == null)
		{
			Shader shader = Shader.Find("Universal Render Pipeline/Lit");
			if (shader == null)
				shader = Shader.Find("Standard");

			material = new Material(shader)
			{
				color = _color
			};

			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", _color);

			AssetDatabase.CreateAsset(material, path);
		}

		return material;
	}

	private static Transform FindSceneTransformByName(string _name)
	{
		Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		for (int i = 0; i < transforms.Length; i++)
		{
			if (transforms[i].name == _name)
				return transforms[i];
		}

		return null;
	}
	#endregion

	#region Animation Fixes
	private static void FixLocomotionAnimationEvents()
	{
		for (int i = 0; i < s_LocomotionFootstepFixes.Length; i++)
		{
			(string clipPath, float eventA, float eventB) = s_LocomotionFootstepFixes[i];
			AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
			if (clip == null)
			{
				Debug.LogWarning($"[FootstepSurfaceContentBuilder] Missing animation clip: {clipPath}");
				continue;
			}

			AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
			if (events.Any(e => e.functionName == "Footstep"))
				continue;

			var newEvents = new List<AnimationEvent>(events)
			{
				CreateFootstepEvent(eventA)
			};

			if (eventB >= 0f)
				newEvents.Add(CreateFootstepEvent(eventB));

			AnimationUtility.SetAnimationEvents(clip, newEvents.ToArray());
			EditorUtility.SetDirty(clip);
		}
	}

	private static AnimationEvent CreateFootstepEvent(float _time)
	{
		return new AnimationEvent
		{
			time = _time,
			functionName = "Footstep"
		};
	}

	private static void FixAnimatorForwardClipReferences()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/UnitAnimController.controller");
		if (controller == null)
			return;

		AnimationClip walkForward = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Rifle/Stand/Walk_F_Loop.anim");
		AnimationClip runForward = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Rifle/Stand/Run_F.anim");
		if (walkForward == null || runForward == null)
			return;

		ReplaceBlendTreeClip(controller, "RifleStandWalkRelax", 0f, walkForward);
		ReplaceBlendTreeClip(controller, "RifleStandRunRelax", 0f, runForward);
		EditorUtility.SetDirty(controller);
	}

	private static void ReplaceBlendTreeClip(AnimatorController _controller, string _blendTreeName, float _threshold, AnimationClip _clip)
	{
		ChildAnimatorState[] states = _controller.layers[0].stateMachine.states;
		for (int i = 0; i < states.Length; i++)
		{
			if (states[i].state.motion is BlendTree blendTree && blendTree.name == _blendTreeName)
			{
				ChildMotion[] motions = blendTree.children;
				for (int m = 0; m < motions.Length; m++)
				{
					if (Mathf.Approximately(motions[m].threshold, _threshold))
					{
						motions[m].motion = _clip;
						blendTree.children = motions;
						return;
					}
				}
			}
		}
	}
	#endregion

	#region Helpers
	private static string[] CollectFootstepWavAssetPaths()
	{
		string dataRoot = Application.dataPath.Replace('\\', '/');
		string physicalRoot = Path.Combine(Application.dataPath, "Audio/Combat/Footsteps");
		if (!Directory.Exists(physicalRoot))
			return Array.Empty<string>();

		return Directory.GetFiles(physicalRoot, "*.wav", SearchOption.AllDirectories)
			.Select(_path => "Assets" + _path.Replace('\\', '/').Substring(dataRoot.Length))
			.OrderBy(_path => _path, StringComparer.Ordinal)
			.ToArray();
	}

	private static string ReadMetaGuid(string _metaPath)
	{
		foreach (string line in File.ReadAllLines(_metaPath))
		{
			if (line.StartsWith("guid: ", StringComparison.Ordinal))
				return line.Substring("guid: ".Length).Trim();
		}

		return string.Empty;
	}

	private static void WriteMetaGuid(string _metaPath, string _guid)
	{
		string content = File.ReadAllText(_metaPath);
		content = Regex.Replace(content, "^guid: .+$", "guid: " + _guid, RegexOptions.Multiline);
		File.WriteAllText(_metaPath, content, new UTF8Encoding(false));
	}

	private static void EnsureSceneLoaded()
	{
		if (SceneManager.GetActiveScene().path != c_ScenePath)
			EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
	}

	private static void EnsureDirectory(string _path)
	{
		if (!AssetDatabase.IsValidFolder(_path))
		{
			string parent = Path.GetDirectoryName(_path)?.Replace('\\', '/');
			string leaf = Path.GetFileName(_path);
			if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
				EnsureDirectory(parent);
			AssetDatabase.CreateFolder(parent, leaf);
		}
	}
	#endregion
}
#endif
