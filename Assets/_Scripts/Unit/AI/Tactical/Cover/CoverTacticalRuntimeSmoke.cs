using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #13.5 Play: Stay / RepositionRequest. Not Move. Not Fire.
/// Report: Assets/_Docs/Logs/Tests/CoverTactical_LAST.txt
/// </summary>
[DefaultExecutionOrder(66)]
[DisallowMultipleComponent]
public sealed class CoverTacticalRuntimeSmoke : MonoBehaviour
{
	#region Nested
	private sealed class ListSource : ICoverCandidateSource
	{
		public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(8);

		public void Generate(
			CoverRegionId _region,
			Bounds _bounds,
			int _geometryVersion,
			List<CoverCandidate> _destination)
		{
			for (int i = 0; i < Candidates.Count; i++)
				_destination.Add(Candidates[i]);
		}
	}
	#endregion

	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(7600f, 0f, 7600f);
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_Arena;
	private GameObject m_Unit;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverTactical;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverTactical)
			return;
		if (FindAnyObjectByType<CoverTacticalRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverTacticalRuntimeSmoke");
		go.AddComponent<CoverTacticalRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyArena();
		if (DetectionHarnessPlayMode.RunCoverTactical)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		yield return null;

		m_Report.Length = 0;
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine("STAGE 13.5 — TACTICAL COVER / POSITION SWITCHING");
		AppendLine("================================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Stay / RepositionRequest. Destination ≠ Move. Not Fire. Not #14 pathing.");
		AppendLine("---");

		Bounds regionBounds = SpawnArena();
		Physics.SyncTransforms();
		NavMeshSurface surface = m_Arena.GetComponent<NavMeshSurface>();
		surface.BuildNavMesh();
		yield return null;

		bool sampled = NavMesh.SamplePosition(
			regionBounds.center + Vector3.up * 0.1f,
			out NavMeshHit navHit,
			2f,
			NavMesh.AllAreas);
		Check("S0_NavMeshBake", surface.navMeshData != null && sampled,
			"sample=" + sampled);

		Vector3 unit = regionBounds.center;
		Vector3 target = unit + new Vector3(0f, 1.5f, 40f);

		AppendLine("[S1] Good current + slightly better nearby → Stay");
		TacticalCoverDecision stayGate = new TacticalCoverSolver().DecideFromScores(
			8f, 8.1f, 1f, true, 1, 2);
		Check("S1_StayGate", stayGate.Decision == TacticalCoverDecisionKind.Stay,
			"decision=" + stayGate.Decision);
		CoverCandidate currentGood = Make(1, unit, Vector3.forward, CoverType.Standing, 1f);
		CoverCandidate slightly = Make(2, unit + new Vector3(1.2f, 0f, 0f), Vector3.forward, CoverType.Standing, 1f);
		CoverSituation atCover = Situation(unit, target, CoverMissionIntent.Hold);
		TacticalCoverDecision stay = OverlayDecide(
			new[] { currentGood, slightly }, in atCover, 1f);
		Check("S1_Stay", stay.Decision == TacticalCoverDecisionKind.Stay,
			"decision=" + stay.Decision + " reason=" + stay.Reason);

		AppendLine("[S2] Significantly better → RepositionRequest, no Walk");
		TacticalCoverDecision switchGate = new TacticalCoverSolver().DecideFromScores(
			8f, 10f, 1f, true, 1, 2);
		Check("S2_SwitchGate", switchGate.Decision == TacticalCoverDecisionKind.Reposition,
			"decision=" + switchGate.Decision);
		CoverCandidate poor = Make(1, unit, Vector3.forward, CoverType.Standing, 0.05f);
		CoverCandidate excellent = Make(2, unit + new Vector3(4f, 0f, 0f), Vector3.forward, CoverType.Standing, 1f);
		TacticalCoverDecision move = OverlayDecide(new[] { poor, excellent }, in atCover, 1f);
		Check("S2_Reposition", move.Decision == TacticalCoverDecisionKind.Reposition,
			"decision=" + move.Decision);
		Check("S2_HasDest", move.HasDestination, "reason=" + move.Reason);

		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var geometry = new PhysicsCoverGeometrySource();
		var generator = new CoverCandidateGenerator(
			geometry,
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe());
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(generator);

		m_Unit = new GameObject("TacticalUnit");
		m_Unit.transform.position = unit;
		UnitAIController ai = m_Unit.AddComponent<UnitAIController>();
		ai.BindCoverCache(cache);
		ai.Tick(0.05f);
		Check("S2_Idle", ai.CurrentState == UnitAIState.Idle, "state=" + ai.CurrentState);
		Check("S2_NoWalk", !ai.TacticalNavigationIssued && !ai.SearchHasMoveIntent,
			"issued=" + ai.TacticalNavigationIssued);
		Check("S2_DestNotContext",
			!ai.HasTacticalRepositionRequest ||
			!ai.CurrentContext.HasDestination ||
			ai.CurrentContext.Destination != ai.TacticalCoverDestination,
			"ctx=" + ai.CurrentContext.Destination);

		AppendLine("[S3] Cover intact, target changed → reevaluate");
		CoverCandidate hold = Make(3, unit, Vector3.forward, CoverType.Standing, 1f);
		CoverCandidate other = Make(4, unit + new Vector3(6f, 0f, 0f), Vector3.forward, CoverType.Standing, 0.8f);
		CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(hold, true);
		var solver = new TacticalCoverSolver();
		solver.Decide(in atCover, new[] { hold, other }, in occupying);
		CoverSituation movedTarget = atCover;
		movedTarget.TargetPosition = unit + new Vector3(18f, 1.5f, 8f);
		solver.Decide(in movedTarget, new[] { hold, other }, in occupying);
		Check("S3_Reeval", solver.DecideCount == 2, "count=" + solver.DecideCount);

		AppendLine("[S4] Current invalid → mandatory reposition");
		TacticalCoverDecision invalid = new TacticalCoverSolver().Decide(
			in atCover, new[] { excellent }, CurrentTacticalPosition.Invalid);
		Check("S4_Invalid", invalid.Decision == TacticalCoverDecisionKind.Reposition &&
		                    invalid.Reason == TacticalCoverReason.CurrentInvalid,
			"decision=" + invalid.Decision + " reason=" + invalid.Reason);
		var versionOverlay = new TacticalCoverOverlay();
		versionOverlay.BindCache(cache);
		versionOverlay.Update(false, UnitAIState.Idle, in atCover);
		int decideBefore = versionOverlay.Solver.DecideCount;
		int genBefore = cache.GenerationCount;
		cache.BumpGeometryVersion();
		versionOverlay.Update(false, UnitAIState.Idle, in atCover);
		Check("S4_GeometryReeval", versionOverlay.Solver.DecideCount == decideBefore + 1,
			"count=" + versionOverlay.Solver.DecideCount);
		Check("S4_GeometryRegen", cache.GenerationCount == genBefore + 1,
			"gen=" + cache.GenerationCount);

		AppendLine("[S5] Attack: mission-aware vs safer back");
		CoverCandidate back = Make(11, unit + new Vector3(0f, 0f, -6f), Vector3.back, CoverType.Standing, 1f);
		CoverCandidate forward = Make(12, unit + new Vector3(0f, 0f, 8f), Vector3.forward, CoverType.Standing, 0.85f);
		CoverSituation attackSit = Situation(unit, target, CoverMissionIntent.Attack);
		CoverSituation defenseSit = Situation(unit, target, CoverMissionIntent.Defense);
		float attackFwd = CoverScoreMath.PositionScore(forward, in attackSit, null);
		float defenseFwd = CoverScoreMath.PositionScore(forward, in defenseSit, null);
		Check("S5_MissionScoresDiffer", attackFwd != defenseFwd,
			"atk=" + attackFwd + " def=" + defenseFwd);
		TacticalCoverDecision attackDecision = new TacticalCoverSolver().Decide(
			in attackSit, new[] { back, forward }, CurrentTacticalPosition.Invalid, null, 1f);
		TacticalCoverDecision defenseDecision = new TacticalCoverSolver().Decide(
			in defenseSit, new[] { back, forward }, CurrentTacticalPosition.Invalid, null, 1f);
		Check("S5_DecisionsDifferOrScores",
			attackDecision.BestScore != defenseDecision.BestScore ||
			attackDecision.SelectedCandidateId != defenseDecision.SelectedCandidateId,
			"atkId=" + attackDecision.SelectedCandidateId + " defId=" + defenseDecision.SelectedCandidateId);

		AppendLine("[S6] 20 units / 3 regions → 3 geometry, idle does not Walk");
		SharedCoverSpatialCache shared = new SharedCoverSpatialCache(generator);
		int fired = 0;
		fired += FireUnits(shared, regionBounds.center, 7);
		fired += FireUnits(shared, regionBounds.center + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f), 7);
		fired += FireUnits(shared, regionBounds.center + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters), 6);
		Check("S6_Generations", shared.GenerationCount == 3, "gen=" + shared.GenerationCount);
		Check("S6_Units", fired == 20, "n=" + fired);

		AppendLine("[S7] Overlay CURRENT / BEST / SWITCH COST / RESULT");
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();
		debug.CaptureTactical(
			regionBounds,
			new[] { poor, excellent },
			move.Evaluations,
			move.CurrentCandidateId,
			move.BestCandidateId,
			move.CurrentScore,
			move.BestScore,
			move.SwitchingCost,
			move.Decision,
			false);
		Check("S7_TacticalActive", debug.TacticalCoverActive, "off");
		Check("S7_Result", debug.TacticalDecision == TacticalCoverDecisionKind.Reposition,
			"kind=" + debug.TacticalDecision);
		Check("S7_BestHighlighted", debug.SelectedId == move.BestCandidateId,
			"sel=" + debug.SelectedId);

		AppendLine("[S8] 100 ticks, no event → no recomputation");
		var stable = new TacticalCoverOverlay();
		stable.BindCache(shared);
		CoverSituation stableSit = Situation(regionBounds.center, target, CoverMissionIntent.Hold);
		stable.Update(false, UnitAIState.Idle, in stableSit);
		int stableDecide = stable.Solver.DecideCount;
		int stableGen = shared.GenerationCount;
		for (int i = 0; i < 100; i++)
			stable.Update(false, UnitAIState.Idle, in stableSit);
		Check("S8_NoRecompute", stable.Solver.DecideCount == stableDecide,
			"count=" + stable.Solver.DecideCount);
		Check("S8_NoGenerate", shared.GenerationCount == stableGen,
			"gen=" + shared.GenerationCount);

		yield return null;
		Finish();
	}

	private static TacticalCoverDecision OverlayDecide(
		CoverCandidate[] _candidates,
		in CoverSituation _situation,
		float _cost)
	{
		var source = new ListSource();
		for (int i = 0; i < _candidates.Length; i++)
			source.Candidates.Add(_candidates[i]);
		var overlay = new TacticalCoverOverlay();
		overlay.BindCache(new SharedCoverSpatialCache(source));
		return overlay.Update(false, UnitAIState.Idle, in _situation, null, null, _cost);
	}

	private static CoverCandidate Make(
		int _id,
		Vector3 _position,
		Vector3 _normal,
		CoverType _type,
		float _prot)
	{
		return new CoverCandidate
		{
			CandidateId = _id,
			Position = _position,
			Normal = _normal,
			CoverType = _type,
			StandingValid = _type == CoverType.Standing,
			CrouchValid = true,
			NavMeshValid = true,
			StandingProfile = new CoverProtectionProfile
			{
				Head = _prot,
				Torso = _prot,
				Pelvis = _prot,
				Legs = _prot
			},
			CrouchProfile = new CoverProtectionProfile
			{
				Head = _prot,
				Torso = _prot,
				Pelvis = _prot,
				Legs = _prot
			},
			GeometryVersion = 1
		};
	}

	private static CoverSituation Situation(
		Vector3 _unit,
		Vector3 _target,
		CoverMissionIntent _mission)
	{
		Vector3 hostile = _target - _unit;
		hostile.y = 0f;
		if (hostile.sqrMagnitude < 0.0001f)
			hostile = Vector3.forward;
		return new CoverSituation
		{
			UnitPosition = _unit,
			Stance = CoverStance.Standing,
			Mission = _mission,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			TargetPosition = _target,
			HasTarget = true,
			SectorForward = Vector3.forward,
			HostileDirection = hostile,
			GeometryVersion = 1
		};
	}

	private static int FireUnits(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
	{
		int n = 0;
		for (int i = 0; i < _count; i++)
		{
			Vector3 pos = _anchor + Vector3.right * (i * 1.1f);
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(_cache);
			CoverSituation situation = Situation(pos, pos + new Vector3(0f, 1.5f, 20f), CoverMissionIntent.Hold);
			overlay.Update(false, UnitAIState.Idle, in situation);
			n++;
		}

		return n;
	}

	private Bounds SpawnArena()
	{
		DestroyArena();
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			s_Origin,
			CoverSpatialMath.DefaultRegionSizeMeters);
		Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
		Vector3 c = bounds.center;

		m_Arena = new GameObject("CoverTacticalArena");
		m_Arena.transform.position = c;
		NavMeshSurface surface = m_Arena.AddComponent<NavMeshSurface>();
		surface.agentTypeID = 0;
		surface.collectObjects = CollectObjects.Children;
		surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
		surface.ignoreNavMeshAgent = true;
		surface.ignoreNavMeshObstacle = true;
		surface.minRegionArea = 0.5f;
		m_Arena.AddComponent<CoverCandidateDebugDraw>();

		CreateBox("Ground", c + new Vector3(0f, -0.1f, 0f), new Vector3(22f, 0.2f, 22f));
		CreateBox("TestWall_B", c + new Vector3(0f, 1.1f, 5.5f), new Vector3(8f, 2.2f, 0.4f));
		CreateBox("TestWall_A", c + new Vector3(-5.5f, 0.575f, 0f), new Vector3(0.4f, 1.15f, 8f));
		CreateBox("WallR2", c + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 1.1f, 0f), new Vector3(6f, 2.2f, 0.4f));
		CreateBox("WallR3", c + new Vector3(0f, 1.1f, CoverSpatialMath.DefaultRegionSizeMeters), new Vector3(0.4f, 2.2f, 6f));
		return bounds;
	}

	private void CreateBox(string _name, Vector3 _world, Vector3 _lossyScale)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(m_Arena.transform, true);
		go.transform.position = _world;
		go.transform.localScale = _lossyScale;
	}

	private void Check(string _id, bool _pass, string _detail)
	{
		if (_pass)
		{
			m_PassCount++;
			AppendLine("PASS " + _id);
			return;
		}

		m_FailCount++;
		AppendLine("FAIL " + _id + " " + _detail);
	}

	private void DestroyArena()
	{
		if (m_Unit != null)
		{
			Destroy(m_Unit);
			m_Unit = null;
		}

		if (m_Arena == null)
			return;
		Destroy(m_Arena);
		m_Arena = null;
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine("RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
		           " pass=" + m_PassCount + " fail=" + m_FailCount);
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "CoverTactical_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverTactical] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverTactical;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
