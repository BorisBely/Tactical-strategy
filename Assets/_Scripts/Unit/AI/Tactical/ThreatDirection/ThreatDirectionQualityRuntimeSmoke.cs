using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14C.2 Play: source quality, aging, cover/facing weights, no polling.
/// Report: Assets/_Docs/Logs/Tests/ThreatDirectionQuality_LAST.txt
/// </summary>
[DefaultExecutionOrder(69)]
[DisallowMultipleComponent]
public sealed class ThreatDirectionQualityRuntimeSmoke : MonoBehaviour
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
	private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
	private static readonly Vector3 s_EastPoint = new Vector3(10f, 0f, 0f);
	private static readonly Vector3 s_NorthEastPoint = new Vector3(10f, 0f, 10f);
	private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);

	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunThreatDirectionQuality;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunThreatDirectionQuality)
			return;
		if (FindAnyObjectByType<ThreatDirectionQualityRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ThreatDirectionQualityRuntimeSmoke");
		go.AddComponent<ThreatDirectionQualityRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunThreatDirectionQuality)
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
		AppendLine("STAGE 14C.2 — THREAT DIRECTION CONFIDENCE & UNCERTAINTY");
		AppendLine("=======================================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Source quality → aging → cover/facing weights. Event-driven. Not CoverScore / Move / Fire.");
		AppendLine("---");

		RunScenario();
		RunWeights();
		RunNoPolling();
		RunIsolation();
		RunLogs();

		Finish();
		yield break;
	}

	private void RunScenario()
	{
		AppendLine("[Scenario] Spawn Expected → Sound E → Visual NE → Lost → Stale → Expected N");
		var controller = new ThreatDirectionController();
		Check("P1_SpawnExpectedNorthLow",
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f) &&
			controller.CurrentState == ThreatDirectionState.Expected &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North &&
			controller.GetThreatConfidence() < ThreatDirectionMath.SoundConfidence &&
			controller.GetThreatUncertainty() >= 40f,
			"state=" + controller.CurrentState + " conf=" + controller.GetThreatConfidence());

		float expectedConf = controller.GetThreatConfidence();
		float expectedUnc = controller.GetThreatUncertainty();
		Check("P2_GunshotEastMedium",
			controller.ApplyGunshot(s_Origin, s_EastPoint, 1f) &&
			controller.GetThreatCompass() == ThreatDirectionCompass.East &&
			controller.GetThreatConfidence() > expectedConf &&
			controller.GetThreatUncertainty() < expectedUnc,
			"dir=" + controller.GetThreatCompass() + " conf=" + controller.GetThreatConfidence());

		float soundConf = controller.GetThreatConfidence();
		Check("P3_VisualNorthEastHigh",
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f) &&
			controller.CurrentState == ThreatDirectionState.Known &&
			controller.GetThreatCompass() == ThreatDirectionCompass.NorthEast &&
			controller.GetThreatConfidence() > soundConf &&
			controller.GetThreatUncertainty() <= ThreatDirectionMath.VisualUncertaintyDegrees + 0.01f,
			"dir=" + controller.GetThreatCompass() + " conf=" + controller.GetThreatConfidence());

		float visualConf = controller.GetThreatConfidence();
		float visualUnc = controller.GetThreatUncertainty();
		Check("P4_LostStaleKeepsNorthEast",
			controller.ApplyHostileLost(3f) &&
			controller.CurrentState == ThreatDirectionState.Stale &&
			controller.GetThreatCompass() == ThreatDirectionCompass.NorthEast &&
			controller.GetThreatConfidence() < visualConf &&
			controller.GetThreatUncertainty() > visualUnc,
			controller.CurrentState + " " + controller.GetThreatCompass());

		float staleConf = controller.GetThreatConfidence();
		controller.Tick(6f);
		Check("P5_StaleDecays",
			controller.CurrentState == ThreatDirectionState.Stale &&
			controller.GetThreatConfidence() < staleConf &&
			controller.GetThreatUncertainty() > visualUnc,
			"conf=" + controller.GetThreatConfidence());

		controller.Tick(3f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.1f);
		Check("P6_FallbackExpectedNorth",
			controller.CurrentState == ThreatDirectionState.Expected &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North &&
			Mathf.Abs(controller.GetThreatConfidence() - ThreatDirectionMath.ExpectedConfidence) < 0.001f,
			controller.CurrentState + " " + controller.GetThreatCompass());
	}

	private void RunWeights()
	{
		AppendLine("[Weights] Cover and facing follow confidence / uncertainty");
		CoverCandidate north = StandingCover(1, s_CoverPos, s_North);
		CoverCandidate south = StandingCover(2, s_CoverPos, s_South);
		CoverEvaluationResult expected = Evaluate(north, south, ExpectedKnowledge());
		CoverEvaluationResult visual = Evaluate(north, south, VisualNorthKnowledge());
		Check("P7_ExpectedPrefersNorthWeak",
			expected.HasBest && expected.Best.Candidate.CandidateId == 1,
			"best=" + (expected.HasBest ? expected.Best.Candidate.CandidateId.ToString() : "none"));
		Check("P8_VisualStrongerCoverWeight",
			visual.HasBest &&
			visual.Best.Candidate.CandidateId == 1 &&
			Mathf.Abs(visual.Best.ThreatDirectionAdjustment) >
			Mathf.Abs(expected.Best.ThreatDirectionAdjustment),
			"exp=" + expected.Best.ThreatDirectionAdjustment + " vis=" + visual.Best.ThreatDirectionAdjustment);
		Check("P9_FacingSlackVisualTighter",
			ThreatDirectionFacingController.FacingSlackDegrees(VisualNorthKnowledge()) <
			ThreatDirectionFacingController.FacingSlackDegrees(ExpectedKnowledge()),
			"visualSlack vs expectedSlack");

		CoverSituation occupiedSit = Isolated(VisualNorthKnowledge());
		occupiedSit.UnitPosition = Vector3.zero;
		CoverCandidate hold = StandingCover(1, Vector3.zero, s_North);
		TacticalCoverDecision stay = new TacticalCoverSolver().Decide(
			in occupiedSit,
			new[] { hold, south },
			CurrentTacticalPosition.FromCandidate(hold, true));
		Check("P10_StayCommitted",
			stay.Decision == TacticalCoverDecisionKind.Stay && stay.SelectedCandidateId == 1,
			stay.Decision + " sel=" + stay.SelectedCandidateId);
	}

	private void RunNoPolling()
	{
		AppendLine("[Events] Empty ticks do not rewrite direction");
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
		int logs = controller.LogCount;
		int quality = controller.QualityLogCount;
		Vector3 dir = controller.GetThreatDirection();
		for (int i = 0; i < 40; i++)
			controller.Tick(i * 0.05f, s_Origin, AIPerceptionFrame.Empty);
		Check("P11_NoPollingDirection",
			controller.LogCount == logs &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North &&
			(dir - controller.GetThreatDirection()).sqrMagnitude < 0.0001f,
			"logs=" + controller.LogCount);
		Check("P12_NoPollingExpectedQuality",
			controller.QualityLogCount == quality,
			"qualityLogs=" + controller.QualityLogCount);
	}

	private void RunIsolation()
	{
		AppendLine("[Independence] CoverScore / 0.60 / Search / Readiness");
		CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
		CoverSituation expected = Isolated(ExpectedKnowledge());
		CoverSituation visual = Isolated(VisualNorthKnowledge());
		Check("P13_CoverScoreUnchanged",
			Mathf.Abs(
				CoverScoreMath.EvaluateOne(cover, in expected, null).Score -
				CoverScoreMath.EvaluateOne(cover, in visual, null).Score) < 0.0001f,
			"score changed");
		Check("P14_AcquireUnchanged",
			Mathf.Abs(TacticalArrivalMath.DefaultAcquireToleranceMeters - 0.6f) < 0.0001f,
			"acquire=" + TacticalArrivalMath.DefaultAcquireToleranceMeters);
		Check("P15_SearchUntouched",
			!TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false),
			"search tactical allowed");

		var readiness = new ReadinessController();
		readiness.Reset(ReadinessRankKind.Soldier, 0f);
		ReadinessState before = readiness.CurrentState;
		var threat = new ThreatDirectionController();
		threat.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
		threat.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
		Check("P16_ReadinessUnchanged",
			readiness.CurrentState == before,
			readiness.CurrentState.ToString());
	}

	private void RunLogs()
	{
		AppendLine("[Logs] THREAT_DIRECTION_UPDATE on quality events");
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
		Check("P17_UpdatePayloadExpected",
			controller.LastQualityPayload.IndexOf("source=Initial", StringComparison.Ordinal) >= 0 &&
			controller.LastQualityPayload.IndexOf("confidence=", StringComparison.Ordinal) >= 0 &&
			controller.LastQualityPayload.IndexOf("uncertainty=", StringComparison.Ordinal) >= 0,
			controller.LastQualityPayload);
		controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
		Check("P18_UpdatePayloadVisual",
			controller.LastQualityPayload.IndexOf("source=Visual", StringComparison.Ordinal) >= 0 &&
			controller.LastQualityPayload.IndexOf("state=Known", StringComparison.Ordinal) >= 0 &&
			controller.LastQualityPayload.IndexOf("dir=NE", StringComparison.Ordinal) >= 0,
			controller.LastQualityPayload);
		Check("P19_UpdateChannel",
			ThreatDirectionLog.UpdateChannel == "THREAT_DIRECTION_UPDATE",
			ThreatDirectionLog.UpdateChannel);
	}

	private static CoverEvaluationResult Evaluate(
		CoverCandidate _a,
		CoverCandidate _b,
		ThreatDirectionKnowledge _knowledge)
	{
		CoverSituation situation = Isolated(_knowledge);
		return new CoverPositionEvaluator().Evaluate(new[] { _a, _b }, in situation);
	}

	private static CoverSituation Isolated(ThreatDirectionKnowledge _knowledge)
	{
		var situation = new CoverSituation
		{
			UnitPosition = Vector3.zero,
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
		string path = Path.Combine(dir, "ThreatDirectionQuality_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[ThreatDirectionQuality] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunThreatDirectionQuality;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
