#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class WorldPickupPrefabBuilderBootstrap
{
	private const string c_MarkerPath = "Assets/.polygone_build_loot_marker";

	static WorldPickupPrefabBuilderBootstrap()
	{
		EditorApplication.delayCall += TryRunFromMarker;
	}

	private static void TryRunFromMarker()
	{
		if (!File.Exists(c_MarkerPath))
			return;

		try
		{
			File.Delete(c_MarkerPath);
			if (File.Exists(c_MarkerPath + ".meta"))
				File.Delete(c_MarkerPath + ".meta");

			WorldPickupPrefabBuilder.ReorganizeAndBuildAll();
		}
		catch (Exception exception)
		{
			Debug.LogError($"[WorldPickupPrefabBuilder] Auto-run failed: {exception.Message}");
		}
	}
}

/// <summary>
/// Реорганизация папки Prefabs и сборка префабов лута (WorldPickupItem) из визуалов экипировки.
/// </summary>
public static class WorldPickupPrefabBuilder
{
	#region Constants
	private const string c_InventoryDataRoot = "Assets/GameData/Inventory";
	private const string c_LootLayerName = "Loot";

	private const string c_WeaponsRoot = "Assets/Prefabs/Weapons";
	private const string c_WorldLootRoot = "Assets/Prefabs/World/Loot";

	private static readonly (string Source, string Destination)[] s_FolderMoves =
	{
		("Assets/Prefabs/Equipment/M4/Equipped_M4_ModA_1.prefab", $"{c_WeaponsRoot}/M4/Equipped/Equipped_M4_ModA_1.prefab"),
		("Assets/Prefabs/Equipment/M4/Equipped_M4_ModA_2.prefab", $"{c_WeaponsRoot}/M4/Equipped/Equipped_M4_ModA_2.prefab"),
		("Assets/Prefabs/Equipment/ShellCasing_PolygonMilitary_556.prefab", $"{c_WeaponsRoot}/Effects/ShellCasing_PolygonMilitary_556.prefab"),
		("Assets/Prefabs/PlayerUnit.prefab", "Assets/Prefabs/Characters/PlayerUnit.prefab"),
		("Assets/Prefabs/Loot/Loot_AmmoBox_556NATO.prefab", $"{c_WorldLootRoot}/Ammo/Loot_AmmoBox_556NATO.prefab"),
	};
	#endregion

	#region Menu
	[MenuItem("Tools/Polygone/Prefabs/Reorganize Prefab Folders")]
	public static void ReorganizePrefabFolders()
	{
		EnsureDirectory($"{c_WeaponsRoot}/M4/Equipped");
		EnsureDirectory($"{c_WeaponsRoot}/M4/Visuals/Magazines");
		EnsureDirectory($"{c_WeaponsRoot}/M4/Visuals/Attachments");
		EnsureDirectory($"{c_WeaponsRoot}/Effects");
		EnsureDirectory("Assets/Prefabs/Characters");
		EnsureDirectory($"{c_WorldLootRoot}/Ammo");
		EnsureDirectory($"{c_WorldLootRoot}/M4/Weapons");
		EnsureDirectory($"{c_WorldLootRoot}/M4/Magazines");
		EnsureDirectory($"{c_WorldLootRoot}/M4/Attachments");

		MoveAssetIfExists("Assets/Prefabs/Equipment/M4/Magazines", $"{c_WeaponsRoot}/M4/Visuals/Magazines");
		MoveAssetIfExists("Assets/Prefabs/Equipment/M4/Attachments", $"{c_WeaponsRoot}/M4/Visuals/Attachments");

		for (int i = 0; i < s_FolderMoves.Length; i++)
			MoveAssetIfExists(s_FolderMoves[i].Source, s_FolderMoves[i].Destination);

		TryDeleteEmptyFolder("Assets/Prefabs/Loot/M4");
		TryDeleteEmptyFolder("Assets/Prefabs/Loot");
		TryDeleteEmptyFolder("Assets/Prefabs/Equipment/M4");
		TryDeleteEmptyFolder("Assets/Prefabs/Equipment");

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[WorldPickupPrefabBuilder] Prefab folders reorganized.");
	}

