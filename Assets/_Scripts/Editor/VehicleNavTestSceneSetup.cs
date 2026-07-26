#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the vehicle navigation test track and positions camera + car.
/// </summary>
public static class VehicleNavTestSceneSetup
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";

	[MenuItem("Polygone/Vehicles/Build NAVIGATION Test Track")]
	public static void BuildNavigationTestTrack()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		
		// Destroy old demo area if it exists
		GameObject oldDemo = GameObject.Find(VehiclePhysicsDemoArea.RootName);
		if (oldDemo != null)
			Object.DestroyImmediate(oldDemo);

		// Find the vehicle
		VehicleController vehicle = Object.FindFirstObjectByType<VehicleController>();
		if (vehicle == null)
		{
			GameObject named = GameObject.Find("Light_Armored_Car");
			if (named != null) vehicle = named.GetComponent<VehicleController>();
		}

		Vector3 carPos = vehicle != null ? vehicle.transform.position : new Vector3(13.24f, 0f, 5f);
		
		// Build test track to the RIGHT of the car
		Vector3 trackOrigin = new Vector3(carPos.x + c_TrackOffsetX, 0f, carPos.z);
		GameObject trackRoot = VehicleNavigationTestArea.Build(trackOrigin);
		
		// Create a large ground slab under the track (extends from car to end of track)
		float slabWidth = 30f;
		float slabLength = 140f;
		CreateGroundSlab(trackOrigin, slabWidth, slabLength);

		// Place car at start of track
		if (vehicle != null)
		{
			vehicle.transform.position = new Vector3(trackOrigin.x, 0.5f, trackOrigin.z + 2f);
			vehicle.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
			// Snap to ground
			vehicle.SnapChassisToGround(true);
		}

		// Move camera above car
		MoveCameraToStart(vehicle != null ? vehicle.transform.position : trackOrigin);

		// Rebuild NavMesh
		RebuildNavMesh();

		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);

		Debug.Log($"[NavTest] Track built at X={trackOrigin.x} Z={trackOrigin.z}. Car positioned at start. Camera moved.");
	}

	private const float c_TrackOffsetX = 60f;

	private static void CreateGroundSlab(Vector3 _origin, float _width, float _length)
	{
		GameObject existing = GameObject.Find("NavTestGround");
		if (existing != null) Object.DestroyImmediate(existing);

		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = "NavTestGround";
		go.transform.position = new Vector3(_origin.x, -0.5f, _origin.z + _length * 0.5f);
		go.transform.localScale = new Vector3(_width, 1f, _length);
		go.layer = LayerMask.NameToLayer("Ground");

		if (go.TryGetComponent(out MeshRenderer r)) r.enabled = false;
		if (go.TryGetComponent(out Collider c)) { c.isTrigger = false; c.enabled = true; }

		SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
	}

	private static void MoveCameraToStart(Vector3 _carPos)
	{
		// Find Cinemachine brain or main camera
		var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
		foreach (var cam in cameras)
		{
			if (cam.name.Contains("Main") || cam.name.Contains("Camera") || cam.CompareTag("MainCamera"))
			{
				cam.transform.position = _carPos + new Vector3(0f, 15f, -10f);
				cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
				Debug.Log($"[NavTest] Camera moved to {cam.transform.position}");
				return;
			}
		}

		// Fallback: any camera
		if (cameras.Length > 0)
		{
			cameras[0].transform.position = _carPos + new Vector3(0f, 15f, -10f);
			cameras[0].transform.rotation = Quaternion.Euler(55f, 0f, 0f);
		}
	}

	private static void RebuildNavMesh()
	{
		// Trigger Unity to rebake NavMesh
		UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
		Debug.Log("[NavTest] NavMesh rebuilt");
	}
}
#endif
