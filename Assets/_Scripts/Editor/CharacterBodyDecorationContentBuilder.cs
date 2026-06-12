#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Профили предпочтений и BodyDeco-префабы визуальных декораций тела (рация, очки).
/// Каждый декор сохраняется в Assets/Prefabs/Characters/BodyDecorations с позой с референса сцены.
/// </summary>
public static class CharacterBodyDecorationContentBuilder
{
	#region Constants
	private const string c_SceneReferenceRootName = "SM_Chr_Soldier_Male_02_Alt_01";
	private const string c_ChestAnchorName = "Spine_03";
	private const string c_HeadAnchorName = "Head";
	private const string c_PlayerUnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_BodyDecoRoot = "Assets/Prefabs/Characters/BodyDecorations";

	private const string c_ProfileRoot = "Assets/GameData/Character/VisualPreferences";
	private const string c_ProfileRadioPath = c_ProfileRoot + "/Profile_Body_Radio.asset";
	private const string c_ProfileGlassesPath = c_ProfileRoot + "/Profile_Body_Glasses.asset";
	#endregion

	#region Decoration Specs
	private sealed class DecorationBuildSpec
	{
		public string OutputPrefabPath;
		public string SceneObjectName;
		public string AnchorName;
		public string FallbackPolygonPath;
		public string UnitVariantPropertyName;
		public string MeshChildName;
	}