	[MenuItem("Tools/Polygone/Prefabs/Build Loot Prefabs From Equipment")]
	public static void BuildLootPrefabsFromEquipment()
	{
		EnsureDirectory($"{c_WorldLootRoot}/M4/Weapons");
		EnsureDirectory($"{c_WorldLootRoot}/M4/Magazines");
		EnsureDirectory($"{c_WorldLootRoot}/M4/Attachments");

		int lootLayer = LayerMask.NameToLayer(c_LootLayerName);
		if (lootLayer < 0)
			Debug.LogWarning($"[WorldPickupPrefabBuilder] Layer '{c_LootLayerName}' not found; using Default.");

		Dictionary<CaliberType, AmmoDefinition> ammoByCaliber = BuildAmmoLookup();
		string[] itemGuids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { c_InventoryDataRoot });
		int builtCount = 0;

		for (int i = 0; i < itemGuids.Length; i++)
		{
			string assetPath = AssetDatabase.GUIDToAssetPath(itemGuids[i]);
			ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
			if (item == null || !TryGetLootBuildKind(item, out LootBuildKind kind))
				continue;

			if (item.EquippedVisualPrefab == null && kind != LootBuildKind.AmmoContainer)
			{
				Debug.LogWarning($"[WorldPickupPrefabBuilder] Skip '{item.name}': no EquippedVisualPrefab.");
				continue;
			}

			string outputPath = GetLootOutputPath(item, kind);
			EnsureDirectory(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));

			GameObject root = new GameObject(GetLootPrefabName(item, kind));

			if (root == null)
				continue;

			try
			{
				ConfigureLootRoot(root, item, kind, lootLayer, ammoByCaliber);
				GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, outputPath);
				if (saved != null)
				{
					AssignDropWorldPrefab(item, saved);
					builtCount++;
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		DeleteLegacyLootPrefabs();
		Debug.Log($"[WorldPickupPrefabBuilder] Built/updated {builtCount} loot prefabs.");
	}

	[MenuItem("Tools/Polygone/Prefabs/Reorganize And Build All Loot")]
	public static void ReorganizeAndBuildAll()
	{
		ReorganizePrefabFolders();
		BuildLootPrefabsFromEquipment();
	}
	#endregion

	#region Types
	private enum LootBuildKind
	{
		Weapon,
		Magazine,
		Attachment,
		AmmoContainer
	}
	#endregion

	#region Build
	private static void ConfigureLootRoot(
		GameObject _root,
		ItemDefinition _item,
		LootBuildKind _kind,
		int _lootLayer,
		Dictionary<CaliberType, AmmoDefinition> _ammoByCaliber)
	{
		_root.name = GetLootPrefabName(_item, _kind);
		_root.layer = _lootLayer >= 0 ? _lootLayer : 0;
		_root.transform.localPosition = Vector3.zero;
		_root.transform.localRotation = Quaternion.identity;
		_root.transform.localScale = Vector3.one;

		StripRootRenderersAndColliders(_root);

		Transform visualRoot = _root.transform.Find("Visual");
		if (visualRoot != null)
			UnityEngine.Object.DestroyImmediate(visualRoot.gameObject);

		if (_item.EquippedVisualPrefab != null)
		{
			GameObject visual = PrefabUtility.InstantiatePrefab(_item.EquippedVisualPrefab, _root.transform) as GameObject;
			if (visual != null)
			{
				visual.name = "Visual";
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = GetLootVisualLocalRotation(_kind);
				visual.transform.localScale = Vector3.one;
				SetLayerRecursively(visual, _root.layer);
				DisablePhysicsOnVisual(visual);
			}
		}

		EnsurePickupCollider(_root, _kind);
		EnsureRigidbody(_root, _kind);

		WorldPickupItem pickup = GetOrAddComponent<WorldPickupItem>(_root);
		ItemInstanceState instanceState = CreateDefaultInstanceState(_item, _kind, _ammoByCaliber);
		WeaponAttachmentDefinition[] attachmentPreset = CopyAttachmentPresetFromVisual(_root);
		ApplyPickupSerializedFields(pickup, _item, instanceState, attachmentPreset);
	}

	private static ItemInstanceState CreateDefaultInstanceState(
		ItemDefinition _item,
		LootBuildKind _kind,
		Dictionary<CaliberType, AmmoDefinition> _ammoByCaliber)
	{
		ItemInstanceState state = ItemInstanceState.CreateForDefinition(_item);

		if (_kind != LootBuildKind.Magazine || _item.MagazineDefinition == null)
			return state;

		AmmoDefinition ammo = ResolveAmmoForMagazine(_item.MagazineDefinition, _ammoByCaliber);
		if (ammo == null)
			return state;

		state.MagazineState?.Configure(_item.MagazineDefinition, ammo, _item.MagazineDefinition.Capacity);
		return state;
	}

