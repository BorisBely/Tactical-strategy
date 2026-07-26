#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places VehiclePhysicsDemo (ground collider + relief course) into SampleScene permanently.
/// </summary>
public static class VehiclePhysicsDemoSceneSetup
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";

	[MenuItem("Polygone/Vehicles/Build Physics Demo Area On Scene")]
	public static void BuildOnSampleScene()
	{
		BuildInternal(_save: true, _log: true);
	}

	/// <summary>Batchmode entry: Unity.exe -executeMethod VehiclePhysicsDemoSceneSetup.BuildBatch</summary>
	public static void BuildBatch()
	{
		BuildInternal(_save: true, _log: true);
		EditorApplication.Exit(0);
	}

	[MenuItem("Polygone/Vehicles/Setup Light Armored Car Physics Drive And Demo")]
	public static void SetupDriveAndDemo()
	{
		LightArmoredCarPhysicsDriveSetup.SetupPrefab();
		BuildOnSampleScene();
	}

	private static void BuildInternal(bool _save, bool _log)
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		GameObject root = VehiclePhysicsDemoArea.EnsureInActiveScene();
		EditorSceneManager.MarkSceneDirty(scene);
		if (_save)
			EditorSceneManager.SaveScene(scene);
		if (_log)
		{
			Debug.Log(
				"[VehiclePhysicsDemo] Built on SampleScene ahead of Light_Armored_Car (+Z). " +
				$"Root at {root.transform.position}. Lane 9 m; bumps ≤0.18 m; ramps ~4° / ~9°. " +
				"PhysicsGroundCollider top at y=0.");
		}
	}
}
#endif
