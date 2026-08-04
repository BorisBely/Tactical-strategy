#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Безопасный bake слотов ленты M2/MK19 на Light_Armored_Car через PrefabUtility
/// (без ручного YAML / подстановки GUID).
/// </summary>
public static class VehicleTurretBeltFeedBakeSetup
{
	#region Constants
	private const string c_VehiclePrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_EffectsFolder = "Assets/Prefabs/Vehicles/Effects";
	private const string c_M2RoundPrefabPath = "Assets/Prefabs/Vehicles/Effects/BeltRound_127.prefab";
	private const string c_Mk19RoundPrefabPath = "Assets/Prefabs/Vehicles/Effects/BeltRound_40mm.prefab";
	#endregion

	#region Menu
	[MenuItem("Polygone/Vehicles/Bake Turret Belt Slots (Light Armored Car)")]
	public static void BakeLightArmoredCarBeltSlots()
	{
		int choice = EditorUtility.DisplayDialogComplex(
			"Bake Turret Belt Slots",
			"Запечёт local pose слотов M2/MK19 из дочерних шаблонов на Light_Armored_Car, " +
			"назначит round prefab'ы и сохранит prefab через Unity.\n\n" +
			"«Bake Only» — шаблоны 12_7 / 40mm останутся в иерархии (рекомендуется для первого прогона).\n" +
			"«Bake + Remove Templates» — удалит детей BulletBelt / MK19_1 после bake.",
			"Bake Only",
			"Cancel",
			"Bake + Remove Templates");

		if (choice == 1)
			return;

		bool removeTemplates = choice == 2;
		if (!BakeVehiclePrefab(c_VehiclePrefabPath, removeTemplates, out string summary))
			return;

		EditorUtility.DisplayDialog("Bake Turret Belt Slots", summary, "OK");
	}

	[MenuItem("Polygone/Vehicles/Bake Turret Belt Slots (Selected Vehicle)", true)]
	private static bool ValidateBakeSelectedVehicle()
	{
		return Selection.activeGameObject != null &&
		       Selection.activeGameObject.GetComponentInParent<VehicleTurretBeltFeed>() != null;
	}

	[MenuItem("Polygone/Vehicles/Bake Turret Belt Slots (Selected Vehicle)")]
	public static void BakeSelectedVehicleBeltSlots()
	{
		VehicleTurretBeltFeed feed = Selection.activeGameObject.GetComponentInParent<VehicleTurretBeltFeed>();
		if (feed == null)
		{
			EditorUtility.DisplayDialog("Bake Turret Belt Slots", "VehicleTurretBeltFeed not found on selection.", "OK");
			return;
		}

		int choice = EditorUtility.DisplayDialogComplex(
			"Bake Turret Belt Slots",
			$"Bake belt slots on '{feed.gameObject.name}'?\n\nRemove template children after bake?",
			"Bake Only",
			"Cancel",
			"Bake + Remove Templates");
		if (choice == 1)
			return;

		bool removeTemplates = choice == 2;
		EnsureRoundPrefabs(feed, out string roundSummary);
		VehicleTurretBeltFeed.EditorBeltBakeResult result = feed.EditorBakeBeltSlotsFromSceneTemplates(removeTemplates);
		EditorUtility.SetDirty(feed);

		string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(feed.gameObject);
		if (!string.IsNullOrEmpty(path))
			PrefabUtility.SavePrefabAsset(PrefabUtility.GetOutermostPrefabInstanceRoot(feed.gameObject));

		EditorUtility.DisplayDialog(
			"Bake Turret Belt Slots",
			BuildSummary(feed.gameObject.name, result, roundSummary, path),
			"OK");
	}
	#endregion

	#region Prefab Components
	private static VehicleTurretBeltFeed EnsureBeltFeedForPrefabBake(GameObject _prefabRoot)
	{
		if (!_prefabRoot.TryGetComponent(out VehicleTurretHierarchyBinder hierarchy))
			hierarchy = _prefabRoot.AddComponent<VehicleTurretHierarchyBinder>();
		hierarchy.EnsureBound();

		if (!_prefabRoot.TryGetComponent(out VehicleTurretBeltFeed feed))
			feed = _prefabRoot.AddComponent<VehicleTurretBeltFeed>();

		return feed;
	}
	#endregion

