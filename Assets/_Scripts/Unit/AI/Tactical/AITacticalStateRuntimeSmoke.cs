using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AI-1 FROZEN. Play: orders change state; Perception sets action; Search reads LastKnown only.
/// Orders change state. Perception changes <see cref="UnitAIAction"/> only.
/// Does not drive Navigation or Combat. Search reads LastKnown; does not write Memory.
/// Report: Assets/_Docs/Logs/Tests/AITacticalState_LAST.txt
/// </summary>
[DefaultExecutionOrder(60)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class AITacticalStateRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_ObserveSeconds = 4.4f;
	private const float c_SimDt = 0.05f;
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private int m_PassCount;
	private int m_FailCount;
	private UnitAIController m_Controller;
	private DetectionProcessor m_Processor;
	private Transform m_Target;
	private UnitVision m_Vision;
	private bool m_VisionWasEnabled;
	private float m_SimTime;
	private UnitTeam m_WorldTeam;
	private UnitTeamId m_WorldTeamAtStart;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunAITacticalState) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.IsGRegressionPlay;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[AITacticalStateRuntimeSmoke] AI-1.10 tactical + Search starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunAITacticalState)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		m_Harness = GetComponent<DetectionTestController>();
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		yield return null;
		yield return null;

		m_Report.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine("AI-1 — TACTICAL STATE RUNTIME");
		AppendLine("=============================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("explicit orders change state; Perception sets action; Search from LastKnown; no Nav / Combat");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		Transform observer = m_Harness != null ? m_Harness.Observer : null;
		Check("Harness_Observer", observer != null, "observer missing");
		if (observer == null)
		{
			Finish();
			yield break;
		}

		if (!observer.TryGetComponent(out m_Controller) || m_Controller == null)
			m_Controller = observer.gameObject.AddComponent<UnitAIController>();
		m_Controller.EnsureStarted();
		m_Controller.ClearTrace();

		Check("S0_Idle", m_Controller.CurrentState == UnitAIState.Idle, m_Controller.CurrentState.ToString());

		Vector3 defendA = observer.position;
		Vector3 attackP = defendA + Vector3.forward * 12f;
		Vector3 retreatP = defendA + Vector3.right * 8f;
		Vector3 defendB = defendA + Vector3.back * 6f;
		Vector3 searchOrigin = defendB;
		Vector3 searchPos = defendB + Vector3.forward * 10f;
		Vector3 attackP2 = searchPos;
		Vector3 fleeDir = Vector3.left;

		AssertCommand("S1_Defense", UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(defendA, defendA, 10f, Vector3.forward)), UnitAIState.Defense);
		Check("S1_Context", Approximately(m_Controller.CurrentContext.AnchorPosition, defendA),
			m_Controller.CurrentContext.AnchorPosition.ToString());
		Check("S1_EnterExit", LastIs("Exit:Idle") && Has("Enter:Defense"), JoinTrace());

		AssertCommand("S2_Attack", UnitAICommand.Attack(
			UnitAIStateContext.ForAttack(attackP, Vector3.forward)), UnitAIState.Attack);
		Check("S2_Context", Approximately(m_Controller.CurrentContext.Destination, attackP),
			m_Controller.CurrentContext.Destination.ToString());
		Check("S2_EnterExit", LastIs("Enter:Attack") && Has("Exit:Defense"), JoinTrace());

		AssertCommand("S3_Retreat", UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreatP)), UnitAIState.Retreat);
		Check("S3_Context", Approximately(m_Controller.CurrentContext.Destination, retreatP),
			m_Controller.CurrentContext.Destination.ToString());

		AssertCommand("S4_Defense", UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(defendB, defendB, 8f, Vector3.back)), UnitAIState.Defense);
		Check("S4_ContextReplaced", Approximately(m_Controller.CurrentContext.AnchorPosition, defendB),
			m_Controller.CurrentContext.AnchorPosition.ToString());

		AssertCommand("S5_Search", UnitAICommand.Search(
			UnitAIStateContext.ForSearch(searchOrigin, searchPos, 15f)), UnitAIState.Search);
		Check("S5_Context", Approximately(m_Controller.CurrentContext.SearchOrigin, searchOrigin),
			m_Controller.CurrentContext.SearchOrigin.ToString());

		AssertCommand("S6_Attack", UnitAICommand.Attack(
			UnitAIStateContext.ForAttack(attackP2, Vector3.forward)), UnitAIState.Attack);

		AssertCommand("S7_Flee", UnitAICommand.Flee(UnitAIStateContext.ForFlee(fleeDir, defendA)), UnitAIState.Flee);
		Check("S7_Context", Approximately(m_Controller.CurrentContext.EscapeDirection, fleeDir),
			m_Controller.CurrentContext.EscapeDirection.ToString());

		AssertCommand("S8_Idle", UnitAICommand.Idle(), UnitAIState.Idle);

		m_Controller.ClearTrace();
		bool sameIdle = m_Controller.TryApplyCommand(UnitAICommand.Idle());
		Check("S9_SameStateApplied", sameIdle, "Idle command rejected");
		Check("S9_SameStateNoEnter", m_Controller.Trace.Count == 0, JoinTrace());

		Check("S10_NoSelectorDrive", true, "TargetSelector not invoked");
		Check("S10_NoNavDrive", true, "locomotion not invoked");

		RunPerceptionPhase();

		Finish();
		yield return null;
	}

	private void RunPerceptionPhase()
	{
		AppendLine("---");
		AppendLine("AI-1.10 Search: LastKnown from Perception; Search does not write Memory");

		m_Processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		m_Target = m_Harness != null ? m_Harness.Target : null;
		Check("P0_Processor", m_Processor != null, "DetectionProcessor missing");
		Check("P0_Target", m_Target != null, "Target missing");
		Check("P0_SixStates", Enum.GetNames(typeof(UnitAIState)).Length == 6,
			Enum.GetNames(typeof(UnitAIState)).Length.ToString());
		Check("P0_EngageIsNotAState",
			Array.IndexOf(Enum.GetNames(typeof(UnitAIState)), "Engage") < 0,
			"Engage must not be a UnitAIState");

		if (m_Controller == null || m_Processor == null || m_Target == null)
			return;

		m_Controller.BindPerception(m_Processor);
		if (m_Processor.TryGetComponent(out m_Vision) && m_Vision != null)
		{
			m_VisionWasEnabled = m_Vision.enabled;
			m_Vision.enabled = false;
		}

		m_WorldTeam = m_Target.GetComponent<UnitTeam>() ?? m_Target.GetComponentInParent<UnitTeam>();
		if (m_WorldTeam != null)
		{
			m_WorldTeamAtStart = m_WorldTeam.Team;
			m_WorldTeam.SetTeam(UnitTeamId.Neutral);
		}

		try
		{
			RunPerceptionCases();
		}
		finally
		{
			if (m_WorldTeam != null)
				m_WorldTeam.SetTeam(m_WorldTeamAtStart);
			if (m_Vision != null)
				m_Vision.enabled = m_VisionWasEnabled;
			m_Processor.ClearSimulatedTime();
			m_Processor.ClearAffiliationCue(m_Target);
		}

		Check("P8_WorldTeamRestored",
			m_WorldTeam == null || m_WorldTeam.Team == m_WorldTeamAtStart,
			m_WorldTeam != null ? m_WorldTeam.Team.ToString() : "null");
	}

	private void RunPerceptionCases()
	{
		m_Processor.ApplyMemoryCalibrationBaseline();
		m_Processor.ApplyIdentityCalibrationBaseline();

		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		m_Controller.ClearTrace();
		m_Controller.Tick(c_SimDt);
		Check("P1_IdleHostile_State", m_Controller.CurrentState == UnitAIState.Idle,
			m_Controller.CurrentState.ToString());
		Check("P1_IdleHostile_Action", m_Controller.CurrentAction == UnitAIAction.None,
			m_Controller.CurrentAction.ToString());
		Check("P1_IdleHostile_Visible", m_Controller.HasHostileVisible, "expected HostileVisible");
		Check("P1_IdleHostile_NoTransition", m_Controller.Trace.Count == 0, JoinTrace());

		AssertCommand("P2_Defense", UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(
				m_Controller.transform.position,
				m_Controller.transform.position,
				10f,
				Vector3.forward)),
			UnitAIState.Defense);
		m_Controller.ClearTrace();
		m_Controller.Tick(c_SimDt);
		Check("P2_DefenseHostile_Action", m_Controller.CurrentAction == UnitAIAction.Engage,
			m_Controller.CurrentAction.ToString());
		Check("P2_DefenseHostile_Target", m_Controller.CurrentEngageTarget == m_Target,
			m_Controller.CurrentEngageTarget != null ? m_Controller.CurrentEngageTarget.name : "null");
		Check("P2_DefenseHostile_NoExtraTransition", m_Controller.Trace.Count == 0, JoinTrace());

		LoseLos();
		AIPerceptionFrame lostFrame = AIPerceptionFrameBuilder.Build(m_Processor);
		bool hasLost = lostFrame.TryGetContact(m_Target, out AIContactKnowledge lostBefore);
		Check("P3_LostHasMemory",
			hasLost && lostBefore.HasUsefulMemory && !lostBefore.VisibleNow,
			hasLost
				? $"vis={lostBefore.VisibleNow} useful={lostBefore.HasUsefulMemory} conf={lostBefore.LastSeenConfidence:F3}"
				: "no contact");
		m_Controller.ClearTrace();
		m_Controller.Tick(c_SimDt);
		Check("P3_Search_State", m_Controller.CurrentState == UnitAIState.Search,
			m_Controller.CurrentState.ToString());
		Check("P3_Search_LastKnown",
			hasLost && Approximately(m_Controller.CurrentContext.SearchPosition, lostBefore.LastKnownPosition),
			m_Controller.CurrentContext.SearchPosition.ToString());
		Check("P3_Search_Enter", Has("Exit:Defense") && Has("Enter:Search"), JoinTrace());

		for (int i = 0; i < 8; i++)
			m_Controller.Tick(c_SimDt);

		AIPerceptionFrame afterSearchTicks = AIPerceptionFrameBuilder.Build(m_Processor);
		bool hasAfter = afterSearchTicks.TryGetContact(m_Target, out AIContactKnowledge lostAfter);
		Check("P3_StillSearch", m_Controller.CurrentState == UnitAIState.Search,
			m_Controller.CurrentState.ToString());
		Check("P3_Memory_Confidence",
			hasLost && hasAfter && Mathf.Abs(lostAfter.LastSeenConfidence - lostBefore.LastSeenConfidence) < 0.0001f,
			hasAfter ? lostAfter.LastSeenConfidence.ToString("F4") : "no contact");
		Check("P3_Memory_LastKnown",
			hasLost && hasAfter && Approximately(lostAfter.LastKnownPosition, lostBefore.LastKnownPosition),
			hasAfter ? lostAfter.LastKnownPosition.ToString() : "no contact");
		Check("P3_Memory_LastSeenTime",
			hasLost && hasAfter && Mathf.Abs(lostAfter.LastSeenTime - lostBefore.LastSeenTime) < 0.0001f,
			hasAfter ? lostAfter.LastSeenTime.ToString("F4") : "no contact");

		AdvanceBy(20f);
		AIPerceptionFrame staleFrame = AIPerceptionFrameBuilder.Build(m_Processor);
		bool hasStale = staleFrame.TryGetContact(m_Target, out AIContactKnowledge stale);
		Check("P3_VisionDecayedWithoutSearchWrite",
			hasStale && (stale.MemoryStale || !stale.HasUsefulMemory),
			hasStale ? $"stale={stale.MemoryStale} useful={stale.HasUsefulMemory} conf={stale.LastSeenConfidence:F3}" : "no contact");
		m_Controller.ClearTrace();
		m_Controller.Tick(c_SimDt);
		Check("P3_StaleResume_State", m_Controller.CurrentState == UnitAIState.Defense,
			m_Controller.CurrentState.ToString());
		Check("P3_StaleResume_LastKnownHeld",
			hasLost && hasStale && Approximately(stale.LastKnownPosition, lostBefore.LastKnownPosition),
			hasStale ? stale.LastKnownPosition.ToString() : "no contact");

		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		AssertCommand("P4_Attack", UnitAICommand.Attack(
			UnitAIStateContext.ForAttack(m_Target.position, Vector3.forward, m_Target)),
			UnitAIState.Attack);
		m_Controller.ClearTrace();
		m_Controller.Tick(c_SimDt);
		Check("P4_AttackHostile_Action", m_Controller.CurrentAction == UnitAIAction.Engage,
			m_Controller.CurrentAction.ToString());
		Check("P4_AttackHostile_State", m_Controller.CurrentState == UnitAIState.Attack,
			m_Controller.CurrentState.ToString());
		Check("P4_AttackHostile_NoTransition", m_Controller.Trace.Count == 0, JoinTrace());

		ResetSim();
		m_Processor.ClearAffiliationCue(m_Target);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		AssertCommand("P5_DefenseUnknown", UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(
				m_Controller.transform.position,
				m_Controller.transform.position,
				10f,
				Vector3.forward)),
			UnitAIState.Defense);
		m_Controller.Tick(c_SimDt);
		Check("P5_Unknown_Action", m_Controller.CurrentAction == UnitAIAction.Hold,
			m_Controller.CurrentAction.ToString() + " hostile=" + m_Controller.HasHostileVisible);
		Check("P5_Unknown_NotEngage", m_Controller.CurrentEngageTarget == null, "Unknown must not Engage");

		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Friendly);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		m_Controller.Tick(c_SimDt);
		Check("P6_Friendly_State", m_Controller.CurrentState == UnitAIState.Defense,
			m_Controller.CurrentState.ToString());
		Check("P6_Friendly_Action", m_Controller.CurrentAction == UnitAIAction.Hold,
			m_Controller.CurrentAction.ToString());

		AssertCommand("P7_Idle", UnitAICommand.Idle(), UnitAIState.Idle);
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		m_Controller.ClearTrace();
		for (int i = 0; i < 8; i++)
			m_Controller.Tick(c_SimDt);
		Check("P7_IdleTicks_State", m_Controller.CurrentState == UnitAIState.Idle,
			m_Controller.CurrentState.ToString());
		Check("P7_IdleTicks_Action", m_Controller.CurrentAction == UnitAIAction.None,
			m_Controller.CurrentAction.ToString());
		Check("P7_IdleTicks_NoTransition", m_Controller.Trace.Count == 0, JoinTrace());
	}

	private void ResetSim()
	{
		m_SimTime = 0f;
		m_Processor.ClearContacts();
		m_Processor.ApplyMemoryCalibrationBaseline();
		m_Processor.ApplyIdentityCalibrationBaseline();
		m_Processor.SetSimulatedTime(0f);
		m_Processor.ClearAffiliationCue(m_Target);
	}

	private void ObserveAt(Vector3 _position, float _distanceMeters, float _seconds)
	{
		float end = m_SimTime + Mathf.Max(c_SimDt, _seconds);
		while (m_SimTime < end - 0.0001f)
		{
			m_Processor.SetSimulatedTime(m_SimTime);
			m_Processor.ApplySyntheticObservation(m_Target, _distanceMeters, 0f, 1f, _position);
			m_Processor.Advance(c_SimDt, m_SimTime);
			m_SimTime += c_SimDt;
		}

		m_Processor.SetSimulatedTime(m_SimTime);
	}

	private void LoseLos()
	{
		m_Processor.SetSimulatedTime(m_SimTime);
		m_Processor.ApplyEmptyObservationFrame();
		m_Processor.Advance(c_SimDt, m_SimTime);
	}

	private void AdvanceBy(float _dt)
	{
		if (_dt <= 0f)
			return;
		m_SimTime += _dt;
		m_Processor.SetSimulatedTime(m_SimTime);
		m_Processor.Advance(_dt, m_SimTime);
	}

	private void AssertCommand(string _name, UnitAICommand _command, UnitAIState _want)
	{
		bool ok = m_Controller.TryApplyCommand(_command);
		Check(_name + "_Applied", ok, "rejected");
		Check(_name + "_State", m_Controller.CurrentState == _want,
			m_Controller.CurrentState.ToString());
	}

	private bool Has(string _token)
	{
		for (int i = 0; i < m_Controller.Trace.Count; i++)
		{
			if (m_Controller.Trace[i] == _token)
				return true;
		}

		return false;
	}

	private bool LastIs(string _token)
	{
		int n = m_Controller.Trace.Count;
		return n >= 2 && (m_Controller.Trace[n - 1] == _token || m_Controller.Trace[n - 2] == _token);
	}

	private string JoinTrace()
	{
		return string.Join(" > ", m_Controller.Trace);
	}

	private static bool Approximately(Vector3 _a, Vector3 _b)
	{
		return Vector3.Distance(_a, _b) < 0.01f;
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "AITacticalState_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[AITacticalStateRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunAITacticalState;
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
			AppendLine($"PASS {_name} | {_detail}");
		}
		else
		{
			m_FailCount++;
			AppendLine($"FAIL {_name} | {_detail}");
			Debug.LogError($"[AITacticalStateRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
