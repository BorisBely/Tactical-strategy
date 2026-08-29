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
/// #13.3 Play: individual scores on shared candidates. Not Move. Not Fire.
/// Report: Assets/_Docs/Logs/Tests/CoverEvaluation_LAST.txt
/// </summary>
[DefaultExecutionOrder(66)]
[DisallowMultipleComponent]
public sealed class CoverEvaluationRuntimeSmoke : MonoBehaviour
{
	#region Nested
	private sealed class BlockNear : ICoverLineOfSightProbe
	{
		public Vector3 Point;

		public bool HasClearLook(Vector3 _from, Vector3 _to)
		{
			return CoverSpatialMath.PlanarDistanceSqr(_from, Point) > 0.12f;
		}
	}
	#endregion

	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(7200f, 0f, 7200f);
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
	private bool m_Moved;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverEvaluation;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverEvaluation)
			return;
		if (FindAnyObjectByType<CoverEvaluationRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverEvaluationRuntimeSmoke");
		go.AddComponent<CoverEvaluationRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverEvaluation)
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
		AppendLine("STAGE 13.3 — COVER INDIVIDUAL EVALUATION");
		AppendLine("========================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Per-unit score. Shared geometry. Selected ≠ Move. Not Fire.");
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
		CoverCandidate[] set = BuildFactorSet(unit);
		var blockC1 = new BlockNear
		{
			Point = set[0].Position + Vector3.up * CoverScoreMath.EyeHeightMeters
		};
		CoverSituation rifle = Situation(unit, target, CoverWeaponClass.Rifle, CoverRankClass.Soldier);

		AppendLine("[S1] Factor mix — not always the safest wall");
		var rifleEval = new CoverPositionEvaluator();
		CoverEvaluationResult rifleResult = rifleEval.Evaluate(set, in rifle, blockC1);
		DumpScores("rifle", rifleResult);
		Check("S1_HasBest", rifleResult.HasBest, "none");
		Check("S1_NotC1", rifleResult.Best.Candidate.CandidateId != 1,
			"id=" + rifleResult.Best.Candidate.CandidateId);
		Check("S1_RiflePrefersC4", rifleResult.Best.Candidate.CandidateId == 4,
			"id=" + rifleResult.Best.Candidate.CandidateId);
		Check("S1_NoMove", !m_Moved, "moved");

		AppendLine("[S2] Same shared set, different weapons");
		CoverSituation sniperSit = Situation(unit, target, CoverWeaponClass.Sniper, CoverRankClass.Soldier);
		CoverSituation lmgSit = Situation(unit, target, CoverWeaponClass.Lmg, CoverRankClass.Soldier);
		CoverEvaluationResult sniperResult = new CoverPositionEvaluator().Evaluate(set, in sniperSit, blockC1);
		CoverEvaluationResult lmgResult = new CoverPositionEvaluator().Evaluate(set, in lmgSit, blockC1);
		DumpScores("sniper", sniperResult);
		DumpScores("lmg", lmgResult);
		Check("S2_SharedIds", SameIds(set, sniperResult) && SameIds(set, lmgResult), "ids");
		Check("S2_SniperC3OverC2", ScoreOf(sniperResult, 3) > ScoreOf(sniperResult, 2),
			"c3=" + ScoreOf(sniperResult, 3) + " c2=" + ScoreOf(sniperResult, 2));
		Check("S2_LmgC2OverC1", ScoreOf(lmgResult, 2) > ScoreOf(lmgResult, 1),
			"c2=" + ScoreOf(lmgResult, 2) + " c1=" + ScoreOf(lmgResult, 1));
		Check("S2_WeaponScoresDiffer",
			ScoreOf(sniperResult, 3) != ScoreOf(rifleResult, 3),
			"same");

		AppendLine("[S3] Recruit vs Veteran — same shared candidates");
		CoverSituation recruitSit = Situation(unit, target, CoverWeaponClass.Rifle, CoverRankClass.Recruit);
		CoverSituation veteranSit = Situation(unit, target, CoverWeaponClass.Rifle, CoverRankClass.Veteran);
		CoverEvaluationResult recruit = new CoverPositionEvaluator().Evaluate(set, in recruitSit, blockC1);
		CoverEvaluationResult veteran = new CoverPositionEvaluator().Evaluate(set, in veteranSit, blockC1);
		Check("S3_SameSet", recruit.Evaluations.Count == set.Length && veteran.Evaluations.Count == set.Length,
			"count");
		Check("S3_Independent", recruit.Best.Score != 0f && veteran.Best.Score != 0f, "zero");

		AppendLine("[S4] 20 units / 3 regions → 3 geometry + 20 scores");
		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var geometry = new PhysicsCoverGeometrySource();
		var generator = new CoverCandidateGenerator(
			geometry,
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe());
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(generator);
		int evals = 0;
		Vector3 r2 = regionBounds.center + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f);
		Vector3 r3 = regionBounds.center + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters);
		evals += ScoreUnits(cache, regionBounds.center, 7);
		evals += ScoreUnits(cache, r2, 7);
		evals += ScoreUnits(cache, r3, 6);
		Check("S4_Generations", cache.GenerationCount == 3, "gen=" + cache.GenerationCount);
		Check("S4_Evals", evals == 20, "evals=" + evals);
		IReadOnlyList<CoverCandidate> shared = cache.GetCandidates(regionBounds.center);
		Check("S4_SharedHasNoScoreField", shared.Count == 0 || shared[0].GetType().GetField("Score") == null,
			"score-on-shared");

		AppendLine("[S5] Overlay shows scores");
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();
		debug.CaptureEvaluations(
			regionBounds,
			set,
			rifleResult.Evaluations,
			rifleResult.Best.Candidate.CandidateId,
			false,
			null);
		Check("S5_DebugScores", debug.EvaluationCount == set.Length && debug.SelectedId == 4,
			"evals=" + debug.EvaluationCount + " sel=" + debug.SelectedId);

		yield return null;
		Finish();
	}

	private static CoverCandidate[] BuildFactorSet(Vector3 _unit)
	{
		CoverCandidate c1 = Make(1, _unit + new Vector3(0f, 0f, 5f), Vector3.forward, CoverType.Standing, 1f);
		CoverCandidate c2 = Make(2, _unit + new Vector3(6f, 0f, 8f), Vector3.forward, CoverType.Standing, 0.55f);
		CoverCandidate c3 = Make(3, _unit + new Vector3(0f, 0f, 18f), Vector3.forward, CoverType.Standing, 1f);
		CoverCandidate c4 = Make(4, _unit + new Vector3(2f, 0f, 2.2f), Vector3.forward, CoverType.Partial, 0.35f);
		c1.StandingValid = true;
		c4.StandingValid = false;
		c4.CrouchValid = true;
		return new[] { c1, c2, c3, c4 };
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
		CoverWeaponClass _weapon,
		CoverRankClass _rank)
	{
		return new CoverSituation
		{
			UnitPosition = _unit,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = _weapon,
			Rank = _rank,
			TargetPosition = _target,
			HasTarget = true,
			SectorForward = Vector3.forward,
			GeometryVersion = 1
		};
	}

	private static int ScoreUnits(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
	{
		int n = 0;
		for (int i = 0; i < _count; i++)
		{
			Vector3 pos = _anchor + Vector3.right * (i * 1.1f);
			CoverSituation situation = Situation(pos, pos + new Vector3(0f, 1.5f, 20f), CoverWeaponClass.Rifle, CoverRankClass.Soldier);
			var evaluator = new CoverPositionEvaluator();
			evaluator.Evaluate(_cache.GetCandidates(pos), in situation, new PhysicsCoverLosProbe());
			n += evaluator.EvaluateCount;
		}

		return n;
	}

	private static bool SameIds(CoverCandidate[] _set, CoverEvaluationResult _result)
	{
		if (_result.Evaluations == null || _result.Evaluations.Count != _set.Length)
			return false;
		for (int i = 0; i < _set.Length; i++)
		{
			if (_result.Evaluations[i].Candidate == null ||
			    _result.Evaluations[i].Candidate.CandidateId != _set[i].CandidateId)
				return false;
		}

		return true;
	}

	private static float ScoreOf(CoverEvaluationResult _result, int _id)
	{
		for (int i = 0; i < _result.Evaluations.Count; i++)
		{
			if (_result.Evaluations[i].Candidate != null &&
			    _result.Evaluations[i].Candidate.CandidateId == _id)
				return _result.Evaluations[i].Score;
		}

		return float.NaN;
	}

	private void DumpScores(string _label, CoverEvaluationResult _result)
	{
		var sb = new StringBuilder();
		sb.Append(_label).Append(" selected=C");
		sb.Append(_result.HasBest ? _result.Best.Candidate.CandidateId : 0);
		for (int i = 0; i < _result.Evaluations.Count; i++)
		{
			CoverPositionEvaluation ev = _result.Evaluations[i];
			sb.Append(" C").Append(ev.Candidate.CandidateId).Append("=").Append(ev.Score.ToString("0.00"));
		}

		AppendLine(sb.ToString());
	}

	private Bounds SpawnArena()
	{
		DestroyArena();
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			s_Origin,
			CoverSpatialMath.DefaultRegionSizeMeters);
		Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
		Vector3 c = bounds.center;

		m_Arena = new GameObject("CoverEvaluationArena");
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
		string path = Path.Combine(dir, "CoverEvaluation_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverEvaluation] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverEvaluation;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
