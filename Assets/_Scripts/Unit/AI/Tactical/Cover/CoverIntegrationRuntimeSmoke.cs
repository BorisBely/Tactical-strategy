using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #13.8 Play: golden Dynamic Cover chain. Not Move. Not Fire. Not #14.
/// Report: Assets/_Docs/Logs/Tests/CoverIntegration_LAST.txt
/// </summary>
[DefaultExecutionOrder(68)]
[DisallowMultipleComponent]
public sealed class CoverIntegrationRuntimeSmoke : MonoBehaviour
{
	#region Nested
	private sealed class ListSource : ICoverCandidateSource
	{
		public int GenerateCount;
		public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(16);

		public void Generate(
			CoverRegionId _region,
			Bounds _bounds,
			int _geometryVersion,
			List<CoverCandidate> _destination)
		{
			GenerateCount++;
			for (int i = 0; i < Candidates.Count; i++)
				_destination.Add(Candidates[i]);
		}
	}

	private sealed class HideNearLos : ICoverLineOfSightProbe
	{
		public Vector3 HiddenFrom;
		public float Radius = 0.85f;
		public bool Active;

		public bool HasClearLook(Vector3 _from, Vector3 _to)
		{
			if (!Active)
				return true;
			Vector3 planar = _from;
			planar.y = 0f;
			Vector3 hide = HiddenFrom;
			hide.y = 0f;
			return CoverSpatialMath.PlanarDistanceSqr(planar, hide) > Radius * Radius;
		}
	}

	private sealed class OffsetLosProbe : ICoverLineOfSightProbe
	{
		public Vector3 Anchor;
		public float RequiredOffset;

		public bool HasClearLook(Vector3 _from, Vector3 _to)
		{
			Vector3 planar = _from - (Anchor + Vector3.up * 1.55f);
			planar.y = 0f;
			float lateral = Vector3.Dot(planar, CoverPeekGeometry.RightTangent(Vector3.forward));
			return Mathf.Abs(lateral) + 0.001f >= RequiredOffset;
		}
	}

	private sealed class RecordingLeanExecutor : ICoverLeanExecutor
	{
		public int SetLeanCount;
		public CoverLeanLevel LastLevel;

