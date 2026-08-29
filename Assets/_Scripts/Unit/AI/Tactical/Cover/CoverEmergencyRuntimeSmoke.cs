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
/// #13.4 Play: ImmediateThreat overlay picks a hide destination. Not Move. Not Fire.
/// Report: Assets/_Docs/Logs/Tests/CoverEmergency_LAST.txt
/// </summary>
[DefaultExecutionOrder(66)]
[DisallowMultipleComponent]
public sealed class CoverEmergencyRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(7400f, 0f, 7400f);
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
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverEmergency;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverEmergency)
			return;
		if (FindAnyObjectByType<CoverEmergencyRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverEmergencyRuntimeSmoke");
		go.AddComponent<CoverEmergencyRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverEmergency)
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
		AppendLine("STAGE 13.4 — EMERGENCY COVER");
		AppendLine("============================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("ImmediateThreat overlay. Destination ≠ Move. Not Fire. Not a new UnitAIState.");
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

		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var geometry = new PhysicsCoverGeometrySource();
		var generator = new CoverCandidateGenerator(
			geometry,
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe());
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(generator);
		Vector3 unit = regionBounds.center;
		Vector3 target = unit + new Vector3(0f, 1.5f, 40f);

		AppendLine("[S1] Open ground — destination set, no Walk");
		m_Unit = new GameObject("EmergencyUnit");
		m_Unit.transform.position = unit;
		UnitAIController ai = m_Unit.AddComponent<UnitAIController>();
		ai.BindCoverCache(cache);
		ai.ImmediateThreat = true;
		ai.Tick(0.05f);
		Check("S1_Idle", ai.CurrentState == UnitAIState.Idle, "state=" + ai.CurrentState);
		Check("S1_Active", ai.EmergencyCoverActive, "inactive");
		Check("S1_HasDest", ai.HasEmergencyCoverDestination, "result=" + ai.LastEmergencyCoverDecision.Result);
		Check("S1_NoWalk", !ai.TacticalNavigationIssued && !ai.SearchHasMoveIntent,
			"issued=" + ai.TacticalNavigationIssued);
		Check("S1_DestNotContext",
			!ai.CurrentContext.HasDestination ||
			ai.CurrentContext.Destination != ai.EmergencyCoverDestination,
			"ctx=" + ai.CurrentContext.Destination);

		AppendLine("[S2] Already behind good cover → Stay");
		IReadOnlyList<CoverCandidate> shared = cache.GetCandidates(unit);
		CoverCandidate occupying = BestProtected(shared, target);
		Check("S2_HasOccupying", occupying != null, "none");
		if (occupying != null)
		{
			CoverSituation atCover = Situation(occupying.Position, occupying.Position + new Vector3(0f, 1.5f, 20f),
				CoverWeaponClass.Rifle, CoverRankClass.Soldier);
			CoverCandidate fullCover = Make(
				99, occupying.Position, occupying.Normal, CoverType.Standing, 1f);
			EmergencyCoverDecision staySolver = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in atCover, null, fullCover);
			Check("S2_Stay", staySolver.Result == EmergencyCoverResult.Stay, "result=" + staySolver.Result);

			int genBefore = cache.GenerationCount;
			var stayOverlay = new EmergencyCoverOverlay();
			stayOverlay.BindCache(cache);
			EmergencyCoverDecision stay1 = stayOverlay.Update(true, UnitAIState.Idle, in atCover);
			EmergencyCoverDecision stay2 = stayOverlay.Update(true, UnitAIState.Idle, in atCover);
			Check("S2_HasDecision", stay1.Active && stay1.Result != EmergencyCoverResult.None,
				"result=" + stay1.Result);
			Check("S2_Reuse", stay2.Result == stay1.Result, "r1=" + stay1.Result + " r2=" + stay2.Result);
			Check("S2_NoSecondGenerate", cache.GenerationCount == genBefore,
				"gen=" + cache.GenerationCount + " before=" + genBefore);
		}

		AppendLine("[S3] Close-poor vs far-good — emergency profile");
		CoverCandidate closePoor = Make(1, unit + new Vector3(1f, 0f, 0f), Vector3.forward, CoverType.Standing, 0.12f);
		CoverCandidate farGood = Make(2, unit + new Vector3(12f, 0f, 0f), Vector3.forward, CoverType.Standing, 1f);
		CoverSituation rifle = Situation(unit, target, CoverWeaponClass.Rifle, CoverRankClass.Soldier);
		EmergencyCoverDecision profile = new EmergencyCoverSolver().Decide(
			true, UnitAIState.Idle, in rifle, new[] { closePoor, farGood }, null);
		Check("S3_FarWins", profile.SelectedCandidateId == 2, "id=" + profile.SelectedCandidateId);
		Check("S3_Selected", profile.Result == EmergencyCoverResult.Selected, "result=" + profile.Result);

		AppendLine("[S4] Shared list, independent scores");
		CoverCandidate[] set = BuildSharedSet(unit);
		CoverSituation sniperSit = Situation(unit, target, CoverWeaponClass.Sniper, CoverRankClass.Soldier);
		CoverSituation lmgSit = Situation(unit, target, CoverWeaponClass.Lmg, CoverRankClass.Soldier);
		CoverSituation recruitSit = Situation(unit, target, CoverWeaponClass.Rifle, CoverRankClass.Recruit);
		CoverSituation veteranSit = Situation(unit, target, CoverWeaponClass.Rifle, CoverRankClass.Veteran);
		float sniper = CoverEmergencyScoreMath.Score(set[2], in sniperSit, null);
		float lmg = CoverEmergencyScoreMath.Score(set[2], in lmgSit, null);
		float recruit = CoverEmergencyScoreMath.Score(set[0], in recruitSit, null);
		float veteran = CoverEmergencyScoreMath.Score(set[0], in veteranSit, null);
		Check("S4_IndependentWeapon", sniper != lmg, "same weapon score");
		Check("S4_IndependentRank", recruit != veteran, "same rank score");
		Check("S4_SharedIds", set[0].CandidateId == 1 && set[2].CandidateId == 3, "ids");

		AppendLine("[S5] 20 units / 3 regions / incoming fire → 3 geometry");
		SharedCoverSpatialCache fireCache = new SharedCoverSpatialCache(generator);
		int fired = 0;
		fired += FireUnits(fireCache, regionBounds.center, 7);
		fired += FireUnits(fireCache, regionBounds.center + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f), 7);
		fired += FireUnits(fireCache, regionBounds.center + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters), 6);
		Check("S5_Generations", fireCache.GenerationCount == 3, "gen=" + fireCache.GenerationCount);
		Check("S5_Units", fired == 20, "n=" + fired);

		AppendLine("[S6] Overlay Stay vs Selected");
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();
		debug.CaptureEmergency(
			regionBounds,
			new[] { closePoor, farGood },
			profile.Evaluations,
			profile.Result,
			profile.SelectedCandidateId,
			false,
			true);
		Check("S6_EmergencyActive", debug.EmergencyCoverActive, "off");
		Check("S6_Selected", debug.SelectedId == 2, "sel=" + debug.SelectedId);

		yield return null;
		Finish();
	}

	private static CoverCandidate[] BuildSharedSet(Vector3 _unit)
	{
		return new[]
		{
			Make(1, _unit + new Vector3(3f, 0f, 0f), Vector3.forward, CoverType.Standing, 0.6f),
			Make(2, _unit + new Vector3(18f, 0f, 0f), Vector3.forward, CoverType.Standing, 1f),
			Make(3, _unit + new Vector3(8f, 0f, 2f), Vector3.forward, CoverType.Standing, 0.8f)
		};
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

	private static CoverCandidate BestProtected(IReadOnlyList<CoverCandidate> _candidates, Vector3 _target)
	{
		CoverCandidate best = null;
		float bestProt = -1f;
		if (_candidates == null)
			return null;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (!CoverScoreMath.IsSelectable(candidate))
				continue;
			CoverSituation sit = Situation(candidate.Position, _target, CoverWeaponClass.Rifle, CoverRankClass.Soldier);
			float prot = CoverScoreMath.ProtectionScore(candidate, in sit);
			if (prot <= bestProt)
				continue;
			bestProt = prot;
			best = candidate;
		}

		return best;
	}

	private static int FireUnits(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
	{
		int n = 0;
		for (int i = 0; i < _count; i++)
		{
			Vector3 pos = _anchor + Vector3.right * (i * 1.1f);
			var overlay = new EmergencyCoverOverlay();
			overlay.BindCache(_cache);
			CoverSituation situation = Situation(pos, pos + new Vector3(0f, 1.5f, 20f),
				CoverWeaponClass.Rifle, CoverRankClass.Soldier);
			overlay.Update(true, UnitAIState.Idle, in situation);
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

		m_Arena = new GameObject("CoverEmergencyArena");
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
		string path = Path.Combine(dir, "CoverEmergency_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverEmergency] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverEmergency;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
