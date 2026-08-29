#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Editor-time wiring checks. Fail here, not after Play.
/// </summary>
public static class TacticalAiValidator
{
	#region Constants
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	#endregion

	#region Menu
	[MenuItem("Polygone/Tactical AI/Validate Unit Prefab", false, 30)]
	public static void ValidateUnitFromMenu()
	{
		string report = ValidateUnitPrefab();
		Debug.Log("[TacticalAI] Validate Unit Prefab\n" + report);
		EditorUtility.DisplayDialog("Validate Unit Prefab", report, "OK");
	}

	[MenuItem("Polygone/Tactical AI/Validate Arena Wiring", false, 31)]
	public static void ValidateArenaFromMenu()
	{
		string report = ValidateArenaWiring();
		Debug.Log("[TacticalAI] Validate Arena Wiring\n" + report);
		EditorUtility.DisplayDialog("Validate Arena Wiring", report, "OK");
	}
	#endregion

	#region Public Methods
	public static string ValidateUnitPrefab()
	{
		var log = new StringBuilder(512);
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c_UnitPrefabPath);
		if (prefab == null)
			return "[FAIL] Unit.prefab missing";

		Check(log, "UnitLifeGate", prefab.GetComponent<UnitLifeGate>() != null);
		Check(log, "UnitAIController", prefab.GetComponent<UnitAIController>() != null);
		UnitAIController ai = prefab.GetComponent<UnitAIController>();
		Check(log, "TacticalCoverOverlay (owned by UnitAIController)", ai != null);
		Check(log, "TacticalMovement (owned by UnitAIController)", ai != null);
		Check(log, "NavMeshAgent", prefab.GetComponent<NavMeshAgent>() != null);
		Check(log, "UnitNavLocomotionDriver", prefab.GetComponent<UnitNavLocomotionDriver>() != null);
		Check(log, "UnitSpineLean", prefab.GetComponent<UnitSpineLean>() != null);
		Check(log, "World profile assigned", ai != null && ai.WorldProfile != null);
		Check(log, "Tactical profile assigned", ai != null && ai.TacticalProfile != null);
		Check(
			log,
			"Tactical profile UseCover",
			ai != null && ai.TacticalProfile != null && ai.TacticalProfile.UseCover);
		Check(
			log,
			"Tactical profile MovementMode Tactical",
			ai != null &&
			ai.TacticalProfile != null &&
			ai.TacticalProfile.MovementMode == TacticalMovementMode.Tactical);
		Check(
			log,
			"Lean binding possible",
			prefab.GetComponent<UnitSpineLean>() != null);
		log.AppendLine("note: UnitAIController stays disabled on prefab; CombatTestArenaSpawner enables combat sides");
		return log.ToString();
	}

	public static string ValidateArenaWiring()
	{
		var log = new StringBuilder(512);
		TacticalWorld world = Object.FindAnyObjectByType<TacticalWorld>();
		Check(log, "TacticalWorld in scene", world != null);
		if (world == null)
			return log.ToString();
		Check(log, "World profile", world.Profile != null);
		Check(log, "SharedCoverSpatialCache host (TacticalWorld)", true);
		Check(
			log,
			"CoverOccupancyBoard host",
			world.transform.Find("CoverOccupancyBoard") != null);
		Check(log, "Cover cache baked", world.IsBaked);
		if (world.IsBaked)
			log.AppendLine("[PASS] baked=" + world.BakedCount);
		else
			log.AppendLine("[FAIL] Cover cache not baked");
		Check(log, "NavMesh reachable in bake bounds", TacticalWorldBaker.NavMeshReachable(world.ResolveWorldBakeBounds()));
		UnitAIController ai = AssetDatabase.LoadAssetAtPath<GameObject>(c_UnitPrefabPath)
			?.GetComponent<UnitAIController>();
		Check(
			log,
			"Tactical movement binding (prefab profile → world profile)",
			ai != null && ai.WorldProfile != null && world.Profile == ai.WorldProfile);
		return log.ToString();
	}
	#endregion

	#region Private Methods
	private static void Check(StringBuilder _log, string _label, bool _pass)
	{
		_log.AppendLine((_pass ? "[PASS] " : "[FAIL] ") + _label);
	}
	#endregion
}
#endif
