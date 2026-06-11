#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Сборка префабов шлемов, ItemDefinition и профилей визуала из референсов на сцене.
/// </summary>
public static class HelmetContentBuilder
{
	#region Constants
	private const string c_SceneReferenceRootName = "SM_Chr_Soldier_Male_02_Alt_01";
	private const string c_HeadAnchorName = "Head";
	private static readonly string[] s_RequiredHelmetChildNames =
	{
		"Helmet_1",
		"Helmet_2",
		"Helmet_03",
		"Helmet_4"
	};

	private const string c_EquippedRoot = "Assets/Prefabs/Equipment/Helmets/Equipped";
	private const string c_LootRoot = "Assets/Prefabs/World/Loot/Helmets";
	private const string c_ItemRoot = "Assets/GameData/Inventory/Helmets";
	private const string c_ProfileRoot = "Assets/GameData/Inventory/Helmets/Profiles";
	private const string c_CatalogPath = EquipmentVisualProfileCatalog.DefaultAssetPath;

	private const string c_ProfileKevlarPath = c_ProfileRoot + "/Profile_Helmet_Kevlar.asset";
	private const string c_ProfileTacticalPath = c_ProfileRoot + "/Profile_Helmet_Tactical.asset";
	private const string c_ProfileCrewPath = c_ProfileRoot + "/Profile_Helmet_Crew.asset";
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Helmet Content From Scene Reference")]
	public static void BuildHelmetContentFromSceneReference()
	{
		Transform head = FindSceneHeadReference();
		if (head == null)
		{
			Debug.LogError(
				$"[HelmetContentBuilder] Не найден '{c_SceneReferenceRootName}/{c_HeadAnchorName}' в открытых сценах.");
			return;
		}

		EnsureDirectory(c_EquippedRoot);
		EnsureDirectory(c_LootRoot);
		EnsureDirectory(c_ItemRoot);
		EnsureDirectory(c_ProfileRoot);

		EquipmentVisualProfileDefinition profileKevlar = CreateOrUpdateProfile(
			c_ProfileKevlarPath,
			"helmet_kevlar",
			new[]
			{
				new EquipmentVisualVariantWeight(0, 20),
				new EquipmentVisualVariantWeight(1, 30),
				new EquipmentVisualVariantWeight(2, 50)
			},
			0.5f);

		EquipmentVisualProfileDefinition profileTactical = CreateOrUpdateProfile(
			c_ProfileTacticalPath,
			"helmet_tactical",
			new[]
			{
				new EquipmentVisualVariantWeight(0, 10),
				new EquipmentVisualVariantWeight(1, 10),
				new EquipmentVisualVariantWeight(2, 80)
			},
			0.5f);

		EquipmentVisualProfileDefinition profileCrew = CreateOrUpdateProfile(
			c_ProfileCrewPath,
			"helmet_crew",
			new[] { new EquipmentVisualVariantWeight(0, 100) },
			0f);

		EquipmentVisualProfileCatalog catalog = CreateOrUpdateCatalog(
			new[] { profileKevlar, profileTactical, profileCrew });

		BuildHelmetEntry(head, "Helmet_1", "Helmet_1_Kevlar", "item.helmet.kevlar_1", profileKevlar);
		BuildHelmetEntry(head, "Helmet_2", "Helmet_2_Kevlar_Mod", "item.helmet.kevlar_2", profileKevlar);
		BuildHelmetEntry(head, "Helmet_03", "Helmet_3_Tactical", "item.helmet.tactical", profileTactical);
		BuildHelmetEntry(head, "Helmet_4", "Helmet_4_Crew", "item.helmet.crew", profileCrew);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		HelmetUnitSetupEditor.SetupUnitHelmetComponents(catalog);
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();
		Debug.Log("[HelmetContentBuilder] Helmet content built and mission prep available equipment set rebuilt.");
	}
	#endregion

