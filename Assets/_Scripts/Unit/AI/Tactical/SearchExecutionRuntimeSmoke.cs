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
/// Stage 3 FROZEN Search locomotion Play: one Walk to snapshotted LastKnown, stop at 15 m, stay Search.
/// Does not retune Vision / Identity / G6 / CombatIntent math. Search does not write Memory.
/// Report: Assets/_Docs/Logs/Tests/SearchExecution_LAST.txt
/// </summary>
[DefaultExecutionOrder(60)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class SearchExecutionRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_ObserveSeconds = 4.4f;
	private const float c_SimDt = 0.05f;
	private const float c_SearchDistance = 22f;
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
	private Vector3 m_FrozenSearchPos;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunSearchExecution) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.RunCombatEngageExecution &&
		!DetectionHarnessPlayMode.RunTacticalNavigationExecution &&
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

		Debug.Log("[SearchExecutionRuntimeSmoke] Stage 3 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		RestoreNav();
		if (DetectionHarnessPlayMode.RunSearchExecution)
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
		AppendLine("STAGE 3 — SEARCH NAVIGATION EXECUTION");
		AppendLine("=====================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("Search walks Walk to snapshotted SearchPosition. Stop at 15 m is not Found.");
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

		yield return RunT1WalkTowardLastKnown();
		yield return RunT2StopAtRadiusStaySearch();
		yield return RunT3HostileOnTheWay();
		yield return RunT4HostileInArea();
		RunT5StaleResume();
		RunT6ConfidenceZeroResume();
		yield return RunT7RetreatCancels();
		RunT8NoUsefulMemoryNoSearch();

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		m_Processor.ClearAffiliationCue(m_Target);
		RestoreNav();

		Finish();
		yield return null;
	}

	private IEnumerator RunT1WalkTowardLastKnown()
	{
		AppendLine("---");
		AppendLine("[T1] Defense + lost useful Hostile → Search Walk toward snapshotted LastKnown");
		if (!PrepareLostSearch(c_SearchDistance))
			yield break;

		m_FrozenSearchPos = m_Controller.CurrentContext.SearchPosition;
		float startDist = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, m_FrozenSearchPos);
		Check("T1_Search", m_Controller.CurrentState == UnitAIState.Search, m_Controller.CurrentState.ToString());
		Check("T1_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			$"{m_Controller.CurrentAction}/{m_Controller.CurrentCombatIntent}");
		Check("T1_Radius", Mathf.Abs(m_Controller.CurrentContext.AreaRadius - UnitAISearchDecision.DefaultAreaRadius) < 0.01f,
			m_Controller.CurrentContext.AreaRadius.ToString("F1"));
		Check("T1_StartOutsideRadius", startDist > UnitAISearchDecision.DefaultAreaRadius,
			startDist.ToString("F2"));
		Check("T1_NavIssuedOrIntent",
			m_Controller.SearchNavigationIssued || m_Controller.SearchHasMoveIntent,
			$"issued={m_Controller.SearchNavigationIssued} intent={m_Controller.SearchHasMoveIntent} reason={m_Controller.CurrentNavigationReason}");

		bool moved = false;
		float until = Time.unscaledTime + 8f;
		while (Time.unscaledTime < until)
		{
			float now = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, m_FrozenSearchPos);
			if (now < startDist - 0.35f)
			{
				moved = true;
				break;
			}

			yield return null;
		}

		float after = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, m_FrozenSearchPos);
		Check("T1_Walked", moved, $"start={startDist:F2} now={after:F2} intent={m_Controller.SearchHasMoveIntent}");
		Check("T1_SearchPositionFrozen",
			Approximately(m_Controller.CurrentContext.SearchPosition, m_FrozenSearchPos),
			m_Controller.CurrentContext.SearchPosition.ToString());
		Check("T1_StillSearch", m_Controller.CurrentState == UnitAIState.Search, m_Controller.CurrentState.ToString());
	}

	private IEnumerator RunT2StopAtRadiusStaySearch()
	{
		AppendLine("---");
		AppendLine("[T2] planar dist ≤ 15 m → HardStop, stay Search, keep observing");
		if (m_Controller.CurrentState != UnitAIState.Search)
		{
			if (!PrepareLostSearch(c_SearchDistance))
				yield break;
			m_FrozenSearchPos = m_Controller.CurrentContext.SearchPosition;
		}

		float until = Time.unscaledTime + 20f;
		while (Time.unscaledTime < until && !m_Controller.SearchAreaReached)
			yield return null;

		float dist = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, m_Controller.CurrentContext.SearchPosition);
		Check("T2_ReachedArea", m_Controller.SearchAreaReached || dist <= UnitAISearchDecision.DefaultAreaRadius + 0.35f,
			$"reached={m_Controller.SearchAreaReached} dist={dist:F2}");
		Check("T2_StillSearch", m_Controller.CurrentState == UnitAIState.Search, m_Controller.CurrentState.ToString());
		Check("T2_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			m_Controller.CurrentCombatIntent.ToString());
		Check("T2_Stopped", !m_Controller.SearchHasMoveIntent, $"intent={m_Controller.SearchHasMoveIntent}");
		Check("T2_SearchPositionFrozen",
			Approximately(m_Controller.CurrentContext.SearchPosition, m_FrozenSearchPos),
			m_Controller.CurrentContext.SearchPosition.ToString());
	}

	private IEnumerator RunT3HostileOnTheWay()
	{
		AppendLine("---");
		AppendLine("[T3] Hostile VisibleNow on the way → ReturnState + Engage (CombatIntent)");
		if (!PrepareLostSearch(c_SearchDistance))
			yield break;

		float until = Time.unscaledTime + 4f;
		while (Time.unscaledTime < until && !m_Controller.SearchHasMoveIntent && !m_Controller.SearchNavigationIssued)
			yield return null;

		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);

		Check("T3_ReturnState", m_Controller.CurrentState == UnitAIState.Defense, m_Controller.CurrentState.ToString());
		Check("T3_Engage", m_Controller.CurrentAction == UnitAIAction.Engage, m_Controller.CurrentAction.ToString());
		Check("T3_IntentEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		Check("T3_NotSearchWalk",
			m_Controller.CurrentNavigationReason != UnitNavigationReason.Search,
			m_Controller.CurrentNavigationReason.ToString());
		yield return null;
	}

	private IEnumerator RunT4HostileInArea()
	{
		AppendLine("---");
		AppendLine("[T4] Hostile VisibleNow in the 15 m area → ReturnState + Engage");
		if (!PrepareLostSearch(c_SearchDistance))
			yield break;

		float until = Time.unscaledTime + 20f;
		while (Time.unscaledTime < until && !m_Controller.SearchAreaReached)
			yield return null;

		ObserveAt(m_Target.position, 15f, 0.4f);
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);

		Check("T4_ReturnState", m_Controller.CurrentState == UnitAIState.Defense, m_Controller.CurrentState.ToString());
		Check("T4_Engage", m_Controller.CurrentAction == UnitAIAction.Engage, m_Controller.CurrentAction.ToString());
		Check("T4_IntentEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		yield return null;
	}

	private void RunT5StaleResume()
	{
		AppendLine("---");
		AppendLine("[T5] memory stale → Resume Defense, LastKnown not rewritten by Search");
		if (!PrepareLostSearch(c_SearchDistance))
			return;

		Vector3 known = m_Controller.CurrentContext.SearchPosition;
		if (!m_Processor.TryGetContact(m_Target, out PerceivedContact before) || before == null)
		{
			Check("T5_HasContact", false, "no contact");
			return;
		}

		Vector3 lastKnown = before.LastKnownPosition;
		AdvanceBy(20f);
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);

		bool hasAfter = m_Processor.TryGetContact(m_Target, out PerceivedContact after) && after != null;
		Check("T5_ResumeDefense", m_Controller.CurrentState == UnitAIState.Defense, m_Controller.CurrentState.ToString());
		Check("T5_NotSearchWalk",
			m_Controller.CurrentNavigationReason != UnitNavigationReason.Search,
			m_Controller.CurrentNavigationReason.ToString());
		Check("T5_LastKnownHeld",
			hasAfter && Approximately(after.LastKnownPosition, lastKnown),
			hasAfter ? after.LastKnownPosition.ToString() : "no contact");
		Check("T5_SearchDidNotChase", Approximately(known, lastKnown), known.ToString());
	}

	private void RunT6ConfidenceZeroResume()
	{
		AppendLine("---");
		AppendLine("[T6] LastSeenConfidence = 0 → Resume, Search did not write memory");
		if (!PrepareLostSearch(c_SearchDistance))
			return;

		if (!m_Processor.TryGetContact(m_Target, out PerceivedContact contact) || contact == null)
		{
			Check("T6_HasContact", false, "no contact");
			return;
		}

		Vector3 lastKnown = contact.LastKnownPosition;
		contact.LastSeenConfidence = 0f;
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);

		Check("T6_ResumeDefense", m_Controller.CurrentState == UnitAIState.Defense, m_Controller.CurrentState.ToString());
		Check("T6_ConfStaysZero", Mathf.Abs(contact.LastSeenConfidence) < 0.0001f,
			contact.LastSeenConfidence.ToString("F3"));
		Check("T6_LastKnownHeld", Approximately(contact.LastKnownPosition, lastKnown),
			contact.LastKnownPosition.ToString());
		Check("T6_NotSearchWalk",
			m_Controller.CurrentNavigationReason != UnitNavigationReason.Search,
			m_Controller.CurrentNavigationReason.ToString());
	}

	private IEnumerator RunT7RetreatCancels()
	{
		AppendLine("---");
		AppendLine("[T7] external Retreat cancels Search nav, then Retreat Walk");
		if (!PrepareLostSearch(c_SearchDistance))
			yield break;

		float until = Time.unscaledTime + 3f;
		while (Time.unscaledTime < until &&
		       !m_Controller.SearchHasMoveIntent &&
		       !m_Controller.SearchNavigationIssued)
			yield return null;

		Vector3 retreatDest = m_Observer.position + Vector3.left * 12f;
		if (NavMesh.SamplePosition(retreatDest, out NavMeshHit retreatHit, 12f, NavMesh.AllAreas))
			retreatDest = retreatHit.position;
		bool applied = m_Controller.TryApplyCommand(
			UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreatDest)));
		Check("T7_RetreatApplied", applied, applied ? "Search→Retreat" : "Search→Retreat rejected");
		Check("T7_StateRetreat", m_Controller.CurrentState == UnitAIState.Retreat, m_Controller.CurrentState.ToString());
		Check("T7_SearchReasonCleared",
			m_Controller.CurrentNavigationReason != UnitNavigationReason.Search,
			m_Controller.CurrentNavigationReason.ToString());
		bool retreatIssued = m_Controller.CurrentNavigationReason == UnitNavigationReason.Retreat ||
		                     m_Controller.TacticalNavigationIssued;
		Check("T7_RetreatWalkIssued", retreatIssued,
			$"reason={m_Controller.CurrentNavigationReason} issued={m_Controller.TacticalNavigationIssued} intent={m_Controller.SearchHasMoveIntent}");
		Check("T7_ActionNone", m_Controller.CurrentAction == UnitAIAction.None, m_Controller.CurrentAction.ToString());
		yield return null;
	}

	private void RunT8NoUsefulMemoryNoSearch()
	{
		AppendLine("---");
		AppendLine("[T8] Defense without useful memory does not start Search");
		ResetSim();
		ResetObserverPose();
		m_Controller.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
		m_Controller.TryApplyCommand(DefenseCommand());
		m_Controller.SetPerceptionFrame(AIPerceptionFrame.Empty);
		m_Controller.Tick(c_SimDt);
		Check("T8_Empty_Defense", m_Controller.CurrentState == UnitAIState.Defense, m_Controller.CurrentState.ToString());

		m_Controller.ClearPerceptionOverride();
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(SampleLastKnown(c_SearchDistance), c_SearchDistance, c_ObserveSeconds);
		LoseLos();
		if (m_Processor.TryGetContact(m_Target, out PerceivedContact contact) && contact != null)
			contact.LastSeenConfidence = 0.1f;
		m_Controller.TryApplyCommand(DefenseCommand());
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);
		Check("T8_Stale_NoSearch", m_Controller.CurrentState == UnitAIState.Defense,
			$"{m_Controller.CurrentState} conf={(contact != null ? contact.LastSeenConfidence.ToString("F2") : "none")}");
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

	private bool PrepareLostSearch(float _distanceMeters)
	{
		ResetSim();
		ResetObserverPose();
		m_Controller.ImmediateThreat = false;
		m_Controller.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
		m_Controller.TryApplyCommand(DefenseCommand());
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		if (m_Target.TryGetComponent(out VisualIdentityEvidence look))
			look.SetPrimaryAffiliation(VisualAffiliation.Enemy);

		Vector3 lastKnown = SampleLastKnown(_distanceMeters);
		float dist = UnitSearchNavigationMath.PlanarDistance(m_Observer.position, lastKnown);
		ObserveAt(lastKnown, dist, c_ObserveSeconds);
		LoseLos();
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(c_SimDt);

		bool search = m_Controller.CurrentState == UnitAIState.Search;
		Check("Prepare_Search", search,
			$"{m_Controller.CurrentState} dist={dist:F1} pos={lastKnown}");
		return search;
	}

	private Vector3 SampleLastKnown(float _distanceMeters)
	{
		Vector3 origin = m_Observer != null ? m_Observer.position : Vector3.zero;
		Vector3 forward = m_Observer != null ? m_Observer.forward : Vector3.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = Vector3.forward;
		forward.Normalize();

		float[] distances = { _distanceMeters, 28f, 36f, 48f };
		Vector3[] dirs = { forward, Vector3.forward, Vector3.right, -forward };
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

				if (planar > UnitAISearchDecision.DefaultAreaRadius + 1f)
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
		string path = Path.Combine(dir, "SearchExecution_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[SearchExecutionRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunSearchExecution;
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
			Debug.LogError($"[SearchExecutionRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
