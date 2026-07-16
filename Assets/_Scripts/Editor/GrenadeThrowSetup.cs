#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Автоматически добавляет UnitGrenadeThrowController на Unit.prefab,
/// назначает GrenadeThrowData в RtsUnitSelectionManager на сцене,
/// и настраивает все ссылки.
/// </summary>
[InitializeOnLoad]
public static class GrenadeThrowSetup
{
	#region Constants
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_ThrowDataPath = "Assets/GameData/Combat/GrenadeThrowData.asset";
	private const string c_MarkerFile = "Assets/.grenade_throw_setup_done";
	#endregion

	#region Bootstrap
	static GrenadeThrowSetup()
	{
		if (!System.IO.File.Exists(c_MarkerFile))
			EditorApplication.delayCall += RunFullSetup;
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Setup Grenade Throw (Full)")]
	public static void RunFullSetup()
	{
		Debug.Log("[GrenadeThrowSetup] Starting full setup...");

		// 1. Build grenade content (items, loot, attach prefabs)
		GrenadeContentBuilder.BuildAllGrenades();

		// 2. Build thrown prefabs and GrenadeThrowData asset
		GrenadeThrowContentBuilder.BuildGrenadeThrowContent();

		// 3. Bake animation events and setup animator layer
		GrenadeThrowAnimationSetup.SetupGrenadeThrowLayer();

		// 4. Add UnitGrenadeThrowController to Unit.prefab
		SetupUnitPrefab();

		// 5. Assign GrenadeThrowData to RtsUnitSelectionManager in scene
		SetupSceneReferences();

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		// Create marker so this doesn't re-run
		System.IO.File.WriteAllText(c_MarkerFile, "done");
		Debug.Log("[GrenadeThrowSetup] *** FULL SETUP COMPLETE ***");
	}

	[MenuItem("Polygone/Equipment/Setup Unit Prefab for Grenade Throw")]
	public static void SetupUnitPrefabMenuItem()
	{
		SetupUnitPrefab();
		AssetDatabase.SaveAssets();
		Debug.Log("[GrenadeThrowSetup] Unit.prefab updated with UnitGrenadeThrowController.");
	}

	[MenuItem("Polygone/Equipment/Setup Scene Refs for Grenade Throw")]
	public static void SetupSceneRefsMenuItem()
	{
		SetupSceneReferences();
		Debug.Log("[GrenadeThrowSetup] Scene references updated.");
	}
	#endregion

	#region Prefab Setup
	private static void SetupUnitPrefab()
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		try
		{
			UnitGrenadeThrowController controller = unitRoot.GetComponent<UnitGrenadeThrowController>();
			if (controller == null)
			{
				controller = unitRoot.AddComponent<UnitGrenadeThrowController>();
				Debug.Log("[GrenadeThrowSetup] Added UnitGrenadeThrowController to Unit.prefab.");
			}

			GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrowDataPath);
			if (data != null)
			{
				SerializedObject so = new SerializedObject(controller);
				so.FindProperty("m_Data").objectReferenceValue = data;
				so.ApplyModifiedPropertiesWithoutUndo();
				Debug.Log("[GrenadeThrowSetup] Assigned GrenadeThrowData to controller on Unit.prefab.");
			}
			else
			{
				Debug.LogWarning("[GrenadeThrowSetup] GrenadeThrowData not found at " + c_ThrowDataPath +
				                 ". Run Build Grenade Throw Content first.");
			}

			PrefabUtility.SaveAsPrefabAsset(unitRoot, c_UnitPrefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(unitRoot);
		}
	}
	#endregion

	#region Scene Setup
	private static void SetupSceneReferences()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		bool openedScene = false;

		if (!activeScene.IsValid() || activeScene.path != c_ScenePath)
		{
			activeScene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
			openedScene = true;
		}

		try
		{
			GrenadeThrowData data = AssetDatabase.LoadAssetAtPath<GrenadeThrowData>(c_ThrowDataPath);
			if (data == null)
			{
				Debug.LogWarning("[GrenadeThrowSetup] GrenadeThrowData asset not found.");
				return;
			}

			RtsUnitSelectionManager[] managers = UnityEngine.Object.FindObjectsByType<RtsUnitSelectionManager>(
				FindObjectsInactive.Include, FindObjectsSortMode.None);

			for (int i = 0; i < managers.Length; i++)
			{
				SerializedObject so = new SerializedObject(managers[i]);
				SerializedProperty prop = so.FindProperty("m_GrenadeThrowData");
				if (prop != null && prop.objectReferenceValue != data)
				{
					prop.objectReferenceValue = data;
					so.ApplyModifiedPropertiesWithoutUndo();
					EditorUtility.SetDirty(managers[i]);
					Debug.Log($"[GrenadeThrowSetup] Assigned GrenadeThrowData to {managers[i].name}.");
				}
			}

			if (openedScene)
			{
				EditorSceneManager.MarkSceneDirty(activeScene);
				EditorSceneManager.SaveScene(activeScene);
			}
		}
		finally
		{
			if (openedScene)
				EditorSceneManager.SaveScene(activeScene);
		}
	}
	#endregion
}
#endif