	private static AmmoDefinition ResolveAmmoForMagazine(
		MagazineDefinition _magazine,
		Dictionary<CaliberType, AmmoDefinition> _ammoByCaliber)
	{
		if (_magazine == null)
			return null;

		if (_ammoByCaliber.TryGetValue(_magazine.SupportedCaliber, out AmmoDefinition byCaliber))
			return byCaliber;

		return null;
	}

	private static WeaponAttachmentDefinition[] CopyAttachmentPresetFromVisual(GameObject _root)
	{
		EquippedWeapon equippedWeapon = _root.GetComponentInChildren<EquippedWeapon>(true);
		if (equippedWeapon == null)
			return Array.Empty<WeaponAttachmentDefinition>();

		SerializedObject serializedWeapon = new SerializedObject(equippedWeapon);
		SerializedProperty attachmentsProperty = serializedWeapon.FindProperty("m_EquippedAttachments");
		if (attachmentsProperty == null || !attachmentsProperty.isArray)
			return Array.Empty<WeaponAttachmentDefinition>();

		WeaponAttachmentDefinition[] result = new WeaponAttachmentDefinition[attachmentsProperty.arraySize];
		for (int i = 0; i < attachmentsProperty.arraySize; i++)
			result[i] = attachmentsProperty.GetArrayElementAtIndex(i).objectReferenceValue as WeaponAttachmentDefinition;

		return result;
	}

	private static void ApplyPickupSerializedFields(
		WorldPickupItem _pickup,
		ItemDefinition _item,
		ItemInstanceState _instanceState,
		WeaponAttachmentDefinition[] _attachmentPreset)
	{
		SerializedObject serializedPickup = new SerializedObject(_pickup);
		serializedPickup.FindProperty("m_Definition").objectReferenceValue = _item;
		WriteInstanceState(serializedPickup.FindProperty("m_InstanceState"), _instanceState);

		SerializedProperty attachmentsProperty = serializedPickup.FindProperty("m_EquippedAttachments");
		attachmentsProperty.arraySize = _attachmentPreset.Length;
		for (int i = 0; i < _attachmentPreset.Length; i++)
			attachmentsProperty.GetArrayElementAtIndex(i).objectReferenceValue = _attachmentPreset[i];

		serializedPickup.ApplyModifiedPropertiesWithoutUndo();
	}

	private static void WriteInstanceState(SerializedProperty _property, ItemInstanceState _state)
	{
		if (_property == null)
			return;

		WriteWeaponState(_property.FindPropertyRelative("m_WeaponState"), _state?.WeaponState);
		WriteMagazineState(_property.FindPropertyRelative("m_MagazineState"), _state?.MagazineState);
		WriteAmmoContainerState(_property.FindPropertyRelative("m_AmmoContainerState"), _state?.AmmoContainerState);
	}

	private static void WriteWeaponState(SerializedProperty _property, WeaponRuntimeState _state)
	{
		if (_property == null)
			return;

		_property.FindPropertyRelative("m_WeaponDefinition").objectReferenceValue = _state?.WeaponDefinition;
		_property.FindPropertyRelative("m_SelectedFireMode").enumValueIndex = _state != null ? (int)_state.SelectedFireMode : 0;
		_property.FindPropertyRelative("m_Wear01").floatValue = _state?.Wear01 ?? 0f;
		_property.FindPropertyRelative("m_Fouling01").floatValue = _state?.Fouling01 ?? 0f;
		_property.FindPropertyRelative("m_IsTerminallyBroken").boolValue = _state?.IsTerminallyBroken ?? false;
		_property.FindPropertyRelative("m_HasRoundInChamber").boolValue = _state?.HasRoundInChamber ?? false;
		_property.FindPropertyRelative("m_ChamberedAmmoDefinition").objectReferenceValue = _state?.ChamberedAmmoDefinition;
	}

	private static void WriteMagazineState(SerializedProperty _property, MagazineRuntimeState _state)
	{
		if (_property == null)
			return;

		_property.FindPropertyRelative("m_Definition").objectReferenceValue = _state?.Definition;
		_property.FindPropertyRelative("m_LoadedAmmoDefinition").objectReferenceValue = _state?.LoadedAmmoDefinition;
		_property.FindPropertyRelative("m_CurrentAmmoCount").intValue = _state?.CurrentAmmoCount ?? 0;
	}

