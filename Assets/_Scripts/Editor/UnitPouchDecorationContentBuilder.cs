#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Печёт подсумки с референса сцены и подключает их к UnitInventoryBodyDecorations.
/// </summary>
public static class UnitPouchDecorationContentBuilder
{
	#region Constants
	private const string c_SceneReferenceRootName = "SM_Chr_Soldier_Male_02_Alt_01";
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_PlayerUnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_PouchRoot = "Assets/Prefabs/Characters/PouchDecorations";
	private const string c_ProfileRoot = "Assets/GameData/Character/VisualPreferences";

	private const string c_Spine01Name = "Spine_01";
	private const string c_Spine02Name = "Spine_02";
	private const int c_AttachedGrenadeCellCount = 2;
	private const string c_Spine03Name = "Spine_03";
	#endregion

	#region Specs
	private sealed class PouchSpec
	{
		public string SceneObjectName;
		public string OutputName;
		public string AnchorName;
		public string ComponentPropertyName;
		public int ArrayIndex = -1;
	}

	private static readonly PouchSpec[] s_PouchSpecs =
	{
		new PouchSpec
		{
			SceneObjectName = "Pouch_Mag_0",
			OutputName = "PouchDeco_Mag_0",
			AnchorName = c_Spine01Name,
			ComponentPropertyName = "m_MagDefaultVariant"
		},
		new PouchSpec { SceneObjectName = "Pouch_Mag_M4_1", OutputName = "PouchDeco_Mag_M4_1", AnchorName = c_Spine01Name, ComponentPropertyName = "m_MagM4Variants", ArrayIndex = 0 },
		new PouchSpec { SceneObjectName = "Pouch_Mag_M4_2", OutputName = "PouchDeco_Mag_M4_2", AnchorName = c_Spine01Name, ComponentPropertyName = "m_MagM4Variants", ArrayIndex = 1 },
		new PouchSpec { SceneObjectName = "Pouch_Mag_M4_3", OutputName = "PouchDeco_Mag_M4_3", AnchorName = c_Spine01Name, ComponentPropertyName = "m_MagM4Variants", ArrayIndex = 2 },
		new PouchSpec { SceneObjectName = "Pouch_Mag_AK_1", OutputName = "PouchDeco_Mag_AK_1", AnchorName = c_Spine01Name, ComponentPropertyName = "m_MagAkVariants", ArrayIndex = 0 },
		new PouchSpec { SceneObjectName = "Pouch_Mag_AK_2", OutputName = "PouchDeco_Mag_AK_2", AnchorName = c_Spine01Name, ComponentPropertyName = "m_MagAkVariants", ArrayIndex = 1 },
		new PouchSpec { SceneObjectName = "Pouch_Mag_AK_3", OutputName = "PouchDeco_Mag_AK_3", AnchorName = c_Spine01Name, ComponentPropertyName = "m_MagAkVariants", ArrayIndex = 2 },
		new PouchSpec
		{
			SceneObjectName = "PouchDeco_Mag_12",
			OutputName = "PouchDeco_Mag_12",
			AnchorName = c_Spine01Name,
			ComponentPropertyName = "m_Mag12GaugeVariant"
		},
		new PouchSpec { SceneObjectName = "Pouch_R_1", OutputName = "PouchDeco_Side_R_1", AnchorName = c_Spine01Name, ComponentPropertyName = "m_SideRightVariants", ArrayIndex = 0 },
		new PouchSpec { SceneObjectName = "Pouch_R_2", OutputName = "PouchDeco_Side_R_2", AnchorName = c_Spine01Name, ComponentPropertyName = "m_SideRightVariants", ArrayIndex = 1 },
		new PouchSpec { SceneObjectName = "Pouch_R_3", OutputName = "PouchDeco_Side_R_3", AnchorName = c_Spine01Name, ComponentPropertyName = "m_SideRightVariants", ArrayIndex = 2 },
		new PouchSpec { SceneObjectName = "Pouch_L_1", OutputName = "PouchDeco_Side_L_1", AnchorName = c_Spine01Name, ComponentPropertyName = "m_SideLeftVariants", ArrayIndex = 0 },
		new PouchSpec { SceneObjectName = "Pouch_L_2", OutputName = "PouchDeco_Side_L_2", AnchorName = c_Spine01Name, ComponentPropertyName = "m_SideLeftVariants", ArrayIndex = 1 },
		new PouchSpec { SceneObjectName = "Pouch_L_3", OutputName = "PouchDeco_Side_L_3", AnchorName = c_Spine01Name, ComponentPropertyName = "m_SideLeftVariants", ArrayIndex = 2 },
		new PouchSpec { SceneObjectName = "Pouch_Spine_03_1", OutputName = "PouchDeco_Spine03_1", AnchorName = c_Spine03Name, ComponentPropertyName = "m_ChestVariants", ArrayIndex = 0 },
		new PouchSpec { SceneObjectName = "Pouch_Spine_03_2", OutputName = "PouchDeco_Spine03_2", AnchorName = c_Spine03Name, ComponentPropertyName = "m_ChestVariants", ArrayIndex = 1 }
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Unit Pouch Decoration Content")]
	public static void BuildUnitPouchDecorationContent()
	{
		EnsureSceneLoaded();
		Transform sceneRoot = FindSceneReferenceRoot();
		if (sceneRoot == null)
		{
			Debug.LogError($"[UnitPouchDecorationContentBuilder] Reference root not found: {c_SceneReferenceRootName}");
			return;
		}

		EnsureDirectory(c_PouchRoot);
		EnsureDirectory(c_ProfileRoot);

		Dictionary<string, GameObject> builtPrefabs = BuildPouchPrefabs(sceneRoot);
		CreateOrUpdateProfiles();
		SetupUnitPrefab(sceneRoot, builtPrefabs);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[UnitPouchDecorationContentBuilder] Unit pouch decoration content build complete.");
	}
	#endregion

	#region Build Steps
	private static Dictionary<string, GameObject> BuildPouchPrefabs(Transform _sceneRoot)
	{
		Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
		for (int i = 0; i < s_PouchSpecs.Length; i++)
		{
			PouchSpec spec = s_PouchSpecs[i];
			GameObject prefab = BuildPouchPrefab(_sceneRoot, spec);
			prefabs[spec.OutputName] = prefab;
		}

		BuildGrenadePouchPrefabs(_sceneRoot, prefabs);
		return prefabs;
	}

	private static GameObject BuildPouchPrefab(Transform _sceneRoot, PouchSpec _spec)
	{
		Transform source = FindChildByName(_sceneRoot, _spec.SceneObjectName);
		if (source == null)
			source = FindInLoadedScenes(_spec.SceneObjectName);

		if (source == null)
		{
			Debug.LogError($"[UnitPouchDecorationContentBuilder] Missing scene pouch: {_spec.SceneObjectName}");
			return AssetDatabase.LoadAssetAtPath<GameObject>(GetPouchPath(_spec.OutputName));
		}

		GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
		try
		{
			clone.name = _spec.OutputName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			clone.transform.localScale = Vector3.one;
			PrepareDecorationPrefab(clone);
			return SaveAsPrefab(clone, GetPouchPath(_spec.OutputName));
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(clone);
		}
	}

	private static void BuildGrenadePouchPrefabs(Transform _sceneRoot, Dictionary<string, GameObject> _prefabs)
	{
		List<Transform> grenadePouches = new List<Transform>();
		Transform[] children = _sceneRoot.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			Transform child = children[i];
			if (child.name.StartsWith("SM_Chr_Attach_Pouch_Grenade_01", StringComparison.Ordinal) &&
			    HasParentNamed(child, c_Spine01Name))
				grenadePouches.Add(child);
		}

		grenadePouches.Sort((a, b) => b.localPosition.z.CompareTo(a.localPosition.z));
		_prefabs["PouchDeco_Grenade_R_4"] = BuildGrenadePouchPrefab(grenadePouches, 0, "PouchDeco_Grenade_R_4");
		_prefabs["PouchDeco_Grenade_L_4"] = BuildGrenadePouchPrefab(grenadePouches, 1, "PouchDeco_Grenade_L_4");
	}