	#region Build Steps
	private static void BuildHelmetEntry(
		Transform _head,
		string _sceneHelmetName,
		string _assetSuffix,
		string _localizationKey,
		EquipmentVisualProfileDefinition _profile)
	{
		Transform source = _head.Find(_sceneHelmetName);
		if (source == null)
		{
			Debug.LogWarning($"[HelmetContentBuilder] Skip missing scene object '{_sceneHelmetName}'.");
			return;
		}

		string equippedPath = $"{c_EquippedRoot}/Equipped_{_assetSuffix}.prefab";
		GameObject equippedRoot = BuildEquippedPrefab(source, equippedPath);

		string lootPath = $"{c_LootRoot}/Loot_{_assetSuffix}.prefab";
		GameObject lootPrefab = BuildLootPrefab(equippedRoot, lootPath, _assetSuffix);

		string itemPath = $"{c_ItemRoot}/Item_{_assetSuffix}.asset";
		ItemDefinition item = CreateOrUpdateItemDefinition(
			itemPath,
			_assetSuffix,
			_localizationKey,
			_profile,
			equippedRoot,
			lootPrefab);
		AssignLootPickupDefinition(lootPrefab, item);
	}

	private static void AssignLootPickupDefinition(GameObject _lootPrefab, ItemDefinition _item)
	{
		if (_lootPrefab == null || _item == null)
			return;

		string path = AssetDatabase.GetAssetPath(_lootPrefab);
		if (string.IsNullOrEmpty(path))
			return;

		GameObject contents = PrefabUtility.LoadPrefabContents(path);
		try
		{
			if (contents.TryGetComponent(out WorldPickupItem pickup))
			{
				SerializedObject so = new SerializedObject(pickup);
				so.FindProperty("m_Definition").objectReferenceValue = _item;
				so.ApplyModifiedPropertiesWithoutUndo();
			}

			PrefabUtility.SaveAsPrefabAsset(contents, path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(contents);
		}
	}

	private static GameObject BuildEquippedPrefab(Transform _source, string _outputPath)
	{
		GameObject clone = UnityEngine.Object.Instantiate(_source.gameObject);
		clone.name = Path.GetFileNameWithoutExtension(_outputPath);
		clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
		clone.transform.localScale = Vector3.one;
		clone.SetActive(true);

		DisableAllDecorationChildren(clone.transform);

		HelmetEquippedVisual visual = clone.GetComponent<HelmetEquippedVisual>();
		if (visual == null)
			visual = clone.AddComponent<HelmetEquippedVisual>();

		visual.ResolveDecorationRootsFromChildren();
		visual.ApplyDefault();

		GameObject prefab = SavePrefab(clone, _outputPath);
		UnityEngine.Object.DestroyImmediate(clone);
		return prefab;
	}

	private static GameObject BuildLootPrefab(GameObject _equippedPrefab, string _outputPath, string _assetSuffix)
	{
		GameObject root = new GameObject($"Loot_{_assetSuffix}");
		try
		{
			int lootLayer = LayerMask.NameToLayer("Loot");
			root.layer = lootLayer >= 0 ? lootLayer : 0;

			GameObject visual = PrefabUtility.InstantiatePrefab(_equippedPrefab, root.transform) as GameObject;
			if (visual != null)
			{
				visual.name = "Visual";
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = Quaternion.identity;
				visual.transform.localScale = Vector3.one;
				visual.SetActive(true);
				if (visual.TryGetComponent(out HelmetEquippedVisual helmetVisual))
					helmetVisual.ApplyDefault();
			}

			BoxCollider collider = root.AddComponent<BoxCollider>();
			collider.size = new Vector3(0.28f, 0.24f, 0.28f);
			collider.center = new Vector3(0f, 0.04f, 0f);

			Rigidbody body = root.AddComponent<Rigidbody>();
			body.mass = 1.2f;
			body.collisionDetectionMode = CollisionDetectionMode.Discrete;

			root.AddComponent<WorldPickupItem>();
			return SavePrefab(root, _outputPath);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
		}
	}

	private static ItemDefinition CreateOrUpdateItemDefinition(
		string _assetPath,
		string _displaySuffix,
		string _localizationKey,
		EquipmentVisualProfileDefinition _profile,
		GameObject _equippedPrefab,
		GameObject _lootPrefab)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_assetPath);
		if (item == null)
		{
			item = ScriptableObject.CreateInstance<ItemDefinition>();
			AssetDatabase.CreateAsset(item, _assetPath);
		}

