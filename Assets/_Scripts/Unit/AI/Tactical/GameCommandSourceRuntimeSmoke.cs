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
/// Stage 6.2 game command source Play. Attack → Retreat → Cancel through DebugGameCommandSource.
/// Does not retune Vision / Combat / navigation. Not RTS.
/// Report: Assets/_Docs/Logs/Tests/GameCommandSource_LAST.txt
/// </summary>
[DefaultExecutionOrder(63)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class GameCommandSourceRuntimeSmoke : MonoBehaviour
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
	private readonly StringBuilder m_Report = new StringBuilder(8192);
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
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunGameCommandSource) &&
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
		!DetectionHarnessPlayMode.RunGameCommandInput &&
		!DetectionHarnessPlayMode.RunGameCommandLayer &&
		!DetectionHarnessPlayMode.IsGRegressionPlay;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[GameCommandSourceRuntimeSmoke] Stage 6.2 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		RestoreNav();
		if (DetectionHarnessPlayMode.RunGameCommandSource)
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
		AppendLine("STAGE 6.2 — GAME COMMAND SOURCE");
		AppendLine("===============================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("DebugGameCommandSource → GameCommandService → IssueCommand. Attack → Retreat → Cancel.");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		m_Processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		m_Target = m_Harness != null ? m_Harness.Target : null;
		m_Observer = m_Harness != null ? m_Harness.Observer : null;
		Check("Harness_Processor", m_Processor != null, "DetectionProcessor missing");
		Check("Harness_Target", m_Target != null, "Target missing");
		Check("Harness_Observer", m_Observer != null, "observer missing");
		if (m_Processor == null || m_Target == null || m_Observer == null)
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
		yield return RunAttackRetreatCancel();

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		if (m_Processor != null)
		{
			m_Processor.ClearSimulatedTime();
			m_Processor.ClearAffiliationCue(m_Target);
		}

		RestoreNav();
		Finish();
		yield return null;
	}

	private IEnumerator RunAttackRetreatCancel()
	{
		AppendLine("---");
		AppendLine("[G1] DebugGameCommandSource.Attack(P) → state Attack, MOVE reason=Attack");
		ResetForOrder();
		Vector3 attackDest = SampleAway(20f);
		GameCommandResult attack = DebugGameCommandSource.Attack(m_Controller, attackDest);
		Check("G1_Accepted", attack.Accepted, attack.Reason.ToString());
		Check("G1_Attack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
		Check("G1_Dest", Approximately(m_Controller.CurrentContext.Destination, attackDest),
			m_Controller.CurrentContext.Destination.ToString());
		yield return null;
		Check("G1_Issued",
			m_Controller.TacticalNavigationIssued || m_Controller.SearchHasMoveIntent,
			$"issued={m_Controller.TacticalNavigationIssued} reason={m_Controller.CurrentNavigationReason}");
		Check("G1_ReasonAttack",
			m_Controller.CurrentNavigationReason == UnitNavigationReason.Attack ||
			m_Controller.TacticalNavigationIssued,
			m_Controller.CurrentNavigationReason.ToString());
		Check("G1_NoFire", m_Controller.CurrentCombatIntent != CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());

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

		Check("G1_Walked", moved,
			$"start={startDist:F2} now={UnitSearchNavigationMath.PlanarDistance(m_Observer.position, attackDest):F2}");

		AppendLine("[G2] Arrival → remain Attack");
		WarpObserver(attackDest);
		m_Controller.Tick(c_SimDt);
		yield return null;
		Check("G2_Reached", m_Controller.TacticalDestinationReached, "not reached");
		Check("G2_StillAttack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());

		AppendLine("[G3] Retreat(R) → MOVE reason=Retreat");
		Vector3 retreatDest = SampleAway(20f, Vector3.left);
		GameCommandResult retreat = DebugGameCommandSource.Retreat(m_Controller, retreatDest);
		Check("G3_Accepted", retreat.Accepted, retreat.Reason.ToString());
		Check("G3_Retreat", m_Controller.CurrentState == UnitAIState.Retreat, m_Controller.CurrentState.ToString());
		Check("G3_Dest", Approximately(m_Controller.CurrentContext.Destination, retreatDest),
			m_Controller.CurrentContext.Destination.ToString());
		Check("G3_NotAttackDest",
			UnitSearchNavigationMath.PlanarDistance(retreatDest, attackDest) > 1f,
			$"A={attackDest} B={retreatDest}");
		yield return null;
		Check("G3_ReasonRetreat",
			m_Controller.CurrentNavigationReason == UnitNavigationReason.Retreat ||
			m_Controller.TacticalNavigationIssued,
			m_Controller.CurrentNavigationReason.ToString());

		AppendLine("[G4] Cancel → Idle, nav stop");
		GameCommandResult cancel = DebugGameCommandSource.Cancel(m_Controller);
		Check("G4_Accepted", cancel.Accepted, cancel.Reason.ToString());
		Check("G4_Idle", m_Controller.CurrentState == UnitAIState.Idle, m_Controller.CurrentState.ToString());
		yield return null;
		Check("G4_NavStop", !m_Controller.SearchHasMoveIntent, $"intent={m_Controller.SearchHasMoveIntent}");
		Check("G4_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			m_Controller.CurrentCombatIntent.ToString());
	}

	private void ResetForOrder()
	{
		if (m_Processor != null)
		{
			m_Processor.ClearContacts();
			m_Processor.ApplyMemoryCalibrationBaseline();
			m_Processor.ApplyIdentityCalibrationBaseline();
			m_Processor.SetSimulatedTime(0f);
			m_Processor.ClearAffiliationCue(m_Target);
		}

		ResetObserverPose();
		m_Controller.ImmediateThreat = false;
		m_Controller.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
		m_Controller.ClearPerceptionOverride();
		DebugGameCommandSource.Cancel(m_Controller);
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
		Vector3[] dirs = { forward, Vector3.forward, Vector3.right, -forward, Vector3.left };
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
		string path = Path.Combine(dir, "GameCommandSource_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[GameCommandSourceRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunGameCommandSource;
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
			Debug.LogError($"[GameCommandSourceRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}
