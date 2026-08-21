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
/// Stage 4 FROZEN Attack / Retreat / Flee locomotion Play. One Walk via existing infantry driver.
/// Does not retune Vision / Identity / G6 / CombatIntent / Search decision.
/// Report: Assets/_Docs/Logs/Tests/TacticalNavigation_LAST.txt
/// </summary>
[DefaultExecutionOrder(61)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class TacticalNavigationRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_SimDt = 0.05f;
	private const float c_ObserveSeconds = 4.4f;
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private int m_PassCount;
	private int m_FailCount;
	private DetectionProcessor m_Processor;
	private Transform m_Target;
	private Transform m_Observer;
	private UnitVision m_Vision;
	private bool m_VisionWasEnabled;
	private float m_SimTime;
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
		(m_RunOnStart || DetectionHarnessPlayMode.RunTacticalNavigationExecution) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.RunCombatEngageExecution &&
		!DetectionHarnessPlayMode.RunSearchExecution &&
		!DetectionHarnessPlayMode.RunTacticalCommandContract &&
		!DetectionHarnessPlayMode.RunGameCommandSource &&
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

		Debug.Log("[TacticalNavigationRuntimeSmoke] Stage 4 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		RestoreNav();
		if (DetectionHarnessPlayMode.RunTacticalNavigationExecution)
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
		AppendLine("STAGE 4 — TACTICAL NAVIGATION EXECUTION");
		AppendLine("=======================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("Attack/Retreat/Flee Walk through existing UnitNavLocomotionDriver. Search unchanged.");
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
		yield return RunT1AttackWalk();
		yield return RunT2AttackReachedStay();
		yield return RunT3AttackEngageKeepsWalking();
		yield return RunT4RetreatReachedStay();
		yield return RunT5FleeReachedIdle();
		yield return RunT6AttackToRetreatCancels();
		RunT7Isolation();

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		m_Processor.ClearAffiliationCue(m_Target);
		RestoreNav();

		Finish();
		yield return null;
	}

	private IEnumerator RunT1AttackWalk()
	{
		AppendLine("---");
		AppendLine("[T1] Attack + destination → Walk, remains Attack");
		if (!PrepareAttackAway())
			yield break;

		Vector3 dest = m_Controller.CurrentContext.Destination;
		float startDist = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, dest);
		Check("T1_Attack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
		Check("T1_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			$"{m_Controller.CurrentAction}/{m_Controller.CurrentCombatIntent}");
		Check("T1_Issued", m_Controller.TacticalNavigationIssued || m_Controller.SearchHasMoveIntent,
			$"issued={m_Controller.TacticalNavigationIssued} reason={m_Controller.CurrentNavigationReason}");

		bool moved = false;
		float until = Time.unscaledTime + 8f;
		while (Time.unscaledTime < until)
		{
			float now = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, dest);
			if (now < startDist - 0.35f)
			{
				moved = true;
				break;
			}

			yield return null;
		}

		float after = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, dest);
		Check("T1_Walked", moved, $"start={startDist:F2} now={after:F2}");
		Check("T1_StillAttack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
	}

	private IEnumerator RunT2AttackReachedStay()
	{
		AppendLine("---");
		AppendLine("[T2] Attack reached → HardStop, remains Attack");
		if (m_Controller.CurrentState != UnitAIState.Attack && !PrepareAttackAway())
			yield break;

		Vector3 dest = m_Controller.CurrentContext.Destination;
		WarpObserver(dest);
		m_Controller.Tick(c_SimDt);
		yield return null;

		Check("T2_Reached", m_Controller.TacticalDestinationReached, "not reached");
		Check("T2_StillAttack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
		Check("T2_Stopped", !m_Controller.SearchHasMoveIntent, $"intent={m_Controller.SearchHasMoveIntent}");
		Check("T2_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			m_Controller.CurrentCombatIntent.ToString());
	}

	private IEnumerator RunT3AttackEngageKeepsWalking()
	{
		AppendLine("---");
		AppendLine("[T3] Attack + Hostile VisibleNow → Engage, Walk continues, state stays Attack");
		if (!PrepareAttackAway())
			yield break;

		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);

		Check("T3_Attack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
		Check("T3_Engage", m_Controller.CurrentAction == UnitAIAction.Engage, m_Controller.CurrentAction.ToString());
		Check("T3_IntentEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		Check("T3_StillHasMoveOrIssued",
			m_Controller.SearchHasMoveIntent || m_Controller.TacticalNavigationIssued,
			$"intent={m_Controller.SearchHasMoveIntent} issued={m_Controller.TacticalNavigationIssued}");
		yield return null;
	}

	private IEnumerator RunT4RetreatReachedStay()
	{
		AppendLine("---");
		AppendLine("[T4] Retreat + destination → Walk, reached → HardStop, remains Retreat");
		ResetForOrder();
		Vector3 dest = SampleAway(18f);
		bool applied = m_Controller.TryApplyCommand(DefenseCommand()) &&
			m_Controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(dest)));
		Check("T4_RetreatApplied", applied && m_Controller.CurrentState == UnitAIState.Retreat,
			m_Controller.CurrentState.ToString());
		Check("T4_Issued", m_Controller.TacticalNavigationIssued || m_Controller.SearchHasMoveIntent,
			m_Controller.CurrentNavigationReason.ToString());

		WarpObserver(dest);
		m_Controller.Tick(c_SimDt);
		yield return null;
		Check("T4_StillRetreat", m_Controller.CurrentState == UnitAIState.Retreat, m_Controller.CurrentState.ToString());
		Check("T4_Stopped", !m_Controller.SearchHasMoveIntent, $"intent={m_Controller.SearchHasMoveIntent}");
	}

	private IEnumerator RunT5FleeReachedIdle()
	{
		AppendLine("---");
		AppendLine("[T5] Flee + destination → Walk, reached → HardStop → Idle");
		ResetForOrder();
		Vector3 dest = SampleAway(16f);
		bool applied = m_Controller.TryApplyCommand(
			UnitAICommand.Flee(UnitAIStateContext.ForFlee(Vector3.back, dest)));
		Check("T5_FleeApplied", applied && m_Controller.CurrentState == UnitAIState.Flee,
			m_Controller.CurrentState.ToString());

		WarpObserver(dest);
		m_Controller.Tick(c_SimDt);
		yield return null;
		Check("T5_Idle", m_Controller.CurrentState == UnitAIState.Idle, m_Controller.CurrentState.ToString());
		Check("T5_Stopped", !m_Controller.SearchHasMoveIntent, $"intent={m_Controller.SearchHasMoveIntent}");
	}

	private IEnumerator RunT6AttackToRetreatCancels()
	{
		AppendLine("---");
		AppendLine("[T6] Attack Walk(A) → Retreat Walk(B), A cancelled");
		if (!PrepareAttackAway())
			yield break;

		Vector3 attackDest = m_Controller.CurrentContext.Destination;
		Vector3 retreatDest = SampleAway(20f, Vector3.left);
		bool applied = m_Controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreatDest)));
		Check("T6_RetreatApplied", applied, applied ? "Attack→Retreat" : "rejected");
		Check("T6_StateRetreat", m_Controller.CurrentState == UnitAIState.Retreat, m_Controller.CurrentState.ToString());
		Check("T6_ReasonRetreat",
			m_Controller.CurrentNavigationReason == UnitNavigationReason.Retreat ||
			m_Controller.TacticalNavigationIssued,
			m_Controller.CurrentNavigationReason.ToString());
		Check("T6_NotAttackDest",
			UnitSearchNavigationMath.PlanarDistance(retreatDest, attackDest) > 1f,
			$"A={attackDest} B={retreatDest}");
		yield return null;
	}

	private void RunT7Isolation()
	{
		AppendLine("---");
		AppendLine("[T7] Attack Walk does not rewrite LastKnown / Identity and does not leave Attack");
		ResetForOrder();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 12f, c_ObserveSeconds);
		if (!m_Processor.TryGetContact(m_Target, out PerceivedContact before) || before == null)
		{
			Check("T7_HasContact", false, "no contact");
			return;
		}

		Vector3 lastKnown = before.LastKnownPosition;
		PerceivedIdentity identity = before.Identity;
		float conf = before.LastSeenConfidence;
		Vector3 dest = SampleAway(18f);
		m_Controller.TryApplyCommand(UnitAICommand.Attack(UnitAIStateContext.ForAttack(dest, Vector3.forward)));
		m_Controller.Tick(c_SimDt);

		bool hasAfter = m_Processor.TryGetContact(m_Target, out PerceivedContact after) && after != null;
		Check("T7_StillAttack", m_Controller.CurrentState == UnitAIState.Attack, m_Controller.CurrentState.ToString());
		Check("T7_LastKnownHeld",
			hasAfter && Approximately(after.LastKnownPosition, lastKnown),
			hasAfter ? after.LastKnownPosition.ToString() : "no contact");
		Check("T7_IdentityHeld", hasAfter && after.Identity == identity,
			hasAfter ? after.Identity.ToString() : "no contact");
		Check("T7_ConfHeld", hasAfter && Mathf.Abs(after.LastSeenConfidence - conf) < 0.0001f,
			hasAfter ? after.LastSeenConfidence.ToString("F3") : "no contact");
	}

	private bool PrepareAttackAway()
	{
		ResetForOrder();
		Vector3 dest = SampleAway(20f);
		bool applied = m_Controller.TryApplyCommand(
			UnitAICommand.Attack(UnitAIStateContext.ForAttack(dest, Vector3.forward)));
		Check("Prepare_Attack", applied && m_Controller.CurrentState == UnitAIState.Attack,
			$"{m_Controller.CurrentState} dest={dest}");
		return applied && m_Controller.CurrentState == UnitAIState.Attack;
	}

	private void ResetForOrder()
	{
		ResetSim();
		ResetObserverPose();
		m_Controller.ImmediateThreat = false;
		m_Controller.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
		m_Controller.ClearPerceptionOverride();
		m_Controller.TryApplyCommand(UnitAICommand.Idle());
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

	private UnitAICommand DefenseCommand()
	{
		Vector3 origin = m_Observer != null ? m_Observer.position : Vector3.zero;
		return UnitAICommand.Defense(UnitAIStateContext.ForDefense(origin, origin, 10f, Vector3.forward));
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
		string path = Path.Combine(dir, "TacticalNavigation_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[TacticalNavigationRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunTacticalNavigationExecution;
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
			Debug.LogError($"[TacticalNavigationRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
