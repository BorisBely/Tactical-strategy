#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Сборка рюкзаков: equipped prefab, loot prefab, ItemDefinition и wiring Unit.prefab.
/// Использует уже созданные Equipped_Backpack_* prefab'ы (без зависимости от открытой сцены).
/// </summary>
public static class BackpackContentBuilder
{
	#region Constants
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
		public string AssetSuffix;
		public string LocalizationKey;
		public string FallbackPrefabPath;
		public int Capacity;
	}

	private static readonly BackpackBuildSpec[] s_Specs =
	{
		new BackpackBuildSpec
		{
			AssetSuffix = "Backpack_1",
			LocalizationKey = "item.backpack.1",
			FallbackPrefabPath = c_FallbackRoot + "/SM_Chr_Attach_Backpack_01.prefab",
			Capacity = 20
		},
		new BackpackBuildSpec
		{
			AssetSuffix = "Backpack_2",
			LocalizationKey = "item.backpack.2",
			FallbackPrefabPath = c_FallbackRoot + "/SM_Chr_Attach_Backpack_02.prefab",
			Capacity = 12
		}
	};
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Build Backpack Content")]
	public static void BuildBackpackContent()
	{
		EnsureDirectory(c_EquippedRoot);
		EnsureDirectory(c_LootRoot);
		EnsureDirectory(c_ItemRoot);

		for (int i = 0; i < s_Specs.Length; i++)
			BuildBackpackEntry(s_Specs[i]);

		SetupUnitBackComponents();
		MissionPrepAvailableEquipmentBaker.RebuildAvailableEquipmentSet();

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[BackpackContentBuilder] Backpack content built from equipped prefabs and mission prep set rebuilt.");
	}

	[MenuItem("Polygone/Equipment/Build Backpack Content From Scene Reference")]
	public static void BuildBackpackContentFromSceneReference()
	{
		BuildBackpackContent();
	}
	#endregion

	#region Build Steps
	private static void BuildBackpackEntry(BackpackBuildSpec _spec)
	{
		string equippedPath = $"{c_EquippedRoot}/Equipped_{_spec.AssetSuffix}.prefab";
		GameObject equippedPrefab = BuildEquippedPrefab(_spec, equippedPath);

		string lootPath = $"{c_LootRoot}/Loot_{_spec.AssetSuffix}.prefab";
		GameObject lootPrefab = BuildLootPrefab(equippedPrefab, lootPath, _spec.AssetSuffix);

		string itemPath = $"{c_ItemRoot}/Item_{_spec.AssetSuffix}.asset";
		ItemDefinition item = CreateOrUpdateItemDefinition(itemPath, _spec, equippedPrefab, lootPrefab);
		AssignLootPickupDefinition(lootPrefab, item);
	}

	private static GameObject BuildEquippedPrefab(BackpackBuildSpec _spec, string _outputPath)
	{
		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(_outputPath);
		if (existing != null)
		{
			GameObject clone = (GameObject)PrefabUtility.InstantiatePrefab(existing);
			try
			{
				clone.name = $"Equipped_{_spec.AssetSuffix}";
				PrepareVisual(clone);
				return PrefabUtility.SaveAsPrefabAsset(clone, _outputPath);
			}
			finally
			{
				Object.DestroyImmediate(clone);
			}
		}

		GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(_spec.FallbackPrefabPath);
		if (fallback == null)
		{
			Debug.LogError($"[BackpackContentBuilder] Не найден equipped prefab и fallback '{_spec.FallbackPrefabPath}'.");
			return null;
		}

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fallback);
		try
		{
			instance.name = $"Equipped_{_spec.AssetSuffix}";
			instance.transform.SetParent(null, false);
			instance.transform.localPosition = Vector3.zero;
			instance.transform.localRotation = Quaternion.identity;
			instance.transform.localScale = Vector3.one;
			PrepareVisual(instance);
			Debug.LogWarning($"[BackpackContentBuilder] Equipped prefab '{_outputPath}' не найден. Создан из fallback с нулевой позой.");
			return PrefabUtility.SaveAsPrefabAsset(instance, _outputPath);
		}
		finally
		{
			Object.DestroyImmediate(instance);
		}
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

		GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(_equippedPrefab);
		try
		{
			visual.transform.SetParent(root.transform, false);
			visual.name = "Visual";
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
			CharacterDecorationSpawnUtility.StripPickupAndPhysics(visual);
		}
		catch
		{
			Object.DestroyImmediate(visual);
			throw;
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
