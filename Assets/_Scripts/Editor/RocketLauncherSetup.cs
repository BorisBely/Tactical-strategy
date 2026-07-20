#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Полная установка механики гранатомётов: контент, анимации, компоненты на Unit.prefab.
/// </summary>
public static class RocketLauncherSetup
{
	#region Constants
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_MarkerPath = "Assets/.rocket_launcher_setup_done";
	#endregion

	#region Bootstrap
	[InitializeOnLoadMethod]
	private static void AutoSetupOnce()
	{
		EditorApplication.delayCall += () =>
		{
			// Only rebuild content when loot prefabs are actually missing.
			bool lootMissing =
				!AssetDatabase.IsValidFolder("Assets/Prefabs/World/Loot/RocketLaunchers") ||
				AssetDatabase.LoadAssetAtPath<GameObject>(
					"Assets/Prefabs/World/Loot/RocketLaunchers/Loot_Item_Weapon_Rpg7.prefab") == null;

			if (lootMissing)
			{
				try
				{
					RocketLauncherContentBuilder.BuildRocketLauncherContent();
				}
				catch (System.Exception ex)
				{
					Debug.LogWarning($"[RocketLauncherSetup] Content rebuild deferred: {ex.Message}");
				}
			}

			if (System.IO.File.Exists(c_MarkerPath))
				return;

			try
			{
				RocketLauncherAnimationSetup.SetupRocketLauncherLayer();
				AddControllersToUnitPrefab();
				System.IO.File.WriteAllText(c_MarkerPath, System.DateTime.UtcNow.ToString("o"));
				AssetDatabase.Refresh();
				Debug.Log("[RocketLauncherSetup] Controllers/animation setup complete.");
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[RocketLauncherSetup] Auto setup deferred: {ex.Message}");
			}
		};
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Setup Rocket Launchers (Full)")]
	public static void RunFullSetup()
	{
		Debug.Log("[RocketLauncherSetup] Starting full setup...");

		RocketLauncherContentBuilder.BuildRocketLauncherContent();
		RocketLauncherAnimationSetup.SetupRocketLauncherLayer();
		RocketLauncherAudioSetup.RunSetup();
		AddControllersToUnitPrefab();

		System.IO.File.WriteAllText(c_MarkerPath, System.DateTime.UtcNow.ToString("o"));
		AssetDatabase.Refresh();
		Debug.Log("[RocketLauncherSetup] *** FULL SETUP COMPLETE ***");
	}
	#endregion

	#region Private
	private static void AddControllersToUnitPrefab()
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		if (unitRoot == null)
		{
			Debug.LogError($"[RocketLauncherSetup] Unit prefab missing: {c_UnitPrefabPath}");
			return;
		}

		try
		{
			UnitRpg7LauncherHandler rpg = unitRoot.GetComponent<UnitRpg7LauncherHandler>();
			if (rpg == null)
				rpg = unitRoot.AddComponent<UnitRpg7LauncherHandler>();

			UnitDisposableLauncherHandler disposable = unitRoot.GetComponent<UnitDisposableLauncherHandler>();
			if (disposable == null)
				disposable = unitRoot.AddComponent<UnitDisposableLauncherHandler>();

			UnitRocketLauncherOrderController order = unitRoot.GetComponent<UnitRocketLauncherOrderController>();
			if (order == null)
				order = unitRoot.AddComponent<UnitRocketLauncherOrderController>();

			RocketLauncherData data = RocketLauncherContentBuilder.LoadOrCreateData();
			SerializedObject so = new SerializedObject(order);
			so.FindProperty("m_Data").objectReferenceValue = data;
			so.FindProperty("m_RpgHandler").objectReferenceValue = rpg;
			so.FindProperty("m_DisposableHandler").objectReferenceValue = disposable;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset(unitRoot, c_UnitPrefabPath);
			Debug.Log("[RocketLauncherSetup] Unit.prefab updated with rocket launcher controllers.");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(unitRoot);
		}
	}
	#endregion
}
#endif
