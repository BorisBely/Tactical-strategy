#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Сборка рюкзаков: equipped prefab, loot prefab, ItemDefinition и wiring Unit.prefab.
/// </summary>
public static class BackpackContentBuilder
{
	#region Constants
	private const string c_SceneReferenceRootName = "SM_Chr_Soldier_Male_02_Alt_01";
	private const string c_BackAnchorName = "Spine_02";
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";

	private const string c_EquippedRoot = "Assets/Prefabs/Equipment/Backpacks/Equipped";
	private const string c_LootRoot = "Assets/Prefabs/World/Loot/Backpacks";
	private const string c_ItemRoot = "Assets/GameData/Inventory/Backpacks";
	private const string c_FallbackRoot = "Assets/PolygonMilitary/Prefabs/Characters/Attachments";
	#endregion

	#region Specs
	private sealed class BackpackBuildSpec
	{
		public string SceneObjectName;
		public string AssetSuffix;
		public string LocalizationKey;
		public string FallbackPrefabPath;
		public int Capacity;
	}

	private static readonly BackpackBuildSpec[] s_Specs =
	{
		new BackpackBuildSpec
		{
			SceneObjectName = "Backpack_1",
			AssetSuffix = "Backpack_1",
			LocalizationKey = "item.backpack.1",
			FallbackPrefabPath = c_FallbackRoot + "/SM_Chr_Attach_Backpack_01.prefab",
			Capacity = 20
		},
		new BackpackBuildSpec
		{
			SceneObjectName = "Backpack_2",
			AssetSuffix = "Backpack_2",
			LocalizationKey = "item.backpack.2",
			FallbackPrefabPath = c_FallbackRoot + "/SM_Chr_Attach_Backpack_02.prefab",
			Capacity = 12
		}
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Backpack Content From Scene Reference")]
	public static void BuildBackpackContentFromSceneReference()
	{
		Transform spine02 = FindSceneBackReference();
		if (spine02 == null)
		{
			Debug.LogError($"[BackpackContentBuilder] Не найден '{c_SceneReferenceRootName}/{c_BackAnchorName}' в открытых сценах.");
			return;
		}

		EnsureDirectory(c_EquippedRoot);
		EnsureDirectory(c_LootRoot);
		EnsureDirectory(c_ItemRoot);

		for (int i = 0; i < s_Specs.Length; i++)
			BuildBackpackEntry(spine02, s_Specs[i]);

		SetupUnitBackComponents();
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[BackpackContentBuilder] Backpack content built and mission prep set rebuilt.");
	}
	#endregion

	#region Build Steps
	private static void BuildBackpackEntry(Transform _spine02, BackpackBuildSpec _spec)
	{
		Transform source = _spine02.Find(_spec.SceneObjectName);
		if (source == null)
			source = FindChildByName(_spine02, _spec.SceneObjectName);

		string equippedPath = $"{c_EquippedRoot}/Equipped_{_spec.AssetSuffix}.prefab";
		GameObject equippedPrefab = BuildEquippedPrefab(source, _spec, equippedPath);

		string lootPath = $"{c_LootRoot}/Loot_{_spec.AssetSuffix}.prefab";
		GameObject lootPrefab = BuildLootPrefab(equippedPrefab, lootPath, _spec.AssetSuffix);

		string itemPath = $"{c_ItemRoot}/Item_{_spec.AssetSuffix}.asset";
		ItemDefinition item = CreateOrUpdateItemDefinition(itemPath, _spec, equippedPrefab, lootPrefab);
		AssignLootPickupDefinition(lootPrefab, item);
	}

	private static GameObject BuildEquippedPrefab(Transform _source, BackpackBuildSpec _spec, string _outputPath)
	{
		GameObject clone;
		if (_source != null)
		{
			_source.gameObject.SetActive(true);
			SetActiveRecursively(_source);
			clone = Object.Instantiate(_source.gameObject);
			clone.name = $"Equipped_{_spec.AssetSuffix}";
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = _source.localPosition;
			clone.transform.localRotation = _source.localRotation;
			clone.transform.localScale = _source.localScale;
		}
		else
		{
			GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(_spec.FallbackPrefabPath);
			if (fallback == null)
			{
				Debug.LogError($"[BackpackContentBuilder] Не найден '{_spec.SceneObjectName}' и fallback '{_spec.FallbackPrefabPath}'.");
				return AssetDatabase.LoadAssetAtPath<GameObject>(_outputPath);
			}

			clone = PrefabUtility.InstantiatePrefab(fallback) as GameObject;
			clone.name = $"Equipped_{_spec.AssetSuffix}";
			clone.transform.SetParent(null, false);
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = Quaternion.identity;
			clone.transform.localScale = Vector3.one;
			Debug.LogWarning($"[BackpackContentBuilder] Референс '{_spec.SceneObjectName}' не найден. Использован fallback с нулевой позой.");
		}

		PrepareVisual(clone);
		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, _outputPath);
		Object.DestroyImmediate(clone);
		return prefab;
	}

