using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures the vehicle physics ground collider + relief demo exist after scene load.
/// </summary>
public static class VehiclePhysicsDemoBootstrap
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void OnAfterSceneLoad()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
		EnsureDemo();
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode) => EnsureDemo();

	private static void EnsureDemo()
	{
		if (GameObject.Find(VehiclePhysicsDemoArea.RootName) != null)
			return;

		if (Object.FindFirstObjectByType<VehicleController>() == null &&
		    GameObject.Find("Light_Armored_Car") == null)
			return;

		VehiclePhysicsDemoArea.EnsureInActiveScene();
	}
}
