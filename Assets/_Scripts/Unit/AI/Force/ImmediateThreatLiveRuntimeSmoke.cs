using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #7 Play: live ImmediateThreat source → RoE Allow/Deny. Does not retune G6 or the UseOfForce matrix.
/// Report: Assets/_Docs/Logs/Tests/ImmediateThreatLive_LAST.txt
/// Menu: Tools/Tests/Run Regression (Play)
/// </summary>
[DefaultExecutionOrder(62)]
[DisallowMultipleComponent]
public sealed class ImmediateThreatLiveRuntimeSmoke : MonoBehaviour, IPlaySmokeSuite
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
	private GameObject m_Ally;
	private GameObject m_Neutral;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunImmediateThreatLive;

	public int LastPassCount => m_PassCount;
	public int LastFailCount => m_FailCount;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunImmediateThreatLive)
			return;
		if (FindAnyObjectByType<ImmediateThreatLiveRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ImmediateThreatLiveRuntimeSmoke");
		go.AddComponent<ImmediateThreatLiveRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunImmediateThreatLive &&
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
		AppendLine("STAGE 7 — IMMEDIATE THREAT LIVE");
		AppendLine("==============================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("ImmediateThreat from hostile attack only. RoE is veto. Allow ≠ Fire.");
		AppendLine("---");

		Check("Log_Enabled", UnitActionLog.Enabled, "UnitActionLog session off in Editor Play");
		Check(
			"Log_Folder",
			!string.IsNullOrEmpty(UnitActionLogSession.Folder) && Directory.Exists(UnitActionLogSession.Folder),
			UnitActionLogSession.Folder ?? "null");

		SpawnFourSides();
		UnitAIController playerAi = m_Player.GetComponent<UnitAIController>();
		EngagementDecisionController engagement = m_Player.GetComponent<EngagementDecisionController>();
		DetectionProcessor processor = m_Player.GetComponent<DetectionProcessor>();
		TargetSelector selector = m_Player.GetComponent<TargetSelector>();

		AppendLine("[D1] Hostile visible, SelfDefense, no attack → ImmediateThreat false, Aim/Fire denied");
		ArmSelfDefense(playerAi, processor, selector, engagement, false);
		Check("D1_ThreatOff", !playerAi.ImmediateThreat, "threat already set");
		Check("D1_Deny", !engagement.LastForcePermission.Allowed, engagement.LastForcePermission.ToString());
		Check("D1_NoAimFire",
			engagement.CurrentDecision != EngagementDecision.Fire &&
			engagement.CurrentDecision != EngagementDecision.Aim,
			engagement.CurrentDecision.ToString());

		AppendLine("[D2] Enemy fires at player → ImmediateThreat true, Allow");
		ImmediateThreatSignal.NotifyIncomingFire(m_Enemy.GetComponent<UnitTeam>(), m_Player.transform);
		playerAi.Tick(0.05f);
		engagement.RefreshDecisionNow();
		Check("D2_ThreatOn", playerAi.ImmediateThreat, "incoming fire did not set threat");
		Check("D2_Allow", engagement.LastForcePermission.Allowed, engagement.LastForcePermission.ToString());

		AppendLine("[D3] Allow does not Ignore G6 (SHOT is Combat Engage T3b)");
		Check("D3_NotIgnore",
			engagement.CurrentDecision != EngagementDecision.Ignore,
			engagement.CurrentDecision.ToString());
		Check("D3_SelectionIsEnemy",
			selector.SelectedTarget == m_Enemy.transform,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");

		AppendLine("[D4] Friendly fire event → no threat");
		playerAi.ImmediateThreat = false;
		ImmediateThreatSignal.NotifyIncomingFire(m_Ally.GetComponent<UnitTeam>(), m_Player.transform);
		playerAi.Tick(0.05f);
		Check("D4_NoThreat", !playerAi.ImmediateThreat, "friendly event set threat");

		AppendLine("[D5/D6] Attack on player does not set ally/enemy/neutral");
		ImmediateThreatSignal.NotifyIncomingFire(m_Enemy.GetComponent<UnitTeam>(), m_Player.transform);
		playerAi.Tick(0.05f);
		Check("D5_AllyClear", !m_Ally.GetComponent<UnitAIController>().ImmediateThreat, "ally received threat");
		Check("D6_EnemyClear", !m_Enemy.GetComponent<UnitAIController>().ImmediateThreat, "attacker received threat");
		Check("D6_NeutralClear", !m_Neutral.GetComponent<UnitAIController>().ImmediateThreat, "neutral received threat");

		AppendLine("[F] Four sides present");
		Check("F_Player", m_Player != null && m_Player.GetComponent<UnitTeam>().Team == UnitTeamId.Player, "player");
		Check("F_Enemy", m_Enemy != null && m_Enemy.GetComponent<UnitTeam>().Team == UnitTeamId.Enemy, "enemy");
		Check("F_Ally", m_Ally != null && m_Ally.GetComponent<UnitTeam>().Team == UnitTeamId.Player, "ally");
		Check("F_Neutral", m_Neutral != null && m_Neutral.GetComponent<UnitTeam>().Team == UnitTeamId.Neutral, "neutral");

		bool threatLog = FindThreatLog();
		Check("Log_THREAT", threatLog || !UnitActionLog.Enabled, "THREAT line missing in session folder");

		DestroyActors();
		Finish();
		yield return null;
	}

	private void SpawnFourSides()
	{
		DestroyActors();
		m_Player = CreateCombatActor("IT_Player", UnitTeamId.Player);
		m_Enemy = CreateCombatActor("IT_Enemy", UnitTeamId.Enemy);
		m_Ally = CreateCombatActor("IT_Ally", UnitTeamId.Player);
		m_Neutral = CreateCombatActor("IT_Neutral", UnitTeamId.Neutral);
		m_Enemy.transform.position = new Vector3(12f, 0f, 0f);
		m_Ally.transform.position = new Vector3(-4f, 0f, 0f);
		m_Neutral.transform.position = new Vector3(0f, 0f, 8f);
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
		go.AddComponent<UnitAIController>();
		go.SetActive(true);
		go.GetComponent<UnitAIController>().EnsureImmediateThreatSource();
		go.GetComponent<DetectionProcessor>().SetSimulatedTime(0f);
		return go;
	}

	private void ArmSelfDefense(
		UnitAIController _ai,
		DetectionProcessor _processor,
		TargetSelector _selector,
		EngagementDecisionController _engagement,
		bool _notify)
	{
		_ai.ImmediateThreat = false;
		_ai.TrySetUseOfForcePolicy(UseOfForceLevel.SelfDefense);
		_ai.TryApplyCommand(
			UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		_ai.SetPerceptionFrame(HostileVisibleFrame(m_Enemy.transform));
		_ai.Tick(0.05f);
		_processor.SetAffiliationCue(m_Enemy.transform, ObservableAffiliation.Hostile);
		Vector3 position = m_Enemy.transform.position;
		float now = 0f;
		for (int i = 0; i < 44; i++)
		{
			_processor.ApplySyntheticObservation(m_Enemy.transform, 15f, 0f, 1f, position);
			now += 0.05f;
			_processor.Advance(0.05f, now);
		}

		_selector.SetSelectedTargetForDiagnostics(m_Enemy.transform, m_Enemy.transform.position);
		if (_notify)
		{
			ImmediateThreatSignal.NotifyIncomingFire(m_Enemy.GetComponent<UnitTeam>(), m_Player.transform);
			_ai.Tick(0.05f);
		}

		_engagement.RefreshDecisionNow();
	}

	private static AIPerceptionFrame HostileVisibleFrame(Transform _target)
	{
		AIContactKnowledge contact = new AIContactKnowledge(
			_target,
			DetectionState.Detected,
			ObservationState.Observed,
			PerceivedIdentity.Hostile,
			1f,
			PerceivedRelationship.Hostile,
			ThreatLevel.High,
			_target.position,
			_target.position,
			0f,
			1f,
			true,
			false,
			false,
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
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			contact.Threat);
	}

	private static bool FindThreatLog()
	{
		string folder = UnitActionLogSession.Folder;
		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			return false;
		UnitActionLogSession.FlushAll();
		string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
		for (int i = 0; i < files.Length; i++)
		{
			string name = files[i];
			if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				continue;
			string fileName = Path.GetFileName(name);
			if (string.Equals(fileName, "_index.txt", StringComparison.OrdinalIgnoreCase))
				continue;
			if (!TryReadSharedText(name, out string text))
				continue;
			if (text.IndexOf("THREAT", StringComparison.Ordinal) >= 0)
				return true;
		}

		return false;
	}

	private static bool TryReadSharedText(string _path, out string _text)
	{
		_text = string.Empty;
		try
		{
			using (var stream = new FileStream(
				       _path,
				       FileMode.Open,
				       FileAccess.Read,
				       FileShare.ReadWrite | FileShare.Delete))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true))
			{
				_text = reader.ReadToEnd();
			}

			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private void DestroyActors()
	{
		DestroyIfAlive(ref m_Player);
		DestroyIfAlive(ref m_Enemy);
		DestroyIfAlive(ref m_Ally);
		DestroyIfAlive(ref m_Neutral);
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
		string path = Path.Combine(dir, "ImmediateThreatLive_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[ImmediateThreatLiveRuntimeSmoke] wrote " + path +
			" RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount,
			this);

		bool exitPlay = !DetectionHarnessPlayMode.RunFrozenLayersPlay &&
		                (m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunImmediateThreatLive);
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
		Debug.LogError("[ImmediateThreatLiveRuntimeSmoke] FAIL " + _name + " | " + _detail, this);
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
