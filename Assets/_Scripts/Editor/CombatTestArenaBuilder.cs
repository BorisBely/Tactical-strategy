#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the 150x50 CQB combat test arena in SampleScene.
/// Interior walls: SM_Prop_Barrier_Tall_01. Perimeter: Tall_Group_01. Small cover only.
/// Batch: Unity.exe -batchmode -quit -executeMethod CombatTestArenaBuilder.BuildAndSaveBatch
/// Trigger: Temp/CombatTestArena.build
/// </summary>
public static class CombatTestArenaBuilder
{
	#region Constants
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_RootName = "CombatTestArena_150x50";
	private const string c_LegacyRootName = "CombatTestArena_300x100";
	private const string c_NavMeshAssetPath = "Assets/Scenes/SampleScene/NavMesh-CombatTestArena.asset";
	private const string c_TriggerPath = "Temp/CombatTestArena.build";
	private const string c_WireTriggerPath = "Temp/CombatTestArena.wire";

	private const float c_Length = 150f;
	private const float c_Width = 50f;
	private const float c_Door = 2.4f;
	private const float c_WallOverlap = 0.28f;
	private const float c_MinSpan = 1.0f;
	private const int c_GroundLayer = 6;
	private const float c_PerimX = 24.6f;
	private const float c_Edge = 24.2f;

	private const string c_TallGroup = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_Tall_Group_01.prefab";
	private const string c_Tall01 = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_Tall_01.prefab";
	private const string c_Jersey = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_01.prefab";
	private const string c_Jersey2 = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_02.prefab";
	private const string c_Jersey3 = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_03.prefab";
	private const string c_Jersey4 = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_04.prefab";
	private const string c_Jersey5 = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Barrier_05.prefab";
	private const string c_HescoSingle = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Barrier_Base_Single_01.prefab";
	private const string c_RoadBarrier = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Road_Barrier_01.prefab";
	private const string c_CrateCube = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Crate_Cube_01.prefab";
	private const string c_CrateAmmo = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Crate_Ammo_01.prefab";
	private const string c_CrateWood = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Crate_Wood_01.prefab";
	private const string c_CrateStack = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Crate_Stack_01.prefab";
	private const string c_Barrel = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Barrel_01.prefab";
	private const string c_BarrelStack = "Assets/PolygonMilitary/Prefabs/Props/Military/SM_Prop_Barrel_Stack_01.prefab";
	private const string c_Makeshift = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Makeshift_Fence_01.prefab";
	private const string c_Pallet = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Pallet_01.prefab";
	private const string c_Cone = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Cone_01.prefab";
	private const string c_Flag = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Flag_01.prefab";
	private const string c_Flag2 = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Flag_02.prefab";
	private const string c_Light = "Assets/PolygonMilitary/Prefabs/Props/SM_Prop_Light_Portable_01.prefab";
	private const string c_Sign = "Assets/PolygonMilitary/Prefabs/Props/Signs/SM_Prop_Sign_Medical_01.prefab";
	private const string c_GroundMat = "Assets/PolygonMilitary/Materials/PolygonMilitary_Mat_01_A.mat";
	#endregion

	#region Private Fields
	private static readonly List<Vector3> s_Doors = new List<Vector3>(128);
	private static readonly List<CombatTestSpawnMarker> s_Markers = new List<CombatTestSpawnMarker>(40);
	private static readonly StringBuilder s_Log = new StringBuilder(8192);
	private static int s_FailCount;
	private static Transform s_Walls;
	private static Transform s_Cover;
	private static Transform s_Decor;
	private static GameObject s_InteriorPrefab;
	private static GameObject s_PerimeterPrefab;
	private static double s_LastTriggerPoll;
	#endregion

	#region Menu / Batch
	[InitializeOnLoadMethod]
	private static void AutoBuildIfTriggered()
	{
		EditorApplication.delayCall += TryRunTriggeredBuild;
		EditorApplication.update -= PollTriggers;
		EditorApplication.update += PollTriggers;
	}

	private static void PollTriggers()
	{
		if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			return;

		double now = EditorApplication.timeSinceStartup;
		if (now - s_LastTriggerPoll < 0.5d)
			return;

		s_LastTriggerPoll = now;
		if (!File.Exists(c_WireTriggerPath) && !File.Exists(c_TriggerPath))
			return;

		TryRunTriggeredBuild();
	}

	private static void TryRunTriggeredBuild()
	{
		if (File.Exists(c_WireTriggerPath))
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				EditorApplication.delayCall += TryRunTriggeredBuild;
				return;
			}

			try
			{
				File.Delete(c_WireTriggerPath);
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[CombatTestArena] Could not delete wire trigger: " + exception.Message);
			}

			try
			{
				WireArenaSpawner();
			}
			catch (Exception exception)
			{
				WriteReport("WIRE FAILED\n" + exception);
				Debug.LogException(exception);
			}

