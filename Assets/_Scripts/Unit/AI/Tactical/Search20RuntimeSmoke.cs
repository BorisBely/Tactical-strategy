using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #10 Play SearchTestArena: visual loss and sound → SearchArea → candidates → Found / Threat.
/// Does not retune Vision / G6 / CombatIntent math. Search does not write Memory.
/// Report: Assets/_Docs/Logs/Tests/Search20_LAST.txt
/// Menu: Tools/Tests/Run Regression (Play)
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
public sealed class Search20RuntimeSmoke : MonoBehaviour, IPlaySmokeSuite
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_Player;
	private GameObject m_Enemy;
	private GameObject m_Wall;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunSearch20;

	public int LastPassCount => m_PassCount;
	public int LastFailCount => m_FailCount;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunSearch20)
			return;
		if (FindAnyObjectByType<Search20RuntimeSmoke>() != null)
			return;
		var go = new GameObject("Search20RuntimeSmoke");
		go.AddComponent<Search20RuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyActors();
		if (DetectionHarnessPlayMode.RunSearch20 &&
		    !DetectionHarnessPlayMode.RunFrozenLayersPlay)
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

	public IEnumerator RunAndWait()
	{
		yield return RunSuite();
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		yield return null;

		m_Report.Length = 0;
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine("STAGE 10 — SEARCH 2.0");
		AppendLine("====================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("SearchTestArena: area + candidates + inspect. Found = VisibleNow.");
		AppendLine("---");

		SpawnArena();
		UnitAIController playerAi = m_Player.GetComponent<UnitAIController>();
		UnitMoveCommandRecorder recorder = m_Player.GetComponent<UnitMoveCommandRecorder>();

		AppendLine("[E1] Visual loss → SearchArea → Search");
		playerAi.TryApplyCommand(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		Vector3 lastKnown = new Vector3(12f, 0f, 0f);
		playerAi.SetPerceptionFrame(VisualLostFrame(m_Enemy.transform, lastKnown, 0.9f));
		playerAi.Tick(0.05f);
		Check("E1_Search", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		Check(
			"E1_VisualCue",
			playerAi.CurrentContext.SearchCue == UnitAISearchCue.VisualMemory,
			playerAi.CurrentContext.SearchCue.ToString());
		Check(
			"E1_AreaCenter",
			Approximately(playerAi.CurrentSearchArea.Center, lastKnown),
			playerAi.CurrentSearchArea.Center.ToString());
		Check(
			"E1_Candidates",
			playerAi.SearchSession != null && playerAi.SearchSession.Candidates.Count > 0,
			playerAi.SearchSession != null ? playerAi.SearchSession.Candidates.Count.ToString() : "none");
		Check(
			"E1_Bounded",
			playerAi.SearchSession != null &&
			playerAi.SearchSession.Candidates.Count <= UnitAISearchPlanner.MaxSearchCandidates,
			"cap");

		AppendLine("[D] Inspect A then B; target at B → Found / Engage");
		if (playerAi.SearchSession != null && playerAi.SearchSession.Candidates.Count > 1)
		{
			playerAi.transform.position = playerAi.CurrentContext.SearchPosition;
			playerAi.Tick(0.05f);
			Check("D1_InspectA", playerAi.SearchAreaReached && playerAi.CurrentState == UnitAIState.Search, "A");
			playerAi.Tick(UnitAISearchDecision.InspectDuration);
			Check("D2_ToB", playerAi.CurrentState == UnitAIState.Search && playerAi.SearchSession != null && playerAi.SearchSession.Index == 1, "B");
			playerAi.transform.position = playerAi.CurrentContext.SearchPosition;
			playerAi.Tick(0.05f);
			playerAi.SetPerceptionFrame(VisualVisibleFrame(m_Enemy.transform));
			playerAi.Tick(0.05f);
			Check("D4_Found", playerAi.LastSearchCompletionReason == UnitAISearchCompletionReason.Found, "found");
			Check("D4_Engage", playerAi.CurrentAction == UnitAIAction.Engage, playerAi.CurrentAction.ToString());
			Check("D4_Defense", playerAi.CurrentState == UnitAIState.Defense, playerAi.CurrentState.ToString());
		}
		else
		{
			Check("D_CandidatesForChain", false, "need 2 candidates");
		}

		AppendLine("[E2] Shot / Sound → SearchArea");
		playerAi.TryApplyCommand(UnitAICommand.Idle());
		playerAi.TryApplyCommand(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		Vector3 soundPos = new Vector3(10f, 0f, 2f);
		playerAi.SetPerceptionFrame(SoundFrame(m_Enemy.transform, soundPos));
		playerAi.Tick(0.05f);
		Check("E2_Search", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		Check(
			"E2_SoundCue",
			playerAi.CurrentContext.SearchCue == UnitAISearchCue.Sound,
			playerAi.CurrentContext.SearchCue.ToString());
		Check(
			"E2_Area",
			Approximately(playerAi.CurrentSearchArea.Center, soundPos),
			playerAi.CurrentSearchArea.Center.ToString());

		AppendLine("[E3] Ally report → SearchArea");
		playerAi.TryApplyCommand(UnitAICommand.Idle());
		playerAi.TryApplyCommand(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		Vector3 reportPos = new Vector3(7f, 0f, 1f);
		playerAi.SetPerceptionFrame(ReportFrame(m_Player.transform, m_Enemy.transform, reportPos));
		playerAi.Tick(0.05f);
		Check("E3_Search", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		Check(
			"E3_ReportCue",
			playerAi.CurrentContext.SearchCue == UnitAISearchCue.AllyReport,
			playerAi.CurrentContext.SearchCue.ToString());

		AppendLine("[E5] ImmediateThreat does not leave Search");
		playerAi.ImmediateThreat = true;
		playerAi.Tick(0.05f);
		Check("E5_StaySearch", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		Check(
			"E5_NotThreat",
			playerAi.LastSearchCompletionReason != UnitAISearchCompletionReason.Threat,
			playerAi.LastSearchCompletionReason.ToString());

		AppendLine("[D6] New command cancels Search");
		playerAi.ImmediateThreat = false;
		playerAi.TryApplyCommand(UnitAICommand.Idle());
		playerAi.TryApplyCommand(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		playerAi.SetPerceptionFrame(VisualLostFrame(m_Enemy.transform, new Vector3(14f, 0f, 0f), 0.9f));
		playerAi.Tick(0.05f);
		Check("D6_InSearch", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		playerAi.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(Vector3.left * 8f)));
		Check("D6_Retreat", playerAi.CurrentState == UnitAIState.Retreat, playerAi.CurrentState.ToString());
		Check(
			"D6_Cancelled",
			playerAi.LastSearchCompletionReason == UnitAISearchCompletionReason.Cancelled,
			playerAi.LastSearchCompletionReason.ToString());
		Check("D6_Recorder", recorder != null, "move recorder");

		AppendLine("[Arena] wall spawned for SearchTestArena geometry");
		Check("Arena_Wall", m_Wall != null, m_Wall != null ? m_Wall.name : "none");

		DestroyActors();
		Finish();
		yield return null;
	}

	private void SpawnArena()
	{
		DestroyActors();
		m_Player = CreateCombatActor("S10_Player", UnitTeamId.Player);
		m_Enemy = CreateCombatActor("S10_Enemy", UnitTeamId.Enemy);
		m_Enemy.transform.position = new Vector3(12f, 0f, 0f);
		m_Wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
		m_Wall.name = "S10_Building";
		m_Wall.transform.position = new Vector3(6f, 1f, 0f);
		m_Wall.transform.localScale = new Vector3(4f, 2f, 8f);
		NavMeshObstacle obstacle = m_Wall.AddComponent<NavMeshObstacle>();
		obstacle.carving = true;
		obstacle.carveOnlyStationary = true;
	}

	private static GameObject CreateCombatActor(string _name, UnitTeamId _team)
	{
		var go = new GameObject(_name);
		go.SetActive(false);
		go.AddComponent<UnitTeam>().SetTeam(_team);
		go.AddComponent<UnitObservationSource>();
		go.AddComponent<UnitPerception>();
		go.AddComponent<DetectionProcessor>();
		go.AddComponent<TargetSelector>();
		go.AddComponent<EngagementDecisionController>();
		go.AddComponent<UnitMoveCommandRecorder>();
		go.AddComponent<UnitAIController>();
		go.SetActive(true);
		go.GetComponent<DetectionProcessor>().SetSimulatedTime(0f);
		return go;
	}

	private static AIPerceptionFrame VisualLostFrame(Transform _target, Vector3 _lastKnown, float _confidence)
	{
		AIContactKnowledge lost = Knowledge(
			_target,
			ObservationState.RecentlyLost,
			_confidence,
			false,
			true,
			_confidence > 0.25f,
			_confidence <= 0.25f,
			_lastKnown);
		return new AIPerceptionFrame(
			new[] { lost },
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High);
	}

	private static AIPerceptionFrame VisualVisibleFrame(Transform _target)
	{
		AIContactKnowledge visible = Knowledge(
			_target,
			ObservationState.Observed,
			1f,
			true,
			false,
			true,
			false,
			_target.position);
		return new AIPerceptionFrame(
			new[] { visible },
			new[] { visible },
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			new[] { visible },
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High);
	}

	private static AIPerceptionFrame SoundFrame(Transform _source, Vector3 _position)
	{
		return new AIPerceptionFrame(
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.None,
			new[]
			{
				new AISoundContact(_source, _position, SoundEventType.Gunshot, 0.82f, 0f, 0.1f, true)
			},
			Array.Empty<AIReportContact>());
	}

	private static AIPerceptionFrame ReportFrame(Transform _reporter, Transform _subject, Vector3 _position)
	{
		return new AIPerceptionFrame(
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.None,
			Array.Empty<AISoundContact>(),
			new[]
			{
				new AIReportContact(_reporter, _subject, _position, PerceivedIdentity.Hostile, 0.7f, 1f, 0.2f)
			});
	}

	private static AIContactKnowledge Knowledge(
		Transform _target,
		ObservationState _observation,
		float _lastSeenConfidence,
		bool _visibleNow,
		bool _recentlyLost,
		bool _useful,
		bool _stale,
		Vector3 _lastKnown)
	{
		return new AIContactKnowledge(
			_target,
			DetectionState.Detected,
			_observation,
			PerceivedIdentity.Hostile,
			1f,
			PerceivedRelationship.Hostile,
			ThreatLevel.High,
			_lastKnown,
			_lastKnown,
			12.5f,
			_lastSeenConfidence,
			_visibleNow,
			_recentlyLost,
			_observation == ObservationState.Lost,
			_useful,
			_stale,
			false,
			true,
			false,
			false,
			true,
			false,
			false,
			false,
			true);
	}

	private static bool Approximately(Vector3 _a, Vector3 _b)
	{
		return (_a - _b).sqrMagnitude < 0.05f;
	}

	private void DestroyActors()
	{
		DestroyIfAlive(ref m_Player);
		DestroyIfAlive(ref m_Enemy);
		DestroyIfAlive(ref m_Wall);
	}

	private static void DestroyIfAlive(ref GameObject _go)
	{
		if (_go == null)
			return;
		Destroy(_go);
		_go = null;
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine("RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
		           " pass=" + m_PassCount + " fail=" + m_FailCount);
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "Search20_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[Search20RuntimeSmoke] wrote " + path +
			" RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount,
			this);

		bool exitPlay = !DetectionHarnessPlayMode.RunFrozenLayersPlay &&
		                (m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunSearch20);
#if UNITY_EDITOR
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		if (_ok)
		{
			m_PassCount++;
			AppendLine("PASS " + _name + " | " + _detail);
			return;
		}

		m_FailCount++;
		AppendLine("FAIL " + _name + " | " + _detail);
		Debug.LogError("[Search20RuntimeSmoke] FAIL " + _name + " | " + _detail, this);
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
