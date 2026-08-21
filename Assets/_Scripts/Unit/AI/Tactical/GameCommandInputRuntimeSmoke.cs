using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Stage 6.3 game command input Play. Spawns light Player/Enemy/Neutral units on the harness.
/// Programmatic ConfirmPoint: selected Attack, Enemy Defense, Enemy Retreat.
/// Does not retune Vision / Combat. Report: Assets/_Docs/Logs/Tests/GameCommandInput_LAST.txt
/// </summary>
[DefaultExecutionOrder(64)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class GameCommandInputRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private readonly List<GameObject> m_Spawned = new List<GameObject>(8);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunGameCommandInput) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.RunCombatEngageExecution &&
		!DetectionHarnessPlayMode.RunSearchExecution &&
		!DetectionHarnessPlayMode.RunTacticalNavigationExecution &&
		!DetectionHarnessPlayMode.RunTacticalCommandContract &&
		!DetectionHarnessPlayMode.RunGameCommandSource &&
		!DetectionHarnessPlayMode.RunGameCommandLayer &&
		!DetectionHarnessPlayMode.IsGRegressionPlay;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[GameCommandInputRuntimeSmoke] Stage 6.3 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroySpawned();
		if (DetectionHarnessPlayMode.RunGameCommandInput)
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
		AppendLine("STAGE 6.3 — GAME COMMAND INPUT");
		AppendLine("==============================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("GameCommandInput → IssueMany. Selected Attack / Enemy Defense / Enemy Retreat.");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		Check("Harness", m_Harness != null, m_Harness != null ? "ok" : "DetectionTestController missing");

		GameCommandInput input = GameCommandInput.Instance;
		if (input == null && m_Harness != null)
			input = m_Harness.gameObject.AddComponent<GameCommandInput>();
		Check("Input", input != null, input != null ? "GameCommandInput ready" : "missing");

		Vector3 origin = m_Harness != null && m_Harness.Observer != null
			? m_Harness.Observer.position
			: Vector3.zero;

		UnitAIController[] players = new UnitAIController[3];
		UnitAIController[] enemies = new UnitAIController[4];
		for (int i = 0; i < 3; i++)
			players[i] = SpawnInfantry("AI63_P" + i, UnitTeamId.Player, origin + new Vector3(i, 0f, 0f), true);
		for (int i = 0; i < 4; i++)
			SpawnInfantry("AI63_E" + i, UnitTeamId.Enemy, origin + new Vector3(i, 0f, 2f), false);
		UnitAIController neutral = SpawnInfantry("AI63_N0", UnitTeamId.Neutral, origin + new Vector3(0f, 0f, 4f), true);

		Vector3 pointA = origin + new Vector3(10f, 0f, 10f);
		Vector3 pointB = origin + new Vector3(20f, 0f, 5f);
		Vector3 pointC = origin + new Vector3(-8f, 0f, 4f);

		if (input != null)
		{
			input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
			int accepted = input.ConfirmPoint(pointA, new Component[] { players[0], players[1] });
			Check("Player_SelectedAttack_Count", accepted == 2, "accepted=" + accepted);
			Check("Player0_Attack", IsState(players[0], UnitAIState.Attack, pointA, false),
				Describe(players[0]));
			Check("Player1_Attack", IsState(players[1], UnitAIState.Attack, pointA, false),
				Describe(players[1]));
			Check("Player2_UnchangedIdle", players[2] != null && players[2].CurrentState == UnitAIState.Idle,
				Describe(players[2]));
			Check("Neutral_IdleAfterPlayerAttack",
				neutral != null && neutral.CurrentState == UnitAIState.Idle, Describe(neutral));
			Check("Command_NotFire",
				players[0] != null && players[0].CurrentCombatIntent == CombatIntent.Hold,
				players[0] != null ? players[0].CurrentCombatIntent.ToString() : "null");

			input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
			accepted = input.ConfirmPoint(pointB);
			Check("Enemy_Defense_Accepted", accepted >= 4, "accepted=" + accepted);
			for (int i = 0; i < 4; i++)
			{
				GameObject go = FindSpawned("AI63_E" + i);
				enemies[i] = go != null ? go.GetComponent<UnitAIController>() : null;
				Check("Enemy" + i + "_Defense", IsState(enemies[i], UnitAIState.Defense, pointB, true),
					Describe(enemies[i]));
			}

			Check("Player0_StillAttack", IsState(players[0], UnitAIState.Attack, pointA, false),
				Describe(players[0]));
			Check("Player2_StillIdle", players[2] != null && players[2].CurrentState == UnitAIState.Idle,
				Describe(players[2]));
			Check("Neutral_IdleAfterEnemyDefense",
				neutral != null && neutral.CurrentState == UnitAIState.Idle, Describe(neutral));

			input.BeginPending(GameCommandInputMode.RetreatPending, GameCommandAudience.EnemyDebug);
			accepted = input.ConfirmPoint(pointC);
			Check("Enemy_Retreat_Accepted", accepted >= 4, "accepted=" + accepted);
			for (int i = 0; i < 4; i++)
			{
				Check("Enemy" + i + "_Retreat", IsState(enemies[i], UnitAIState.Retreat, pointC, false),
					Describe(enemies[i]));
			}

			Check("Player0_StillAttackAfterRetreat",
				IsState(players[0], UnitAIState.Attack, pointA, false), Describe(players[0]));
			Check("Neutral_IdleAfterEnemyRetreat",
				neutral != null && neutral.CurrentState == UnitAIState.Idle, Describe(neutral));
		}

		DestroySpawned();
		Finish();
	}

	private UnitAIController SpawnInfantry(string _name, UnitTeamId _team, Vector3 _position, bool _withAi)
	{
		var go = new GameObject(_name);
		go.transform.position = _position;
		go.AddComponent<UnitTeam>().SetTeam(_team);
		go.AddComponent<UnitHealth>();
		UnitAIController ai = _withAi ? go.AddComponent<UnitAIController>() : null;
		m_Spawned.Add(go);
		return ai;
	}

	private GameObject FindSpawned(string _name)
	{
		for (int i = 0; i < m_Spawned.Count; i++)
		{
			if (m_Spawned[i] != null && m_Spawned[i].name == _name)
				return m_Spawned[i];
		}

		return null;
	}

	private void DestroySpawned()
	{
		for (int i = 0; i < m_Spawned.Count; i++)
		{
			if (m_Spawned[i] != null)
				Destroy(m_Spawned[i]);
		}

		m_Spawned.Clear();
	}

	private static bool IsState(UnitAIController _ai, UnitAIState _state, Vector3 _point, bool _defenseAnchor)
	{
		if (_ai == null)
			return false;
		if (_ai.CurrentState != _state)
			return false;
		Vector3 actual = _defenseAnchor ? _ai.CurrentContext.AnchorPosition : _ai.CurrentContext.Destination;
		return (_point - actual).sqrMagnitude < 0.05f * 0.05f;
	}

	private static string Describe(UnitAIController _ai)
	{
		if (_ai == null)
			return "ai=null";
		return "state=" + _ai.CurrentState +
		       " dest=" + _ai.CurrentContext.Destination +
		       " anchor=" + _ai.CurrentContext.AnchorPosition +
		       " intent=" + _ai.CurrentCombatIntent;
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "GameCommandInput_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[GameCommandInputRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunGameCommandInput;
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
			Debug.LogError($"[GameCommandInputRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}