	private static GameObject BuildGrenadePouchPrefab(List<Transform> _sources, int _index, string _outputName)
	{
		if (_sources == null || _index < 0 || _index >= _sources.Count)
		{
			Debug.LogError($"[UnitPouchDecorationContentBuilder] Missing grenade pouch source for {_outputName}.");
			return AssetDatabase.LoadAssetAtPath<GameObject>(GetPouchPath(_outputName));
		}

		GameObject root = new GameObject(_outputName);
		try
		{
			Transform source = _sources[_index];
			GameObject child = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
			child.name = source.name;
			child.transform.localPosition = source.localPosition;
			child.transform.localRotation = source.localRotation;
			child.transform.localScale = source.localScale;
			PrepareDecorationPrefab(root);
			return SaveAsPrefab(root, GetPouchPath(_outputName));
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static void CreateOrUpdateProfiles()
	{
		EquipmentVisualProfileDefinition sideRight = CreateOrUpdateProfile(
			"Profile_Body_Pouch_Side_Right.asset",
			UnitInventoryBodyDecorations.SideRightProfileId,
			new[]
			{
				new EquipmentVisualVariantWeight(1, 33),
				new EquipmentVisualVariantWeight(2, 33),
				new EquipmentVisualVariantWeight(3, 34)
			});

		EquipmentVisualProfileDefinition sideLeft = CreateOrUpdateProfile(
			"Profile_Body_Pouch_Side_Left.asset",
			UnitInventoryBodyDecorations.SideLeftProfileId,
			new[]
			{
				new EquipmentVisualVariantWeight(1, 33),
				new EquipmentVisualVariantWeight(2, 33),
				new EquipmentVisualVariantWeight(3, 34)
			});

		EquipmentVisualProfileDefinition chest = CreateOrUpdateProfile(
			"Profile_Body_Pouch_Chest.asset",
			UnitInventoryBodyDecorations.ChestProfileId,
			new[]
			{
				new EquipmentVisualVariantWeight(0, 40),
				new EquipmentVisualVariantWeight(1, 30),
				new EquipmentVisualVariantWeight(2, 30)
			});

		AppendProfilesToCatalog(sideRight, sideLeft, chest);
	}

	private static EquipmentVisualProfileDefinition CreateOrUpdateProfile(
		string _fileName,
		string _profileId,
		EquipmentVisualVariantWeight[] _weights)
	{
		string path = $"{c_ProfileRoot}/{_fileName}";
		EquipmentVisualProfileDefinition profile =
			AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileDefinition>(path);
		if (profile == null)
		{
			profile = ScriptableObject.CreateInstance<EquipmentVisualProfileDefinition>();
			AssetDatabase.CreateAsset(profile, path);
		}

		SerializedObject so = new SerializedObject(profile);
		so.FindProperty("m_ProfileId").stringValue = _profileId;
		so.FindProperty("m_ChinStrapIndependentChance").floatValue = 0f;
		SerializedProperty weights = so.FindProperty("m_PrimaryVariantWeights");
		weights.arraySize = _weights.Length;
		for (int i = 0; i < _weights.Length; i++)
		{
			SerializedProperty weight = weights.GetArrayElementAtIndex(i);
			weight.FindPropertyRelative("m_VariantIndex").intValue = _weights[i].VariantIndex;
			weight.FindPropertyRelative("m_Weight").intValue = _weights[i].Weight;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(profile);
		return profile;
	}

	private static void AppendProfilesToCatalog(params EquipmentVisualProfileDefinition[] _profiles)
	{
		EquipmentVisualProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileCatalog>(
			EquipmentVisualProfileCatalog.DefaultAssetPath);
		if (catalog == null)
		{
			Debug.LogError($"[UnitPouchDecorationContentBuilder] Catalog missing: {EquipmentVisualProfileCatalog.DefaultAssetPath}");
			return;
		}

		SerializedObject so = new SerializedObject(catalog);
		SerializedProperty profiles = so.FindProperty("m_Profiles");
		for (int i = 0; i < _profiles.Length; i++)
			AppendProfileIfMissing(profiles, _profiles[i]);

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(catalog);
	}

	private static void SetupUnitPrefab(Transform _sceneRoot, Dictionary<string, GameObject> _builtPrefabs)
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_PlayerUnitPrefabPath);
		try
		{
			Transform spine01 = FindChildByName(unitRoot.transform, c_Spine01Name);
			Transform spine02 = FindChildByName(unitRoot.transform, c_Spine02Name);
			Transform spine03 = FindChildByName(unitRoot.transform, c_Spine03Name);
			if (spine01 == null || spine02 == null || spine03 == null)
				throw new InvalidOperationException("[UnitPouchDecorationContentBuilder] Missing Unit.prefab spine anchors.");

			RemoveStaticPouches(spine01);
			RemoveStaticPouches(spine03);
			Transform[] cells = SetupAttachedGrenadeCells(_sceneRoot, spine02);

			UnitInventoryBodyDecorations decorations = unitRoot.GetComponent<UnitInventoryBodyDecorations>();
			if (decorations == null)
				decorations = unitRoot.AddComponent<UnitInventoryBodyDecorations>();

			SerializedObject so = new SerializedObject(decorations);
			so.FindProperty("m_Spine01Anchor").objectReferenceValue = spine01;
			so.FindProperty("m_Spine02Anchor").objectReferenceValue = spine02;
			so.FindProperty("m_Spine03Anchor").objectReferenceValue = spine03;
			WriteTransformArray(so.FindProperty("m_AttachedGrenadeCells"), cells);
			AssignPouchPrefabs(so, _builtPrefabs);
			so.ApplyModifiedPropertiesWithoutUndo();

			AssignProfileCatalog(unitRoot);
			PrefabUtility.SaveAsPrefabAsset(unitRoot, c_PlayerUnitPrefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(unitRoot);
		}
	}
	#endregion

	#region Unit Setup Helpers
	private static Transform[] SetupAttachedGrenadeCells(Transform _sceneRoot, Transform _unitSpine02)
	{
		RemoveNamedChildren(_unitSpine02, "Attach_Grenade_Cell_");

		Transform sceneSpine02 = FindChildByName(_sceneRoot, c_Spine02Name);
		Transform[] cells = new Transform[c_AttachedGrenadeCellCount];
		for (int i = 0; i < cells.Length; i++)
		{
			string cellName = $"Attach_Grenade_Cell_{i + 1:00}";
			GameObject cellObject = new GameObject(cellName);
			Transform cell = cellObject.transform;
			cell.SetParent(_unitSpine02, false);

			Transform source = sceneSpine02 != null ? FindChildByName(sceneSpine02, cellName) : null;
			if (source != null)
			{
				cell.localPosition = source.localPosition;
				cell.localRotation = source.localRotation;
				cell.localScale = source.localScale;
			}
			else
			{
				cell.localPosition = Vector3.zero;
				cell.localRotation = Quaternion.identity;
				cell.localScale = Vector3.one;
			}

			cells[i] = cell;
		}

		return cells;
	}

	private static void AssignPouchPrefabs(SerializedObject _decorationsSo, Dictionary<string, GameObject> _builtPrefabs)
	{
		for (int i = 0; i < s_PouchSpecs.Length; i++)
		{
			PouchSpec spec = s_PouchSpecs[i];
			AssignDecorationVariant(_decorationsSo, spec.ComponentPropertyName, spec.ArrayIndex, _builtPrefabs[spec.OutputName]);
		}

		AssignDecorationVariant(_decorationsSo, "m_GrenadeRightPouchVariant", -1, _builtPrefabs["PouchDeco_Grenade_R_4"]);
		AssignDecorationVariant(_decorationsSo, "m_GrenadeLeftPouchVariant", -1, _builtPrefabs["PouchDeco_Grenade_L_4"]);
	}

	private static void AssignDecorationVariant(
		SerializedObject _decorationsSo,
		string _propertyName,
		int _arrayIndex,
		GameObject _prefab)
	{
		SerializedProperty property = _decorationsSo.FindProperty(_propertyName);
		if (_arrayIndex >= 0)
		{
			property.arraySize = Mathf.Max(property.arraySize, _arrayIndex + 1);
			property = property.GetArrayElementAtIndex(_arrayIndex);
		}

		property.FindPropertyRelative("m_Prefab").objectReferenceValue = _prefab;
		property.FindPropertyRelative("m_LocalPosition").vector3Value = Vector3.zero;
		property.FindPropertyRelative("m_LocalEulerAngles").vector3Value = Vector3.zero;
	}

	private static void WriteTransformArray(SerializedProperty _property, Transform[] _cells)
	{
		_property.arraySize = _cells != null ? _cells.Length : 0;
		for (int i = 0; i < _property.arraySize; i++)
			_property.GetArrayElementAtIndex(i).objectReferenceValue = _cells[i];
	}

	private static void AssignProfileCatalog(GameObject _unitRoot)
	{
		UnitIndividualTraits traits = _unitRoot.GetComponent<UnitIndividualTraits>();
		EquipmentVisualProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileCatalog>(
			EquipmentVisualProfileCatalog.DefaultAssetPath);
		if (traits == null || catalog == null)
			return;

		SerializedObject so = new SerializedObject(traits);
		so.FindProperty("m_EquipmentVisualProfileCatalog").objectReferenceValue = catalog;
		so.ApplyModifiedPropertiesWithoutUndo();
	}

	private static void RemoveStaticPouches(Transform _anchor)
	{
		if (_anchor == null)
			return;

		for (int i = _anchor.childCount - 1; i >= 0; i--)
		{
			Transform child = _anchor.GetChild(i);
			if (child.name.StartsWith("SM_Chr_Attach_Pouch_", StringComparison.Ordinal) ||
			    child.name.StartsWith("Pouch_", StringComparison.Ordinal))
			{
				UnityEngine.Object.DestroyImmediate(child.gameObject);
			}
		}
	}

	private static void RemoveNamedChildren(Transform _parent, string _prefix)
	{
		if (_parent == null)
			return;

		for (int i = _parent.childCount - 1; i >= 0; i--)
		{
			Transform child = _parent.GetChild(i);
			if (child.name.StartsWith(_prefix, StringComparison.Ordinal))
				UnityEngine.Object.DestroyImmediate(child.gameObject);
		}
	}
	#endregion

	#region General Helpers
	private static Transform FindSceneReferenceRoot()
	{
		for (int s = 0; s < SceneManager.sceneCount; s++)
		{
			Scene scene = SceneManager.GetSceneAt(s);
			if (!scene.isLoaded)
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for (int r = 0; r < roots.Length; r++)
			{
				Transform found = FindChildByName(roots[r].transform, c_SceneReferenceRootName);
				if (found != null)
					return found;
			}
		}

		return null;
	}

	private static void EnsureSceneLoaded()
	{
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene scene = SceneManager.GetSceneAt(i);
			if (scene.isLoaded && scene.path == c_ScenePath)
				return;
		}

		EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
	}

	private static Transform FindChildByName(Transform _root, string _name)
	{
		if (_root == null)
			return null;

		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i].name == _name)
				return children[i];
		}

		return null;
	}

	private static Transform FindInLoadedScenes(string _name)
	{
		for (int s = 0; s < SceneManager.sceneCount; s++)
		{
			Scene scene = SceneManager.GetSceneAt(s);
			if (!scene.isLoaded)
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for (int r = 0; r < roots.Length; r++)
			{
				Transform found = FindChildByName(roots[r].transform, _name);
				if (found != null)
					return found;
			}
		}

		return null;
	}

	private static bool HasParentNamed(Transform _child, string _parentName)
	{
		Transform current = _child != null ? _child.parent : null;
		while (current != null)
		{
			if (current.name == _parentName)
				return true;

			current = current.parent;
		}

		return false;
	}

	private static string GetPouchPath(string _outputName)
	{
		return $"{c_PouchRoot}/{_outputName}.prefab";
	}

	private static GameObject SaveAsPrefab(GameObject _source, string _path)
	{
		EnsureDirectory(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_source, _path);
		if (prefab == null)
			throw new InvalidOperationException($"Failed to save prefab: {_path}");

		return prefab;
	}

	private static void PrepareDecorationPrefab(GameObject _root)
	{
		SetActiveRecursively(_root.transform);
		CharacterDecorationSpawnUtility.StripPickupAndPhysics(_root);
		EnableRenderers(_root);
	}

	private static void SetActiveRecursively(Transform _root)
	{
		if (_root == null)
			return;

		_root.gameObject.SetActive(true);
		for (int i = 0; i < _root.childCount; i++)
			SetActiveRecursively(_root.GetChild(i));
	}

	private static void EnableRenderers(GameObject _root)
	{
		Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].enabled = true;
	}

	private static void AppendProfileIfMissing(SerializedProperty _profilesProperty, EquipmentVisualProfileDefinition _profile)
	{
		if (_profile == null)
			return;

		for (int i = 0; i < _profilesProperty.arraySize; i++)
		{
			EquipmentVisualProfileDefinition existing =
				_profilesProperty.GetArrayElementAtIndex(i).objectReferenceValue as EquipmentVisualProfileDefinition;
			if (existing != null && string.Equals(existing.ProfileId, _profile.ProfileId, StringComparison.Ordinal))
				return;
		}

		int index = _profilesProperty.arraySize;
		_profilesProperty.InsertArrayElementAtIndex(index);
		_profilesProperty.GetArrayElementAtIndex(index).objectReferenceValue = _profile;
	}

	private static void EnsureDirectory(string _folderPath)
	{
		if (string.IsNullOrEmpty(_folderPath) || AssetDatabase.IsValidFolder(_folderPath))
			return;

		string parent = Path.GetDirectoryName(_folderPath)?.Replace('\\', '/');
		if (!string.IsNullOrEmpty(parent))
			EnsureDirectory(parent);

		AssetDatabase.CreateFolder(parent, Path.GetFileName(_folderPath));
	}
	#endregion
}
#endif
