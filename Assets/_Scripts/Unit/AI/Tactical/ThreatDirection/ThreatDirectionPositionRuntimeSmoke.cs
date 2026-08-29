using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14C.3 Play: direction / confidence / uncertainty preference, Stay Committed, no Move.
/// Report: Assets/_Docs/Logs/Tests/ThreatDirectionPosition_LAST.txt
/// </summary>
[DefaultExecutionOrder(70)]
[DisallowMultipleComponent]
public sealed class ThreatDirectionPositionRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private static readonly Vector3 s_Origin = Vector3.zero;
	private static readonly Vector3 s_North = Vector3.forward;
	private static readonly Vector3 s_East = Vector3.right;
	private static readonly Vector3 s_South = Vector3.back;
	private static readonly Vector3 s_NorthEast = new Vector3(1f, 0f, 1f).normalized;
	private static readonly Vector3 s_NorthWest = new Vector3(-1f, 0f, 1f).normalized;
	private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
	private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
	private static readonly Vector3 s_NorthEastPoint = new Vector3(10f, 0f, 10f);

	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunThreatDirectionPosition;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunThreatDirectionPosition)
			return;
		if (FindAnyObjectByType<ThreatDirectionPositionRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ThreatDirectionPositionRuntimeSmoke");
		go.AddComponent<ThreatDirectionPositionRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunThreatDirectionPosition)
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
		AppendLine("STAGE 14C.3 — THREAT DIRECTION TACTICAL POSITIONING");
		AppendLine("===================================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("TacticalPositionPreference overlay. Event-driven. Not CoverScore / Move / Fire.");
		AppendLine("---");

		RunInitialAndVisual();
		RunConfidenceUncertainty();
		RunStayAndRecalc();
		RunIsolation();
		RunLogs();

		Finish();
		yield break;
	}

	private void RunInitialAndVisual()
	{
		AppendLine("[A] Expected North → Visual NE");
		CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
		CoverCandidate side = StandingCover(2, s_CoverPos, s_East);
		CoverCandidate open = StandingCover(3, s_CoverPos, s_South);
		CoverSituation equalLook = IsolatedEqualLook(ExpectedKnowledge());
		CoverEvaluationResult expected = new CoverPositionEvaluator().Evaluate(
			new[] { north, side, open },
			in equalLook);
		Check("P1_ExpectedPrefersNorth",
			expected.HasBest && expected.Best.Candidate.CandidateId == 1 && expected.Best.PositionAdjustment > 0f,
			"best=" + (expected.HasBest ? expected.Best.Candidate.CandidateId.ToString() : "none"));

		CoverCandidate west = StandingCover(1, s_CoverPos, s_NorthWest);
		CoverCandidate east = StandingCover(2, s_CoverPos, s_NorthEast);
		CoverEvaluationResult afterVisual = EvaluateNorthLook(west, east, VisualNorthEastKnowledge());
		Check("P2_VisualNorthEastPrefersNE",
			afterVisual.HasBest && afterVisual.Best.Candidate.CandidateId == 2,
			"best=" + (afterVisual.HasBest ? afterVisual.Best.Candidate.CandidateId.ToString() : "none"));

		CoverPositionEvaluation northEval = Stamped(north, ExpectedKnowledge());
		CoverPositionEvaluation eastEval = Stamped(side, ExpectedKnowledge());
		CoverPositionEvaluation southEval = Stamped(open, ExpectedKnowledge());
		Check("P3_DirectionSigns",
			northEval.DirectionScore > 0f &&
			Mathf.Abs(eastEval.DirectionScore - ThreatDirectionCoverMath.SideBonus) < 0.0001f &&
			southEval.DirectionScore < 0f,
			"n=" + northEval.DirectionScore + " e=" + eastEval.DirectionScore + " s=" + southEval.DirectionScore);
	}

	private void RunConfidenceUncertainty()
	{
		AppendLine("[B] Confidence / uncertainty");
		CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
		CoverPositionEvaluation expected = Stamped(cover, ExpectedKnowledge());
		CoverPositionEvaluation visual = Stamped(cover, VisualNorthKnowledge());
		CoverPositionEvaluation low = Stamped(cover, LowExpectedKnowledge());
		Check("P4_LowConfidenceWeakerThanVisual",
			Mathf.Abs(low.PositionAdjustment) < Mathf.Abs(visual.PositionAdjustment) &&
			Mathf.Abs(expected.PositionAdjustment) < Mathf.Abs(visual.PositionAdjustment),
			"low=" + low.PositionAdjustment + " vis=" + visual.PositionAdjustment);
		Check("P5_CoverScoreUnchanged",
			Mathf.Abs(
				CoverScoreMath.EvaluateOne(cover, Isolated(ExpectedKnowledge()), null).Score -
				CoverScoreMath.EvaluateOne(cover, Isolated(VisualNorthKnowledge()), null).Score) < 0.0001f,
			"score changed");
		Check("P6_NarrowOverlapLessThanWide",
			ThreatDirectionPositionMath.SectorOverlap(s_North, 5f, s_North, 60f) <
			ThreatDirectionPositionMath.SectorOverlap(s_North, 60f, s_North, 60f),
			"overlap");
		Check("P7_FacingSigns",
			ThreatDirectionPositionMath.FacingScore(s_North, s_North) > 0f &&
			ThreatDirectionPositionMath.FacingScore(s_South, s_North) < 0f,
			"facing");
		Check("P8_Formula",
			Mathf.Abs(
				expected.PositionAdjustment -
				ThreatDirectionPositionMath.FinalAdjustment(
					expected.DirectionScore,
					expected.FacingScore,
					expected.ConfidenceWeight,
					expected.SectorOverlap)) < 0.0001f,
			"adj=" + expected.PositionAdjustment);
	}

	private void RunStayAndRecalc()
	{
		AppendLine("[C] Stay Committed / material change");
		CoverCandidate hold = StandingCover(1, s_Origin, s_North);
		CoverCandidate other = StandingCover(2, s_CoverPos, s_East);
		CoverSituation occupiedSit = Isolated(ExpectedKnowledge());
		occupiedSit.UnitPosition = s_Origin;
		CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(hold, true);
		var solver = new TacticalCoverSolver();
		TacticalCoverDecision first = solver.Decide(in occupiedSit, new[] { hold, other }, in occupying);
		CoverSituation visualSit = Isolated(VisualEastKnowledge());
		visualSit.UnitPosition = s_Origin;
		TacticalCoverDecision second = solver.Decide(in visualSit, new[] { hold, other }, in occupying);
		Check("P9_StayCommitted",
			first.Decision == TacticalCoverDecisionKind.Stay &&
			second.Decision == TacticalCoverDecisionKind.Stay &&
			second.SelectedCandidateId == 1 &&
			!second.HasDestination,
			second.Decision + " sel=" + second.SelectedCandidateId);
		Check("P10_MaterialRecalcPermitted",
			solver.DecideCount == 2,
			"decide=" + solver.DecideCount);

		CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
		CoverCandidate south = StandingCover(2, s_CoverPos, s_South);
		var evaluator = new CoverPositionEvaluator();
		CoverSituation expected = Isolated(ExpectedKnowledge());
		evaluator.Evaluate(new[] { north, south }, in expected);
		CoverSituation slight = Isolated(SlightNorthKnowledge());
		evaluator.Evaluate(new[] { north, south }, in slight);
		Check("P11_SlightNoRecalc",
			evaluator.EvaluateCount == 1 &&
			!ThreatDirectionPositionMath.IsMaterialDirectionChange(
				ExpectedKnowledge().Direction,
				SlightNorthKnowledge().Direction),
			"eval=" + evaluator.EvaluateCount);
	}

	private void RunIsolation()
	{
		AppendLine("[D] CoverScore / 0.60 / Search / no polling");
		Check("P12_AcquireUnchanged",
			Mathf.Abs(TacticalArrivalMath.DefaultAcquireToleranceMeters - 0.6f) < 0.0001f,
			"acquire=" + TacticalArrivalMath.DefaultAcquireToleranceMeters);
		Check("P13_SearchUntouched",
			!TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false),
			"search tactical allowed");

		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
		int logs = controller.LogCount;
		Vector3 dir = controller.GetThreatDirection();
		for (int i = 0; i < 40; i++)
			controller.Tick(i * 0.05f, s_Origin, AIPerceptionFrame.Empty);
		Check("P14_NoPolling",
			controller.LogCount == logs &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North &&
			(dir - controller.GetThreatDirection()).sqrMagnitude < 0.0001f,
			"logs=" + controller.LogCount);

		var readiness = new ReadinessController();
		readiness.Reset(ReadinessRankKind.Soldier, 0f);
		ReadinessState before = readiness.CurrentState;
		controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
		Check("P15_ReadinessUnchanged",
			readiness.CurrentState == before,
			readiness.CurrentState.ToString());
	}

	private void RunLogs()
	{
		AppendLine("[E] TACTICAL_POSITION");
		string payload = ThreatDirectionPositionLog.Format(
			Stamped(StandingCover(3, s_CoverPos, s_North), ExpectedKnowledge()));
		Check("P16_LogPayload",
			payload.IndexOf("cover=C3", StringComparison.Ordinal) >= 0 &&
			payload.IndexOf("dirScore=", StringComparison.Ordinal) >= 0 &&
			payload.IndexOf("adj=", StringComparison.Ordinal) >= 0,
			payload);
		Check("P17_LogChannel",
			ThreatDirectionPositionLog.Channel == "TACTICAL_POSITION",
			ThreatDirectionPositionLog.Channel);
		Check("P18_FirstPickNorth",
			new TacticalCoverSolver().Decide(
				Isolated(ExpectedKnowledge()),
				new[]
				{
					StandingCover(1, s_CoverPos, s_North),
					StandingCover(2, s_CoverPos, s_South)
				},
				CurrentTacticalPosition.Invalid).SelectedCandidateId == 1,
			"first pick");
	}

	private static CoverEvaluationResult EvaluateNorthLook(
		CoverCandidate _a,
		CoverCandidate _b,
		ThreatDirectionKnowledge _knowledge)
	{
		CoverSituation situation = Isolated(_knowledge);
		situation.SectorForward = s_North;
		situation.HostileDirection = s_North;
		return new CoverPositionEvaluator().Evaluate(new[] { _a, _b }, in situation);
	}

	private static CoverPositionEvaluation Stamped(
		CoverCandidate _candidate,
		ThreatDirectionKnowledge _knowledge)
	{
		CoverSituation situation = Isolated(_knowledge);
		return ThreatDirectionCoverMath.Stamp(
			CoverScoreMath.EvaluateOne(_candidate, in situation, null),
			in situation);
	}

	private static CoverSituation Isolated(ThreatDirectionKnowledge _knowledge)
	{
		var situation = new CoverSituation
		{
			UnitPosition = s_Origin,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			HasTarget = false,
			SectorForward = s_East,
			HostileDirection = s_East,
			GeometryVersion = 1
		};
		ThreatDirectionCoverMath.Bind(ref situation, in _knowledge);
		return situation;
	}

	private static CoverSituation IsolatedEqualLook(ThreatDirectionKnowledge _knowledge)
	{
		CoverSituation situation = Isolated(_knowledge);
		situation.SectorForward = s_NorthEast;
		return situation;
	}

	private static ThreatDirectionKnowledge ExpectedKnowledge()
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

	private static ThreatDirectionKnowledge LowExpectedKnowledge()
	{
		return new ThreatDirectionKnowledge(
			s_North,
			ThreatDirectionCompass.North,
			0.3f,
			ThreatDirectionMath.ExpectedUncertaintyDegrees,
			0f,
			ThreatDirectionSource.InitialEstimate,
			ThreatDirectionState.Expected);
	}

	private static ThreatDirectionKnowledge VisualNorthKnowledge()
	{
		return new ThreatDirectionKnowledge(
			s_North,
			ThreatDirectionCompass.North,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static ThreatDirectionKnowledge VisualEastKnowledge()
	{
		return new ThreatDirectionKnowledge(
			s_East,
			ThreatDirectionCompass.East,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static ThreatDirectionKnowledge VisualNorthEastKnowledge()
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

	private static ThreatDirectionKnowledge SlightNorthKnowledge()
	{
		Vector3 slight = new Vector3(
			Mathf.Sin(10f * Mathf.Deg2Rad),
			0f,
			Mathf.Cos(10f * Mathf.Deg2Rad));
		return new ThreatDirectionKnowledge(
			slight,
			ThreatDirectionCompass.North,
			ThreatDirectionMath.ExpectedConfidence,
			ThreatDirectionMath.ExpectedUncertaintyDegrees,
			0f,
			ThreatDirectionSource.InitialEstimate,
			ThreatDirectionState.Expected);
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
			StandingProfile = Profile(1f),
			CrouchProfile = Profile(1f),
			GeometryVersion = 1
		};
	}

	private static CoverProtectionProfile Profile(float _value)
	{
		return new CoverProtectionProfile
		{
			Head = _value,
			Torso = _value,
			Pelvis = _value,
			Legs = _value
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
		string path = Path.Combine(dir, "ThreatDirectionPosition_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[ThreatDirectionPosition] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunThreatDirectionPosition;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
