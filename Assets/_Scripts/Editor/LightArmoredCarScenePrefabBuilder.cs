#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Создаёт префаб LightArmoredCar из объекта машины на SampleScene (исходник со сцены).
/// </summary>
[InitializeOnLoad]
public static class LightArmoredCarScenePrefabBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_PrefabFolder = "Assets/Prefabs/Vehicles";
	private const string c_PrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	private const string c_DoorMarker = "SM_Veh_Light_Armored_Car_fl";
	private const string c_MarkerPath = "Assets/.light_armored_car_scene_prefab_done";
	private static readonly string[] s_RootNames =
	{
		"Light_Armored_Car",
		"LightArmoredCar",
		"2"
	};
	#endregion

	#region Bootstrap
	static LightArmoredCarScenePrefabBuilder()
	{
		if (!File.Exists(c_MarkerPath) || !File.Exists(c_PrefabPath))
			EditorApplication.delayCall += BuildFromScene;
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Vehicles/Create Prefab From Scene Light Armored Car")]
	public static void BuildFromSceneMenu()
	{
		BuildFromScene();
	}
	#endregion

	#region Public Methods
	public static void BuildFromScene()
	{
		EnsureFolder(c_PrefabFolder);

		Scene scene = GetOrOpenSampleScene();
		if (!scene.IsValid() || !scene.isLoaded)
		{
			Debug.LogError("[LightArmoredCarScenePrefabBuilder] SampleScene is not available.");
			return;
		}

		GameObject root = FindSceneVehicleRoot(scene);
		if (root == null)
		{
			Debug.LogError(
				"[LightArmoredCarScenePrefabBuilder] Scene vehicle not found " +
				$"(looked for '{c_DoorMarker}' parent / 'Light_Armored_Car' / '2').");
			return;
		}

		Undo.RegisterCompleteObjectUndo(root, "Setup LightArmoredCar Prefab");
		// Имя префаба/объекта оставляем как на сцене, если уже Light_Armored_Car.
		if (root.name != "Light_Armored_Car")
			root.name = "Light_Armored_Car";

		VehicleController vehicle = root.GetComponent<VehicleController>();
		if (vehicle == null)
			vehicle = Undo.AddComponent<VehicleController>(root);

		vehicle.EnsureComponents();
		VehicleHierarchyBinder.EnsureBound(vehicle);

		if (!root.TryGetComponent(out NavMeshAgent agent))
			agent = Undo.AddComponent<NavMeshAgent>(root);
		agent.radius = 1.4f;
		agent.height = 1.8f;
		agent.baseOffset = 0f;
		agent.speed = 14f;
		agent.acceleration = 6f;
		agent.angularSpeed = 55f;
		agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

		if (root.GetComponent<Collider>() == null)
		{
			BoxCollider box = Undo.AddComponent<BoxCollider>(root);
			box.center = new Vector3(0f, 1.0f, 0.1f);
			box.size = new Vector3(2.2f, 1.9f, 4.4f);
		}

		if (File.Exists(c_PrefabPath))
			AssetDatabase.DeleteAsset(c_PrefabPath);

		GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
			root,
			c_PrefabPath,
			InteractionMode.AutomatedAction);

		if (prefab == null)
		{
			Debug.LogError($"[LightArmoredCarScenePrefabBuilder] Failed to save: {c_PrefabPath}");
			return;
		}

		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);

		File.WriteAllText(c_MarkerPath, "done");
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Debug.Log($"[LightArmoredCarScenePrefabBuilder] Prefab from scene saved: {c_PrefabPath}", prefab);
		EditorGUIUtility.PingObject(prefab);
	}
	#endregion

	#region Private Methods
	private static Scene GetOrOpenSampleScene()
	{
		Scene active = SceneManager.GetActiveScene();
		if (active.IsValid() && active.path == c_ScenePath)
			return active;

		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene loaded = SceneManager.GetSceneAt(i);
			if (loaded.path == c_ScenePath)
				return loaded;
		}

		return EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
	}

	private static GameObject FindSceneVehicleRoot(Scene _scene)
	{
		GameObject[] roots = _scene.GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			GameObject found = FindVehicleUnder(roots[i].transform);
			if (found != null)
				return found;
		}

		return null;
	}

	private static GameObject FindVehicleUnder(Transform _root)
	{
		if (_root == null)
			return null;

		if (IsKnownRootName(_root.name))
		{
			if (HasDoorChild(_root) || _root.name == "Light_Armored_Car" || _root.name == "LightArmoredCar")
				return _root.gameObject;
		}

		Transform door = FindDeep(_root, c_DoorMarker);
		if (door != null && door.parent != null)
			return door.parent.gameObject;

		for (int i = 0; i < _root.childCount; i++)
		{
			GameObject found = FindVehicleUnder(_root.GetChild(i));
			if (found != null)
				return found;
		}

		return null;
	}

	private static bool IsKnownRootName(string _name)
	{
		for (int i = 0; i < s_RootNames.Length; i++)
		{
			if (s_RootNames[i] == _name)
				return true;
		}

		return false;
	}

	private static bool HasDoorChild(Transform _root)
	{
		return FindDeep(_root, c_DoorMarker) != null;
	}

	private static Transform FindDeep(Transform _root, string _name)
	{
		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindDeep(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}

	private static void EnsureFolder(string _folder)
	{
		if (AssetDatabase.IsValidFolder(_folder))
			return;

		string parent = Path.GetDirectoryName(_folder)?.Replace('\\', '/');
		string name = Path.GetFileName(_folder);
		if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
			return;

		if (!AssetDatabase.IsValidFolder(parent))
			EnsureFolder(parent);

		AssetDatabase.CreateFolder(parent, name);
	}
	#endregion
}
#endif
