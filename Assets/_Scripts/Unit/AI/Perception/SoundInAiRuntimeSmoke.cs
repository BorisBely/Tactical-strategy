using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #9 Play: WorldSoundHub → SoundContact → AI snapshot → Defense/Attack Search.
/// CombatEventHub stays independent. Does not retune Stage 16 decay or #7 RoE.
/// Report: Assets/_Docs/Logs/Tests/SoundInAi_LAST.txt
/// Menu: Tools/Tests/Run Sound In AI (Play)
/// </summary>
[DefaultExecutionOrder(64)]
[DisallowMultipleComponent]
public sealed class SoundInAiRuntimeSmoke : MonoBehaviour
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
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunSoundInAi;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunSoundInAi)
			return;
		if (FindAnyObjectByType<SoundInAiRuntimeSmoke>() != null)
			return;
		var go = new GameObject("SoundInAiRuntimeSmoke");
		go.AddComponent<SoundInAiRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunSoundInAi)
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
		AppendLine("STAGE 9 — SOUND / REPORTS IN AI");
		AppendLine("===============================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("WorldSoundHub → SoundContact → snapshot → Search. CombatEventHub independent.");
		AppendLine("---");

		SpawnActors();
		UnitAIController playerAi = m_Player.GetComponent<UnitAIController>();
		DetectionProcessor processor = m_Player.GetComponent<DetectionProcessor>();
		Vector3 soundPos = m_Enemy.transform.position;

		AppendLine("[R8] CombatEvent Gunshot does not create SoundContact");
		int hubBefore = CombatEventHub.PublishCount;
		CombatEventHub.Publish(CombatEvent.Gunshot(
			m_Enemy.GetComponent<UnitTeam>(),
			m_Enemy.GetComponent<UnitTeam>(),
			m_Player.transform,
			soundPos));
		AIPerceptionFrame afterEvent = AIPerceptionFrameBuilder.Build(processor);
		Check("R8_CombatEventPublished", CombatEventHub.PublishCount == hubBefore + 1, "count=" + CombatEventHub.PublishCount);
		Check("R8_NoSoundFromEvent", afterEvent.SoundContacts.Count == 0, "sounds=" + afterEvent.SoundContacts.Count);
		Check("R8_NoVisualFromEvent", afterEvent.AllContacts.Count == 0, "visual=" + afterEvent.AllContacts.Count);

		AppendLine("[A/B] WorldSound Gunshot → SoundContact, Observed false");
		WorldSoundHub.PublishGunshot(m_Enemy.transform, soundPos);
		AIPerceptionFrame heard = AIPerceptionFrameBuilder.Build(processor);
		Check("A_SoundCount", heard.SoundContacts.Count == 1, "sounds=" + heard.SoundContacts.Count);
		Check(
			"A_TypeGunshot",
			heard.SoundContacts.Count > 0 && heard.SoundContacts[0].Type == SoundEventType.Gunshot,
			heard.SoundContacts.Count > 0 ? heard.SoundContacts[0].Type.ToString() : "none");
		Check("A_Hostile", heard.SoundContacts.Count > 0 && heard.SoundContacts[0].Hostile, "hostile cue");
		Check("B_NoVisual", heard.VisibleContacts.Count == 0, "visible=" + heard.VisibleContacts.Count);
		Check("B_NoAllVisual", heard.AllContacts.Count == 0, "all=" + heard.AllContacts.Count);
		bool hasContact = processor.TryGetContact(m_Enemy.transform, out PerceivedContact raw);
		Check("B_NotObserved", hasContact && raw.ObservationState == ObservationState.NotObserved, "obs");
		Check("B_IdentityUnknown", hasContact && raw.Identity == PerceivedIdentity.Unknown, "id");
		Check("B_LastKnownEmpty", hasContact && raw.LastKnownPosition == Vector3.zero, "lastKnown");
		Check("R8_HubUnchangedBySound", CombatEventHub.PublishCount == hubBefore + 1, "count=" + CombatEventHub.PublishCount);

		AppendLine("[E1] Defense + hostile gunshot → Search at SoundPosition");
		bool defended = playerAi.TryApplyCommand(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		Check("E1_DefenseIssued", defended, "defense command");
		playerAi.Tick(0.05f);
		Check("E1_Search", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		Check(
			"E1_SoundCue",
			playerAi.CurrentContext.SearchCue == UnitAISearchCue.Sound,
			playerAi.CurrentContext.SearchCue.ToString());
		Check(
			"E1_SearchPos",
			Approximately(playerAi.CurrentContext.SearchPosition, soundPos),
			playerAi.CurrentContext.SearchPosition.ToString());

		AppendLine("[E3] Idle + sound → no Search");
		GameObject idleGo = CreateCombatActor("S9_Idle", UnitTeamId.Player);
		UnitAIController idleAi = idleGo.GetComponent<UnitAIController>();
		idleGo.GetComponent<DetectionProcessor>().ApplySyntheticSound(
			m_Enemy.transform,
			soundPos,
			0.9f,
			SoundEventType.Gunshot);
		idleAi.BindPerception(idleGo.GetComponent<DetectionProcessor>());
		idleAi.Tick(0.05f);
		Check("E3_Idle", idleAi.CurrentState == UnitAIState.Idle, idleAi.CurrentState.ToString());
		Destroy(idleGo);

		AppendLine("[E5] Attack + VisibleNow → sound does not reset Attack");
		AIContactKnowledge visible = VisibleHostile(m_Enemy.transform);
		playerAi.TryApplyCommand(UnitAICommand.Idle());
		playerAi.TryApplyCommand(UnitAICommand.Attack(
			UnitAIStateContext.ForAttack(new Vector3(2f, 0f, 0f), Vector3.forward, m_Enemy.transform)));
		playerAi.SetPerceptionFrame(new AIPerceptionFrame(
			new[] { visible },
			new[] { visible },
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			new[] { visible },
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High,
			heard.SoundContacts,
			Array.Empty<AIReportContact>()));
		playerAi.Tick(0.05f);
		Check("E5_StayAttack", playerAi.CurrentState == UnitAIState.Attack, playerAi.CurrentState.ToString());

		AppendLine("[E found] Search + VisibleNow → leave Search");
		playerAi.TryApplyCommand(UnitAICommand.Idle());
		playerAi.TryApplyCommand(UnitAICommand.Defense(
			UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
		playerAi.SetPerceptionFrame(new AIPerceptionFrame(
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.None,
			heard.SoundContacts,
			Array.Empty<AIReportContact>()));
		playerAi.Tick(0.05f);
		Check("Found_EnterSearch", playerAi.CurrentState == UnitAIState.Search, playerAi.CurrentState.ToString());
		playerAi.SetPerceptionFrame(new AIPerceptionFrame(
			new[] { visible },
			new[] { visible },
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			new[] { visible },
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High));
		playerAi.Tick(0.05f);
		Check("Found_ResumeDefense", playerAi.CurrentState == UnitAIState.Defense, playerAi.CurrentState.ToString());

		DestroyActors();
		Finish();
		yield return null;
	}

	private void SpawnActors()
	{
		DestroyActors();
		m_Player = CreateCombatActor("S9_Player", UnitTeamId.Player);
		m_Enemy = CreateCombatActor("S9_Enemy", UnitTeamId.Enemy);
		m_Ally = CreateCombatActor("S9_Ally", UnitTeamId.Player);
		m_Enemy.transform.position = new Vector3(12f, 0f, 0f);
		m_Ally.transform.position = new Vector3(-4f, 0f, 0f);
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
		go.GetComponent<DetectionProcessor>().SetSimulatedTime(0f);
		return go;
	}

	private static AIContactKnowledge VisibleHostile(Transform _target)
	{
		return new AIContactKnowledge(
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
	}

	private static bool Approximately(Vector3 _a, Vector3 _b)
	{
		return (_a - _b).sqrMagnitude < 0.05f;
	}

	private void DestroyActors()
	{
		DestroyIfAlive(ref m_Player);
		DestroyIfAlive(ref m_Enemy);
		DestroyIfAlive(ref m_Ally);
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
		string path = Path.Combine(dir, "SoundInAi_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[SoundInAiRuntimeSmoke] wrote " + path +
			" RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount,
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunSoundInAi;
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
		Debug.LogError("[SoundInAiRuntimeSmoke] FAIL " + _name + " | " + _detail, this);
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
