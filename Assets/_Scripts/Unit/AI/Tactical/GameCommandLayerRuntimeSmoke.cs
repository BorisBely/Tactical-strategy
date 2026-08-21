using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Stage 6.4 command-layer Play. Observer walk via GameCommandInput.ConfirmPoint,
/// plus mass Player/Enemy isolation. Not DebugGameCommandSource, not overlay, not SHOT.
/// Report: Assets/_Docs/Logs/Tests/GameCommandLayer_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class GameCommandLayerRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_SimDt = 0.05f;
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(12288);
	private readonly List<GameObject> m_Spawned = new List<GameObject>(16);
	private int m_PassCount;
	private int m_FailCount;
	private DetectionProcessor m_Processor;
	private Transform m_Target;
	private Transform m_Observer;
	private UnitVision m_Vision;
	private bool m_VisionWasEnabled;
	private UnitAIController m_Controller;
	private UnitNavLocomotionDriver m_Driver;
	private UnitClickToMove m_ClickToMove;
	private RtsUnitMember m_RtsMember;
	private bool m_DriverWasEnabled;
	private bool m_ClickWasEnabled;
	private bool m_RtsWasEnabled;
	private Vector3 m_ObserverStart;
	private Quaternion m_ObserverStartRot;
	private NavMeshAgent m_TargetAgent;
	private UnitNavLocomotionDriver m_TargetDriver;
	private bool m_TargetAgentWasEnabled;
	private bool m_TargetDriverWasEnabled;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunGameCommandLayer) &&
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
		!DetectionHarnessPlayMode.RunGameCommandInput &&
		!DetectionHarnessPlayMode.IsGRegressionPlay;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[GameCommandLayerRuntimeSmoke] Stage 6.4 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroySpawned();
		RestoreNav();
		if (DetectionHarnessPlayMode.RunGameCommandLayer)
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
		AppendLine("STAGE 6.4 — GAME COMMAND LAYER");
		AppendLine("==============================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("GameCommandInput.ConfirmPoint. Walk + mass isolation. No SHOT criterion.");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		m_Processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		m_Target = m_Harness != null ? m_Harness.Target : null;
		m_Observer = m_Harness != null ? m_Harness.Observer : null;
		Check("Harness_Processor", m_Processor != null, "DetectionProcessor missing");
		Check("Harness_Target", m_Target != null, "Target missing");
		Check("Harness_Observer", m_Observer != null, "observer missing");

		GameCommandInput input = GameCommandInput.Instance;
		if (input == null && m_Harness != null)
			input = m_Harness.gameObject.AddComponent<GameCommandInput>();
		Check("Input", input != null, input != null ? "GameCommandInput ready" : "missing");

		if (m_Processor == null || m_Target == null || m_Observer == null || input == null)
		{
			Finish();
			yield break;
		}

		BindObserver(m_Observer.gameObject);
		if (m_Processor.TryGetComponent(out m_Vision) && m_Vision != null)
		{
			m_VisionWasEnabled = m_Vision.enabled;
			m_Vision.enabled = false;
		}

		yield return null;
		yield return RunObserverWalk(input);
		yield return RunMassIsolation(input);

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		if (m_Processor != null)
		{
			m_Processor.ClearSimulatedTime();
			m_Processor.ClearAffiliationCue(m_Target);
		}

		DestroySpawned();
		RestoreNav();
		Finish();
		yield return null;
	}

	private IEnumerator RunObserverWalk(GameCommandInput _input)
	{
		AppendLine("---");
		AppendLine("[W1] ConfirmPoint Attack → Walk reason=Attack");
		ParkTargetForWalk();
		ResetForWalkOrder();
		Vector3 attackDest = SampleAway(20f, AwayFromTarget());
		_input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
		int accepted = _input.ConfirmPoint(attackDest, new Component[] { m_Controller });
		Check("W1_Accepted", accepted == 1, "accepted=" + accepted);
		Check("W1_Attack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
		Check("W1_Dest", Approximately(m_Controller.CurrentContext.Destination, attackDest),
			m_Controller.CurrentContext.Destination.ToString());
		yield return null;
		Check("W1_Issued",
			m_Controller.TacticalNavigationIssued || m_Controller.SearchHasMoveIntent,
			$"issued={m_Controller.TacticalNavigationIssued} reason={m_Controller.CurrentNavigationReason}");
		Check("W1_NoFire", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			m_Controller.CurrentCombatIntent.ToString());
		Check("W1_NoCombatTarget", m_Controller.CurrentEngageTarget == null, "engage target set");

		float startDist = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, attackDest);
		bool moved = false;
		float until = Time.unscaledTime + 8f;
		while (Time.unscaledTime < until)
		{
			float now = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, attackDest);
			if (now < startDist - 0.35f)
			{
				moved = true;
				break;
			}

			yield return null;
		}

		Check("W1_Walked", moved, FormatWalkDetail(startDist, attackDest));

		AppendLine("[W2] Defense → Walk to anchor, stay Defense");
		Vector3 defense = SampleAway(12f, Vector3.right);
		_input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.PlayerSelected);
		accepted = _input.ConfirmPoint(defense, new Component[] { m_Controller });
		Check("W2_Accepted", accepted == 1, "accepted=" + accepted);
		Check("W2_Defense", m_Controller.CurrentState == UnitAIState.Defense, m_Controller.CurrentState.ToString());
		Check("W2_Anchor", Approximately(m_Controller.CurrentContext.AnchorPosition, defense),
			m_Controller.CurrentContext.AnchorPosition.ToString());
		Check("W2_Dest", Approximately(m_Controller.CurrentContext.Destination, defense),
			m_Controller.CurrentContext.Destination.ToString());
		yield return null;
		Check("W2_Issued",
			m_Controller.TacticalNavigationIssued || m_Controller.SearchHasMoveIntent,
			$"issued={m_Controller.TacticalNavigationIssued} reason={m_Controller.CurrentNavigationReason}");

		float defenseStart = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, defense);
		bool defenseMoved = false;
		float defenseUntil = Time.unscaledTime + 8f;
		while (Time.unscaledTime < defenseUntil)
		{
			float now = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, defense);
			if (now < defenseStart - 0.35f)
			{
				defenseMoved = true;
				break;
			}

			yield return null;
		}

		Check("W2_Walked", defenseMoved, FormatWalkDetail(defenseStart, defense));
		Check("W2_StillDefense", m_Controller.CurrentState == UnitAIState.Defense,
			m_Controller.CurrentState.ToString());

		AppendLine("[W3] Retreat then Cancel → Idle");
		Vector3 retreat = SampleAway(20f, Vector3.left);
		_input.BeginPending(GameCommandInputMode.RetreatPending, GameCommandAudience.PlayerSelected);
		accepted = _input.ConfirmPoint(retreat, new Component[] { m_Controller });
		Check("W3_Retreat", accepted == 1 && m_Controller.CurrentState == UnitAIState.Retreat,
			m_Controller.CurrentState.ToString());
		GameCommandResult cancel = GameCommandService.Issue(
			m_Controller, TacticalCommand.Cancel(TacticalCommandSource.Game));
		Check("W3_Cancel", cancel.Accepted && m_Controller.CurrentState == UnitAIState.Idle,
			m_Controller.CurrentState.ToString());
		yield return null;
		Check("W3_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			m_Controller.CurrentCombatIntent.ToString());
		RestoreTargetPark();
	}

	private IEnumerator RunMassIsolation(GameCommandInput _input)
	{
		AppendLine("---");
		AppendLine("[M] 5 Player + 5 Enemy + Neutral. Live collect / death / Cancel.");
		Vector3 origin = m_Observer != null ? m_Observer.position : Vector3.zero;
		UnitAIController[] players = new UnitAIController[5];
		for (int i = 0; i < 5; i++)
			players[i] = SpawnInfantry("AI64_P" + i, UnitTeamId.Player, origin + new Vector3(i, 0f, 0f), true);
		for (int i = 0; i < 5; i++)
			SpawnInfantry("AI64_E" + i, UnitTeamId.Enemy, origin + new Vector3(i, 0f, 2f), false);
		UnitAIController neutral = SpawnInfantry("AI64_N0", UnitTeamId.Neutral, origin + new Vector3(0f, 0f, 4f), true);
		yield return null;

		Vector3 pointA = origin + new Vector3(10f, 0f, 10f);
		Vector3 pointB = origin + new Vector3(20f, 0f, 5f);
		Vector3 pointC = origin + new Vector3(-8f, 0f, 4f);

		_input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.PlayerSelected);
		int accepted = _input.ConfirmPoint(pointA, new Component[] { players[0], players[1] });
		Check("M_PlayerAttack_Count", accepted == 2, "accepted=" + accepted);
		Check("M_P0_Attack", IsState(players[0], UnitAIState.Attack, pointA, false), Describe(players[0]));
		Check("M_P1_Attack", IsState(players[1], UnitAIState.Attack, pointA, false), Describe(players[1]));
		Check("M_P2_Idle", players[2].CurrentState == UnitAIState.Idle, Describe(players[2]));
		Check("M_Neutral_Idle", neutral.CurrentState == UnitAIState.Idle, Describe(neutral));
		Check("M_NotFire", players[0].CurrentCombatIntent == CombatIntent.Hold,
			players[0].CurrentCombatIntent.ToString());

		_input.BeginPending(GameCommandInputMode.DefensePending, GameCommandAudience.EnemyDebug);
		accepted = _input.ConfirmPoint(pointB);
		Check("M_EnemyDefense_Accepted", accepted >= 5, "accepted=" + accepted);
		for (int i = 0; i < 5; i++)
		{
			UnitAIController enemy = FindAi("AI64_E" + i);
			Check("M_E" + i + "_Defense", IsState(enemy, UnitAIState.Defense, pointB, true), Describe(enemy));
		}

		Check("M_P0_StillAttack", IsState(players[0], UnitAIState.Attack, pointA, false), Describe(players[0]));
		Check("M_Neutral_StillIdle", neutral.CurrentState == UnitAIState.Idle, Describe(neutral));

		SpawnInfantry("AI64_E5", UnitTeamId.Enemy, origin + new Vector3(5f, 0f, 2f), false);
		_input.BeginPending(GameCommandInputMode.AttackPending, GameCommandAudience.EnemyDebug);
		accepted = _input.ConfirmPoint(pointA);
		Check("M_EnemyAttack_LiveCollect", accepted >= 6, "accepted=" + accepted);
		Check("M_E5_Attack", IsState(FindAi("AI64_E5"), UnitAIState.Attack, pointA, false),
			Describe(FindAi("AI64_E5")));

		UnitAIController dying = FindAi("AI64_E0");
		if (dying != null && dying.TryGetComponent(out UnitHealth health))
			health.EnterDead();

		_input.BeginPending(GameCommandInputMode.RetreatPending, GameCommandAudience.EnemyDebug);
		accepted = _input.ConfirmPoint(pointC);
		Check("M_EnemyRetreat_Accepted", accepted >= 5, "accepted=" + accepted);
		Check("M_E0_DeadSkipped", dying != null && dying.CurrentState == UnitAIState.Attack, Describe(dying));
		Check("M_E1_Retreat", IsState(FindAi("AI64_E1"), UnitAIState.Retreat, pointC, false),
			Describe(FindAi("AI64_E1")));
		Check("M_E5_Retreat", IsState(FindAi("AI64_E5"), UnitAIState.Retreat, pointC, false),
			Describe(FindAi("AI64_E5")));

		TacticalCommand cancel = TacticalCommand.Cancel(TacticalCommandSource.Game);
		int cancelled = GameCommandService.IssueMany(new Component[] { players[0], players[1] }, in cancel);
		Check("M_PlayerCancel", cancelled == 2, "cancelled=" + cancelled);
		Check("M_P0_Idle", players[0].CurrentState == UnitAIState.Idle, Describe(players[0]));
		Check("M_P1_Idle", players[1].CurrentState == UnitAIState.Idle, Describe(players[1]));
		Check("M_P2_StillIdle", players[2].CurrentState == UnitAIState.Idle, Describe(players[2]));
		yield return null;
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

	private UnitAIController FindAi(string _name)
	{
		GameObject go = FindSpawned(_name);
		return go != null ? go.GetComponent<UnitAIController>() : null;
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

	private void BindObserver(GameObject _observer)
	{
		if (!_observer.TryGetComponent(out m_Controller) || m_Controller == null)
			m_Controller = _observer.AddComponent<UnitAIController>();
		m_Controller.EnsureStarted();
		_observer.TryGetComponent(out m_ClickToMove);
		_observer.TryGetComponent(out m_Driver);
		_observer.TryGetComponent(out m_RtsMember);
		m_ClickWasEnabled = m_ClickToMove != null && m_ClickToMove.enabled;
		m_DriverWasEnabled = m_Driver != null && m_Driver.enabled;
		m_RtsWasEnabled = m_RtsMember != null && m_RtsMember.enabled;
		if (m_ClickToMove != null)
			m_ClickToMove.enabled = false;
		if (m_RtsMember != null)
			m_RtsMember.enabled = false;
		Check("NavDriver_Present", m_Driver != null, "UnitNavLocomotionDriver missing");
		if (m_Driver != null)
			m_Driver.enabled = true;
		if (_observer.TryGetComponent(out NavMeshAgent agent) && agent != null)
			agent.enabled = true;
		if (m_Driver != null && !_observer.TryGetComponent(out UnitNavMoveCommand _))
			_observer.AddComponent<UnitNavMoveCommand>();
		m_ObserverStart = _observer.transform.position;
		m_ObserverStartRot = _observer.transform.rotation;
		WarpObserver(m_ObserverStart);
		Check("EngagementNav_Enabled", m_Driver != null && m_Driver.enabled, "driver disabled");
	}

	private Vector3 SampleAway(float _distanceMeters, Vector3 _preferredDir = default)
	{
		Vector3 origin = m_Observer != null ? m_Observer.position : Vector3.zero;
		Vector3 forward = _preferredDir.sqrMagnitude > 0.0001f
			? _preferredDir
			: (m_Observer != null ? m_Observer.forward : Vector3.forward);
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = Vector3.forward;
		forward.Normalize();

		float[] distances = { _distanceMeters, 28f, 36f, 48f };
		Vector3 away = AwayFromTarget();
		Vector3[] dirs = { forward, away, Vector3.forward, Vector3.right, -forward, Vector3.left };
		Vector3 best = origin + forward * _distanceMeters;
		float bestDist = 0f;
		for (int d = 0; d < dirs.Length; d++)
		{
			Vector3 dir = dirs[d];
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.0001f)
				continue;
			dir.Normalize();
			for (int i = 0; i < distances.Length; i++)
			{
				Vector3 desired = origin + dir * distances[i];
				if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 8f, NavMesh.AllAreas) &&
				    !NavMesh.SamplePosition(desired, out hit, 24f, NavMesh.AllAreas))
					continue;
				float planar = UnitSearchNavigationMath.PlanarDistance(origin, hit.position);
				if (planar > bestDist)
				{
					best = hit.position;
					bestDist = planar;
				}

				if (planar > TacticalNavigationMath.DefaultPointArrivalRadius + 4f)
					return hit.position;
			}
		}

		return best;
	}

	private void ResetObserverPose()
	{
		if (m_Driver != null && m_Driver.enabled)
			m_Driver.HardStop();
		WarpObserver(m_ObserverStart);
		if (m_Observer != null)
			m_Observer.rotation = m_ObserverStartRot;
	}

	private void WarpObserver(Vector3 _position)
	{
		if (m_Observer == null)
			return;
		if (NavMesh.SamplePosition(_position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
			_position = hit.position;
		if (m_Observer.TryGetComponent(out NavMeshAgent agent) && agent != null && agent.enabled)
		{
			agent.Warp(_position);
			agent.isStopped = true;
			agent.ResetPath();
			agent.velocity = Vector3.zero;
			return;
		}

		m_Observer.SetPositionAndRotation(_position, m_ObserverStartRot);
	}

	private void RestoreNav()
	{
		RestoreTargetPark();
		if (m_Driver != null)
		{
			m_Driver.HardStop();
			m_Driver.enabled = m_DriverWasEnabled;
		}

		if (m_ClickToMove != null)
			m_ClickToMove.enabled = m_ClickWasEnabled;
		if (m_RtsMember != null)
			m_RtsMember.enabled = m_RtsWasEnabled;
	}

	private void ResetForWalkOrder()
	{
		if (m_Processor != null)
		{
			m_Processor.ClearContacts();
			m_Processor.SetSimulatedTime(0f);
			m_Processor.ClearAffiliationCue(m_Target);
		}

		ResetObserverPose();
		if (m_Controller == null)
			return;

		m_Controller.ImmediateThreat = false;
		m_Controller.ClearPerceptionOverride();
		GameCommandService.Issue(m_Controller, TacticalCommand.Cancel(TacticalCommandSource.Game));
	}

	private void ParkTargetForWalk()
	{
		if (m_Target == null)
			return;

		m_Target.TryGetComponent(out m_TargetAgent);
		m_Target.TryGetComponent(out m_TargetDriver);
		m_TargetAgentWasEnabled = m_TargetAgent != null && m_TargetAgent.enabled;
		m_TargetDriverWasEnabled = m_TargetDriver != null && m_TargetDriver.enabled;
		if (m_TargetDriver != null)
			m_TargetDriver.enabled = false;
		if (m_TargetAgent == null || !m_TargetAgent.enabled)
			return;

		m_TargetAgent.isStopped = true;
		m_TargetAgent.ResetPath();
		m_TargetAgent.velocity = Vector3.zero;
		m_TargetAgent.enabled = false;
	}

	private void RestoreTargetPark()
	{
		if (m_TargetDriver != null)
			m_TargetDriver.enabled = m_TargetDriverWasEnabled;
		if (m_TargetAgent != null)
			m_TargetAgent.enabled = m_TargetAgentWasEnabled;
	}

	private Vector3 AwayFromTarget()
	{
		if (m_Observer == null || m_Target == null)
			return Vector3.zero;

		Vector3 away = m_Observer.position - m_Target.position;
		away.y = 0f;
		return away;
	}

	private string FormatWalkDetail(float _startDist, Vector3 _dest)
	{
		float now = m_Observer != null
			? UnitSearchNavigationMath.PlanarDistance(m_Observer.position, _dest)
			: 0f;
		string detail =
			$"start={_startDist:F2} now={now:F2} state={m_Controller.CurrentState} scale={Time.timeScale:F2}";
		if (m_Observer == null || !m_Observer.TryGetComponent(out NavMeshAgent agent) || agent == null)
			return detail;

		string rem = agent.enabled && agent.isOnNavMesh && !float.IsPositiveInfinity(agent.remainingDistance)
			? agent.remainingDistance.ToString("F2")
			: "-";
		return detail +
		       $" stopped={agent.isStopped} path={agent.hasPath} pending={agent.pathPending}" +
		       $" onNav={agent.isOnNavMesh} rem={rem}";
	}

	private static bool Approximately(Vector3 _a, Vector3 _b)
	{
		return (_a - _b).sqrMagnitude < 0.05f * 0.05f;
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "GameCommandLayer_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[GameCommandLayerRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunGameCommandLayer;
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
			Debug.LogError($"[GameCommandLayerRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}