	private static void WriteAmmoContainerState(SerializedProperty _property, AmmoContainerRuntimeState _state)
	{
		if (_property == null)
			return;

		_property.FindPropertyRelative("m_AmmoDefinition").objectReferenceValue = _state?.AmmoDefinition;
		_property.FindPropertyRelative("m_CurrentAmmoCount").intValue = _state?.CurrentAmmoCount ?? 0;
	}

	private static Quaternion GetLootVisualLocalRotation(LootBuildKind _kind)
	{
		return _kind switch
		{
			LootBuildKind.Weapon => Quaternion.Euler(0f, 90f, 0f),
			LootBuildKind.Magazine => Quaternion.Euler(90f, 0f, 0f),
			_ => Quaternion.identity
		};
	}

	private static void EnsurePickupCollider(GameObject _root, LootBuildKind _kind)
	{
		RemoveComponents<BoxCollider>(_root);
		BoxCollider boxCollider = _root.AddComponent<BoxCollider>();
		if (boxCollider == null)
			return;

		if (!TryGetCombinedRendererBounds(_root.transform, out Bounds bounds))
		{
			boxCollider.center = Vector3.zero;
			boxCollider.size = _kind switch
			{
				LootBuildKind.Weapon => new Vector3(0.9f, 0.12f, 0.25f),
				LootBuildKind.Magazine => new Vector3(0.07f, 0.2f, 0.09f),
				_ => new Vector3(0.12f, 0.12f, 0.12f)
			};
			return;
		}

		const float padding = 0.02f;
		boxCollider.center = bounds.center - _root.transform.position;
		boxCollider.size = bounds.size + Vector3.one * padding;
	}

	private static void EnsureRigidbody(GameObject _root, LootBuildKind _kind)
	{
		RemoveComponents<Rigidbody>(_root);
		Rigidbody rigidbody = _root.AddComponent<Rigidbody>();
		if (rigidbody == null)
		{
			Debug.LogWarning($"[WorldPickupPrefabBuilder] Failed to add Rigidbody to '{_root.name}'.");
			return;
		}

		rigidbody.mass = _kind switch
		{
			LootBuildKind.Weapon => 3.5f,
			LootBuildKind.Magazine => 0.35f,
			LootBuildKind.Attachment => 0.25f,
			_ => 1f
		};
		rigidbody.linearDamping = 0.15f;
		rigidbody.angularDamping = 0.4f;
		rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
	}

	private static T GetOrAddComponent<T>(GameObject _root) where T : Component
	{
		if (_root.TryGetComponent(out T component))
			return component;

		return _root.AddComponent<T>();
	}

	private static void RemoveComponents<T>(GameObject _root) where T : Component
	{
		T[] components = _root.GetComponents<T>();
		for (int i = 0; i < components.Length; i++)
		{
			if (components[i] == null)
				continue;

			UnityEngine.Object.DestroyImmediate(components[i]);
		}
	}

	private static void StripRootRenderersAndColliders(GameObject _root)
	{
		MeshFilter meshFilter = _root.GetComponent<MeshFilter>();
		if (meshFilter != null)
			UnityEngine.Object.DestroyImmediate(meshFilter);

		MeshRenderer meshRenderer = _root.GetComponent<MeshRenderer>();
		if (meshRenderer != null)
			UnityEngine.Object.DestroyImmediate(meshRenderer);

		RemoveComponents<Collider>(_root);
	}

	private static bool TryGetCombinedRendererBounds(Transform _root, out Bounds _bounds)
	{
		_bounds = default;
		bool hasBounds = false;

		Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			if (renderer == null || !renderer.enabled)
				continue;

			if (!hasBounds)
			{
				_bounds = renderer.bounds;
				hasBounds = true;
			}
			else
			{
				_bounds.Encapsulate(renderer.bounds);
			}
		}