	private static GameObject BuildLootPrefab(GameObject _equippedPrefab, string _outputPath, string _assetSuffix)
	{
		if (_equippedPrefab == null)
			return AssetDatabase.LoadAssetAtPath<GameObject>(_outputPath);

		GameObject root = new GameObject($"Loot_{_assetSuffix}");
		root.layer = LayerMask.NameToLayer("Loot") >= 0 ? LayerMask.NameToLayer("Loot") : 0;
		root.transform.localPosition = Vector3.zero;
		root.transform.localRotation = Quaternion.identity;
		root.transform.localScale = Vector3.one;

		BoxCollider collider = root.AddComponent<BoxCollider>();
		collider.size = new Vector3(0.38f, 0.42f, 0.24f);
		collider.center = new Vector3(0f, 0.2f, 0f);

		Rigidbody body = root.AddComponent<Rigidbody>();
		body.mass = 1.5f;

		root.AddComponent<WorldPickupItem>();

		GameObject visual = PrefabUtility.InstantiatePrefab(_equippedPrefab, root.transform) as GameObject;
		if (visual != null)
		{
			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			CharacterDecorationSpawnUtility.StripPickupAndPhysics(visual);
		}

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, _outputPath);
		Object.DestroyImmediate(root);
		return prefab;
	}

	private static ItemDefinition CreateOrUpdateItemDefinition(
		string _itemPath,
		BackpackBuildSpec _spec,
		GameObject _equippedPrefab,
		GameObject _lootPrefab)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_itemPath);
		if (item == null)
		{
			item = ScriptableObject.CreateInstance<ItemDefinition>();
			AssetDatabase.CreateAsset(item, _itemPath);
		}

		SerializedObject so = new SerializedObject(item);
		so.FindProperty("m_LocalizationKey").stringValue = _spec.LocalizationKey;
		so.FindProperty("m_Description").stringValue = string.Empty;
		so.FindProperty("m_Category").enumValueIndex = (int)ItemCategory.Equipment;
		so.FindProperty("m_EquipmentKind").enumValueIndex = (int)EquipmentKind.Backpack;
		so.FindProperty("m_EquippedVisualPrefab").objectReferenceValue = _equippedPrefab;
		so.FindProperty("m_DropWorldPrefab").objectReferenceValue = _lootPrefab;
		so.FindProperty("m_BackpackCapacity").intValue = _spec.Capacity;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
		return item;
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

	private static void SetupUnitBackComponents()
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		try
		{
			Transform spine02 = FindChildByName(unitRoot.transform, c_BackAnchorName);
			if (spine02 == null)
			{
				Debug.LogError($"[BackpackContentBuilder] На Unit.prefab не найден '{c_BackAnchorName}'.");
				return;
			}

			RemoveStaticBackpacks(spine02);

			UnitBackEquipment backEquipment = unitRoot.GetComponent<UnitBackEquipment>();
			if (backEquipment == null)
				backEquipment = unitRoot.AddComponent<UnitBackEquipment>();

			SerializedObject so = new SerializedObject(backEquipment);
			so.FindProperty("m_BackAnchor").objectReferenceValue = spine02;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset(unitRoot, c_UnitPrefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(unitRoot);
		}
	}
	#endregion

	#region Helpers
	private static void RemoveStaticBackpacks(Transform _spine02)
	{
		string[] names =
		{
			"SM_Chr_Attach_Backpack_01",
			"SM_Chr_Attach_Backpack_02",
			"Backpack_1",
			"Backpack_2"
		};

		for (int i = _spine02.childCount - 1; i >= 0; i--)
		{
			Transform child = _spine02.GetChild(i);
			for (int n = 0; n < names.Length; n++)
			{
				if (child.name != names[n])
					continue;

				Object.DestroyImmediate(child.gameObject);
				break;
			}
		}
	}

	private static Transform FindSceneBackReference()
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
					if (children[c].name != c_SceneReferenceRootName)
						continue;

					return FindChildByName(children[c], c_BackAnchorName);
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

	private static void PrepareVisual(GameObject _root)
	{
		if (_root == null)
			return;

		SetActiveRecursively(_root.transform);
		CharacterDecorationSpawnUtility.StripPickupAndPhysics(_root);

		Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].enabled = true;
	}

	private static void SetActiveRecursively(Transform _root)
	{
		if (_root == null)
			return;

		_root.gameObject.SetActive(true);
		for (int i = 0; i < _root.childCount; i++)
			SetActiveRecursively(_root.GetChild(i));
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
