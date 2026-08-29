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
/// #13.6 Play: occupancy / reservation. Not Move. Not Group. Not geometry regen.
/// Report: Assets/_Docs/Logs/Tests/CoverOccupancy_LAST.txt
/// </summary>
[DefaultExecutionOrder(66)]
[DisallowMultipleComponent]
public sealed class CoverOccupancyRuntimeSmoke : MonoBehaviour
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
	private static readonly Vector3 s_Origin = new Vector3(7800f, 0f, 7800f);
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
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverOccupancy;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverOccupancy)
			return;
		if (FindAnyObjectByType<CoverOccupancyRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverOccupancyRuntimeSmoke");
		go.AddComponent<CoverOccupancyRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverOccupancy)
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
		AppendLine("STAGE 13.6 — OCCUPANCY / RESERVATION");
		AppendLine("====================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Available / Reserved / Occupied. Not Move. Not Group. OccupancyVersion ≠ GeometryVersion.");
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
		Check("S0_NavMeshBake", surface.navMeshData != null && sampled, "sample=" + sampled);

		Vector3 unit = regionBounds.center;
		Vector3 target = unit + new Vector3(0f, 1.5f, 40f);
		CoverCandidate c1 = Make(1, unit + new Vector3(3f, 0f, 0f), Vector3.forward, 1f);
		CoverCandidate c2 = Make(2, unit + new Vector3(6f, 0f, 0f), Vector3.forward, 1f);

		AppendLine("[S1] Two soldiers, one cover — first reserves, second takes C2");
		var board = new CoverOccupancyBoard();
		CoverSituation sitA = Situation(unit, target, 11);
		CoverSituation sitB = Situation(unit, target, 12);
		CoverReserveOutcome a = board.TryReserve(c1, 11, Time.time);
		sitB.OccupancyVersion = board.OccupancyVersion;
		CoverEvaluationResult evalB = new CoverPositionEvaluator().Evaluate(
			new[] { c1, c2 }, in sitB, null, board);
		Check("S1_AReserved", a.Success && board.GetState(c1, Time.time) == CoverOccupancy.Reserved,
			"state=" + board.GetState(c1, Time.time));
		Check("S1_BPicksC2", evalB.HasBest && evalB.Best.Candidate.CandidateId == 2,
			"id=" + (evalB.HasBest ? evalB.Best.Candidate.CandidateId : 0));
		Check("S1_ScoreNotZero", CoverScoreMath.PositionScore(c1, in sitB, null) > 0f, "zeroed");

		AppendLine("[S2] Release then B can take C1");
		board.Release(c1, 11, Time.time, CoverReservationReason.CommandChanged);
		CoverReserveOutcome bTakes = board.TryReserve(c1, 12, Time.time);
		Check("S2_BGetsC1", bTakes.Success, "result=" + bTakes.Result);

		AppendLine("[S3] Death releases");
		board.ReleaseUnit(12, Time.time, CoverReservationReason.Death);
		Check("S3_Available", board.IsAvailable(c1, Time.time), "still held");

		AppendLine("[S4] Emergency skips reserved, takes free");
		board.TryReserve(c1, 11, Time.time);
		CoverSituation threat = Situation(unit, target, 12);
		threat.OccupancyVersion = board.OccupancyVersion;
		EmergencyCoverDecision emergency = new EmergencyCoverSolver().Decide(
			true, UnitAIState.Idle, in threat, new[] { c1, c2 }, null, null, board);
		Check("S4_SkipsReserved", emergency.SelectedCandidateId == 2,
			"id=" + emergency.SelectedCandidateId);

		AppendLine("[S5] 20 units / 3 regions → 3 geometry; occupancy does not regenerate");
		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var generator = new CoverCandidateGenerator(
			new PhysicsCoverGeometrySource(),
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe());
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(generator);
		CoverOccupancyBoard fireBoard = new CoverOccupancyBoard();
		int fired = 0;
		fired += FireUnits(cache, fireBoard, regionBounds.center, 7);
		fired += FireUnits(cache, fireBoard, regionBounds.center + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f), 7);
		fired += FireUnits(cache, fireBoard, regionBounds.center + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters), 6);
		int gen = cache.GenerationCount;
		int occupancyBefore = fireBoard.OccupancyVersion;
		fireBoard.TryReserve(Make(99, unit, Vector3.forward, 1f), 50, Time.time);
		Check("S5_Generations", gen == 3, "gen=" + gen);
		Check("S5_Units", fired == 20, "n=" + fired);
		Check("S5_NoRegenOnReserve", cache.GenerationCount == gen, "gen=" + cache.GenerationCount);
		Check("S5_OccupancyVersionMoved", fireBoard.OccupancyVersion > occupancyBefore,
			"occ=" + fireBoard.OccupancyVersion);
		Check("S5_VersionsDiffer", fireBoard.OccupancyVersion != cache.GeometryVersion,
			"occ=" + fireBoard.OccupancyVersion + " geo=" + cache.GeometryVersion);

		AppendLine("[S6] 100 simultaneous TryReserve → 1 winner");
		var race = new CoverOccupancyBoard();
		int win = 0;
		int lose = 0;
		for (int i = 1; i <= 100; i++)
		{
			if (race.TryReserve(c1, i, Time.time).Success)
				win++;
			else
				lose++;
		}

		Check("S6_OneWinner", win == 1 && lose == 99, "win=" + win + " lose=" + lose);

		AppendLine("[S7] Overlay AVAILABLE / RESERVED + idle does not Walk");
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();
		debug.CaptureOccupancy(new[] { c1, c2 }, board, Time.time);
		Check("S7_OccupancySamples", debug.OccupancySampleCount >= 2, "n=" + debug.OccupancySampleCount);
		Check("S7_ReservedLabel", debug.TryGetOccupancyLabel(1, out string label) && label.Contains("RESERVED"),
			"label=" + label);

		m_Unit = new GameObject("OccupancyUnit");
		m_Unit.transform.position = unit;
		UnitAIController ai = m_Unit.AddComponent<UnitAIController>();
		ai.BindCoverCache(cache);
		ai.BindCoverOccupancy(board);
		ai.Tick(0.05f);
		Check("S7_Idle", ai.CurrentState == UnitAIState.Idle, "state=" + ai.CurrentState);
		Check("S7_NoWalk", !ai.TacticalNavigationIssued && !ai.SearchHasMoveIntent,
			"issued=" + ai.TacticalNavigationIssued);

		AppendLine("[S8] ConfirmOccupied API (not Movement)");
		CoverReserveOutcome reserved = board.TryReserve(c2, 21, Time.time);
		CoverReserveOutcome occupied = board.ConfirmOccupied(c2, 21, Time.time);
		Check("S8_Reserved", reserved.Success, "r=" + reserved.Result);
		Check("S8_Occupied", occupied.Success && board.GetState(c2, Time.time) == CoverOccupancy.Occupied,
			"state=" + board.GetState(c2, Time.time));

		yield return null;
		Finish();
	}

	private static CoverCandidate Make(int _id, Vector3 _position, Vector3 _normal, float _prot)
	{
		return new CoverCandidate
		{
			CandidateId = _id,
			Position = _position,
			Normal = _normal,
			CoverType = CoverType.Standing,
			StandingValid = true,
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
			GeometryVersion = 1,
			Occupancy = CoverOccupancy.Available
		};
	}

	private static CoverSituation Situation(Vector3 _unit, Vector3 _target, int _unitId)
	{
		Vector3 hostile = _target - _unit;
		hostile.y = 0f;
		if (hostile.sqrMagnitude < 0.0001f)
			hostile = Vector3.forward;
		return new CoverSituation
		{
			UnitPosition = _unit,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			TargetPosition = _target,
			HasTarget = true,
			SectorForward = Vector3.forward,
			HostileDirection = hostile,
			GeometryVersion = 1,
			UnitId = _unitId
		};
	}

	private static int FireUnits(
		SharedCoverSpatialCache _cache,
		CoverOccupancyBoard _board,
		Vector3 _anchor,
		int _count)
	{
		int n = 0;
		for (int i = 0; i < _count; i++)
		{
			Vector3 pos = _anchor + Vector3.right * (i * 1.1f);
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(_cache);
			overlay.BindOccupancy(_board);
			CoverSituation situation = Situation(pos, pos + new Vector3(0f, 1.5f, 20f), 100 + i);
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

		m_Arena = new GameObject("CoverOccupancyArena");
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
		string path = Path.Combine(dir, "CoverOccupancy_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverOccupancy] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverOccupancy;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