			return;
		}

		if (!File.Exists(c_TriggerPath))
			return;
		if (EditorApplication.isCompiling || EditorApplication.isUpdating)
		{
			EditorApplication.delayCall += TryRunTriggeredBuild;
			return;
		}

		try
		{
			File.Delete(c_TriggerPath);
		}
		catch (Exception exception)
		{
			Debug.LogWarning("[CombatTestArena] Could not delete trigger: " + exception.Message);
		}

		try
		{
			BuildAndSave();
		}
		catch (Exception exception)
		{
			WriteReport("BUILD FAILED\n" + exception);
			Debug.LogException(exception);
		}
	}

	[MenuItem("Polygone/Combat Test/Build 150x50 Arena (SampleScene)")]
	public static void BuildFromMenu()
	{
		BuildAndSave();
	}

	[MenuItem("Polygone/Combat Test/Wire Arena Spawner")]
	public static void WireArenaSpawnerFromMenu()
	{
		WireArenaSpawner();
	}

	public static void BuildAndSaveBatch()
	{
		BuildAndSave();
		EditorApplication.Exit(s_FailCount > 0 ? 1 : 0);
	}

	public static void WireArenaSpawnerBatch()
	{
		s_Log.Length = 0;
		s_FailCount = 0;
		WireArenaSpawner();
		EditorApplication.Exit(s_FailCount > 0 ? 1 : 0);
	}

	public static void WireArenaSpawner()
	{
		s_Log.Length = 0;
		s_FailCount = 0;
		Log("CombatTestArena wire spawner " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

		Scene scene = EditorSceneManager.GetActiveScene();
		if (scene.path != c_ScenePath)
			scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

		DisableStartupSpawns(scene);
		GameObject root = FindSceneObject(scene, c_RootName);
		if (root == null)
		{
			Check("ArenaPresent", false, c_RootName);
			WriteReport(s_Log.ToString());
			return;
		}

		SetupArenaSpawner(scene, root);
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		AssetDatabase.SaveAssets();
		WriteReport(s_Log.ToString());
		Debug.Log("[CombatTestArena] Spawner wired. Fails=" + s_FailCount + "\n" + s_Log);
	}

	public static void BuildAndSave()
	{
		s_Doors.Clear();
		s_Markers.Clear();
		s_Log.Length = 0;
		s_FailCount = 0;
		Log("CombatTestArena CQB 150x50 rebuild " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

		Scene scene = EditorSceneManager.GetActiveScene();
		if (scene.path != c_ScenePath)
			scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

		HideLegacyRange(scene);
		DisableStartupSpawns(scene);
		DestroyRoot(scene, c_RootName);
		DestroyRoot(scene, c_LegacyRootName);

		GameObject root = new GameObject(c_RootName);
		SceneManager.MoveGameObjectToScene(root, scene);
		root.transform.position = Vector3.zero;

		Transform ground = Group(root.transform, "Ground");
		Transform perimeter = Group(root.transform, "Perimeter");
		s_Walls = Group(root.transform, "Walls");
		Transform playerZone = Group(root.transform, "PlayerZone");
		Transform centralZone = Group(root.transform, "CentralZone");
		Transform enemyZone = Group(root.transform, "EnemyZone");
		s_Cover = Group(root.transform, "Cover");
		s_Decor = Group(root.transform, "Decor");
		Transform spawnMarkers = Group(root.transform, "SpawnMarkers");
		Transform navigation = Group(root.transform, "Navigation");

		s_PerimeterPrefab = Load(c_TallGroup);
		s_InteriorPrefab = Load(c_Tall01);
		Log("Perimeter " + PrefabSize(s_PerimeterPrefab) + " interior " + PrefabSize(s_InteriorPrefab) + " door=" + c_Door);

		BuildGround(ground);
		BuildPerimeter(perimeter);
		BuildPlayerYard(playerZone);
		BuildSouthCqb();
		BuildCenterKnot(centralZone);
		BuildNorthCqb();
		BuildEnemyYard(enemyZone);
		BuildSpawnMarkers(spawnMarkers);
		SetupArenaSpawner(scene, root);
		PlaceCamera();

		NavMeshSurface surface = BakeNavMesh(root, navigation);
		Validate(scene, surface);

		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		AssetDatabase.SaveAssets();
		WriteReport(s_Log.ToString());
		Debug.Log("[CombatTestArena] Done. Fails=" + s_FailCount + "\n" + s_Log);
	}
	#endregion

	#region Legacy
	private static void DestroyRoot(Scene _scene, string _name)
	{
		GameObject oldRoot = FindSceneObject(_scene, _name);
		if (oldRoot == null)
			return;
		Object.DestroyImmediate(oldRoot);
		Log("Destroyed " + _name);
	}

	private static void HideLegacyRange(Scene _scene)
	{
		string[] names =
		{
			"ShootingRange", "NavTestPolygon", "Plane", "Cube", "Cube (1)", "DetectionG1Harness"
		};
		for (int i = 0; i < names.Length; i++)
		{
			GameObject go = FindSceneObject(_scene, names[i]);
			if (go == null)
			{
				Log("Hide skip (missing): " + names[i]);
				continue;
			}

			if (go.activeSelf)
				go.SetActive(false);
			Log("Hidden: " + GetPath(go));
		}
	}

	private static void DisableStartupSpawns(Scene _scene)
	{
		UnitSceneSpawner unitSpawner = FindComponent<UnitSceneSpawner>(_scene);
		if (unitSpawner != null)
		{
			SerializedObject so = new SerializedObject(unitSpawner);
			so.FindProperty("m_SpawnOnStart").boolValue = false;
			so.ApplyModifiedPropertiesWithoutUndo();
			Log("UnitSceneSpawner.m_SpawnOnStart = false");
		}

		MissionPrepSquadSpawner missionSpawner = FindComponent<MissionPrepSquadSpawner>(_scene);
		if (missionSpawner != null)
		{
			SerializedObject so = new SerializedObject(missionSpawner);
			so.FindProperty("m_SpawnOnStart").boolValue = false;
			so.ApplyModifiedPropertiesWithoutUndo();
			Log("MissionPrepSquadSpawner.m_SpawnOnStart = false");
		}
	}

	private static void SetupArenaSpawner(Scene _scene, GameObject _root)
	{
		if (_root == null)
			return;

		CombatTestArenaSpawner spawner = _root.GetComponent<CombatTestArenaSpawner>();
		if (spawner == null)
			spawner = _root.AddComponent<CombatTestArenaSpawner>();

		Transform parent = _root.transform.Find("SpawnedUnits");
		if (parent == null)
			parent = Group(_root.transform, "SpawnedUnits");

		UnitSceneSpawner sceneSpawner = FindComponent<UnitSceneSpawner>(_scene);
		GameObject unitPrefab = sceneSpawner != null ? sceneSpawner.UnitPrefab : null;
		UnitSpawnConfig[] neutrals = sceneSpawner != null
			? CombatTestArenaSpawner.CollectNamed(sceneSpawner.CivilianSpawns, CombatTestArenaSpawner.CivilianPresetName)
			: Array.Empty<UnitSpawnConfig>();

		spawner.AssignFromEditor(unitPrefab, parent, neutrals);
		bool kitsOk = CombatTestArenaLoadoutBaker.Apply(spawner);
		EditorUtility.SetDirty(spawner);
		Log("ArenaSpawner prefab=" + (unitPrefab != null ? unitPrefab.name : "null") +
		    " playerUnique=" + spawner.PlayerUniqueKitCount +
		    " playerFill=" + spawner.PlayerFillKitCount +
		    " enemyUnique=" + spawner.EnemyUniqueKitCount +
		    " enemyFill=" + spawner.EnemyFillKitCount +
		    " grenades=" + spawner.GrenadeTypeCount +
		    " helmets=" + spawner.PlayerHelmetCount +
		    " neutrals=" + neutrals.Length);
		Check("ArenaSpawnerPrefab", unitPrefab != null, "Unit prefab");
		Check("PlayerUniqueKits", spawner.PlayerUniqueKitCount >= 5, "count=" + spawner.PlayerUniqueKitCount);
		Check("PlayerFillKits", spawner.PlayerFillKitCount >= 4, "count=" + spawner.PlayerFillKitCount);
		Check("EnemyUniqueKits", spawner.EnemyUniqueKitCount >= 3, "count=" + spawner.EnemyUniqueKitCount);
		Check("EnemyFillKits", spawner.EnemyFillKitCount >= 8, "count=" + spawner.EnemyFillKitCount);
		Check("ArenaGrenades", spawner.GrenadeTypeCount >= 5, "count=" + spawner.GrenadeTypeCount);
		Check("PlayerHelmets", spawner.PlayerHelmetCount >= 4, "count=" + spawner.PlayerHelmetCount);
		Check("ArenaLoadoutBake", kitsOk, "baker");
		Check("CivilianTemplate", neutrals.Length == 1, "count=" + neutrals.Length);
	}

	#endregion

	#region Layout
	private static void BuildGround(Transform _parent)
	{
		GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
		ground.name = "ArenaFloor";
		ground.layer = c_GroundLayer;
		ground.transform.SetParent(_parent, false);
		ground.transform.position = new Vector3(0f, -0.1f, c_Length * 0.5f);
		ground.transform.localScale = new Vector3(c_Width, 0.2f, c_Length);
		MeshRenderer renderer = ground.GetComponent<MeshRenderer>();
		Material mat = AssetDatabase.LoadAssetAtPath<Material>(c_GroundMat);
		if (mat != null)
			renderer.sharedMaterial = mat;
		SetNavStatic(ground);
		Log("Ground flat " + c_Width + "x" + c_Length);
	}

	private static void BuildPerimeter(Transform _parent)
	{
		FillWallX(_parent, s_PerimeterPrefab, 0.4f, -25f, 25f);
		FillWallX(_parent, s_PerimeterPrefab, 149.6f, -25f, 25f);
		FillWallZ(_parent, s_PerimeterPrefab, -c_PerimX, 0f, 150f);
		FillWallZ(_parent, s_PerimeterPrefab, c_PerimX, 0f, 150f);
		Log("Perimeter closed 150x50, no flank lanes");
	}

	private static void BuildPlayerYard(Transform _parent)
	{
		WallX(18f, -c_PerimX, c_PerimX, -16f, 8f);
		WallZ(-8f, 6f, 18f, 12f);
		WallZ(10f, 6f, 18f, 10f);

		Cover(c_Jersey5, new Vector3(-18f, 0f, 8f), 0f);
		Cover(c_Jersey3, new Vector3(-2f, 0f, 8f), 90f);
		Cover(c_Jersey, new Vector3(16f, 0f, 8f), 0f);
		Cover(c_HescoSingle, new Vector3(-14f, 0f, 14f), 0f);
		Cover(c_CrateCube, new Vector3(4f, 0f, 14f), 15f);
		Cover(c_Barrel, new Vector3(18f, 0f, 14f), 0f);
		Cover(c_Makeshift, new Vector3(0f, 0f, 6.5f), 0f);
		PlaceDecor(Load(c_Flag), _parent, new Vector3(0f, 0f, 5f), 0f, false);
		PlaceDecor(Load(c_Cone), _parent, new Vector3(-2f, 0f, 5f), 0f, false);
		PlaceDecor(Load(c_Cone), _parent, new Vector3(2f, 0f, 5f), 0f, false);
		PlaceDecor(Load(c_Light), _parent, new Vector3(-20f, 0f, 7f), 35f, false);
		Log("Player yard rooms");
	}

	private static void BuildSouthCqb()
	{
		WallX(36f, -c_PerimX, c_PerimX, -6f, 18f);
		WallZ(-14f, 18f, 36f, 26f);
		WallZ(-2f, 18f, 36f, 30f);
		WallZ(10f, 18f, 36f, 22f);

		Cover(c_Jersey2, new Vector3(-20f, 0f, 24f), 90f);
		Cover(c_Jersey, new Vector3(-8f, 0f, 28f), 0f);
		Cover(c_HescoSingle, new Vector3(4f, 0f, 24f), 0f);
		Cover(c_CrateStack, new Vector3(18f, 0f, 28f), 20f);
		Cover(c_Barrel, new Vector3(-18f, 0f, 32f), 0f);
		Cover(c_Jersey4, new Vector3(6f, 0f, 32f), 90f);

		WallX(54f, -c_PerimX, c_PerimX, -14f, 4f);
		WallZ(-10f, 36f, 54f, 44f);
		WallZ(6f, 36f, 54f, 48f);

		Cover(c_Jersey3, new Vector3(-20f, 0f, 42f), 0f);
		Cover(c_Makeshift, new Vector3(-4f, 0f, 42f), 90f);
		Cover(c_CrateCube, new Vector3(14f, 0f, 42f), 0f);
		Cover(c_HescoSingle, new Vector3(-16f, 0f, 50f), 0f);
		Cover(c_Jersey5, new Vector3(12f, 0f, 50f), 90f);
		Cover(c_BarrelStack, new Vector3(20f, 0f, 46f), 0f);
		PlaceDecor(Load(c_Sign), s_Decor, new Vector3(-7f, 1.4f, 36.2f), 0f, false);
		Log("South CQB brick rooms");
	}

	private static void BuildCenterKnot(Transform _parent)
	{
		WallZ(-20f, 54f, 62f);
		WallZ(16f, 88f, 94f);

		WallX(62f, -c_PerimX, -10f, -18f);
		WallX(84f, -c_PerimX, -10f);
		WallZ(-10f, 62f, 84f, 68f);

		WallX(62f, 10f, c_PerimX);
		WallX(84f, 10f, c_PerimX, 14f);
		WallZ(10f, 62f, 84f, 78f);

		WallX(72f, -6f, 6f);
		WallZ(0f, 54f, 62f, 58f);
		WallZ(0f, 84f, 96f, 90f);

		Cover(c_Jersey, new Vector3(-20f, 0f, 58f), 0f);
		Cover(c_CrateAmmo, new Vector3(-12f, 0f, 58f), 90f);
		Cover(c_Jersey3, new Vector3(-20f, 0f, 74f), 90f);
		Cover(c_HescoSingle, new Vector3(-20f, 0f, 78f), 0f);
		Cover(c_CrateCube, new Vector3(-14f, 0f, 80f), 15f);

		Cover(c_Jersey2, new Vector3(16f, 0f, 68f), 90f);
		Cover(c_HescoSingle, new Vector3(20f, 0f, 72f), 0f);
		Cover(c_CrateStack, new Vector3(14f, 0f, 80f), 0f);
		Cover(c_Barrel, new Vector3(20f, 0f, 80f), 25f);

		Cover(c_Makeshift, new Vector3(-4f, 0f, 66f), 0f);
		Cover(c_Jersey5, new Vector3(4f, 0f, 66f), 90f);
		Cover(c_RoadBarrier, new Vector3(0f, 0f, 75.5f), 0f);
		Cover(c_Jersey4, new Vector3(-4f, 0f, 80f), 0f);
		Cover(c_Jersey, new Vector3(4f, 0f, 80f), 90f);

		Cover(c_CrateWood, new Vector3(18f, 0f, 92f), 0f);
		Cover(c_HescoSingle, new Vector3(20f, 0f, 90f), 0f);
		Cover(c_Pallet, new Vector3(-20f, 0f, 56f), 10f);

		PlaceDecor(Load(c_Flag), _parent, new Vector3(0f, 0f, 75f), 0f, false);
		PlaceDecor(Load(c_Light), _parent, new Vector3(-18f, 0f, 70f), -20f, false);
		PlaceDecor(Load(c_Light), _parent, new Vector3(18f, 0f, 76f), 160f, false);
		PlaceDecor(Load(c_Cone), _parent, new Vector3(-1.6f, 0f, 75f), 0f, false);
		PlaceDecor(Load(c_Cone), _parent, new Vector3(1.6f, 0f, 75f), 0f, false);
		PlaceDecor(Load(c_Sign), s_Decor, new Vector3(-10.2f, 1.4f, 68f), 90f, false);
		Log("Center knot + two control rooms");
	}

	private static void BuildNorthCqb()
	{
		WallX(96f, -c_PerimX, c_PerimX, -8f, 20f);
		WallZ(-12f, 96f, 114f, 104f);
		WallZ(4f, 96f, 114f, 108f);

		Cover(c_Jersey, new Vector3(-18f, 0f, 102f), 90f);
		Cover(c_HescoSingle, new Vector3(-4f, 0f, 102f), 0f);
		Cover(c_CrateCube, new Vector3(12f, 0f, 102f), 0f);
		Cover(c_Jersey3, new Vector3(-18f, 0f, 110f), 0f);
		Cover(c_Makeshift, new Vector3(10f, 0f, 110f), 90f);
		Cover(c_Barrel, new Vector3(20f, 0f, 106f), 0f);

		WallX(114f, -c_PerimX, c_PerimX, -16f, 8f);
		WallX(132f, -c_PerimX, c_PerimX, -14f, 4f);
		WallZ(-8f, 114f, 132f, 122f);
		WallZ(12f, 114f, 132f, 118f);

		Cover(c_Jersey2, new Vector3(-18f, 0f, 120f), 0f);
		Cover(c_Jersey5, new Vector3(0f, 0f, 120f), 90f);
		Cover(c_CrateStack, new Vector3(18f, 0f, 126f), 15f);
		Cover(c_HescoSingle, new Vector3(-16f, 0f, 128f), 0f);
		Cover(c_BarrelStack, new Vector3(8f, 0f, 128f), 0f);
		PlaceDecor(Load(c_Sign), s_Decor, new Vector3(4.2f, 1.4f, 114.2f), 0f, false);
		Log("North CQB brick rooms");
	}

	private static void BuildEnemyYard(Transform _parent)
	{
		WallZ(-8f, 132f, 144f, 138f);
		WallZ(8f, 132f, 144f, 140f);

		Cover(c_Jersey5, new Vector3(-18f, 0f, 142f), 0f);
		Cover(c_Jersey3, new Vector3(2f, 0f, 142f), 90f);
		Cover(c_Jersey, new Vector3(16f, 0f, 142f), 0f);
		Cover(c_HescoSingle, new Vector3(-14f, 0f, 136f), 0f);
		Cover(c_CrateCube, new Vector3(-4f, 0f, 136f), 20f);
		Cover(c_Barrel, new Vector3(18f, 0f, 136f), 0f);
		Cover(c_Makeshift, new Vector3(0f, 0f, 143.5f), 0f);
		PlaceDecor(Load(c_Flag2), _parent, new Vector3(0f, 0f, 145f), 180f, false);
		PlaceDecor(Load(c_Cone), _parent, new Vector3(-2f, 0f, 145f), 0f, false);
		PlaceDecor(Load(c_Cone), _parent, new Vector3(2f, 0f, 145f), 0f, false);
		PlaceDecor(Load(c_Light), _parent, new Vector3(20f, 0f, 143f), 200f, false);
		Log("Enemy yard rooms");
	}

	private static void BuildSpawnMarkers(Transform _parent)
	{
		Transform players = Group(_parent, "Player");
		Transform enemies = Group(_parent, "Enemy");
		Transform neutrals = Group(_parent, "Neutral");

		float[] playerX1 = { -18f, -12f, -2f, 4f, 16f };
		float[] playerX2 = { -16f, -4f, 2f, 12f, 18f };
		for (int i = 0; i < 5; i++)
		{
			AddMarker(players, "Spawn_Player_" + (i + 1).ToString("00"), new Vector3(playerX1[i], 0f, 11f), 0f, CombatTestSpawnMarker.MarkerSide.Player);
			AddMarker(players, "Spawn_Player_" + (i + 6).ToString("00"), new Vector3(playerX2[i], 0f, 15f), 0f, CombatTestSpawnMarker.MarkerSide.Player);
		}

		for (int i = 0; i < 5; i++)
		{
			AddMarker(enemies, "Spawn_Enemy_" + (i + 1).ToString("00"), new Vector3(playerX1[i], 0f, 139f), 180f, CombatTestSpawnMarker.MarkerSide.Enemy);
			AddMarker(enemies, "Spawn_Enemy_" + (i + 6).ToString("00"), new Vector3(playerX2[i], 0f, 135f), 180f, CombatTestSpawnMarker.MarkerSide.Enemy);
		}

		Vector3[] inner =
		{
			new Vector3(-16f, 0f, 42f),
			new Vector3(0f, 0f, 66f),
			new Vector3(16f, 0f, 42f),
			new Vector3(-18f, 0f, 58f),
			new Vector3(18f, 0f, 58f),
			new Vector3(-16f, 0f, 72f),
			new Vector3(16f, 0f, 72f),
			new Vector3(0f, 0f, 80f),
			new Vector3(-18f, 0f, 90f),
			new Vector3(18f, 0f, 90f),
			new Vector3(4f, 0f, 92f),
			new Vector3(-16f, 0f, 46f),
			new Vector3(-16f, 0f, 104f),
			new Vector3(8f, 0f, 104f),
			new Vector3(-16f, 0f, 122f),
			new Vector3(16f, 0f, 122f),
			new Vector3(-20f, 0f, 50f),
			new Vector3(20f, 0f, 50f),
			new Vector3(-20f, 0f, 100f),
			new Vector3(20f, 0f, 108f)
		};
		for (int i = 0; i < inner.Length; i++)
		{
			float yaw = ((i + 1) % 4) * 90f;
			AddMarker(neutrals, "Spawn_Neutral_" + (i + 1).ToString("00"), inner[i], yaw, CombatTestSpawnMarker.MarkerSide.Neutral);
		}

		Log("Markers player=10 enemy=10 neutral=20");
	}

	private static void PlaceCamera()
	{
		Camera camera = Camera.main;
		if (camera == null)
			camera = Object.FindAnyObjectByType<Camera>();
		if (camera == null)
			return;

		camera.transform.position = new Vector3(0f, 36f, -16f);
		camera.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
		camera.farClipPlane = Mathf.Max(camera.farClipPlane, 800f);
	}
	#endregion

	#region Walls
	private static void WallX(float _z, float _x0, float _x1, params float[] _doors)
	{
		RegisterDoorsX(_z, _doors);
		FillGappedX(s_Walls, s_InteriorPrefab, _z, _x0, _x1, _doors);
	}

	private static void WallZ(float _x, float _z0, float _z1, params float[] _doors)
	{
		RegisterDoorsZ(_x, _doors);
		FillGappedZ(s_Walls, s_InteriorPrefab, _x, _z0, _z1, _doors);
	}

	private static void RegisterDoorsX(float _z, float[] _doors)
	{
		if (_doors == null)
			return;
		for (int i = 0; i < _doors.Length; i++)
			s_Doors.Add(new Vector3(_doors[i], 0f, _z));
	}

	private static void RegisterDoorsZ(float _x, float[] _doors)
	{
		if (_doors == null)
			return;
		for (int i = 0; i < _doors.Length; i++)
			s_Doors.Add(new Vector3(_x, 0f, _doors[i]));
	}

	private static void FillGappedX(Transform _parent, GameObject _prefab, float _z, float _x0, float _x1, float[] _doors)
	{
		List<(float a, float b)> spans = SubtractGaps(Mathf.Min(_x0, _x1), Mathf.Max(_x0, _x1), _doors);
		for (int i = 0; i < spans.Count; i++)
			FillWallX(_parent, _prefab, _z, spans[i].a, spans[i].b);
	}

	private static void FillGappedZ(Transform _parent, GameObject _prefab, float _x, float _z0, float _z1, float[] _doors)
	{
		List<(float a, float b)> spans = SubtractGaps(Mathf.Min(_z0, _z1), Mathf.Max(_z0, _z1), _doors);
		for (int i = 0; i < spans.Count; i++)
			FillWallZ(_parent, _prefab, _x, spans[i].a, spans[i].b);
	}

	private static List<(float a, float b)> SubtractGaps(float _start, float _end, float[] _doors)
	{
		List<(float a, float b)> spans = new List<(float, float)>(12);
		float cursor = _start;
		if (_doors != null && _doors.Length > 0)
		{
			float[] sorted = (float[])_doors.Clone();
			Array.Sort(sorted);
			for (int i = 0; i < sorted.Length; i++)
			{
				float d0 = sorted[i] - c_Door * 0.5f;
				float d1 = sorted[i] + c_Door * 0.5f;
				if (d0 > cursor)
					spans.Add((cursor, d0));
				cursor = Mathf.Max(cursor, d1);
			}
		}

		if (_end > cursor + 0.05f)
			spans.Add((cursor, _end));
		return spans;
	}

	private static void FillWallX(Transform _parent, GameObject _prefab, float _z, float _x0, float _x1)
	{
		FillOverlapping(_parent, _prefab, true, _z, _x0, _x1);
	}

	private static void FillWallZ(Transform _parent, GameObject _prefab, float _x, float _z0, float _z1)
	{
		FillOverlapping(_parent, _prefab, false, _x, _z0, _z1);
	}

	private static void FillOverlapping(Transform _parent, GameObject _prefab, bool _alongX, float _fixed, float _a, float _b)
	{
		if (_prefab == null)
			return;

		float start = Mathf.Min(_a, _b);
		float end = Mathf.Max(_a, _b);
		float span = end - start;
		if (span < c_MinSpan)
			return;

		float length = WallLength(_prefab);
		float yaw = _alongX ? YawAlongX(_prefab) : YawAlongZ(_prefab);
		if (span <= length)
		{
			if (span < length * 0.72f)
				return;
			PlaceWall(_parent, _prefab, Mid(_alongX, _fixed, (start + end) * 0.5f), yaw);
			return;
		}

		float first = start + length * 0.5f;
		float last = end - length * 0.5f;
		float step = Mathf.Max(0.45f, length - c_WallOverlap);
		int count = Mathf.Max(2, Mathf.CeilToInt((last - first) / step) + 1);
		for (int i = 0; i < count; i++)
		{
			float t = i / (float)(count - 1);
			float p = Mathf.Lerp(first, last, t);
			PlaceWall(_parent, _prefab, Mid(_alongX, _fixed, p), yaw);
		}
	}

	private static Vector3 Mid(bool _alongX, float _fixed, float _p)
	{
		return _alongX ? new Vector3(_p, 0f, _fixed) : new Vector3(_fixed, 0f, _p);
	}

	private static void PlaceWall(Transform _parent, GameObject _prefab, Vector3 _pos, float _yaw)
	{
		Place(_prefab, _parent, _pos, _yaw);
	}

	private static float WallLength(GameObject _prefab)
	{
		Vector3 size = PrefabSize(_prefab);
		return Mathf.Max(size.x, size.z);
	}

	private static float YawAlongX(GameObject _prefab)
	{
		Vector3 size = PrefabSize(_prefab);
		return size.x >= size.z ? 0f : 90f;
	}

	private static float YawAlongZ(GameObject _prefab)
	{
		Vector3 size = PrefabSize(_prefab);
		return size.x >= size.z ? 90f : 0f;
	}
	#endregion

	#region NavMesh
	private static NavMeshSurface BakeNavMesh(GameObject _root, Transform _navigation)
	{
		NavMeshSurface surface = _root.GetComponent<NavMeshSurface>();
		if (surface == null)
			surface = _root.AddComponent<NavMeshSurface>();
		surface.agentTypeID = 0;
		surface.collectObjects = CollectObjects.Children;
		surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
		surface.ignoreNavMeshAgent = true;
		surface.ignoreNavMeshObstacle = true;
		surface.overrideVoxelSize = false;
		surface.minRegionArea = 2f;
		_navigation.transform.SetParent(_root.transform, false);

		string folder = "Assets/Scenes/SampleScene";
		if (!AssetDatabase.IsValidFolder(folder))
			AssetDatabase.CreateFolder("Assets/Scenes", "SampleScene");
		if (AssetDatabase.LoadAssetAtPath<NavMeshData>(c_NavMeshAssetPath) != null)
			AssetDatabase.DeleteAsset(c_NavMeshAssetPath);

		Physics.SyncTransforms();
		surface.BuildNavMesh();
		NavMeshData data = surface.navMeshData;
		if (data == null)
		{
			Check("NavMeshBake", false, "NavMeshSurface produced no data");
			return surface;
		}

		AssetDatabase.CreateAsset(data, c_NavMeshAssetPath);
		surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(c_NavMeshAssetPath);
		surface.RemoveData();
		surface.AddData();
		Log("NavMesh baked -> " + c_NavMeshAssetPath);
		return surface;
	}
	#endregion

	#region Validate
	private static void Validate(Scene _scene, NavMeshSurface _surface)
	{
		Check("LegacyShootingRangeHidden", IsInactive(_scene, "ShootingRange"), "ShootingRange");
		Check("LegacyNavTestHidden", IsInactive(_scene, "NavTestPolygon"), "NavTestPolygon");
		Check("LegacyPlaneHidden", IsInactive(_scene, "Plane"), "Plane");
		Check("LegacyCubeHidden", IsInactive(_scene, "Cube"), "Cube");
		Check("HarnessHidden", IsInactive(_scene, "DetectionG1Harness"), "DetectionG1Harness");
		Check("GameSystemsActive", IsActive(_scene, "GameSystems"), "GameSystems");
		Check("LegacyArenaRemoved", FindSceneObject(_scene, c_LegacyRootName) == null, c_LegacyRootName);
		Check("ArenaPresent", FindSceneObject(_scene, c_RootName) != null, c_RootName);
		Check("MarkerCount", s_Markers.Count == 40, "count=" + s_Markers.Count);
		Check("InteriorPrefab", s_InteriorPrefab != null, c_Tall01);
		Check("PerimeterPrefab", s_PerimeterPrefab != null, c_TallGroup);

		CombatTestArenaSpawner arenaSpawner = FindComponent<CombatTestArenaSpawner>(_scene);
		Check("ArenaSpawnerPresent", arenaSpawner != null, nameof(CombatTestArenaSpawner));

		UnitSceneSpawner unitSpawner = FindComponent<UnitSceneSpawner>(_scene);
		if (unitSpawner != null)
		{
			SerializedObject so = new SerializedObject(unitSpawner);
			Check("UnitSpawnerOff", so.FindProperty("m_SpawnOnStart").boolValue == false, "m_SpawnOnStart");
		}

		MissionPrepSquadSpawner missionSpawner = FindComponent<MissionPrepSquadSpawner>(_scene);
		if (missionSpawner != null)
		{
			SerializedObject so = new SerializedObject(missionSpawner);
			Check("MissionSpawnerOff", so.FindProperty("m_SpawnOnStart").boolValue == false, "m_SpawnOnStart");
		}

		int onMesh = 0;
		for (int i = 0; i < s_Markers.Count; i++)
		{
			CombatTestSpawnMarker marker = s_Markers[i];
			if (marker == null)
				continue;
			if (Sample(marker.transform.position, out _))
				onMesh++;
			else
				Log("OFFMESH " + marker.name + " " + marker.transform.position);
		}

		Check("AllMarkersOnNavMesh", onMesh == s_Markers.Count, onMesh + "/" + s_Markers.Count);

		int doorsOk = 0;
		for (int i = 0; i < s_Doors.Count; i++)
		{
			if (Sample(s_Doors[i], out _))
				doorsOk++;
			else
				Log("DOOR BLOCKED " + s_Doors[i]);
		}

		Check("DoorsWalkable", doorsOk == s_Doors.Count, doorsOk + "/" + s_Doors.Count);

		Vector3 player = MarkerPos("Spawn_Player_01");
		Vector3 enemy = MarkerPos("Spawn_Enemy_01");
		Vector3 center = MarkerPos("Spawn_Neutral_02");
		Vector3 westDogleg = MarkerPos("Spawn_Neutral_01");
		Vector3 eastDogleg = MarkerPos("Spawn_Neutral_16");
		Vector3 westControl = MarkerPos("Spawn_Neutral_06");
		Vector3 eastControl = MarkerPos("Spawn_Neutral_07");
		Vector3 innerNorth = MarkerPos("Spawn_Neutral_08");
		Vector3 westEdgeSouth = new Vector3(-23.2f, 0f, 32f);
		Vector3 westEdgeNorth = new Vector3(-23.2f, 0f, 40f);
		Vector3 eastEdgeSouth = new Vector3(23.2f, 0f, 32f);
		Vector3 eastEdgeNorth = new Vector3(23.2f, 0f, 40f);

		LogPath("P->center", player, center);
		LogPath("E->center", enemy, center);
		LogPath("westDogleg", player, westDogleg);
		LogPath("eastDogleg", enemy, eastDogleg);
		LogPath("westControl", player, westControl);
		LogPath("eastControl", enemy, eastControl);

		Check("Path_PlayerToCenter", HasPath(player, center), "player->N02");
		Check("Path_EnemyToCenter", HasPath(enemy, center), "enemy->N02");
		Check("Path_PlayerToEnemy", HasPath(player, enemy), "player->enemy");
		Check("Path_WestFlank", HasPath(player, westDogleg) && HasPath(westDogleg, center), PathDetail(player, westDogleg, center));
		Check("Path_EastFlank", HasPath(enemy, eastDogleg) && HasPath(eastDogleg, center), PathDetail(enemy, eastDogleg, center));
		Check("Path_InnerPlaza", HasPath(center, innerNorth), "N02->N08 around T");
		Check("Path_WestControl", HasPath(player, westControl), "player->west room");
		Check("Path_EastControl", HasPath(enemy, eastControl), "enemy->east room");
		float westLen = PathLength(westEdgeSouth, westEdgeNorth);
		float eastLen = PathLength(eastEdgeSouth, eastEdgeNorth);
		Log("EDGE westLen=" + westLen.ToString("F1") + " eastLen=" + eastLen.ToString("F1"));
		Check("NoWestRacetrack", westLen < 0f || westLen > 12f, "west edge leak len=" + westLen.ToString("F1"));
		Check("NoEastRacetrack", eastLen < 0f || eastLen > 12f, "east edge leak len=" + eastLen.ToString("F1"));
		Check("NavMeshSurface", _surface != null && _surface.navMeshData != null, "surface");
		Log("FAILS=" + s_FailCount);
	}

	private static Vector3 MarkerPos(string _name)
	{
		for (int i = 0; i < s_Markers.Count; i++)
		{
			if (s_Markers[i] != null && s_Markers[i].name == _name)
				return s_Markers[i].transform.position;
		}

		return Vector3.zero;
	}

	private static bool Sample(Vector3 _pos, out NavMeshHit _hit)
	{
		return NavMesh.SamplePosition(_pos, out _hit, 1.4f, NavMesh.AllAreas);
	}

	private static string PathDetail(Vector3 _a, Vector3 _b, Vector3 _c)
	{
		bool ab = HasPath(_a, _b);
		bool bc = HasPath(_b, _c);
		bool aOn = Sample(_a, out _);
		bool bOn = Sample(_b, out _);
		bool cOn = Sample(_c, out _);
		return $"aOn={aOn} bOn={bOn} cOn={cOn} ab={ab} bc={bc} b={_b}";
	}

	private static float PathLength(Vector3 _from, Vector3 _to)
	{
		if (!Sample(_from, out NavMeshHit a) || !Sample(_to, out NavMeshHit b))
			return -1f;
		NavMeshPath path = new NavMeshPath();
		if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path))
			return -1f;
		if (path.status != NavMeshPathStatus.PathComplete)
			return -1f;
		Vector3[] corners = path.corners;
		if (corners == null || corners.Length < 2)
			return Vector3.Distance(a.position, b.position);
		float length = 0f;
		for (int i = 1; i < corners.Length; i++)
			length += Vector3.Distance(corners[i - 1], corners[i]);
		return length;
	}

	private static bool HasPath(Vector3 _from, Vector3 _to)
	{
		if (!Sample(_from, out NavMeshHit a) || !Sample(_to, out NavMeshHit b))
			return false;
		NavMeshPath path = new NavMeshPath();
		if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path))
			return false;
		return path.status == NavMeshPathStatus.PathComplete;
	}

	private static void LogPath(string _label, Vector3 _from, Vector3 _to)
	{
		bool fromOn = Sample(_from, out NavMeshHit fromHit);
		bool toOn = Sample(_to, out NavMeshHit toHit);
		string status = "offmesh";
		if (fromOn && toOn)
		{
			NavMeshPath path = new NavMeshPath();
			bool ok = NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path);
			status = ok ? path.status.ToString() : "calc-false";
		}

		Log("PATH " + _label + " " + status + " fromOn=" + fromOn + " toOn=" + toOn);
	}
	#endregion

	#region Helpers
	private static Transform Group(Transform _parent, string _name)
	{
		GameObject go = new GameObject(_name);
		go.transform.SetParent(_parent, false);
		return go.transform;
	}

	private static GameObject Load(string _path)
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_path);
		if (prefab == null)
			Log("Missing prefab " + _path);
		return prefab;
	}

	private static GameObject Place(GameObject _prefab, Transform _parent, Vector3 _pos, float _yaw, bool _snap = true)
	{
		if (_prefab == null)
			return null;
		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(_prefab, _parent);
		instance.transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
		if (_snap)
			SnapBottom(instance);
		SetNavStatic(instance);
		return instance;
	}

	private static void PlaceDecor(GameObject _prefab, Transform _parent, Vector3 _pos, float _yaw, bool _keepColliders)
	{
		GameObject instance = Place(_prefab, _parent, _pos, _yaw);
		if (instance != null && !_keepColliders)
			DisableColliders(instance);
	}

	private static void Cover(string _path, Vector3 _pos, float _yaw)
	{
		Place(Load(_path), s_Cover, _pos, _yaw);
	}

	private static void AddMarker(Transform _parent, string _name, Vector3 _pos, float _yaw, CombatTestSpawnMarker.MarkerSide _side)
	{
		GameObject go = new GameObject(_name);
		go.transform.SetParent(_parent, false);
		go.transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
		CombatTestSpawnMarker marker = go.AddComponent<CombatTestSpawnMarker>();
		marker.Side = _side;
		s_Markers.Add(marker);
	}

	private static void DisableColliders(GameObject _root)
	{
		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;
	}

	private static void SnapBottom(GameObject _go)
	{
		Physics.SyncTransforms();
		Bounds bounds = WorldBounds(_go);
		if (bounds.size.sqrMagnitude < 0.0001f)
			return;
		_go.transform.position += new Vector3(0f, -bounds.min.y, 0f);
		Physics.SyncTransforms();
	}

	private static Bounds WorldBounds(GameObject _go)
	{
		Collider[] colliders = _go.GetComponentsInChildren<Collider>(true);
		bool any = false;
		Bounds bounds = new Bounds(_go.transform.position, Vector3.zero);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (!colliders[i].enabled)
				continue;
			if (!any)
			{
				bounds = colliders[i].bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(colliders[i].bounds);
			}
		}

		if (any)
			return bounds;

		Renderer[] renderers = _go.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			if (!any)
			{
				bounds = renderers[i].bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(renderers[i].bounds);
			}
		}

		return bounds;
	}

	private static readonly Dictionary<string, Vector3> s_SizeCache = new Dictionary<string, Vector3>();

	private static Vector3 PrefabSize(GameObject _prefab)
	{
		if (_prefab == null)
			return Vector3.one;
		string key = AssetDatabase.GetAssetPath(_prefab);
		if (string.IsNullOrEmpty(key))
			key = _prefab.name;
		if (s_SizeCache.TryGetValue(key, out Vector3 cached))
			return cached;

		GameObject temp = Object.Instantiate(_prefab);
		temp.hideFlags = HideFlags.HideAndDontSave;
		Bounds bounds = WorldBounds(temp);
		Vector3 size = bounds.size;
		if (size.x < 0.1f) size.x = 1f;
		if (size.y < 0.1f) size.y = 1f;
		if (size.z < 0.1f) size.z = 1f;
		s_SizeCache[key] = size;
		Object.DestroyImmediate(temp);
		return size;
	}

	private static void SetNavStatic(GameObject _go)
	{
		Transform[] transforms = _go.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
			GameObjectUtility.SetStaticEditorFlags(transforms[i].gameObject, StaticEditorFlags.BatchingStatic);
	}

	private static GameObject FindSceneObject(Scene _scene, string _name)
	{
		GameObject[] roots = _scene.GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			if (roots[i].name == _name)
				return roots[i];
			Transform found = FindDeep(roots[i].transform, _name);
			if (found != null)
				return found.gameObject;
		}

		return null;
	}

	private static Transform FindDeep(Transform _parent, string _name)
	{
		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform child = _parent.GetChild(i);
			if (child.name == _name)
				return child;
			Transform nested = FindDeep(child, _name);
			if (nested != null)
				return nested;
		}

		return null;
	}

	private static T FindComponent<T>(Scene _scene) where T : Component
	{
		GameObject[] roots = _scene.GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			T component = roots[i].GetComponentInChildren<T>(true);
			if (component != null)
				return component;
		}

		return null;
	}

	private static bool IsInactive(Scene _scene, string _name)
	{
		GameObject go = FindSceneObject(_scene, _name);
		return go != null && !go.activeInHierarchy;
	}

	private static bool IsActive(Scene _scene, string _name)
	{
		GameObject go = FindSceneObject(_scene, _name);
		return go != null && go.activeInHierarchy;
	}

	private static string GetPath(GameObject _go)
	{
		string path = _go.name;
		Transform t = _go.transform.parent;
		while (t != null)
		{
			path = t.name + "/" + path;
			t = t.parent;
		}

		return path;
	}

	private static void Check(string _name, bool _ok, string _detail)
	{
		if (_ok)
		{
			Log("PASS " + _name + " | " + _detail);
			return;
		}

		s_FailCount++;
		Log("FAIL " + _name + " | " + _detail);
	}

	private static void Log(string _line)
	{
		s_Log.AppendLine(_line);
	}

	private static void WriteReport(string _text)
	{
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string fullPath = Path.Combine(dir, "CombatTestArena_LAST.txt");
		File.WriteAllText(fullPath, _text, Encoding.UTF8);
		AssetDatabase.ImportAsset(
			"Assets/_Docs/Logs/Tests/CombatTestArena_LAST.txt",
			ImportAssetOptions.ForceUpdate);
	}
	#endregion
}
#endif