		return hasBounds;
	}

	private static void DisablePhysicsOnVisual(GameObject _root)
	{
		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			pickups[i].enabled = false;
	}
	#endregion

	#region ItemDefinition helpers
	private static bool TryGetLootBuildKind(ItemDefinition _item, out LootBuildKind _kind)
	{
		if (_item.WeaponDefinition != null)
		{
			_kind = LootBuildKind.Weapon;
			return true;
		}

		if (_item.MagazineDefinition != null)
		{
			_kind = LootBuildKind.Magazine;
			return true;
		}

		if (_item.WeaponAttachmentDefinition != null)
		{
			_kind = LootBuildKind.Attachment;
			return true;
		}

		if (_item.AmmoDefinition != null && _item.DropWorldPrefab != null)
		{
			_kind = LootBuildKind.AmmoContainer;
			return false;
		}

		_kind = default;
		return false;
	}

	private static string GetLootPrefabName(ItemDefinition _item, LootBuildKind _kind)
	{
		string itemName = _item.name;
		if (itemName.StartsWith("Item_Weapon_", StringComparison.Ordinal))
			return "Loot_Wep_" + itemName.Substring("Item_Weapon_".Length);

		if (itemName.StartsWith("Item_Mag_", StringComparison.Ordinal))
			return "Loot_Mag_" + itemName.Substring("Item_Mag_".Length);

		if (itemName.StartsWith("Item_Attachment_", StringComparison.Ordinal))
			return "Loot_Att_" + itemName.Substring("Item_Attachment_".Length);

		return "Loot_" + itemName;
	}

	private static string GetLootOutputPath(ItemDefinition _item, LootBuildKind _kind)
	{
		string fileName = GetLootPrefabName(_item, _kind) + ".prefab";
		string subFolder = _kind switch
		{
			LootBuildKind.Weapon => "Weapons",
			LootBuildKind.Magazine => "Magazines",
			LootBuildKind.Attachment => "Attachments",
			_ => "General"
		};

		return $"{c_WorldLootRoot}/M4/{subFolder}/{fileName}";
	}

	private static void AssignDropWorldPrefab(ItemDefinition _item, GameObject _lootPrefab)
	{
		SerializedObject serializedItem = new SerializedObject(_item);
		serializedItem.FindProperty("m_DropWorldPrefab").objectReferenceValue = _lootPrefab;
		serializedItem.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_item);
	}

	private static Dictionary<CaliberType, AmmoDefinition> BuildAmmoLookup()
	{
		Dictionary<CaliberType, AmmoDefinition> lookup = new Dictionary<CaliberType, AmmoDefinition>();
		string[] guids = AssetDatabase.FindAssets("t:AmmoDefinition");
		for (int i = 0; i < guids.Length; i++)
		{
			AmmoDefinition ammo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
			if (ammo == null)
				continue;

			if (!lookup.ContainsKey(ammo.Caliber))
				lookup.Add(ammo.Caliber, ammo);
		}

		return lookup;
	}
	#endregion

	#region Asset moves
	private static void MoveAssetIfExists(string _source, string _destination)
	{
		if (!AssetExists(_source))
			return;

		EnsureDirectory(Path.GetDirectoryName(_destination)?.Replace('\\', '/'));

		if (AssetDatabase.IsValidFolder(_source))
		{
			MoveFolderContents(_source, _destination);
			return;
		}

		string error = AssetDatabase.MoveAsset(_source, _destination);
		if (!string.IsNullOrEmpty(error))
			Debug.LogWarning($"[WorldPickupPrefabBuilder] Move failed '{_source}' -> '{_destination}': {error}");
	}

	private static void MoveFolderContents(string _sourceFolder, string _destinationFolder)
	{
		EnsureDirectory(_destinationFolder);
		string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { _sourceFolder });
		for (int i = 0; i < assets.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(assets[i]);
			if (path == _sourceFolder || !path.StartsWith(_sourceFolder + "/", StringComparison.Ordinal))
				continue;

			string relative = path.Substring(_sourceFolder.Length + 1);
			MoveAssetIfExists(path, $"{_destinationFolder}/{relative}");
		}
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

	private static bool AssetExists(string _path)
	{
		return !string.IsNullOrEmpty(_path) && (AssetDatabase.IsValidFolder(_path) || File.Exists(_path));
	}

	private static void TryDeleteEmptyFolder(string _folderPath)
	{
		if (!AssetDatabase.IsValidFolder(_folderPath))
			return;

		string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { _folderPath });
		if (assets.Length > 0)
			return;

		AssetDatabase.DeleteAsset(_folderPath);
	}

	private static void DeleteLegacyLootPrefabs()
	{
		string legacyRoot = "Assets/Prefabs/Loot/M4";
		if (!AssetDatabase.IsValidFolder(legacyRoot))
			return;

		string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { legacyRoot });
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
				AssetDatabase.DeleteAsset(path);
		}
	}

	private static void SetLayerRecursively(GameObject _root, int _layer)
	{
		Transform[] transforms = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
			transforms[i].gameObject.layer = _layer;
	}
	#endregion
}
#endif