		SerializedObject so = new SerializedObject(item);
		so.FindProperty("m_LocalizationKey").stringValue = _localizationKey;
		so.FindProperty("m_Description").stringValue = $"Helmet {_displaySuffix}";
		so.FindProperty("m_Category").enumValueIndex = (int)ItemCategory.Equipment;
		so.FindProperty("m_EquipmentKind").enumValueIndex = (int)EquipmentKind.Helmet;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _equippedPrefab;
		so.FindProperty("m_DropWorldPrefab").objectReferenceValue = _lootPrefab;
		so.FindProperty("m_VisualProfile").objectReferenceValue = _profile;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
	}

	private static EquipmentVisualProfileDefinition CreateOrUpdateProfile(
		string _assetPath,
		string _profileId,
		EquipmentVisualVariantWeight[] _weights,
		float _chinStrapChance)
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
		so.FindProperty("m_ChinStrapIndependentChance").floatValue = _chinStrapChance;

		SerializedProperty weightsProperty = so.FindProperty("m_PrimaryVariantWeights");
		weightsProperty.arraySize = _weights.Length;
		for (int i = 0; i < _weights.Length; i++)
		{
			SerializedProperty element = weightsProperty.GetArrayElementAtIndex(i);
			element.FindPropertyRelative("m_VariantIndex").intValue = _weights[i].VariantIndex;
			element.FindPropertyRelative("m_Weight").intValue = _weights[i].Weight;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(profile);
		return profile;
	}

	private static EquipmentVisualProfileCatalog CreateOrUpdateCatalog(
		EquipmentVisualProfileDefinition[] _profiles)
	{
		EquipmentVisualProfileCatalog catalog =
			AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileCatalog>(c_CatalogPath);
		if (catalog == null)
		{
			catalog = ScriptableObject.CreateInstance<EquipmentVisualProfileCatalog>();
			AssetDatabase.CreateAsset(catalog, c_CatalogPath);
		}

		SerializedObject so = new SerializedObject(catalog);
		SerializedProperty profilesProperty = so.FindProperty("m_Profiles");
		profilesProperty.arraySize = _profiles.Length;
		for (int i = 0; i < _profiles.Length; i++)
			profilesProperty.GetArrayElementAtIndex(i).objectReferenceValue = _profiles[i];

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(catalog);
		return catalog;
	}
	#endregion

	#region Helpers
	private static Transform FindSceneHeadReference()
	{
		Transform head = FindHeadInLoadedScenes();
		if (head != null)
			return head;

		const string sampleScenePath = "Assets/Scenes/SampleScene.unity";
		if (!File.Exists(sampleScenePath))
			return null;

		Scene sampleScene = EditorSceneManager.OpenScene(sampleScenePath, OpenSceneMode.Single);
		if (!sampleScene.IsValid())
			return null;

		return FindHeadInLoadedScenes();
	}

	private static Transform FindHeadInLoadedScenes()
	{
		for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
		{
			Scene scene = SceneManager.GetSceneAt(sceneIndex);
			if (!scene.isLoaded)
				continue;

			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
			{
				Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
				Transform helmetHead = FindHeadWithHelmetChildren(all);
				if (helmetHead != null)
					return helmetHead;

				for (int j = 0; j < all.Length; j++)
				{
					Transform candidate = all[j];
					if (candidate.name != c_SceneReferenceRootName)
						continue;

					Transform head = FindChildRecursive(candidate, c_HeadAnchorName);
					if (head != null)
						return head;

					Debug.LogWarning(
						$"[HelmetContentBuilder] Найден '{c_SceneReferenceRootName}', но внутри нет '{c_HeadAnchorName}'. Дочерние объекты: {DescribeImmediateChildren(candidate)}.");
				}
			}
		}

		return null;
	}

	private static Transform FindHeadWithHelmetChildren(Transform[] _transforms)
	{
		if (_transforms == null)
			return null;

		for (int i = 0; i < _transforms.Length; i++)
		{
			Transform candidate = _transforms[i];
			if (candidate == null || candidate.name != c_HeadAnchorName)
				continue;

			if (HasHelmetChildren(candidate))
				return candidate;
		}

		return null;
	}

	private static bool HasHelmetChildren(Transform _head)
	{
		for (int i = 0; i < s_RequiredHelmetChildNames.Length; i++)
		{
			if (_head.Find(s_RequiredHelmetChildNames[i]) == null)
				return false;
		}

		return true;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i].name == _name)
				return children[i];
		}

		return null;
	}

	private static string DescribeImmediateChildren(Transform _root)
	{
		if (_root == null || _root.childCount == 0)
			return "<none>";

		var builder = new StringBuilder();
		for (int i = 0; i < _root.childCount; i++)
		{
			if (i > 0)
				builder.Append(", ");

			builder.Append(_root.GetChild(i).name);
		}

		return builder.ToString();
	}

	private static void DisableAllDecorationChildren(Transform _root)
	{
		if (_root == null)
			return;

		Transform[] descendants = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < descendants.Length; i++)
		{
			Transform child = descendants[i];
			if (child == null || child == _root)
				continue;

			child.gameObject.SetActive(false);
		}
	}

	private static GameObject SavePrefab(GameObject _root, string _path)
	{
		EnsureDirectory(Path.GetDirectoryName(_path)?.Replace('\\', '/'));
		return PrefabUtility.SaveAsPrefabAsset(_root, _path);
	}

	[MenuItem("Polygone/Equipment/Validate Helmet Prefabs")]
	public static void ValidateHelmetPrefabs()
	{
		int issueCount = 0;
		StringBuilder report = new StringBuilder();
		report.AppendLine("[HelmetContentBuilder] Helmet prefab validation:");

		string[] equippedGuids = AssetDatabase.FindAssets("Equipped_Helmet_ t:Prefab", new[] { c_EquippedRoot });
		for (int i = 0; i < equippedGuids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(equippedGuids[i]);
			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
			bool changed = false;
			try
			{
				if (!prefabRoot.activeSelf)
				{
					prefabRoot.SetActive(true);
					changed = true;
					issueCount++;
					report.AppendLine($"  FIXED: {path} — root was inactive, enabled.");
				}

				if (!prefabRoot.TryGetComponent(out HelmetEquippedVisual visual))
				{
					issueCount++;
					report.AppendLine($"  ERROR: {path} — missing HelmetEquippedVisual.");
				}
				else if (!visual.enabled)
				{
					visual.enabled = true;
					changed = true;
					issueCount++;
					report.AppendLine($"  FIXED: {path} — HelmetEquippedVisual was disabled, enabled.");
				}

				if (!prefabRoot.TryGetComponent(out MeshRenderer renderer))
				{
					issueCount++;
					report.AppendLine($"  ERROR: {path} — missing MeshRenderer on root.");
				}
				else if (!renderer.enabled)
				{
					renderer.enabled = true;
					changed = true;
					issueCount++;
					report.AppendLine($"  FIXED: {path} — MeshRenderer was disabled, enabled.");
				}

				if (changed)
					PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
		}

		string[] lootGuids = AssetDatabase.FindAssets("Loot_Helmet_ t:Prefab", new[] { c_LootRoot });
		for (int i = 0; i < lootGuids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(lootGuids[i]);
			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
			bool changed = false;
			try
			{
				if (!prefabRoot.activeSelf)
				{
					prefabRoot.SetActive(true);
					changed = true;
					issueCount++;
					report.AppendLine($"  FIXED: {path} — loot root was inactive, enabled.");
				}

				WorldPickupItem pickup = prefabRoot.GetComponent<WorldPickupItem>();
				if (pickup == null)
				{
					issueCount++;
					report.AppendLine($"  ERROR: {path} — missing WorldPickupItem.");
				}
				else if (!pickup.enabled)
				{
					pickup.enabled = true;
					changed = true;
					issueCount++;
					report.AppendLine($"  FIXED: {path} — WorldPickupItem was disabled, enabled.");
				}

				Transform visual = prefabRoot.transform.Find("Visual");
				if (visual == null)
				{
					issueCount++;
					report.AppendLine($"  ERROR: {path} — missing Visual child.");
				}
				else if (!visual.gameObject.activeSelf)
				{
					visual.gameObject.SetActive(true);
					changed = true;
					issueCount++;
					report.AppendLine($"  FIXED: {path} — Visual child was inactive, enabled.");
				}

				if (changed)
					PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
		}

		if (issueCount == 0)
			report.AppendLine("  All helmet prefabs OK.");
		else
			AssetDatabase.SaveAssets();

		Debug.Log(report.ToString());
	}

	private static void EnsureDirectory(string _path)
	{
		if (string.IsNullOrWhiteSpace(_path))
			return;

		if (!Directory.Exists(_path))
			Directory.CreateDirectory(_path);
	}
	#endregion
}

/// <summary>
/// Подключает компоненты шлема и каталог профилей к префабу юнита.
/// </summary>
public static class HelmetUnitSetupEditor
{
	private const string c_PlayerUnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";

	public static void SetupUnitHelmetComponents(EquipmentVisualProfileCatalog _catalog = null)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(c_PlayerUnitPrefabPath);
		bool changed = false;

		if (EnsureComponent<UnitHeadEquipment>(root, out UnitHeadEquipment headEquipment))
			changed = true;

		if (EnsureComponent<UnitCharacterAppearance>(root, out _))
			changed = true;

		if (EnsureComponent<UnitIndividualTraits>(root, out UnitIndividualTraits traits))
			changed = true;

		Transform headAnchor = FindHeadAnchor(root.transform);
		if (headEquipment != null && headAnchor != null)
		{
			SerializedObject headSo = new SerializedObject(headEquipment);
			headSo.FindProperty("m_HeadAnchor").objectReferenceValue = headAnchor;
			headSo.ApplyModifiedPropertiesWithoutUndo();
			changed = true;
		}

		if (traits != null)
		{
			EquipmentVisualProfileCatalog catalog = _catalog ??
				AssetDatabase.LoadAssetAtPath<EquipmentVisualProfileCatalog>(
					EquipmentVisualProfileCatalog.DefaultAssetPath);

			SerializedObject traitsSo = new SerializedObject(traits);
			if (catalog != null)
			{
				traitsSo.FindProperty("m_EquipmentVisualProfileCatalog").objectReferenceValue = catalog;
				traitsSo.FindProperty("m_RollOnAwake").boolValue = true;
				traitsSo.FindProperty("m_IsInitialized").boolValue = false;
			}

			traitsSo.ApplyModifiedPropertiesWithoutUndo();
			changed = true;
		}

		if (changed)
		{
			PrefabUtility.SaveAsPrefabAsset(root, c_PlayerUnitPrefabPath);
			Debug.Log("[HelmetUnitSetupEditor] Unit prefab updated with head equipment components.");
		}

		PrefabUtility.UnloadPrefabContents(root);
	}

	[MenuItem("Polygone/Equipment/Setup Unit Helmet Components")]
	public static void SetupFromMenu()
	{
		SetupUnitHelmetComponents();
	}

	private static Transform FindHeadAnchor(Transform _root)
	{
		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i].name == "Head")
				return children[i];
		}

		return null;
	}

	private static bool EnsureComponent<T>(GameObject _root, out T _component) where T : Component
	{
		_component = _root.GetComponent<T>();
		if (_component != null)
			return false;

		_component = _root.AddComponent<T>();
		return true;
	}
}
#endif
