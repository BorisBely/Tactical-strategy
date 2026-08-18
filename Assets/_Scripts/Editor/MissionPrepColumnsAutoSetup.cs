#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies collapsible Mission Prep columns once in the editor and saves SampleScene.
/// Does not build layout at Play Mode Start.
/// </summary>
[InitializeOnLoad]
public static class MissionPrepColumnsAutoSetup
{
	private const string c_MarkerPath = "Assets/.mission_prep_columns_setup_v1";
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";

	static MissionPrepColumnsAutoSetup()
	{
		EditorApplication.delayCall += TryAutoSetupOnce;
		EditorSceneManager.sceneOpened += HandleSceneOpened;
	}

	private static void HandleSceneOpened(Scene _scene, OpenSceneMode _mode)
	{
		if (_scene.path == c_ScenePath)
			EditorApplication.delayCall += TryAutoSetupOnce;
	}

	private static void TryAutoSetupOnce()
	{
		if (Application.isPlaying)
			return;
		if (File.Exists(c_MarkerPath))
			return;

		try
		{
			Scene active = SceneManager.GetActiveScene();
			if (active.path != c_ScenePath)
				active = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

			MissionPrepScreenController ctrl =
				Object.FindAnyObjectByType<MissionPrepScreenController>(FindObjectsInactive.Include);
			GameObject screen = ctrl != null ? ctrl.gameObject : null;
			if (screen == null)
				return;

			if (screen.transform.Find("PrepColumnsRow") != null &&
			    FindDeep(screen.transform, "PrepVehicleList") != null)
			{
				WriteMarker();
				return;
			}

			MissionPrepColumnsEditorSetup.SetupInOpenScene();
			EditorSceneManager.MarkSceneDirty(active);
			EditorSceneManager.SaveScene(active);
			AssetDatabase.SaveAssets();
			WriteMarker();
			Debug.Log("[MissionPrepColumns] Auto setup applied and SampleScene saved. Menu: Polygone/Mission Prep/Setup Collapsible Columns.");
		}
		catch (System.Exception ex)
		{
			Debug.LogError($"[MissionPrepColumns] Auto setup failed: {ex}");
		}
	}

	private static void WriteMarker()
	{
		File.WriteAllText(c_MarkerPath, "ok");
		AssetDatabase.ImportAsset(c_MarkerPath);
	}

	private static Transform FindDeep(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		if (_parent.name == _name)
			return _parent;
		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform found = FindDeep(_parent.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
}
#endif