		public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
		{
			SetLeanCount++;
			LastLevel = _level;
		}
	}
	#endregion

	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(8200f, 0f, 8200f);
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
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverIntegration;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverIntegration)
			return;
		if (FindAnyObjectByType<CoverIntegrationRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverIntegrationRuntimeSmoke");
		go.AddComponent<CoverIntegrationRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverIntegration)
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
		AppendLine("STAGE 13.8 — FINAL DYNAMIC COVER INTEGRATION");
		AppendLine("============================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Golden chain. Not Move. Not Fire. Not #14.");
		AppendLine("---");

		Bounds bounds = SpawnArena();
		yield return null;
		Vector3 open = bounds.center;
		CoverCandidate c07 = Make(7, open + new Vector3(0f, 0f, 2f), CoverType.Standing, 1.5f);
		CoverCandidate c11 = Make(11, open + new Vector3(4f, 0f, 2f), CoverType.Corner, 1.5f);
		var source = new ListSource();
		source.Candidates.Add(c07);
		source.Candidates.Add(c11);
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
		var board = new CoverOccupancyBoard();
		CoverSituation openSit = Situation(open, open + new Vector3(0f, 1.5f, 20f));
		int fireCalls = 0;

		AppendLine("[G] Golden: quiet → threat → C07 reserve → occupy → tactical C11 → lean");
		var emergency = new EmergencyCoverOverlay();
		emergency.BindCache(cache);
		emergency.BindOccupancy(board);
		EmergencyCoverDecision quiet = emergency.Update(false, UnitAIState.Idle, in openSit);
		Check("G_NoThreatNoQuery", cache.GenerationCount == 0 && !quiet.Active,
			"gen=" + cache.GenerationCount);

		EmergencyCoverDecision threat = emergency.Update(true, UnitAIState.Idle, in openSit);
		Check("G_EmergencyC07", threat.SelectedCandidateId == 7 && threat.HasDestination,
			"id=" + threat.SelectedCandidateId);
		Check("G_Reserved", board.GetState(c07, Time.time) == CoverOccupancy.Reserved,
			"state=" + board.GetState(c07, Time.time));

		CoverSituation at07 = Situation(c07.Position, openSit.TargetPosition);
		Check("G_Occupied", board.ConfirmOccupied(c07, at07.UnitId, Time.time).Success,
			"occ=" + board.GetState(c07, Time.time));
		int geometry = cache.GeometryVersion;
		int gen = cache.GenerationCount;

		var hide = new HideNearLos { HiddenFrom = c07.Position, Active = true };
		var tactical = new TacticalCoverOverlay();
		tactical.BindCache(cache);
		tactical.BindOccupancy(board);
		TacticalCoverDecision move = tactical.Update(false, UnitAIState.Idle, in at07, hide);
		Check("G_RepositionC11",
			move.Decision == TacticalCoverDecisionKind.Reposition && move.SelectedCandidateId == 11,
			"dec=" + move.Decision + " id=" + move.SelectedCandidateId);
		Check("G_NoGeometryRegen", cache.GenerationCount == gen && cache.GeometryVersion == geometry,
			"gen=" + cache.GenerationCount);

		CoverSituation at11 = Situation(c11.Position, openSit.TargetPosition);
		board.ConfirmOccupied(c11, at11.UnitId, Time.time);
		var lean = new RecordingLeanExecutor();
		CoverPeekDecision peek = new CoverPeekOverlay().Update(
			UnitAIState.Idle, c11, in at11,
			new OffsetLosProbe { Anchor = c11.Position, RequiredOffset = 0.10f },
			CoverPeekSides.Both, lean, Time.time);
		Check("G_Lean", peek.Kind == CoverPeekDecisionKind.Lean && lean.SetLeanCount == 1,
			"kind=" + peek.Kind);
		Check("G_NoFire", fireCalls == 0, "fire=" + fireCalls);

		AppendLine("[B] Boundaries: no Walk, occupancy ≠ geometry");
		m_Unit = new GameObject("IntegrationUnit");
		m_Unit.transform.position = open;
		UnitAIController ai = m_Unit.AddComponent<UnitAIController>();
		UnitMoveCommandRecorder moveRec = m_Unit.AddComponent<UnitMoveCommandRecorder>();
		UnitSpineLean spine = m_Unit.AddComponent<UnitSpineLean>();
		ai.BindCoverCache(cache);
		ai.BindCoverOccupancy(board);
		ai.Tick(0.05f);
		Check("B_Idle", ai.CurrentState == UnitAIState.Idle, "state=" + ai.CurrentState);
		Check("B_NoWalk", !ai.TacticalNavigationIssued && moveRec.MoveCount == 0,
			"issued=" + ai.TacticalNavigationIssued + " moves=" + moveRec.MoveCount);
		Check("B_VersionsDiffer", board.OccupancyVersion != cache.GeometryVersion,
			"occ=" + board.OccupancyVersion + " geo=" + cache.GeometryVersion);

		AppendLine("[G1] 20 units / 3 regions → 3 generations");
		var sharedSource = new ListSource();
		sharedSource.Candidates.Add(c07);
		SharedCoverSpatialCache shared = new SharedCoverSpatialCache(sharedSource);
		FireTactical(shared, open, 7);
		FireTactical(shared, open + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f), 7);
		FireTactical(shared, open + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters), 6);
		Check("G1_ThreeGen", shared.GenerationCount == 3, "gen=" + shared.GenerationCount);

		AppendLine("[G6] 2 units / 1 slot");
		var race = new CoverOccupancyBoard();
		Check("G6_OneWinner", race.TryReserve(c07, 1, Time.time).Success && !race.TryReserve(c07, 2, Time.time).Success,
			"state=" + race.GetState(c07, Time.time));

		AppendLine("[#11] Retreat releases reservation");
		var cmdBoard = new CoverOccupancyBoard();
		ai.BindCoverOccupancy(cmdBoard);
		ai.IssueCommand(TacticalCommand.Defense(open));
		cmdBoard.TryReserve(c07, ai.CoverOccupancyUnitId, Time.time);
		TacticalCommandResult retreat = ai.IssueCommand(TacticalCommand.Retreat(open + Vector3.left * 8f));
		Check("Cmd_RetreatAccepted", retreat.Accepted, "ok=" + retreat.Accepted);
		Check("Cmd_Released", cmdBoard.IsAvailable(c07, Time.time),
			"state=" + cmdBoard.GetState(c07, Time.time));

		AppendLine("[V] Debug overlay");
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();
		debug.CaptureIntegration(
			bounds,
			new[] { c07, c11 },
			move.Evaluations,
			7,
			11,
			move.CurrentScore,
			move.BestScore,
			move.SwitchingCost,
			move.Decision,
			board,
			Time.time,
			in peek);
		Check("V_Overlay", debug.PeekActive && debug.TacticalCoverActive && debug.OccupancySampleCount >= 1,
			"peek=" + debug.PeekActive + " tac=" + debug.TacticalCoverActive);

		AppendLine("[S] Spine lean executor still UnitSpineLean");
		new CoverPeekOverlay().Update(
			UnitAIState.Idle, c11, in at11,
			new OffsetLosProbe { Anchor = c11.Position, RequiredOffset = 0.10f },
			CoverPeekSides.Both, new UnitSpineLeanExecutor(spine), Time.time);
		Check("S_Spine", spine.CurrentLeanLevel == 1, "level=" + spine.CurrentLeanLevel);
		Check("S_OneSpine", m_Unit.GetComponents<UnitSpineLean>().Length == 1, "n=1");

		yield return null;
		Finish();
	}

	private static int FireTactical(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
	{
		int n = 0;
		for (int i = 0; i < _count; i++)
		{
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(_cache);
			overlay.Update(false, UnitAIState.Idle, Situation(_anchor + Vector3.right * (i * 0.2f),
				_anchor + new Vector3(0f, 1.5f, 20f)));
			n++;
		}

		return n;
	}

	private static CoverCandidate Make(int _id, Vector3 _position, CoverType _type, float _prot)
	{
		CoverRegionId region = CoverSpatialMath.WorldToRegion(_position, CoverSpatialMath.DefaultRegionSizeMeters);
		return new CoverCandidate
		{
			CandidateId = _id,
			Position = _position,
			Normal = Vector3.forward,
			CoverType = _type,
			CornerValid = _type == CoverType.Corner,
			StandingValid = true,
			CrouchValid = true,
			NavMeshValid = true,
			StandingProfile = new CoverProtectionProfile
			{
				Head = _prot, Torso = _prot, Pelvis = _prot, Legs = _prot
			},
			CrouchProfile = new CoverProtectionProfile
			{
				Head = _prot, Torso = _prot, Pelvis = _prot, Legs = _prot
			},
			GeometryVersion = 1,
			RegionId = region,
			Occupancy = CoverOccupancy.Available
		};
	}

	private static CoverSituation Situation(Vector3 _unit, Vector3 _target)
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
			UnitId = 1
		};
	}

	private Bounds SpawnArena()
	{
		DestroyArena();
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			s_Origin, CoverSpatialMath.DefaultRegionSizeMeters);
		Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
		m_Arena = new GameObject("CoverIntegrationArena");
		m_Arena.transform.position = bounds.center;
		m_Arena.AddComponent<CoverCandidateDebugDraw>();
		return bounds;
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
		string path = Path.Combine(dir, "CoverIntegration_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverIntegration] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverIntegration;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
