using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #8 Play: world CombatEvent bus. event ≠ knowledge. Does not retune #7 RoE.
/// Report: Assets/_Docs/Logs/Tests/CombatEvent_LAST.txt
/// Menu: Tools/Tests/Run Combat Event World (Play)
/// </summary>
[DefaultExecutionOrder(63)]
[DisallowMultipleComponent]
public sealed class CombatEventWorldRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private int m_PassCount;
	private int m_FailCount;
	private int m_ListenerHits;
	private CombatEvent m_LastHeard;
	private GameObject m_Player;
	private GameObject m_Enemy;
	private GameObject m_Ally;
	private GameObject m_Neutral;
	private GameObject m_Bystander;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunCombatEventWorld;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCombatEventWorld)
			return;
		if (FindAnyObjectByType<CombatEventWorldRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CombatEventWorldRuntimeSmoke");
		go.AddComponent<CombatEventWorldRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		CombatEventHub.Unsubscribe(OnHeard);
		DestroyActors();
		if (DetectionHarnessPlayMode.RunCombatEventWorld)
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
		m_ListenerHits = 0;
		m_LastHeard = default;
		AppendLine("STAGE 8 — COMBAT EVENT WORLD");
		AppendLine("============================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("CombatEvent = world fact. event ≠ knowledge. Impact/Death ≠ ImmediateThreat.");
		AppendLine("---");

		Check("Log_Enabled", UnitActionLog.Enabled, "UnitActionLog session off in Editor Play");
		Check(
			"Log_Folder",
			!string.IsNullOrEmpty(UnitActionLogSession.Folder) && Directory.Exists(UnitActionLogSession.Folder),
			UnitActionLogSession.Folder ?? "null");

		CombatEventHub.ResetForTests();
		CombatEventHub.Subscribe(OnHeard);

		AppendLine("[E2] WorldSoundHub does not publish CombatEvent");
		GameObject soundShooter = CreateBareActor("CE_SoundShooter", UnitTeamId.Enemy);
		int hubBeforeSound = CombatEventHub.PublishCount;
		WorldSoundHub.PublishGunshot(soundShooter.transform, soundShooter.transform.position);
		Check(
			"E2_SoundNotCombatEvent",
			CombatEventHub.PublishCount == hubBeforeSound,
			"count=" + CombatEventHub.PublishCount);
		DestroyIfAlive(ref soundShooter);

		SpawnFourSides();
		UnitAIController playerAi = m_Player.GetComponent<UnitAIController>();
		DetectionProcessor processor = m_Player.GetComponent<DetectionProcessor>();
		ImmediateThreatSource playerThreat = playerAi.EnsureImmediateThreatSource();

		AppendLine("[E1] Gunshot fact delivers; contacts stay empty; WorldSound delivery unchanged");
		int soundBefore = WorldSoundHub.LastPublishDeliveryCount;
		int contactsBefore = processor.Contacts.Count;
		int hubBeforeGunshot = CombatEventHub.PublishCount;
		CombatEventHub.Publish(CombatEvent.Gunshot(
			m_Enemy.GetComponent<UnitTeam>(),
			m_Enemy.GetComponent<UnitTeam>(),
			null,
			m_Enemy.transform.position));
		Check(
			"E1_PublishCount",
			CombatEventHub.PublishCount == hubBeforeGunshot + 1,
			"count=" + CombatEventHub.PublishCount);
		Check(
			"E1_TypeGunshot",
			CombatEventHub.LastPublished.Type == CombatEventType.Gunshot,
			CombatEventHub.LastPublished.Type.ToString());
		Check("E1_Listener", m_ListenerHits == 1 && m_LastHeard.Type == CombatEventType.Gunshot, "hits=" + m_ListenerHits);
		Check(
			"E1_NoKnowledge",
			processor.Contacts.Count == contactsBefore,
			"contacts=" + processor.Contacts.Count);
		Check(
			"E1_SoundUnchanged",
			WorldSoundHub.LastPublishDeliveryCount == soundBefore,
			"sound=" + WorldSoundHub.LastPublishDeliveryCount);
		Check("E1_Idle", playerAi.CurrentState == UnitAIState.Idle, playerAi.CurrentState.ToString());

		AppendLine("[E3] Hostile Gunshot aimed at player → ImmediateThreat IncomingFire, still no knowledge");
		ClearThreat(playerAi);
		CombatEventHub.Publish(CombatEvent.Gunshot(
			m_Enemy.GetComponent<UnitTeam>(),
			m_Enemy.GetComponent<UnitTeam>(),
			m_Player.transform,
			m_Player.transform.position));
		Check("E3_ThreatOn", playerAi.ImmediateThreat, "incoming gunshot did not set threat");
		Check(
			"E3_IncomingFire",
			playerThreat.LastCause == ImmediateThreatCause.IncomingFire,
			playerThreat.LastCause.ToString());
		Check("E3_NoKnowledge", processor.Contacts.Count == contactsBefore, "contacts=" + processor.Contacts.Count);
		Check("E3_Idle", playerAi.CurrentState == UnitAIState.Idle, playerAi.CurrentState.ToString());

		AppendLine("[E4] Hostile Hit → ConfirmedHit");
		ClearThreat(playerAi);
		CombatEventHub.Publish(CombatEvent.Hit(
			m_Enemy.GetComponent<UnitTeam>(),
			m_Enemy.GetComponent<UnitTeam>(),
			playerAi,
			m_Player.transform.position));
		Check("E4_ThreatOn", playerAi.ImmediateThreat, "hit did not set threat");
		Check(
			"E4_ConfirmedHit",
			playerThreat.LastCause == ImmediateThreatCause.ConfirmedHit,
			playerThreat.LastCause.ToString());

		AppendLine("[E5] Friendly Gunshot publishes, no threat");
		ClearThreat(playerAi);
		int friendlyBefore = CombatEventHub.PublishCount;
		CombatEventHub.Publish(CombatEvent.Gunshot(
			m_Ally.GetComponent<UnitTeam>(),
			m_Ally.GetComponent<UnitTeam>(),
			m_Player.transform,
			m_Player.transform.position));
		Check("E5_Published", CombatEventHub.PublishCount == friendlyBefore + 1, "count=" + CombatEventHub.PublishCount);
		Check("E5_NoThreat", !playerAi.ImmediateThreat, "friendly gunshot set threat");

		AppendLine("[E6] Impact does not set ImmediateThreat");
		ClearThreat(playerAi);
		CombatEventHub.Publish(CombatEvent.Impact(
			m_Enemy.GetComponent<UnitTeam>(),
			m_Enemy.GetComponent<UnitTeam>(),
			null,
			Vector3.zero));
		Check(
			"E6_TypeImpact",
			CombatEventHub.LastPublished.Type == CombatEventType.Impact,
			CombatEventHub.LastPublished.Type.ToString());
		Check("E6_NoThreat", !playerAi.ImmediateThreat, "impact set threat");

		AppendLine("[E7] UnitHealth death publishes Death, no threat");
		ClearThreat(playerAi);
		UnitHealth health = m_Player.AddComponent<UnitHealth>();
		health.EnterDead();
		Check(
			"E7_TypeDeath",
			CombatEventHub.LastPublished.Type == CombatEventType.Death,
			CombatEventHub.LastPublished.Type.ToString());
		Check("E7_DeathTarget", CombatEventHub.LastPublished.Target == health, "target mismatch");
		Check("E7_NoThreat", !playerAi.ImmediateThreat, "death set threat");

		AppendLine("[E8] Aimed Gunshot isolates: only the aimed victim");
		ClearThreat(playerAi);
		ClearThreat(m_Bystander.GetComponent<UnitAIController>());
		ClearThreat(m_Ally.GetComponent<UnitAIController>());
		ClearThreat(m_Enemy.GetComponent<UnitAIController>());
		ClearThreat(m_Neutral.GetComponent<UnitAIController>());
		CombatEventHub.Publish(CombatEvent.Gunshot(
			m_Enemy.GetComponent<UnitTeam>(),
			m_Enemy.GetComponent<UnitTeam>(),
			m_Player.transform,
			m_Player.transform.position));
		Check("E8_VictimThreat", playerAi.ImmediateThreat, "aimed victim missed threat");
		Check(
			"E8_BystanderClear",
			!m_Bystander.GetComponent<UnitAIController>().ImmediateThreat,
			"bystander received threat");
		Check("E8_AllyClear", !m_Ally.GetComponent<UnitAIController>().ImmediateThreat, "ally received threat");
		Check("E8_EnemyClear", !m_Enemy.GetComponent<UnitAIController>().ImmediateThreat, "attacker received threat");
		Check(
			"E8_NeutralClear",
			!m_Neutral.GetComponent<UnitAIController>().ImmediateThreat,
			"neutral received threat");

		AppendLine("[E9] DamageableTarget HP death publishes Death");
		var hpGo = new GameObject("CE_HpTarget");
		DamageableTarget hpTarget = hpGo.AddComponent<DamageableTarget>();
		hpTarget.SetMaxHealth(1f, true);
		hpTarget.ApplyDamage(10f, Vector3.zero, Vector3.up, Vector3.forward, null);
		Check(
			"E9_TypeDeath",
			CombatEventHub.LastPublished.Type == CombatEventType.Death,
			CombatEventHub.LastPublished.Type.ToString());
		Check("E9_DeathTarget", CombatEventHub.LastPublished.Target == hpTarget, "target mismatch");
		Destroy(hpGo);

		AppendLine("[E10] #7 Signal API still sets threat without publishing CombatEvent");
		ClearThreat(playerAi);
		int hubBeforeSignal = CombatEventHub.PublishCount;
		ImmediateThreatSignal.NotifyIncomingFire(m_Enemy.GetComponent<UnitTeam>(), m_Player.transform);
		Check("E10_ThreatOn", playerAi.ImmediateThreat, "signal did not set threat");
		Check(
			"E10_NoHubPublish",
			CombatEventHub.PublishCount == hubBeforeSignal,
			"count=" + CombatEventHub.PublishCount);

		AppendLine("[F] Four sides present");
		Check("F_Player", m_Player != null && m_Player.GetComponent<UnitTeam>().Team == UnitTeamId.Player, "player");
		Check("F_Enemy", m_Enemy != null && m_Enemy.GetComponent<UnitTeam>().Team == UnitTeamId.Enemy, "enemy");
		Check("F_Ally", m_Ally != null && m_Ally.GetComponent<UnitTeam>().Team == UnitTeamId.Player, "ally");
		Check("F_Neutral", m_Neutral != null && m_Neutral.GetComponent<UnitTeam>().Team == UnitTeamId.Neutral, "neutral");

		bool threatLog = FindThreatLog();
		Check("Log_THREAT", threatLog || !UnitActionLog.Enabled, "THREAT line missing in session folder");

		CombatEventHub.Unsubscribe(OnHeard);
		DestroyActors();
		Finish();
		yield return null;
	}

	private void SpawnFourSides()
	{
		DestroyActors();
		m_Player = CreateCombatActor("CE_Player", UnitTeamId.Player);
		m_Enemy = CreateCombatActor("CE_Enemy", UnitTeamId.Enemy);
		m_Ally = CreateCombatActor("CE_Ally", UnitTeamId.Player);
		m_Neutral = CreateCombatActor("CE_Neutral", UnitTeamId.Neutral);
		m_Bystander = CreateCombatActor("CE_Bystander", UnitTeamId.Player);
		m_Enemy.transform.position = new Vector3(12f, 0f, 0f);
		m_Ally.transform.position = new Vector3(-4f, 0f, 0f);
		m_Neutral.transform.position = new Vector3(0f, 0f, 8f);
		m_Bystander.transform.position = new Vector3(0f, 0f, -8f);
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

	private static GameObject CreateBareActor(string _name, UnitTeamId _team)
	{
		var go = new GameObject(_name);
		go.AddComponent<UnitTeam>().SetTeam(_team);
		return go;
	}

	private static void ClearThreat(UnitAIController _ai)
	{
		if (_ai != null)
			_ai.ImmediateThreat = false;
	}

	private void OnHeard(CombatEvent _evt)
	{
		m_ListenerHits++;
		m_LastHeard = _evt;
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
		DestroyIfAlive(ref m_Bystander);
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
		string path = Path.Combine(dir, "CombatEvent_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CombatEventWorldRuntimeSmoke] wrote " + path +
			" RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount,
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCombatEventWorld;
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
		Debug.LogError("[CombatEventWorldRuntimeSmoke] FAIL " + _name + " | " + _detail, this);
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
