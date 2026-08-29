using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14C.1 Play: Expected cover preference, Visual override, Stay Committed, facing deadband.
/// Report: Assets/_Docs/Logs/Tests/ThreatDirectionCover_LAST.txt
/// Does not replace ThreatDirection_LAST.txt (14C.0–14C.6).
/// </summary>
[DefaultExecutionOrder(68)]
[DisallowMultipleComponent]
public sealed class ThreatDirectionCoverRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private static readonly Vector3 s_North = Vector3.forward;
	private static readonly Vector3 s_East = Vector3.right;
	private static readonly Vector3 s_South = Vector3.back;
	private static readonly Vector3 s_NorthEast = new Vector3(1f, 0f, 1f).normalized;
	private static readonly Vector3 s_NorthWest = new Vector3(-1f, 0f, 1f).normalized;
	private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
	private static readonly Vector3 s_Near = new Vector3(1.5f, 0f, 0f);

	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunThreatDirectionCover;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunThreatDirectionCover)
			return;
		if (FindAnyObjectByType<ThreatDirectionCoverRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ThreatDirectionCoverRuntimeSmoke");
		go.AddComponent<ThreatDirectionCoverRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunThreatDirectionCover)
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
		AppendLine("STAGE 14C.1 — THREAT DIRECTION COVER ORIENTATION & FACING");
		AppendLine("=========================================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Expected → cover preference + facing. Visual replaces Expected. Stay Committed. Not CoverScore / 0.60.");
		AppendLine("---");

		RunScenarioA();
		RunScenarioB();
		RunScenarioC();
		RunStayCommitted();
		RunFacing();
		RunNoPolling();
		RunIndependence();
		RunLogs();

		Finish();
		yield break;
	}

	private void RunScenarioA()
	{
		AppendLine("[A] Spawn Expected North → cover closing North + facing North");
		CoverEvaluationResult result = Evaluate(
			StandingCover(1, s_CoverPos, s_North),
			StandingCover(2, s_CoverPos, s_South),
			ExpectedNorth(),
			false);
		Check("P1_ExpectedPrefersNorthCover",
			result.HasBest && result.Best.Candidate.CandidateId == 1,
			"best=" + (result.HasBest ? result.Best.Candidate.CandidateId.ToString() : "none"));
		Check("P2_ExpectedAdjustmentBonus",
			result.Best.ThreatDirectionAdjustment >=
			ThreatDirectionCoverMath.WeightedAdjustment(
				s_North,
				s_North,
				ThreatDirectionMath.ExpectedConfidence) - 0.001f,
			"adj=" + result.Best.ThreatDirectionAdjustment);

		var facing = new ThreatDirectionFacingController();
		Check("P3_FacingNorth",
			facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f) &&
			facing.DesiredFacing.z > 0.9f,
			"facing=" + facing.DesiredFacing);
	}

	private void RunScenarioB()
	{
		AppendLine("[B] Visual NorthEast replaces Expected");
		CoverCandidate west = StandingCover(1, s_CoverPos, s_NorthWest);
		CoverCandidate east = StandingCover(2, s_CoverPos, s_NorthEast);
		CoverEvaluationResult expected = Evaluate(west, east, ExpectedNorth(), true);
		CoverEvaluationResult visual = Evaluate(west, east, VisualNorthEast(), true);
		Check("P4_ExpectedPicksNorthWestById",
			expected.HasBest && expected.Best.Candidate.CandidateId == 1,
			"best=" + (expected.HasBest ? expected.Best.Candidate.CandidateId.ToString() : "none"));
		Check("P5_VisualPrefersNorthEastCover",
			visual.HasBest && visual.Best.Candidate.CandidateId == 2,
			"best=" + (visual.HasBest ? visual.Best.Candidate.CandidateId.ToString() : "none"));

		var facing = new ThreatDirectionFacingController();
		facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
		Check("P6_FacingUpdatesToNorthEast",
			facing.Notify(VisualNorthEast(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f) &&
			facing.DesiredFacing.x > 0.5f,
			"facing=" + facing.DesiredFacing);
	}

	private void RunScenarioC()
	{
		AppendLine("[C] Lost contact → Stale NE → Expected North. Occupied cover not forced out.");
		var knowledge = new ThreatDirectionController();
		knowledge.ApplyBattleStart(Vector3.zero, new Vector3(0f, 0f, 10f), 0f);
		knowledge.ApplyHostileVisible(Vector3.zero, new Vector3(10f, 0f, 10f), 1f);
		Check("P7_VisualKnownNorthEast",
			knowledge.CurrentState == ThreatDirectionState.Known &&
			knowledge.GetThreatCompass() == ThreatDirectionCompass.NorthEast,
			knowledge.CurrentState + " " + knowledge.GetThreatCompass());
		Check("P8_LostStaleNorthEast",
			knowledge.ApplyHostileLost(2f) &&
			knowledge.CurrentState == ThreatDirectionState.Stale &&
			knowledge.GetThreatCompass() == ThreatDirectionCompass.NorthEast,
			knowledge.CurrentState + " " + knowledge.GetThreatCompass());
		knowledge.Tick(2f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.1f);
		Check("P9_FallbackExpectedNorth",
			knowledge.CurrentState == ThreatDirectionState.Expected &&
			knowledge.GetThreatCompass() == ThreatDirectionCompass.North,
			knowledge.CurrentState + " " + knowledge.GetThreatCompass());

		CoverCandidate current = StandingCover(1, Vector3.zero, s_NorthWest);
		CoverCandidate other = StandingCover(2, s_Near, s_NorthEast);
		CoverSituation visual = Isolated(VisualNorthEast(), true);
		visual.UnitPosition = Vector3.zero;
		TacticalCoverDecision stay = new TacticalCoverSolver().Decide(
			in visual,
			new[] { current, other },
			CurrentTacticalPosition.FromCandidate(current, true));
		Check("P10_StaleOrVisual_NoForcedSwap",
			stay.Decision == TacticalCoverDecisionKind.Stay &&
			stay.Reason == TacticalCoverReason.Committed &&
			stay.SelectedCandidateId == 1,
			stay.Decision + " " + stay.Reason + " sel=" + stay.SelectedCandidateId);
	}

	private void RunStayCommitted()
	{
		AppendLine("[Stay] Occupied + threat N→NE does not Reposition");
		CoverCandidate current = StandingCover(1, Vector3.zero, s_NorthWest);
		CoverCandidate other = StandingCover(2, s_Near, s_NorthEast);
		CoverSituation north = Isolated(ExpectedNorth(), true);
		north.UnitPosition = Vector3.zero;
		CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
		var solver = new TacticalCoverSolver();
		TacticalCoverDecision first = solver.Decide(in north, new[] { current, other }, in occupying);
		CoverSituation visual = Isolated(VisualNorthEast(), true);
		visual.UnitPosition = Vector3.zero;
		TacticalCoverDecision second = solver.Decide(in visual, new[] { current, other }, in occupying);
		Check("P11_StayCommittedAfterDirectionChange",
			first.Decision == TacticalCoverDecisionKind.Stay &&
			second.Decision == TacticalCoverDecisionKind.Stay &&
			second.SelectedCandidateId == 1 &&
			!second.HasDestination,
			second.Decision + " sel=" + second.SelectedCandidateId);
		Check("P12_DirectionChangeIsEvent",
			solver.DecideCount == 2,
			"decide=" + solver.DecideCount);
	}

	private void RunFacing()
	{
		AppendLine("[Facing] Deadband, no per-tick spin");
		var facing = new ThreatDirectionFacingController();
		facing.Notify(ExpectedNorth(), ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
		ThreatDirectionKnowledge slight = new ThreatDirectionKnowledge(
			new Vector3(Mathf.Sin(10f * Mathf.Deg2Rad), 0f, Mathf.Cos(10f * Mathf.Deg2Rad)),
			ThreatDirectionCompass.North,
			ThreatDirectionMath.ExpectedConfidence,
			ThreatDirectionMath.ExpectedUncertaintyDegrees,
			0f,
			ThreatDirectionSource.InitialEstimate,
			ThreatDirectionState.Expected);
		Check("P13_FacingDeadband",
			!facing.Notify(in slight, ThreatDirectionFacingReason.ThreatDirectionChanged, 0f) &&
			facing.UpdateCount == 1,
			"updates=" + facing.UpdateCount);
		Check("P14_FacingDeadbandDegrees",
			ThreatDirectionFacingController.DeadbandDegrees >= 12f,
			"deadband=" + ThreatDirectionFacingController.DeadbandDegrees);
	}

	private void RunNoPolling()
	{
		AppendLine("[Events] Empty ticks do not rewrite direction or facing");
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(Vector3.zero, new Vector3(0f, 0f, 10f), 0f);
		int threatLogs = controller.LogCount;
		Vector3 dir = controller.GetThreatDirection();
		for (int i = 0; i < 40; i++)
			controller.Tick(i * 0.05f);
		Check("P15_NoPollingThreatDirection",
			controller.LogCount == threatLogs &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North &&
			Mathf.Abs(dir.z - controller.GetThreatDirection().z) < 0.001f,
			"logs=" + controller.LogCount);

		var facing = new ThreatDirectionFacingController();
		ThreatDirectionKnowledge expected = ExpectedNorth();
		facing.Notify(in expected, ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
		int facingLogs = facing.LogCount;
		for (int i = 0; i < 40; i++)
			facing.Notify(in expected, ThreatDirectionFacingReason.ThreatDirectionChanged, 0f);
		Check("P16_NoPollingFacing",
			facing.LogCount == facingLogs && facing.UpdateCount == 1,
			"facingLogs=" + facing.LogCount);
	}

	private void RunIndependence()
	{
		AppendLine("[Independence] CoverScore / 0.60 / Search / Readiness / Fire");
		CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
		CoverSituation bare = Isolated(default, false);
		bare.HasThreatDirection = false;
		CoverSituation bound = Isolated(ExpectedNorth(), false);
		Check("P17_CoverScoreUnchanged",
			Mathf.Abs(
				CoverScoreMath.EvaluateOne(cover, in bare, null).Score -
				CoverScoreMath.EvaluateOne(cover, in bound, null).Score) < 0.0001f,
			"score changed");
		Check("P18_AcquireUnchanged",
			Mathf.Abs(TacticalArrivalMath.DefaultAcquireToleranceMeters - 0.6f) < 0.0001f,
			"acquire=" + TacticalArrivalMath.DefaultAcquireToleranceMeters);
		Check("P19_SearchUntouched",
			!TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false),
			"search tactical allowed");

		var readiness = new ReadinessController();
		readiness.Reset(ReadinessRankKind.Soldier, 0f);
		ReadinessState before = readiness.CurrentState;
		new ThreatDirectionFacingController().Notify(
			VisualNorthEast(),
			ThreatDirectionFacingReason.ThreatDirectionChanged,
			0f);
		Check("P20_ReadinessUnchanged",
			readiness.CurrentState == before && readiness.CurrentState == ReadinessState.Patrol,
			readiness.CurrentState.ToString());
	}

	private void RunLogs()
	{
		AppendLine("[Logs] COVER_DIRECTION / FACING_DIRECTION event format");
		string cover = ThreatDirectionCoverLog.FormatCover(ExpectedNorth(), 3, 0.85f);
		string facing = ThreatDirectionCoverLog.FormatFacing(VisualNorthEast());
		Check("P21_CoverDirectionLog",
			cover.IndexOf("source=Expected", StringComparison.Ordinal) >= 0 &&
			cover.IndexOf("dir=N", StringComparison.Ordinal) >= 0 &&
			cover.IndexOf("cover=C3", StringComparison.Ordinal) >= 0,
			cover);
		Check("P22_FacingDirectionLog",
			facing.IndexOf("dir=NE", StringComparison.Ordinal) >= 0 &&
			facing.IndexOf("source=Visual", StringComparison.Ordinal) >= 0,
			facing);
	}

	private static CoverEvaluationResult Evaluate(
		CoverCandidate _a,
		CoverCandidate _b,
		ThreatDirectionKnowledge _knowledge,
		bool _northLook)
	{
		CoverSituation situation = Isolated(_knowledge, _northLook);
		return new CoverPositionEvaluator().Evaluate(new[] { _a, _b }, in situation);
	}

	private static CoverSituation Isolated(ThreatDirectionKnowledge _knowledge, bool _northLook)
	{
		var situation = new CoverSituation
		{
			UnitPosition = Vector3.zero,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			HasTarget = false,
			SectorForward = _northLook ? s_North : s_East,
			HostileDirection = _northLook ? s_North : s_East,
			GeometryVersion = 1
		};
		ThreatDirectionCoverMath.Bind(ref situation, in _knowledge);
		return situation;
	}

	private static ThreatDirectionKnowledge ExpectedNorth()
	{
		return new ThreatDirectionKnowledge(
			s_North,
			ThreatDirectionCompass.North,
			ThreatDirectionMath.ExpectedConfidence,
			ThreatDirectionMath.ExpectedUncertaintyDegrees,
			0f,
			ThreatDirectionSource.InitialEstimate,
			ThreatDirectionState.Expected);
	}

	private static ThreatDirectionKnowledge VisualNorthEast()
	{
		return new ThreatDirectionKnowledge(
			s_NorthEast,
			ThreatDirectionCompass.NorthEast,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static CoverCandidate StandingCover(int _id, Vector3 _position, Vector3 _normal)
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
				Head = 1f,
				Torso = 1f,
				Pelvis = 1f,
				Legs = 1f
			},
			CrouchProfile = new CoverProtectionProfile
			{
				Head = 1f,
				Torso = 1f,
				Pelvis = 1f,
				Legs = 1f
			},
			GeometryVersion = 1
		};
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
		string path = Path.Combine(dir, "ThreatDirectionCover_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[ThreatDirectionCover] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunThreatDirectionCover;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