	#region Prefab Bake
	private static bool BakeVehiclePrefab(string _prefabPath, bool _removeTemplates, out string _summary)
	{
		_summary = string.Empty;
		if (!File.Exists(_prefabPath))
		{
			EditorUtility.DisplayDialog("Bake Turret Belt Slots", $"Prefab not found:\n{_prefabPath}", "OK");
			return false;
		}

		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(_prefabPath);
		if (prefabRoot == null)
		{
			EditorUtility.DisplayDialog("Bake Turret Belt Slots", "Failed to load prefab contents.", "OK");
			return false;
		}

		try
		{
			VehicleTurretBeltFeed feed = EnsureBeltFeedForPrefabBake(prefabRoot);

			EnsureRoundPrefabs(feed, out string roundSummary);
			VehicleTurretBeltFeed.EditorBeltBakeResult result = feed.EditorBakeBeltSlotsFromSceneTemplates(_removeTemplates);

			if (result.M2SlotCount <= 0 && result.Mk19SlotCount <= 0)
			{
				_summary =
					"No belt template children found under BulletBelt / MK19_1.\n" +
					"Nothing was baked.";
				EditorUtility.DisplayDialog("Bake Turret Belt Slots", _summary, "OK");
				return false;
			}

			EditorUtility.SetDirty(feed);
			VehicleTurretCombatSocketsSetup.EnsurePitchEquippedWeapon(
				prefabRoot.transform,
				VehicleTurretHierarchyBinder.Gun127ObjectName,
				TurretWeaponVariant.Browning127);
			VehicleTurretCombatSocketsSetup.EnsurePitchEquippedWeapon(
				prefabRoot.transform,
				"MK19",
				TurretWeaponVariant.Mk19);
			PrefabUtility.SaveAsPrefabAsset(prefabRoot, _prefabPath);
			_summary = BuildSummary(prefabRoot.name, result, roundSummary, _prefabPath);
			Debug.Log($"[VehicleTurretBeltFeedBakeSetup] {_summary.Replace("\n", " ")}");
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}
	#endregion

	#region Round Prefabs
	private static void EnsureRoundPrefabs(VehicleTurretBeltFeed _feed, out string _summary)
	{
		_feed.ResolveRefsIfNeededEditor();
		_feed.ResolveBeltRootsIfNeededEditor();

		GameObject m2Round = EnsureRoundPrefab(
			c_M2RoundPrefabPath,
			_feed.BulletBeltRootEditor,
			TurretWeaponVariant.Browning127,
			"BeltRound_127");
		GameObject mk19Round = EnsureRoundPrefab(
			c_Mk19RoundPrefabPath,
			_feed.Mk19BeltRootEditor,
			TurretWeaponVariant.Mk19,
			"BeltRound_40mm");

		_feed.AssignRoundPrefabsEditor(m2Round, mk19Round);

		_summary =
			$"Round prefabs: M2={(m2Round != null ? c_M2RoundPrefabPath : "missing")}, " +
			$"MK19={(mk19Round != null ? c_Mk19RoundPrefabPath : "missing")}";
	}

	private static GameObject EnsureRoundPrefab(
		string _assetPath,
		Transform _beltRoot,
		TurretWeaponVariant _variant,
		string _prefabName)
	{
		GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
		if (existing != null)
			return existing;

		GameObject template = FindRoundTemplate(_beltRoot, _variant);
		if (template == null)
			return null;

		EnsureFolder(c_EffectsFolder);

		GameObject instance = Object.Instantiate(template);
		instance.name = _prefabName;
		StripNonVisualComponents(instance);

		GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, _assetPath);
		Object.DestroyImmediate(instance);
		return prefab;
	}

	private static GameObject FindRoundTemplate(Transform _beltRoot, TurretWeaponVariant _variant)
	{
		if (_beltRoot == null || _beltRoot.childCount == 0)
			return null;

		string prefix = _variant == TurretWeaponVariant.Mk19 ? "40mm" : "12_7";
		for (int i = 0; i < _beltRoot.childCount; i++)
		{
			Transform child = _beltRoot.GetChild(i);
			if (child.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
				return child.gameObject;
		}

		return _beltRoot.GetChild(0).gameObject;
	}

	private static void StripNonVisualComponents(GameObject _root)
	{
		Component[] components = _root.GetComponentsInChildren<Component>(true);
		for (int i = 0; i < components.Length; i++)
		{
			Component component = components[i];
			if (component == null)
				continue;
			if (component is Transform || component is MeshFilter || component is MeshRenderer)
				continue;
			Object.DestroyImmediate(component, true);
		}
	}
	#endregion

	#region Helpers
	private static string BuildSummary(
		string _objectName,
		VehicleTurretBeltFeed.EditorBeltBakeResult _result,
		string _roundSummary,
		string _savedPath)
	{
		return
			$"Baked '{_objectName}'.\n" +
			$"M2 slots: {_result.M2SlotCount}\n" +
			$"MK19 slots: {_result.Mk19SlotCount}\n" +
			$"Templates removed: {(_result.RemovedTemplateChildren ? "yes" : "no")}\n" +
			$"{_roundSummary}\n" +
			$"Saved: {_savedPath}";
	}

	private static void EnsureFolder(string _folderPath)
	{
		if (AssetDatabase.IsValidFolder(_folderPath))
			return;

		string parent = Path.GetDirectoryName(_folderPath)?.Replace('\\', '/');
		string leaf = Path.GetFileName(_folderPath);
		if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
			return;

		if (!AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, leaf);
	}
	#endregion
}
#endif
