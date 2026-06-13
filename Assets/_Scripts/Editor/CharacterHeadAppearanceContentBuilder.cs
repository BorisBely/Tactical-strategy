#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает runtime-префабы причёсок, бород и головных уборов с референса сцены.
/// </summary>
public static class CharacterHeadAppearanceContentBuilder
{
	#region Constants
	private const string c_SceneReferenceRootName = "SM_Chr_Soldier_Male_02_Alt_01";
	private const string c_HeadAnchorName = "Head";
	private const string c_PlayerUnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_OutputRoot = "Assets/Prefabs/Characters/HeadDecorations";
	private const string c_RankTableRoot = "Assets/GameData/Character/HeadAppearance";
	#endregion

	#region Specs
	private sealed class HeadDecorationBuildSpec
	{
		public string SceneObjectName;
		public string OutputName;
		public string FallbackPolygonPath;
		public string UnitVariantPropertyName;
	}

	private static readonly HeadDecorationBuildSpec[] s_DecorationSpecs =
	{
		Spec("SM_Chr_Attach_Hair_Male_04", "Head_Hair_Male_04", "m_MaleHairShort04"),
		Spec("SM_Chr_Attach_Hair_Male_02", "Head_Hair_Male_02", "m_MaleHairLongBack02"),
		Spec("SM_Chr_Attach_Hair_Male_03", "Head_Hair_Male_03", "m_MaleHairRaised03"),
		Spec("SM_Chr_Attach_Hair_Male_05", "Head_Hair_Male_05", "m_MaleHairCurly05"),
		Spec("SM_Chr_Attach_Hair_Male_06", "Head_Hair_Male_06", "m_MaleHairMessy06"),
		Spec("SM_Chr_Attach_Hair_Male_07", "Head_Hair_Male_07", "m_MaleHairStylish07"),
		Spec("SM_Chr_Attach_Hair_Male_08", "Head_Hair_Male_08", "m_MaleHairShavedSides08"),
		Spec("SM_Chr_Attach_Hair_Male_10", "Head_Hair_Male_10", "m_MaleHairShavedSidesLong10"),

		Spec("SM_Chr_Attach_Hair_Female_01", "Head_Hair_Female_01", "m_FemaleHair01"),
		Spec("SM_Chr_Attach_Hair_Female_02", "Head_Hair_Female_02", "m_FemaleHair02"),
		Spec("SM_Chr_Attach_Hair_Female_03", "Head_Hair_Female_03", "m_FemaleHair03"),
		Spec("SM_Chr_Attach_Hair_Female_04", "Head_Hair_Female_04", "m_FemaleHair04"),
		Spec("SM_Chr_Attach_Hat_Cap_02", "Head_Hair_Female_Cap_02", "m_FemaleHairCap02"),
		Spec("SM_Chr_Attach_Hat_Cap_02 (1)", "Head_Hair_Female_Cap_02_Alt", "m_FemaleHairCap02Alt", "SM_Chr_Attach_Hat_Cap_02"),
		Spec("SM_Chr_Attach_Hair_Female_05", "Head_Hair_Female_05_Helmet", "m_FemaleHairHelmetShort05"),

		Spec("SM_Chr_Attach_Hat_02", "Head_Hat_02", "m_Hat02"),
		Spec("SM_Chr_Attach_Hat_03", "Head_Hat_03", "m_Hat03"),
		Spec("SM_Chr_Attach_Hat_04", "Head_Hat_04", "m_Hat04"),
		Spec("SM_Chr_Attach_Hat_05", "Head_Hat_05", "m_Hat05"),
		Spec("SM_Chr_Attach_Beanie_01", "Head_Beanie_01", "m_Beanie01"),

		Spec("SM_Chr_Attach_Beard_01", "Head_Beard_01", "m_Beard01"),
		Spec("SM_Chr_Attach_Beard_04_Mustache", "Head_Beard_04_Mustache", "m_Beard04Mustache", "SM_Chr_Attach_Beard_04"),
		Spec("SM_Chr_Attach_Beard_04", "Head_Beard_04", "m_Beard04"),
		Spec("SM_Chr_Attach_Beard_09_Mustache", "Head_Beard_09_Mustache", "m_Beard09Mustache", "SM_Chr_Attach_Beard_09"),
		Spec("SM_Chr_Attach_Beard_09", "Head_Beard_09", "m_Beard09"),
		Spec("SM_Chr_Attach_Beard_10", "Head_Beard_10", "m_Beard10"),
		Spec("SM_Chr_Attach_Beard_11", "Head_Beard_11", "m_Beard11"),
		Spec("SM_Chr_Attach_Beard_12", "Head_Beard_12", "m_Beard12"),
		Spec("SM_Chr_Attach_Mustache_01", "Head_Mustache_01", "m_Mustache01")
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Character Head Appearance Content")]
	public static void BuildCharacterHeadAppearanceContent()
	{
		Transform sceneRoot = FindSceneReferenceRoot();
		if (sceneRoot == null)
		{
			Debug.LogError($"[CharacterHeadAppearanceContentBuilder] Не найден '{c_SceneReferenceRootName}' в открытых сценах.");
			return;
		}

		EnsureDirectory(c_OutputRoot);
		EnsureDirectory(c_RankTableRoot);

		var builtPrefabs = new GameObject[s_DecorationSpecs.Length];
		for (int i = 0; i < s_DecorationSpecs.Length; i++)
			builtPrefabs[i] = BuildPrefab(sceneRoot, s_DecorationSpecs[i]);

		UnitHeadAppearanceRankTable table = CreateOrUpdateRankTable();
		SetupUnitPrefab(builtPrefabs, table);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[CharacterHeadAppearanceContentBuilder] Head appearance content complete.");
	}
	#endregion

	#region Build Steps
	private static GameObject BuildPrefab(Transform _sceneRoot, HeadDecorationBuildSpec _spec)
	{
		Transform head = FindChildByName(_sceneRoot, c_HeadAnchorName);
		Transform source = head != null ? head.Find(_spec.SceneObjectName) : null;
		if (source == null)
			source = FindChildByName(_sceneRoot, _spec.SceneObjectName);

		GameObject clone;
		if (source != null)
		{
			source.gameObject.SetActive(true);
			SetActiveRecursively(source);
			EnableRenderers(source.gameObject);
			clone = UnityEngine.Object.Instantiate(source.gameObject);
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
				Debug.LogError($"[CharacterHeadAppearanceContentBuilder] Не найден '{_spec.SceneObjectName}' и fallback '{_spec.FallbackPolygonPath}'.");
				return AssetDatabase.LoadAssetAtPath<GameObject>(GetOutputPath(_spec.OutputName));
			}

			clone = PrefabUtility.InstantiatePrefab(fallback) as GameObject;
			clone.name = _spec.SceneObjectName;
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			clone.transform.localScale = Vector3.one;
			Debug.LogWarning($"[CharacterHeadAppearanceContentBuilder] Референс '{_spec.SceneObjectName}' не найден на сцене. Использован fallback.");
		}

		CharacterDecorationSpawnUtility.StripPickupAndPhysics(clone);
		EnableRenderers(clone);
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, GetOutputPath(_spec.OutputName));
		UnityEngine.Object.DestroyImmediate(clone);
		return prefab;
	}

	private static UnitHeadAppearanceRankTable CreateOrUpdateRankTable()
	{
		UnitHeadAppearanceRankTable table =
			AssetDatabase.LoadAssetAtPath<UnitHeadAppearanceRankTable>(UnitHeadAppearanceRankTable.DefaultAssetPath);
		if (table == null)
		{
			table = ScriptableObject.CreateInstance<UnitHeadAppearanceRankTable>();
			AssetDatabase.CreateAsset(table, UnitHeadAppearanceRankTable.DefaultAssetPath);
		}

		SerializedObject so = new SerializedObject(table);
		AssignRankWeights(so.FindProperty("m_RankWeights"), UnitHeadAppearanceRankTable.CreateDefaultRankWeights());
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(table);
		return table;
	}

	private static void SetupUnitPrefab(GameObject[] _builtPrefabs, UnitHeadAppearanceRankTable _table)
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_PlayerUnitPrefabPath);

		if (!unitRoot.TryGetComponent(out UnitCharacterHeadAppearance headAppearance))
			headAppearance = unitRoot.AddComponent<UnitCharacterHeadAppearance>();

		Transform headAnchor = FindChildByName(unitRoot.transform, c_HeadAnchorName);
		if (headAnchor == null)
		{
			Debug.LogError($"[CharacterHeadAppearanceContentBuilder] На Unit.prefab не найден якорь '{c_HeadAnchorName}'.");
			PrefabUtility.UnloadPrefabContents(unitRoot);
			return;
		}

		RemoveStaticHeadAppearance(headAnchor);

		SerializedObject headSo = new SerializedObject(headAppearance);
		headSo.FindProperty("m_HeadAnchor").objectReferenceValue = headAnchor;
		for (int i = 0; i < s_DecorationSpecs.Length; i++)
			AssignVariantPrefab(headSo, s_DecorationSpecs[i].UnitVariantPropertyName, _builtPrefabs[i]);
		headSo.ApplyModifiedPropertiesWithoutUndo();

		UnitIndividualTraits traits = UnitIndividualTraits.GetOrCreate(unitRoot);
		SerializedObject traitsSo = new SerializedObject(traits);
		traitsSo.FindProperty("m_HeadAppearanceRankTable").objectReferenceValue = _table;
		traitsSo.ApplyModifiedPropertiesWithoutUndo();

		PrefabUtility.SaveAsPrefabAsset(unitRoot, c_PlayerUnitPrefabPath);
		PrefabUtility.UnloadPrefabContents(unitRoot);
		Debug.Log("[CharacterHeadAppearanceContentBuilder] Unit.prefab updated with head appearance.");
	}
	#endregion

	#region Helpers
	private static HeadDecorationBuildSpec Spec(
		string _sceneObjectName,
		string _outputName,
		string _unitVariantPropertyName,
		string _fallbackName = null)
	{
		string fallbackName = string.IsNullOrWhiteSpace(_fallbackName) ? _sceneObjectName : _fallbackName;
		return new HeadDecorationBuildSpec
		{
			SceneObjectName = _sceneObjectName,
			OutputName = _outputName,
			FallbackPolygonPath = $"Assets/PolygonMilitary/Prefabs/Characters/Attachments/{fallbackName}.prefab",
			UnitVariantPropertyName = _unitVariantPropertyName
		};
	}

	private static void AssignRankWeights(SerializedProperty _property, UnitHeadAppearanceRankWeights[] _rankWeights)
	{
		_property.arraySize = _rankWeights.Length;
		for (int i = 0; i < _rankWeights.Length; i++)
		{
			SerializedProperty entry = _property.GetArrayElementAtIndex(i);
			entry.FindPropertyRelative("m_RankAssetName").stringValue = _rankWeights[i].RankAssetName;
			AssignWeights(entry.FindPropertyRelative("m_MaleHairWeights"), _rankWeights[i].MaleHairWeights);
			AssignWeights(entry.FindPropertyRelative("m_MaleBeardWeights"), _rankWeights[i].MaleBeardWeights);
		}
	}

	private static void AssignWeights(SerializedProperty _property, UnitHeadAppearanceVariantWeight[] _weights)
	{
		_property.arraySize = _weights.Length;
		for (int i = 0; i < _weights.Length; i++)
		{
			SerializedProperty entry = _property.GetArrayElementAtIndex(i);
			entry.FindPropertyRelative("m_VariantIndex").intValue = _weights[i].VariantIndex;
			entry.FindPropertyRelative("m_Weight").intValue = _weights[i].Weight;
		}
	}

	private static void AssignVariantPrefab(SerializedObject _so, string _propertyName, GameObject _prefab)
	{
		if (_prefab == null)
			return;

		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property == null)
			throw new InvalidOperationException($"Missing property '{_propertyName}' on UnitCharacterHeadAppearance.");

		property.FindPropertyRelative("m_Prefab").objectReferenceValue = _prefab;
		property.FindPropertyRelative("m_LocalPosition").vector3Value = Vector3.zero;
		property.FindPropertyRelative("m_LocalEulerAngles").vector3Value = Vector3.zero;
	}

	private static void RemoveStaticHeadAppearance(Transform _headAnchor)
	{
		string[] names =
		{
			"SM_Chr_Attach_Hair_Male_04",
			"SM_Chr_Attach_Hair_Male_02",
			"SM_Chr_Attach_Hair_Male_03",
			"SM_Chr_Attach_Hair_Male_05",
			"SM_Chr_Attach_Hair_Male_06",
			"SM_Chr_Attach_Hair_Male_07",
			"SM_Chr_Attach_Hair_Male_08",
			"SM_Chr_Attach_Hair_Male_10"
		};

		for (int i = _headAnchor.childCount - 1; i >= 0; i--)
		{
			Transform child = _headAnchor.GetChild(i);
			for (int n = 0; n < names.Length; n++)
			{
				if (!string.Equals(child.name, names[n], StringComparison.Ordinal))
					continue;

				UnityEngine.Object.DestroyImmediate(child.gameObject);
				break;
			}
		}
	}

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

	private static string GetOutputPath(string _outputName)
	{
		return $"{c_OutputRoot}/{_outputName}.prefab";
	}

	private static void EnsureDirectory(string _path)
	{
		if (string.IsNullOrWhiteSpace(_path) || Directory.Exists(_path))
			return;

		Directory.CreateDirectory(_path);
	}
	#endregion
}
#endif
