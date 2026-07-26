#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

/// <summary>
/// Menu: Polygone → Vehicles → Build NAVIGATION Test Track
/// Instantiates the vehicle prefab, builds the 10-section test track,
/// positions camera, bakes NavMesh.
/// </summary>
public static class VehicleNavTestSceneSetup
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_VehiclePrefab = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";

	[MenuItem("Polygone/Vehicles/Build NAVIGATION Test Track")]
	public static void BuildTrack()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

		// 1. Remove old debug demo
		GameObject oldDemo = GameObject.Find(VehiclePhysicsDemoArea.RootName);
		if (oldDemo != null) Object.DestroyImmediate(oldDemo);

		// 2. Build the test track at fixed origin
		GameObject track = VehicleNavigationTestArea.Build();
		Vector3 origin = VehicleNavigationTestArea.TrackOrigin;

		// 3. Ensure vehicle is in the scene
		VehicleController vehicle = Object.FindFirstObjectByType<VehicleController>();
		if (vehicle == null)
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c_VehiclePrefab);
			if (prefab != null)
			{
				GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
				vehicle = instance.GetComponent<VehicleController>();
				Debug.Log($"[NavTest] Instantiated vehicle prefab: {instance.name}");
			}
			else
			{
				Debug.LogError($"[NavTest] Vehicle prefab not found at {c_VehiclePrefab}");
				return;
			}
		}

		// 4. Place vehicle at track start
		if (vehicle != null)
		{
			vehicle.transform.position = new Vector3(origin.x, 1f, origin.z + 3f);
			vehicle.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
			Debug.Log($"[NavTest] Vehicle placed at {vehicle.transform.position}");
		}

		// 5. Camera above the car
		Camera cam = Camera.main;
		if (cam == null)
			cam = Object.FindFirstObjectByType<Camera>();
		if (cam != null)
		{
			cam.transform.position = new Vector3(origin.x, 18f, origin.z - 8f);
			cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
			Debug.Log($"[NavTest] Camera at {cam.transform.position}");
		}

		// 6. Bake NavMesh for the new geometry
		UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
		Debug.Log("[NavTest] NavMesh rebuilt");

		// 7. Save
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);

		Debug.Log("[NavTest] ✓ Готово! Polygone → Vehicles → Build NAVIGATION Test Track");
	}
}
#endif