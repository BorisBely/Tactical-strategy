using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Stage 2 FROZEN Play: CombatIntent Hold/Engage → existing combat contour.
/// Does not change Vision / Identity / G6 math. Does not call Fire from AI.
/// Report: Assets/_Docs/Logs/Tests/CombatEngageExecution_LAST.txt
/// </summary>
[DefaultExecutionOrder(60)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class CombatEngageExecutionRuntimeSmoke : MonoBehaviour
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
	private EngagementDecisionController m_Engagement;
	private TargetSelector m_Selector;
	private CombatReadinessController m_Readiness;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private UnitWeaponFireController m_Fire;
	private GameObject m_ObserverBRoot;
	private int m_ShotCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunCombatEngageExecution) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.RunImmediateThreatLive &&
		!DetectionHarnessPlayMode.RunCombatEventWorld &&
		!DetectionHarnessPlayMode.RunSoundInAi &&
		!DetectionHarnessPlayMode.RunSearch20 &&
		!DetectionHarnessPlayMode.RunCommandPriority &&
		!DetectionHarnessPlayMode.RunTargetCalibration &&
		!DetectionHarnessPlayMode.RunFrozenLayersPlay &&
		!DetectionHarnessPlayMode.RunSearchExecution &&
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

		Debug.Log("[CombatEngageExecutionRuntimeSmoke] Stage 2 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyObserverB();
		if (DetectionHarnessPlayMode.RunCombatEngageExecution)
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
		AppendLine("STAGE 2 — COMBAT ENGAGE EXECUTION");
		AppendLine("=================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("AI publishes CombatIntent; Combat shoots. Engage ≠ Fire. ROE veto remains.");
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

		if (m_Fire != null)
			m_Fire.ShotFired += HandleShotFired;

		yield return RunT1MissionCombatShot();
		RunT2NoHostileHold();
		RunT3SelfDefenseNoShot();
		yield return RunT3bSelfDefenseImmediateThreatAllows();
		RunT4LostContactHold();
		RunT5UnknownHold();
		RunT6FriendlyHold();
		RunT7MismatchObserved();
		yield return RunT8TwoSoldiers();

		if (m_Fire != null)
			m_Fire.ShotFired -= HandleShotFired;
		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		m_Processor.ClearAffiliationCue(m_Target);
		if (m_Selector != null)
			m_Selector.ClearSelection(false);

		Finish();
		yield return null;
	}

	private IEnumerator RunT1MissionCombatShot()
	{
		AppendLine("---");
		AppendLine("[T1] Defense + Hostile + MissionCombat → Engage → existing shooter");
		PrepareDefense(UseOfForceLevel.MissionCombat, ObservableAffiliation.Hostile, true);
		Check("T1_ActionEngage", m_Controller.CurrentAction == UnitAIAction.Engage,
			m_Controller.CurrentAction.ToString());
		Check("T1_IntentEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		Check("T1_ReadinessRequested",
			m_Readiness != null && m_Readiness.ReadinessRequested,
			m_Readiness != null ? m_Readiness.LastAppliedIntent.ToString() : "no readiness");
		Check("T1_PoseAuto",
			m_ReadyHands != null &&
			(m_ReadyHands.WantedMode == WeaponPoseMode.Auto ||
			 m_ReadyHands.WantedMode == WeaponPoseMode.Aiming),
			m_ReadyHands != null ? m_ReadyHands.WantedMode.ToString() : "no ready-hands");
		Check("T1_Selected", m_Selector != null && m_Selector.SelectedTarget == m_Target,
			m_Selector != null && m_Selector.SelectedTarget != null ? m_Selector.SelectedTarget.name : "null");
		Check("T1_DecisionAimOrFire",
			m_Engagement.CurrentDecision == EngagementDecision.Aim ||
			m_Engagement.CurrentDecision == EngagementDecision.Fire,
			m_Engagement.CurrentDecision.ToString());
		Check("T1_AiDidNotStartTrigger", m_Fire == null || !m_Fire.IsFiringCommandActive,
			m_Fire != null ? m_Fire.LastShotAttemptResult.ToString() : "no fire");

		if (m_ReadyHands != null)
			m_ReadyHands.SetPoseModeWanted(WeaponPoseMode.Aiming, true);
		m_ShotCount = 0;
		if (m_Fire != null)
		{
			m_Fire.StartFiring();
			m_Fire.TryFireSingleShot();
		}

		float until = Time.unscaledTime + 1.5f;
		while (Time.unscaledTime < until && m_ShotCount == 0)
			yield return null;

		bool shot = m_ShotCount > 0 ||
		            (m_Fire != null &&
		             (m_Fire.LastShotAttemptResult == WeaponShotAttemptResult.Success ||
		              m_Fire.LastShotAttemptResult == WeaponShotAttemptResult.FireRateLimited));
		Check("T1_Shot", shot,
			m_Fire != null ? $"shots={m_ShotCount} last={m_Fire.LastShotAttemptResult}" : "no fire");
		if (m_Fire != null)
			m_Fire.StopFiring();
	}

	private void RunT2NoHostileHold()
	{
		AppendLine("---");
		AppendLine("[T2] Defense + no Hostile → Hold, no fire");
		ResetSim();
		m_Controller.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
		m_Controller.TryApplyCommand(DefenseCommand());
		m_Controller.SetPerceptionFrame(AIPerceptionFrame.Empty);
		m_Controller.Tick(0.05f);
		if (m_Readiness != null)
			m_Readiness.ApplyNow();
		if (m_Engagement != null)
			m_Engagement.RefreshDecisionNow();
		Check("T2_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			m_Controller.CurrentAction.ToString());
		Check("T2_NoFire",
			m_Engagement.CurrentDecision != EngagementDecision.Fire &&
			m_Engagement.CurrentDecision != EngagementDecision.Aim,
			m_Engagement.CurrentDecision.ToString());
	}

	private void RunT3SelfDefenseNoShot()
	{
		AppendLine("---");
		AppendLine("[T3] Engage + SelfDefense + no ImmediateThreat → ROE blocks Aim/Fire");
		PrepareDefense(UseOfForceLevel.SelfDefense, ObservableAffiliation.Hostile, true);
		m_Controller.ImmediateThreat = false;
		m_Controller.Tick(0.05f);
		m_Engagement.RefreshDecisionNow();
		Check("T3_IntentEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		Check("T3_NoAimFire",
			m_Engagement.CurrentDecision != EngagementDecision.Fire &&
			m_Engagement.CurrentDecision != EngagementDecision.Aim,
			m_Engagement.CurrentDecision.ToString());
		if (m_Fire != null)
		{
			WeaponShotAttemptResult shot = m_Fire.TryFireSingleShot();
			Check("T3_NoShot", shot != WeaponShotAttemptResult.Success, shot.ToString());
		}
	}

	private IEnumerator RunT3bSelfDefenseImmediateThreatAllows()
	{
		AppendLine("---");
		AppendLine("[T3b] Engage + SelfDefense + incoming fire → ImmediateThreat, Allow, G6 not Ignore");
		PrepareDefense(UseOfForceLevel.SelfDefense, ObservableAffiliation.Hostile, true);
		EnsureTeam(m_Observer, UnitTeamId.Player);
		EnsureTeam(m_Target, UnitTeamId.Enemy);
		ImmediateThreatSignal.NotifyIncomingFire(m_Target, m_Observer);
		m_Controller.Tick(0.05f);
		m_Engagement.RefreshDecisionNow();
		Check("T3b_ImmediateThreat", m_Controller.ImmediateThreat, "flag still false");
		Check("T3b_Allow",
			m_Engagement.LastForcePermission.Allowed,
			m_Engagement.LastForcePermission.ToString());
		Check("T3b_NotIgnore",
			m_Engagement.CurrentDecision != EngagementDecision.Ignore,
			m_Engagement.CurrentDecision.ToString());
		Check("T3b_SelectionUnchanged",
			m_Selector != null && m_Selector.SelectedTarget == m_Target,
			m_Selector != null && m_Selector.SelectedTarget != null ? m_Selector.SelectedTarget.name : "null");

		if (m_ReadyHands != null)
			m_ReadyHands.SetPoseModeWanted(WeaponPoseMode.Aiming, true);
		m_ShotCount = 0;
		if (m_Fire != null)
		{
			m_Fire.StartFiring();
			m_Fire.TryFireSingleShot();
		}

		float until = Time.unscaledTime + 1.5f;
		while (Time.unscaledTime < until && m_ShotCount == 0)
			yield return null;

		bool shot = m_ShotCount > 0 ||
		            (m_Fire != null &&
		             (m_Fire.LastShotAttemptResult == WeaponShotAttemptResult.Success ||
		              m_Fire.LastShotAttemptResult == WeaponShotAttemptResult.FireRateLimited));
		Check("T3b_ShotOrAllow",
			shot || m_Engagement.LastForcePermission.Allowed,
			m_Fire != null
				? $"shots={m_ShotCount} last={m_Fire.LastShotAttemptResult} decision={m_Engagement.CurrentDecision}"
				: "no fire; Allow is the #7 gate");
		if (m_Fire != null)
			m_Fire.StopFiring();
	}

	private static void EnsureTeam(Component _component, UnitTeamId _id)
	{
		if (_component == null)
			return;
		UnitTeam team = _component.GetComponent<UnitTeam>();
		if (team == null)
			team = _component.gameObject.AddComponent<UnitTeam>();
		team.SetTeam(_id);
	}

	private void RunT4LostContactHold()
	{
		AppendLine("---");
		AppendLine("[T4] Hostile gone → Hold, fire closed");
		PrepareDefense(UseOfForceLevel.MissionCombat, ObservableAffiliation.Hostile, true);
		Check("T4_StartedEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		for (int i = 0; i < 8; i++)
		{
			m_Processor.ApplyEmptyObservationFrame();
			m_SimTime += c_SimDt;
			m_Processor.Advance(c_SimDt, m_SimTime);
			m_Controller.ClearPerceptionOverride();
			m_Controller.Tick(0.05f);
		}

		if (m_Readiness != null)
			m_Readiness.ApplyNow();
		m_Engagement.RefreshDecisionNow();
		Check("T4_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			$"{m_Controller.CurrentState}/{m_Controller.CurrentAction}/{m_Controller.CurrentCombatIntent}");
		Check("T4_NoAimFire",
			m_Engagement.CurrentDecision != EngagementDecision.Fire &&
			m_Engagement.CurrentDecision != EngagementDecision.Aim,
			m_Engagement.CurrentDecision.ToString());
	}

	private void RunT5UnknownHold()
	{
		AppendLine("---");
		AppendLine("[T5] VisibleNow + Unknown → AI not Engage");
		PrepareDefense(UseOfForceLevel.MissionCombat, ObservableAffiliation.Unknown, true);
		Check("T5_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			$"{m_Controller.CurrentAction} id={SnapshotIdentity()}");
	}

	private void RunT6FriendlyHold()
	{
		AppendLine("---");
		AppendLine("[T6] Friendly → AI not Engage");
		PrepareDefense(UseOfForceLevel.MissionCombat, ObservableAffiliation.Friendly, true);
		Check("T6_Hold", m_Controller.CurrentCombatIntent == CombatIntent.Hold,
			$"{m_Controller.CurrentAction} id={SnapshotIdentity()}");
	}

	private void RunT7MismatchObserved()
	{
		AppendLine("---");
		AppendLine("[T7] AI EngageTarget A, Combat Selected B — observed, not auto-fixed");
		PrepareDefense(UseOfForceLevel.MissionCombat, ObservableAffiliation.Hostile, true);
		var dummy = new GameObject("CombatEngageMismatchB");
		dummy.transform.position = m_Target.position + Vector3.right * 2f;
		m_Selector.SetSelectedTargetForDiagnostics(dummy.transform, dummy.transform.position);
		m_Engagement.RefreshDecisionNow();
		Check("T7_Mismatch", m_Engagement.EngageTargetMismatch, "expected mismatch flag");
		Check("T7_CombatKeepsB", m_Selector.SelectedTarget == dummy.transform, "selector overwritten");
		Check("T7_AiKeepsA", m_Controller.CurrentEngageTarget == m_Target, "AI target overwritten");
		Check("T7_StillEngage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		Destroy(dummy);
	}

	private IEnumerator RunT8TwoSoldiers()
	{
		AppendLine("---");
		AppendLine("[T8] two observers, same Hostile: Engage vs Hold");
		PrepareDefense(UseOfForceLevel.MissionCombat, ObservableAffiliation.Hostile, true);
		DetectionProcessor observerB = CreateObserverB();
		Check("T8_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
			yield break;

		if (!observerB.TryGetComponent(out UnitAIController aiB) || aiB == null)
			aiB = observerB.gameObject.AddComponent<UnitAIController>();
		aiB.EnsureStarted();
		aiB.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
		aiB.TryApplyCommand(UnitAICommand.Idle());
		UnitVision visionB = observerB.GetComponent<UnitVision>();
		if (visionB != null)
			visionB.enabled = false;
		observerB.ApplyMemoryCalibrationBaseline();
		observerB.ApplyIdentityCalibrationBaseline();
		observerB.ClearContacts();
		observerB.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveBoth(m_Processor, observerB, m_Target.position, c_ObserveSeconds);
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(0.05f);
		aiB.Tick(0.05f);
		Check("T8_A_Engage", m_Controller.CurrentCombatIntent == CombatIntent.Engage,
			m_Controller.CurrentCombatIntent.ToString());
		Check("T8_B_Hold", aiB.CurrentCombatIntent == CombatIntent.Hold,
			aiB.CurrentAction.ToString());
		DestroyObserverB();
		yield return null;
	}

	private void BindObserver(GameObject _observer)
	{
		if (!_observer.TryGetComponent(out m_Controller) || m_Controller == null)
			m_Controller = _observer.AddComponent<UnitAIController>();
		m_Controller.EnsureStarted();
		_observer.TryGetComponent(out m_Engagement);
		_observer.TryGetComponent(out m_Selector);
		_observer.TryGetComponent(out m_Readiness);
		_observer.TryGetComponent(out m_ReadyHands);
		_observer.TryGetComponent(out m_Fire);
		Check("Engagement_Present", m_Engagement != null, "EngagementDecisionController missing");
		Check("Selector_Present", m_Selector != null, "TargetSelector missing");
		Check("Readiness_Present", m_Readiness != null, "CombatReadinessController missing");
	}

	private void PrepareDefense(
		UseOfForceLevel _policy,
		ObservableAffiliation _cue,
		bool _selectTarget)
	{
		ResetSim();
		m_Controller.ImmediateThreat = false;
		m_Controller.TrySetUseOfForcePolicy(_policy);
		m_Controller.TryApplyCommand(DefenseCommand());
		if (_cue == ObservableAffiliation.Unknown)
		{
			m_Processor.ClearAffiliationCue(m_Target);
			if (m_Target.TryGetComponent(out VisualIdentityEvidence unknownLook))
				unknownLook.SetPrimaryAffiliation(VisualAffiliation.Unknown);
		}
		else
		{
			m_Processor.SetAffiliationCue(m_Target, _cue);
			if (m_Target.TryGetComponent(out VisualIdentityEvidence look) &&
			    _cue == ObservableAffiliation.Hostile)
				look.SetPrimaryAffiliation(VisualAffiliation.Enemy);
		}
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		m_Controller.ClearPerceptionOverride();
		m_Controller.Tick(0.05f);
		if (m_Readiness != null)
			m_Readiness.ApplyNow();
		if (_selectTarget && m_Selector != null)
			m_Selector.SetSelectedTargetForDiagnostics(m_Target, m_Target.position);
		if (m_Engagement != null)
			m_Engagement.RefreshDecisionNow();
	}

	private UnitAICommand DefenseCommand()
	{
		Vector3 origin = m_Observer != null ? m_Observer.position : Vector3.zero;
		return UnitAICommand.Defense(UnitAIStateContext.ForDefense(origin, origin, 10f, Vector3.forward));
	}

	private void ResetSim()
	{
		m_SimTime = 0f;
		m_ShotCount = 0;
		m_Processor.ClearContacts();
		m_Processor.ApplyMemoryCalibrationBaseline();
		m_Processor.ApplyIdentityCalibrationBaseline();
		m_Processor.SetSimulatedTime(0f);
		m_Processor.ClearAffiliationCue(m_Target);
		if (m_Selector != null)
			m_Selector.ClearSelection(false);
		if (m_Fire != null)
			m_Fire.StopFiring();
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

	private void ObserveBoth(DetectionProcessor _a, DetectionProcessor _b, Vector3 _position, float _seconds)
	{
		float end = m_SimTime + Mathf.Max(c_SimDt, _seconds);
		while (m_SimTime < end - 0.0001f)
		{
			if (_a != null)
			{
				_a.SetSimulatedTime(m_SimTime);
				_a.ApplySyntheticObservation(m_Target, 15f, 0f, 1f, _position);
				_a.Advance(c_SimDt, m_SimTime);
			}

			if (_b != null)
			{
				_b.SetSimulatedTime(m_SimTime);
				_b.ApplySyntheticObservation(m_Target, 15f, 0f, 1f, _position);
				_b.Advance(c_SimDt, m_SimTime);
			}

			m_SimTime += c_SimDt;
		}

		if (_a != null)
			_a.SetSimulatedTime(m_SimTime);
		if (_b != null)
			_b.SetSimulatedTime(m_SimTime);
	}

	private PerceivedIdentity SnapshotIdentity()
	{
		if (m_Processor != null && m_Processor.TryGetContact(m_Target, out PerceivedContact contact) && contact != null)
			return contact.Identity;
		return PerceivedIdentity.Unknown;
	}

	private DetectionProcessor CreateObserverB()
	{
		DestroyObserverB();
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner != null)
		{
			m_ObserverBRoot = spawner.SpawnAdditionalPlayer("CombatEngage_ObserverB");
			if (m_ObserverBRoot != null)
			{
				if (!m_ObserverBRoot.TryGetComponent(out DetectionProcessor dp))
					dp = m_ObserverBRoot.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_ObserverBRoot = new GameObject("CombatEngage_ObserverB_Minimal");
		m_ObserverBRoot.AddComponent<UnitObservationSource>();
		m_ObserverBRoot.AddComponent<UnitPerception>();
		return m_ObserverBRoot.AddComponent<DetectionProcessor>();
	}

	private void DestroyObserverB()
	{
		if (m_ObserverBRoot != null)
			Destroy(m_ObserverBRoot);
		m_ObserverBRoot = null;
	}

	private void HandleShotFired(AmmoDefinition _)
	{
		m_ShotCount++;
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "CombatEngageExecution_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[CombatEngageExecutionRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCombatEngageExecution;
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
			Debug.LogError($"[CombatEngageExecutionRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