	private static readonly DecorationBuildSpec[] s_DecorationSpecs =
	{
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_Radio_01.prefab",
			SceneObjectName = "SM_Chr_Attach_Radio_01",
			AnchorName = c_ChestAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Radio_01.prefab",
			UnitVariantPropertyName = "m_RadioVariant",
			MeshChildName = "Radio_01"
		},
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_WalkieTalkie_01.prefab",
			SceneObjectName = "SM_Chr_Attach_Pouch_WalkieTalkie_01",
			AnchorName = c_ChestAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Pouch_WalkieTalkie_01.prefab",
			UnitVariantPropertyName = "m_WalkieTalkiePouchVariant"
		},
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_Glasses_01.prefab",
			SceneObjectName = "SM_Chr_Attach_Glasses_01",
			AnchorName = c_HeadAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Glasses_01.prefab",
			UnitVariantPropertyName = "m_Glasses01Variant"
		},
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_Glasses_02.prefab",
			SceneObjectName = "SM_Chr_Attach_Glasses_02",
			AnchorName = c_HeadAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Glasses_02.prefab",
			UnitVariantPropertyName = "m_Glasses02Variant"
		},
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_Glasses_03.prefab",
			SceneObjectName = "SM_Chr_Attach_Glasses_03",
			AnchorName = c_HeadAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_Glasses_03.prefab",
			UnitVariantPropertyName = "m_Glasses03Variant"
		},
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_SunGlasses_Male.prefab",
			SceneObjectName = "SM_Chr_Attach_SunGlasses_01_Male",
			AnchorName = c_HeadAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_SunGlasses_01_Male.prefab",
			UnitVariantPropertyName = "m_SunGlassesMaleVariant"
		},
		new DecorationBuildSpec
		{
			OutputPrefabPath = c_BodyDecoRoot + "/BodyDeco_SunGlasses_Female.prefab",
			SceneObjectName = "SM_Chr_Attach_SunGlasses_01_Female",
			AnchorName = c_HeadAnchorName,
			FallbackPolygonPath =
				"Assets/PolygonMilitary/Prefabs/Characters/Attachments/SM_Chr_Attach_SunGlasses_01_Female.prefab",
			UnitVariantPropertyName = "m_SunGlassesFemaleVariant"
		}
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Character Body Decoration Content")]
	public static void BuildCharacterBodyDecorationContent()
	{
		Transform sceneRoot = FindSceneReferenceRoot();
		if (sceneRoot == null)
		{
			Debug.LogError(
				$"[CharacterBodyDecorationContentBuilder] Не найден '{c_SceneReferenceRootName}' в открытых сценах.");
			return;
		}

		EnsureDirectory(c_ProfileRoot);
		EnsureDirectory(c_BodyDecoRoot);

		var builtPrefabs = new GameObject[s_DecorationSpecs.Length];
		for (int i = 0; i < s_DecorationSpecs.Length; i++)
			builtPrefabs[i] = BuildBakedDecorationPrefabFromScene(sceneRoot, s_DecorationSpecs[i]);

		EquipmentVisualProfileDefinition profileRadio = CreateOrUpdateProfile(
			c_ProfileRadioPath,
			UnitCharacterBodyDecorations.RadioProfileId,
			new[]
			{
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.VariantNone, 40),
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.RadioVariantRadio, 30),
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.RadioVariantWalkieTalkiePouch, 30)
			});

		EquipmentVisualProfileDefinition profileGlasses = CreateOrUpdateProfile(
			c_ProfileGlassesPath,
			UnitCharacterBodyDecorations.GlassesProfileId,
			new[]
			{
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.VariantNone, 43),
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.GlassesVariant01, 5),
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.GlassesVariant02, 2),
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.GlassesVariant03, 40),
				new EquipmentVisualVariantWeight(UnitCharacterBodyDecorations.GlassesVariantSunGlasses, 10)
			});

		AppendProfilesToCatalog(profileRadio, profileGlasses);
		SetupUnitBodyDecorationComponent(builtPrefabs);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[CharacterBodyDecorationContentBuilder] All body decoration prefabs and unit setup complete.");
	}
	#endregion

	#region Build Steps
	private static GameObject BuildBakedDecorationPrefabFromScene(Transform _sceneRoot, DecorationBuildSpec _spec)
	{
		Transform anchor = FindChildByName(_sceneRoot, _spec.AnchorName);
		Transform source = anchor != null ? anchor.Find(_spec.SceneObjectName) : null;
		if (source == null)
			source = FindChildByName(_sceneRoot, _spec.SceneObjectName);

		GameObject clone;
		if (source != null)
		{
			clone = Object.Instantiate(source.gameObject);
			clone.name = _spec.SceneObjectName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = source.localPosition;
			clone.transform.localRotation = source.localRotation;
			clone.transform.localScale = source.localScale;
		}
		else
		{
			GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(_spec.FallbackPolygonPath);
			if (fallback == null)
			{
				Debug.LogError(
					$"[CharacterBodyDecorationContentBuilder] Не найден fallback для '{_spec.SceneObjectName}': {_spec.FallbackPolygonPath}");
				return AssetDatabase.LoadAssetAtPath<GameObject>(_spec.OutputPrefabPath);
			}

			clone = PrefabUtility.InstantiatePrefab(fallback) as GameObject;
			clone.name = _spec.SceneObjectName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			clone.transform.localScale = Vector3.one;
			Debug.LogWarning(
				$"[CharacterBodyDecorationContentBuilder] Референс '{_spec.SceneObjectName}' не найден на сцене. Сохранён fallback с нулевой позой.");
		}

		if (!string.IsNullOrEmpty(_spec.MeshChildName))
			EnsureMeshChildHierarchy(clone, _spec.MeshChildName);

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, _spec.OutputPrefabPath);
		Object.DestroyImmediate(clone);
		return prefab;
	}

	private static void EnsureMeshChildHierarchy(GameObject _root, string _meshChildName)
	{
		if (_root.transform.Find(_meshChildName) != null)
			return;

		MeshFilter meshFilter = _root.GetComponent<MeshFilter>();
		MeshRenderer meshRenderer = _root.GetComponent<MeshRenderer>();
		if (meshFilter == null && meshRenderer == null)
			return;

		GameObject child = new GameObject(_meshChildName);
		child.transform.SetParent(_root.transform, false);
		child.transform.localPosition = Vector3.zero;
		child.transform.localRotation = Quaternion.identity;
		child.transform.localScale = Vector3.one;

		if (meshFilter != null)
		{
			MeshFilter childFilter = child.AddComponent<MeshFilter>();
			childFilter.sharedMesh = meshFilter.sharedMesh;
			Object.DestroyImmediate(meshFilter);
		}

		if (meshRenderer != null)
		{
			MeshRenderer childRenderer = child.AddComponent<MeshRenderer>();
			childRenderer.sharedMaterials = meshRenderer.sharedMaterials;
			childRenderer.shadowCastingMode = meshRenderer.shadowCastingMode;
			childRenderer.receiveShadows = meshRenderer.receiveShadows;
			Object.DestroyImmediate(meshRenderer);
		}
	}

	private static void SetupUnitBodyDecorationComponent(GameObject[] _builtPrefabs)
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_PlayerUnitPrefabPath);
		bool changed = false;

		if (!unitRoot.TryGetComponent(out UnitCharacterBodyDecorations decorations))
		{
			decorations = unitRoot.AddComponent<UnitCharacterBodyDecorations>();
			changed = true;
		}

		Transform chestAnchor = FindChildByName(unitRoot.transform, c_ChestAnchorName);
		Transform headAnchor = FindChildByName(unitRoot.transform, c_HeadAnchorName);
		if (chestAnchor == null || headAnchor == null)
		{
			Debug.LogError(
				$"[CharacterBodyDecorationContentBuilder] На префабе юнита не найдены якоря '{c_ChestAnchorName}' / '{c_HeadAnchorName}'.");
			PrefabUtility.UnloadPrefabContents(unitRoot);
			return;
		}

		RemoveStaticBodyDecorations(chestAnchor, headAnchor);

		SerializedObject so = new SerializedObject(decorations);
		so.FindProperty("m_ChestAnchor").objectReferenceValue = chestAnchor;
		so.FindProperty("m_HeadAnchor").objectReferenceValue = headAnchor;

		for (int i = 0; i < s_DecorationSpecs.Length; i++)
			AssignBakedDecorationPrefab(so, s_DecorationSpecs[i].UnitVariantPropertyName, _builtPrefabs[i]);

		so.ApplyModifiedPropertiesWithoutUndo();
		changed = true;

		if (changed)
		{
			PrefabUtility.SaveAsPrefabAsset(unitRoot, c_PlayerUnitPrefabPath);
			Debug.Log("[CharacterBodyDecorationContentBuilder] Unit prefab updated with body decorations.");
		}

		PrefabUtility.UnloadPrefabContents(unitRoot);
	}

	private static void RemoveStaticBodyDecorations(Transform _chestAnchor, Transform _headAnchor)
	{
		string[] chestDecorationNames =
		{
			"SM_Chr_Attach_Radio_01",
			"SM_Chr_Attach_Pouch_WalkieTalkie_01"
		};

		string[] headDecorationNames =
		{
			"SM_Chr_Attach_Glasses_01",
			"SM_Chr_Attach_Glasses_02",
			"SM_Chr_Attach_Glasses_03",
			"SM_Chr_Attach_SunGlasses_01_Male",
			"SM_Chr_Attach_SunGlasses_01_Female"
		};

		RemoveNamedChildren(_chestAnchor, chestDecorationNames);
		RemoveNamedChildren(_headAnchor, headDecorationNames);
	}

	private static void RemoveNamedChildren(Transform _parent, string[] _names)
	{
		if (_parent == null || _names == null)
			return;

		for (int i = _parent.childCount - 1; i >= 0; i--)
		{
			Transform child = _parent.GetChild(i);
			for (int n = 0; n < _names.Length; n++)
			{
				if (!string.Equals(child.name, _names[n], System.StringComparison.Ordinal))
					continue;

				Object.DestroyImmediate(child.gameObject);
				break;
			}
		}
	}

	private static void AssignBakedDecorationPrefab(
		SerializedObject _decorationsSo,
		string _propertyName,
		GameObject _prefab)
	{
		SerializedProperty variantProperty = _decorationsSo.FindProperty(_propertyName);
		variantProperty.FindPropertyRelative("m_Prefab").objectReferenceValue = _prefab;
		variantProperty.FindPropertyRelative("m_LocalPosition").vector3Value = Vector3.zero;
		variantProperty.FindPropertyRelative("m_LocalEulerAngles").vector3Value = Vector3.zero;
	}

	private static EquipmentVisualProfileDefinition CreateOrUpdateProfile(
		string _assetPath,
		string _profileId,
		EquipmentVisualVariantWeight[] _weights)
	{
		EquipmentVisualProfileDefinition profile =
			AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileDefinition>(_assetPath);

		if (profile == null)
		{
			profile = ScriptableObject.CreateInstance<EquipmentVisualProfileDefinition>();
			AssetDatabase.CreateAsset(profile, _assetPath);
		}

		SerializedObject so = new SerializedObject(profile);
		so.FindProperty("m_ProfileId").stringValue = _profileId;
		so.FindProperty("m_ChinStrapIndependentChance").floatValue = 0f;
		SerializedProperty weightsProperty = so.FindProperty("m_PrimaryVariantWeights");
		weightsProperty.arraySize = _weights.Length;
		for (int i = 0; i < _weights.Length; i++)
		{
			SerializedProperty entry = weightsProperty.GetArrayElementAtIndex(i);
			entry.FindPropertyRelative("m_VariantIndex").intValue = _weights[i].VariantIndex;
			entry.FindPropertyRelative("m_Weight").intValue = _weights[i].Weight;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(profile);
		return profile;
	}

	private static void AppendProfilesToCatalog(
		EquipmentVisualProfileDefinition _radioProfile,
		EquipmentVisualProfileDefinition _glassesProfile)
	{
		EquipmentVisualProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileCatalog>(
			EquipmentVisualProfileCatalog.DefaultAssetPath);

		if (catalog == null)
		{
			Debug.LogError(
				$"[CharacterBodyDecorationContentBuilder] Каталог не найден: {EquipmentVisualProfileCatalog.DefaultAssetPath}");
			return;
		}

		SerializedObject so = new SerializedObject(catalog);
		SerializedProperty profilesProperty = so.FindProperty("m_Profiles");
		AppendProfileIfMissing(profilesProperty, _radioProfile);
		AppendProfileIfMissing(profilesProperty, _glassesProfile);
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(catalog);
	}

	private static void AppendProfileIfMissing(
		SerializedProperty _profilesProperty,
		EquipmentVisualProfileDefinition _profile)
	{
		if (_profile == null)
			return;

		for (int i = 0; i < _profilesProperty.arraySize; i++)
		{
			EquipmentVisualProfileDefinition existing =
				_profilesProperty.GetArrayElementAtIndex(i).objectReferenceValue as EquipmentVisualProfileDefinition;

			if (existing == null)
				continue;

			if (string.Equals(existing.ProfileId, _profile.ProfileId, System.StringComparison.Ordinal))
				return;
		}

		int index = _profilesProperty.arraySize;
		_profilesProperty.InsertArrayElementAtIndex(index);
		_profilesProperty.GetArrayElementAtIndex(index).objectReferenceValue = _profile;
	}
	#endregion

	#region Scene Helpers
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
				Transform[] children = roots[r].GetComponentsInChildren<Transform>(true);
				for (int c = 0; c < children.Length; c++)
				{
					if (children[c].name == c_SceneReferenceRootName)
						return children[c];
				}
			}
		}

		return null;
	}

	private static Transform FindChildByName(Transform _root, string _name)
	{
		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i].name == _name)
				return children[i];
		}

		return null;
	}

	private static void EnsureDirectory(string _path)
	{
		if (!Directory.Exists(_path))
			Directory.CreateDirectory(_path);
	}
	#endregion
}
#endif
