using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #11 Play: command replace, Search cancel, ImmediateThreat hold, same-state Attack.
/// Report: Assets/_Docs/Logs/Tests/CommandPriority_LAST.txt
/// Menu: Tools/Tests/Run Regression (Play)
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
public sealed class CommandPriorityRuntimeSmoke : MonoBehaviour, IPlaySmokeSuite
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_Actor;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunCommandPriority;

	public int LastPassCount => m_PassCount;
	public int LastFailCount => m_FailCount;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCommandPriority)
			return;
		if (FindAnyObjectByType<CommandPriorityRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CommandPriorityRuntimeSmoke");
		go.AddComponent<CommandPriorityRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyActor();
		if (DetectionHarnessPlayMode.RunCommandPriority &&
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
		AppendLine("STAGE 11 — COMMAND PRIORITY");
		AppendLine("===========================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Command / interrupt / cancel. ImmediateThreat ≠ Flee.");
		AppendLine("---");

		SpawnActor();
		UnitAIController ai = m_Actor.GetComponent<UnitAIController>();
		UnitMoveCommandRecorder recorder = m_Actor.GetComponent<UnitMoveCommandRecorder>();

		AppendLine("[S1] Search → player Attack cancels Search");
		ai.IssueCommand(TacticalCommand.Attack(new Vector3(8f, 0f, 0f)));
		ai.IssueCommand(TacticalCommand.Search(new Vector3(3f, 0f, 4f)));
		Check("S1_Search", ai.CurrentState == UnitAIState.Search, ai.CurrentState.ToString());
		ai.IssueCommand(TacticalCommand.Attack(new Vector3(12f, 0f, 1f)));
		Check("S1_Attack", ai.CurrentState == UnitAIState.Attack, ai.CurrentState.ToString());
		Check(
			"S1_NewOrder",
			ai.LastSearchCompletionReason == UnitAISearchCompletionReason.NewOrder,
			ai.LastSearchCompletionReason.ToString());
		Check("S1_Dest", Approximately(ai.CurrentContext.Destination, new Vector3(12f, 0f, 1f)),
			ai.CurrentContext.Destination.ToString());
		Check("S1_NoResumeSearch", ai.CurrentContext.ResumeState == UnitAIState.Idle,
			ai.CurrentContext.ResumeState.ToString());

		AppendLine("[S2] Attack → Retreat command, nav replaced");
		int stops = recorder.StopCount;
		ai.IssueCommand(TacticalCommand.Retreat(new Vector3(-8f, 0f, 0f)));
		Check("S2_Retreat", ai.CurrentState == UnitAIState.Retreat, ai.CurrentState.ToString());
		Check("S2_Reason", recorder.Reason == UnitNavigationReason.Retreat, recorder.Reason.ToString());
		Check("S2_Stop", recorder.StopCount > stops, "stops=" + recorder.StopCount);
		Check("S2_Dest", Approximately(ai.CurrentContext.Destination, new Vector3(-8f, 0f, 0f)),
			ai.CurrentContext.Destination.ToString());

		AppendLine("[S3] Search → ImmediateThreat stays Search, Emergency HoldState, not Flee");
		ai.IssueCommand(TacticalCommand.Cancel());
		ai.IssueCommand(TacticalCommand.Attack(new Vector3(9f, 0f, 0f)));
		ai.IssueCommand(TacticalCommand.Search(new Vector3(2f, 0f, 2f)));
		Check("S3_Search", ai.CurrentState == UnitAIState.Search, ai.CurrentState.ToString());
		ai.ImmediateThreat = true;
		ai.Tick(0.05f);
		Check("S3_StaySearch", ai.CurrentState == UnitAIState.Search, ai.CurrentState.ToString());
		Check("S3_NotFlee", ai.CurrentState != UnitAIState.Flee, ai.CurrentState.ToString());
		Check(
			"S3_NotThreat",
			ai.LastSearchCompletionReason != UnitAISearchCompletionReason.Threat,
			ai.LastSearchCompletionReason.ToString());
		ai.ImmediateThreat = false;

		AppendLine("[S4] Attack A → Attack B keeps Attack, new context");
		GameObject targetA = new GameObject("CMDPRI_PlayA");
		GameObject targetB = new GameObject("CMDPRI_PlayB");
		ai.IssueCommand(TacticalCommand.Attack(new Vector3(4f, 0f, 0f), targetA.transform));
		ai.ClearTrace();
		ai.IssueCommand(TacticalCommand.Attack(new Vector3(11f, 0f, 2f), targetB.transform));
		Check("S4_State", ai.CurrentState == UnitAIState.Attack, ai.CurrentState.ToString());
		Check("S4_Dest", Approximately(ai.CurrentContext.Destination, new Vector3(11f, 0f, 2f)),
			ai.CurrentContext.Destination.ToString());
		Check("S4_TargetB", ai.CurrentContext.TargetEntity == targetB.transform, "target");
		Check("S4_NoIdleBounce", !TraceContains(ai, "Enter:Idle"), JoinTrace(ai));
		Check(
			"S4_Replace",
			ai.LastPriorityEvaluation.Decision == UnitAIPriorityDecision.ReplaceContext,
			ai.LastPriorityEvaluation.Decision.ToString());
		Destroy(targetA);
		Destroy(targetB);

		Finish();
		yield return null;
	}

	private void SpawnActor()
	{
		DestroyActor();
		m_Actor = new GameObject("CMDPRI_Actor");
		m_Actor.AddComponent<UnitMoveCommandRecorder>();
		m_Actor.AddComponent<UnitAIController>();
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

	private static bool Approximately(Vector3 _a, Vector3 _b)
	{
		return (_a - _b).sqrMagnitude < 0.05f;
	}

	private static bool TraceContains(UnitAIController _ai, string _token)
	{
		for (int i = 0; i < _ai.Trace.Count; i++)
		{
			if (_ai.Trace[i] == _token)
				return true;
		}

		return false;
	}

	private static string JoinTrace(UnitAIController _ai)
	{
		return string.Join(" > ", _ai.Trace);
	}

	private void DestroyActor()
	{
		if (m_Actor == null)
			return;
		Destroy(m_Actor);
		m_Actor = null;
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
		string path = Path.Combine(dir, "CommandPriority_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CommandPriority] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = !DetectionHarnessPlayMode.RunFrozenLayersPlay &&
		                (m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCommandPriority);
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
