using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14C Play: spawn estimate, visual/sound/report, no polling, fallback, independence.
/// Report: Assets/_Docs/Logs/Tests/ThreatDirection_LAST.txt
/// </summary>
[DefaultExecutionOrder(67)]
[DisallowMultipleComponent]
public sealed class ThreatDirectionRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private static readonly Vector3 s_Origin = Vector3.zero;
	private static readonly Vector3 s_North = new Vector3(0f, 0f, 10f);
	private static readonly Vector3 s_East = new Vector3(10f, 0f, 0f);
	private static readonly Vector3 s_NorthEast = new Vector3(10f, 0f, 10f);

	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunThreatDirection;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunThreatDirection)
			return;
		if (FindAnyObjectByType<ThreatDirectionRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ThreatDirectionRuntimeSmoke");
		go.AddComponent<ThreatDirectionRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunThreatDirection)
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
		AppendLine("STAGE 14C — THREAT DIRECTION KNOWLEDGE");
		AppendLine("======================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Spawn estimate → visual / sound / report. Event-driven. Not Cover / Move / Aim / Fire.");
		AppendLine("---");

		RunScenarioA();
		RunScenarioB();
		RunNoPolling();
		RunLogs();
		RunIndependence();
		RunSpawnPins();
		RunApi();

		Finish();
		yield break;
	}

	private void RunScenarioA()
	{
		var controller = new ThreatDirectionController();
		Check("P1_SpawnExpectedNorth",
			controller.ApplyBattleStart(s_Origin, s_North, 0f) &&
			controller.CurrentState == ThreatDirectionState.Expected &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North,
			"expected N");

		Check("P2_VisualKnownNorthEast",
			controller.ApplyHostileVisible(s_Origin, s_NorthEast, 1f) &&
			controller.CurrentState == ThreatDirectionState.Known &&
			controller.GetThreatCompass() == ThreatDirectionCompass.NorthEast,
			"known NE");

		Check("P3_HostileLostStaleNorthEast",
			controller.ApplyHostileLost(2f) &&
			controller.CurrentState == ThreatDirectionState.Stale &&
			controller.GetThreatCompass() == ThreatDirectionCompass.NorthEast,
			"stale NE");

		controller.Tick(2f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.2f);
		Check("P4_QuietFallbackExpectedNorth",
			controller.CurrentState == ThreatDirectionState.Expected &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North &&
			controller.CurrentSource == ThreatDirectionSource.InitialEstimate,
			"fallback N");
	}

	private void RunScenarioB()
	{
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_North, 0f);
		Check("P5_GunshotEast",
			controller.ApplyGunshot(s_Origin, s_East, 1f) &&
			controller.GetThreatCompass() == ThreatDirectionCompass.East &&
			controller.CurrentSource == ThreatDirectionSource.Sound,
			"sound E");

		Check("P6_VisualBeatsSound",
			controller.ApplyHostileVisible(s_Origin, s_NorthEast, 2f) &&
			controller.GetThreatCompass() == ThreatDirectionCompass.NorthEast &&
			controller.CurrentSource == ThreatDirectionSource.Visual,
			"visual NE");

		var report = new ThreatDirectionController();
		report.ApplyBattleStart(s_Origin, s_North, 0f);
		Check("P7_ReportFallback",
			report.ApplyAllyReport(s_Origin, s_East, 1f) &&
			report.CurrentSource == ThreatDirectionSource.AllyReport &&
			report.GetThreatCompass() == ThreatDirectionCompass.East,
			"report E");
	}

	private void RunNoPolling()
	{
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_North, 0f);
		Vector3 before = controller.GetThreatDirection();
		controller.Tick(1f, s_Origin, AIPerceptionFrame.Empty);
		controller.Tick(2f, s_Origin, AIPerceptionFrame.Empty);
		Check("P8_NoPollingEmptyFrames",
			controller.GetThreatDirection() == before &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North,
			"still N");

		controller.Tick(3f, s_Origin, VisualFrame(s_NorthEast, true));
		controller.Tick(4f, s_Origin, VisualFrame(s_East, true));
		Check("P9_HeldVisualDoesNotPoll",
			controller.GetThreatCompass() == ThreatDirectionCompass.NorthEast,
			"held NE");
	}

	private void RunLogs()
	{
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_North, 0f);
		int afterExpected = controller.LogCount;
		Check("P10_LogExpected",
			controller.LastLogPayload.IndexOf("source=Initial", StringComparison.Ordinal) >= 0 &&
			controller.LastLogPayload.IndexOf("state=Expected", StringComparison.Ordinal) >= 0 &&
			controller.LastLogPayload.IndexOf("dir=N confidence=", StringComparison.Ordinal) >= 0,
			controller.LastLogPayload);

		controller.Tick(1f);
		controller.Tick(2f);
		Check("P11_NoLogEveryTick", controller.LogCount == afterExpected, "count=" + controller.LogCount);

		controller.ApplyHostileVisible(s_Origin, s_NorthEast, 3f);
		Check("P12_LogVisual",
			controller.LastLogPayload.IndexOf("source=Visual", StringComparison.Ordinal) >= 0 &&
			controller.LastLogPayload.IndexOf("dir=NE", StringComparison.Ordinal) >= 0,
			controller.LastLogPayload);

		controller.ApplyHostileLost(4f);
		Check("P13_LogStale",
			controller.LastLogPayload.IndexOf("state=Stale", StringComparison.Ordinal) >= 0,
			controller.LastLogPayload);
	}

	private void RunIndependence()
	{
		var readiness = new ReadinessController();
		readiness.Reset(ReadinessRankKind.Soldier, 0f);
		var threat = new ThreatDirectionController();
		threat.ApplyBattleStart(s_Origin, s_North, 0f);
		threat.ApplyHostileVisible(s_Origin, s_NorthEast, 1f);
		Check("P14_DoesNotChangeReadiness",
			readiness.CurrentState == ReadinessState.Patrol,
			readiness.CurrentState.ToString());

		var go = new GameObject("ThreatDirectionIndependenceAi");
		UnitAIController ai = go.AddComponent<UnitAIController>();
		ai.enabled = false;
		UnitAIState state = ai.CurrentState;
		ai.ThreatDirection.ApplyBattleStart(s_Origin, s_North, 0f);
		ai.ThreatDirection.ApplyHostileVisible(s_Origin, s_East, 1f);
		Check("P15_DoesNotChangeAiState",
			ai.CurrentState == state,
			ai.CurrentState.ToString());
		Destroy(go);
	}

	private void RunSpawnPins()
	{
		ThreatDirectionSpawnQuery.Invalidate();
		bool hasPins = ThreatDirectionSpawnQuery.TryGetPlayerAndEnemyCenters(
			out Vector3 playerCenter,
			out Vector3 enemyCenter);
		if (!hasPins)
		{
			Check("P16_SceneSpawnPins", false, "no player/enemy spawn pins");
			return;
		}

		ThreatDirectionEstimator.TryExpectedDirection(playerCenter, enemyCenter, out Vector3 playerDir);
		ThreatDirectionEstimator.TryExpectedDirection(enemyCenter, playerCenter, out Vector3 enemyDir);
		Check("P16_SceneSpawnPinsOpposite",
			Vector3.Dot(playerDir, enemyDir) < -0.99f,
			"player=" + ThreatDirectionEstimator.CompassLabel(
				ThreatDirectionEstimator.CompassFrom(playerDir)) +
			" enemy=" + ThreatDirectionEstimator.CompassLabel(
				ThreatDirectionEstimator.CompassFrom(enemyDir)));

		var player = new ThreatDirectionController();
		var enemy = new ThreatDirectionController();
		player.ApplyBattleStart(playerCenter, enemyCenter, 0f);
		enemy.ApplyBattleStart(enemyCenter, playerCenter, 0f);
		Check("P17_SidesGetOppositeExpected",
			player.CurrentState == ThreatDirectionState.Expected &&
			enemy.CurrentState == ThreatDirectionState.Expected &&
			Vector3.Dot(player.GetThreatDirection(), enemy.GetThreatDirection()) < -0.99f,
			"player=" + player.GetThreatCompass() + " enemy=" + enemy.GetThreatCompass());
	}

	private void RunApi()
	{
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_North, 0f);
		Check("P18_ExpectedQuality",
			controller.GetThreatConfidence() > 0f &&
			controller.GetThreatUncertainty() > 0f &&
			Mathf.Abs(controller.GetThreatUncertainty() - ThreatDirectionMath.ExpectedUncertaintyDegrees) < 0.01f,
			"conf=" + controller.GetThreatConfidence());

		float expectedConf = controller.GetThreatConfidence();
		controller.ApplyHostileVisible(s_Origin, s_NorthEast, 1f);
		Check("P19_VisualConfidenceUp",
			controller.GetThreatConfidence() > expectedConf &&
			controller.GetThreatUncertainty() < ThreatDirectionMath.ExpectedUncertaintyDegrees,
			"visual conf");

		controller.TryGetThreatDirection(out ThreatDirectionKnowledge before);
		controller.Tick(5f);
		controller.TryGetThreatDirection(out ThreatDirectionKnowledge after);
		Check("P20_AgeIncreases",
			after.Age > before.Age && after.Compass == before.Compass,
			"age");
	}

	private static AIPerceptionFrame VisualFrame(Vector3 _lastKnown, bool _visibleNow)
	{
		AIContactKnowledge contact = new AIContactKnowledge(
			null,
			_visibleNow ? DetectionState.Detected : DetectionState.Undetected,
			_visibleNow ? ObservationState.Observed : ObservationState.Lost,
			PerceivedIdentity.Hostile,
			1f,
			PerceivedRelationship.Hostile,
			ThreatLevel.High,
			_lastKnown,
			_lastKnown,
			0f,
			_visibleNow ? 1f : 0.4f,
			_visibleNow,
			!_visibleNow,
			!_visibleNow,
			true,
			false,
			false,
			true,
			false,
			false,
			true,
			false,
			false,
			false,
			true);
		return new AIPerceptionFrame(
			new[] { contact },
			_visibleNow ? new[] { contact } : System.Array.Empty<AIContactKnowledge>(),
			_visibleNow ? System.Array.Empty<AIContactKnowledge>() : new[] { contact },
			System.Array.Empty<AIContactKnowledge>(),
			new[] { contact },
			System.Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High);
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
		string path = Path.Combine(dir, "ThreatDirection_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[ThreatDirection] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunThreatDirection;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
