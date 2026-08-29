#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates TacticalWorld + profiles and puts UnitAIController on Unit.prefab. Does not retune #13/#14.
/// </summary>
public static class TacticalAiWiringInstaller
{
	#region Constants
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_DataFolder = "Assets/_Data/Tactical";
	private const string c_WorldProfilePath = "Assets/_Data/Tactical/CombatArenaWorldProfile.asset";
	private const string c_TacticalProfilePath = "Assets/_Data/Tactical/InfantryDefaultTacticalProfile.asset";
	private const string c_ArenaName = "CombatTestArena_150x50";
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	#endregion

	#region Menu
	[MenuItem("Polygone/Tactical AI/Install Arena Editor Wiring", false, 20)]
	public static void InstallFromMenu()
	{
		string report = Install();
		Debug.Log("[TacticalAI] Install Arena Editor Wiring\n" + report);
		EditorUtility.DisplayDialog("Tactical AI wiring", report, "OK");
	}

	[MenuItem("Polygone/Tactical AI/Bake Cover (TacticalWorld)", false, 21)]
	public static void BakeFromMenu()
	{
		TacticalWorld world = Object.FindAnyObjectByType<TacticalWorld>();
		if (world == null)
		{
			EditorUtility.DisplayDialog("Bake Cover", "No TacticalWorld in the open scene. Run Install first.", "OK");
			return;
		}

		int count = TacticalWorldBaker.Bake(world);
		EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
		EditorSceneManager.SaveScene(world.gameObject.scene);
		string msg = "zones=" + count + " nav=" + TacticalWorldBaker.NavMeshReachable(world.ResolveWorldBakeBounds());
		Debug.Log("[TacticalAI] Bake Protection Zones " + msg);
		EditorUtility.DisplayDialog("Bake Protection Zones", msg, "OK");
	}
	#endregion

	#region Public Methods
	public static string Install()
	{
		var log = new StringBuilder(1024);
		EnsureFolder("Assets/_Data");
		EnsureFolder(c_DataFolder);

		TacticalWorldProfile worldProfile = LoadOrCreateProfile<TacticalWorldProfile>(
			c_WorldProfilePath, "CombatArenaWorldProfile");
		InfantryTacticalProfile tacticalProfile = LoadOrCreateProfile<InfantryTacticalProfile>(
			c_TacticalProfilePath, "InfantryDefaultTacticalProfile");
		ApplyTacticalProfileDefaults(tacticalProfile);
		log.AppendLine("worldProfile=" + AssetDatabase.GetAssetPath(worldProfile));
		log.AppendLine("tacticalProfile=" + AssetDatabase.GetAssetPath(tacticalProfile));

		WireUnitPrefab(worldProfile, tacticalProfile, log);
		WireSceneWorld(worldProfile, log);
		AssetDatabase.SaveAssets();
		return log.ToString();
	}
	#endregion

	#region Private Methods
	private static void WireUnitPrefab(
		TacticalWorldProfile _worldProfile,
		InfantryTacticalProfile _tacticalProfile,
		StringBuilder _log)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		try
		{
			if (!root.TryGetComponent(out UnitAIController ai))
			{
				ai = root.AddComponent<UnitAIController>();
				_log.AppendLine("Unit.prefab: added UnitAIController");
			}
			else
				_log.AppendLine("Unit.prefab: UnitAIController already present");

			if (!root.TryGetComponent(out UnitLifeGate _))
			{
				root.AddComponent<UnitLifeGate>();
				_log.AppendLine("Unit.prefab: added UnitLifeGate");
			}

			ai.AssignTacticalProfiles(_worldProfile, _tacticalProfile);
			SerializedObject so = new SerializedObject(ai);
			so.FindProperty("m_WorldProfile").objectReferenceValue = _worldProfile;
			so.FindProperty("m_TacticalProfile").objectReferenceValue = _tacticalProfile;
			so.ApplyModifiedPropertiesWithoutUndo();
			ai.enabled = false;
			if (!root.TryGetComponent(out TacticalMovementDebugDraw _))
				root.AddComponent<TacticalMovementDebugDraw>();
			if (!root.TryGetComponent(out CoverCandidateDebugDraw _))
				root.AddComponent<CoverCandidateDebugDraw>();
			if (!root.TryGetComponent(out NavMeshAgent _))
				_log.AppendLine("WARN: NavMeshAgent missing");
			if (!root.TryGetComponent(out UnitNavLocomotionDriver _))
				_log.AppendLine("WARN: UnitNavLocomotionDriver missing");
			if (!root.TryGetComponent(out UnitSpineLean _))
				_log.AppendLine("WARN: UnitSpineLean missing");
			_log.AppendLine("Unit.prefab: AI disabled until arena spawn (G-tests stay Idle-off)");
			PrefabUtility.SaveAsPrefabAsset(root, c_UnitPrefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static void WireSceneWorld(TacticalWorldProfile _worldProfile, StringBuilder _log)
	{
		Scene scene = EditorSceneManager.GetActiveScene();
		if (scene.path != c_ScenePath)
			scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);

		GameObject arena = GameObject.Find(c_ArenaName);
		if (arena == null)
		{
			_log.AppendLine("FAIL: " + c_ArenaName + " not in scene");
			return;
		}

		Transform existing = arena.transform.Find(TacticalWorld.DefaultChildName);
		GameObject worldGo = existing != null ? existing.gameObject : null;
		if (worldGo == null)
		{
			worldGo = new GameObject(TacticalWorld.DefaultChildName);
			worldGo.transform.SetParent(arena.transform, false);
			_log.AppendLine("created TacticalWorld child");
		}

		if (!worldGo.TryGetComponent(out TacticalWorld world))
			world = worldGo.AddComponent<TacticalWorld>();
		world.AssignProfile(_worldProfile);
		world.SetBakeBounds(new Bounds(new Vector3(0f, 1f, 75f), new Vector3(50f, 4f, 150f)), true);
		if (worldGo.transform.Find("CoverOccupancyBoard") == null)
		{
			var occupancyHost = new GameObject("CoverOccupancyBoard");
			occupancyHost.transform.SetParent(worldGo.transform, false);
		}

		int baked = TacticalWorldBaker.Bake(world);
		EditorUtility.SetDirty(world);
		_log.AppendLine("bakedZones=" + baked);
		_log.AppendLine("navReachable=" + TacticalWorldBaker.NavMeshReachable(world.ResolveWorldBakeBounds()));
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
	}

	private static void ApplyTacticalProfileDefaults(InfantryTacticalProfile _profile)
	{
		if (_profile == null)
			return;
		SerializedObject so = new SerializedObject(_profile);
		so.FindProperty("m_UseCover").boolValue = true;
		so.FindProperty("m_AllowCoverReservation").boolValue = true;
		so.FindProperty("m_MovementMode").enumValueIndex = (int)TacticalMovementMode.Tactical;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_profile);
	}

	private static T LoadOrCreateProfile<T>(string _path, string _name) where T : ScriptableObject
	{
		T existing = AssetDatabase.LoadAssetAtPath<T>(_path);
		if (existing != null)
			return existing;
		T created = ScriptableObject.CreateInstance<T>();
		created.name = _name;
		AssetDatabase.CreateAsset(created, _path);
		return created;
	}

	private static void EnsureFolder(string _path)
	{
		if (AssetDatabase.IsValidFolder(_path))
			return;
		string parent = Path.GetDirectoryName(_path)?.Replace("\\", "/");
		string leaf = Path.GetFileName(_path);
		if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
			return;
		AssetDatabase.CreateFolder(parent, leaf);
	}
	#endregion
}
#endif
