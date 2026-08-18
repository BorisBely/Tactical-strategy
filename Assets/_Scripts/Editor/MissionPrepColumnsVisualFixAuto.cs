#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MissionPrepColumnsVisualFixAuto
{
	private const string c_MarkerPath = "Assets/.mission_prep_columns_visual_fix_v2";
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";

	static MissionPrepColumnsVisualFixAuto()
	{
		EditorApplication.delayCall += TryFixOnce;
	}

	private static void TryFixOnce()
	{
		if (Application.isPlaying || File.Exists(c_MarkerPath))
			return;

		try
		{
			Scene active = SceneManager.GetActiveScene();
			if (active.path != c_ScenePath)
				active = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

			MissionPrepColumnsEditorSetup.FixVisualsFromMenu();
			File.WriteAllText(c_MarkerPath, "ok");
			AssetDatabase.ImportAsset(c_MarkerPath);
		}
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[MissionPrepColumns] Visual fix auto skipped: {ex.Message}");
		}
	}
}
#endif
