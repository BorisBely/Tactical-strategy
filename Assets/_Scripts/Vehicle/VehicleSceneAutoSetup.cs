using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// После загрузки сцены находит модель с дверями Light Armored Car и вешает VehicleController.
/// </summary>
public static class VehicleSceneAutoSetup
{
	private const string c_DoorMarker = "SM_Veh_Light_Armored_Car_fl";

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void OnAfterSceneLoad()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
		SetupActiveScene();
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode) => SetupActiveScene();

	private static void SetupActiveScene()
	{
		Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
		for (int i = 0; i < transforms.Length; i++)
		{
			Transform t = transforms[i];
			if (t == null)
				continue;

			bool isNamedRoot = t.name == "Light_Armored_Car" || t.name == "LightArmoredCar" || t.name == "2";
			bool isDoor = t.name == c_DoorMarker;
			if (!isNamedRoot && !isDoor)
				continue;

			Transform root = isDoor && t.parent != null ? t.parent : t;
			if (root.GetComponent<VehicleController>() != null)
				continue;

			// Если это дверь, но родитель уже обработан выше по циклу — ок; иначе ставим на parent.
			if (isDoor && root.GetComponentInParent<VehicleController>() != null)
				continue;

			VehicleController vehicle = root.gameObject.AddComponent<VehicleController>();
			vehicle.EnsureComponents();
			VehicleHierarchyBinder.EnsureBound(vehicle);
			if (root.name == "2")
				root.name = "Light_Armored_Car";
		}
	}
}
