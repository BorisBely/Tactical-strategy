using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AI-1A Play: UseOfForcePolicy → ForcePermission → G6 handoff.
/// Does not change EngagementDecisionMath. Does not fire weapons.
/// Report: Assets/_Docs/Logs/Tests/UseOfForcePolicy_LAST.txt
/// </summary>
[DefaultExecutionOrder(60)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class UseOfForcePolicyRuntimeSmoke : MonoBehaviour
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
	private UnitVision m_Vision;
	private bool m_VisionWasEnabled;
	private float m_SimTime;
	private UnitTeam m_WorldTeam;
	private UnitTeamId m_WorldTeamAtStart;
	private UnitAIController m_Controller;
	private EngagementDecisionController m_Engagement;
	private TargetSelector m_Selector;
	private GameObject m_ObserverBRoot;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunUseOfForcePolicy) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunCombatEngageExecution &&
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

		Debug.Log("[UseOfForcePolicyRuntimeSmoke] AI-1A starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyObserverB();
		if (DetectionHarnessPlayMode.RunUseOfForcePolicy)
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
		AppendLine("AI-1A — USE OF FORCE POLICY");
		AppendLine("===========================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("ForcePermission from Relationship; G6 math untouched; no Weapon");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		m_Processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		m_Target = m_Harness != null ? m_Harness.Target : null;
		Transform observer = m_Harness != null ? m_Harness.Observer : null;
		Check("Harness_Processor", m_Processor != null, "DetectionProcessor missing");
		Check("Harness_Target", m_Target != null, "Target missing");
		Check("Harness_Observer", observer != null, "observer missing");
		if (m_Processor == null || m_Target == null || observer == null)
		{
			Finish();
			yield break;
		}

		if (!observer.TryGetComponent(out m_Controller) || m_Controller == null)
			m_Controller = observer.gameObject.AddComponent<UnitAIController>();
		m_Controller.EnsureStarted();
		observer.TryGetComponent(out m_Engagement);
		observer.TryGetComponent(out m_Selector);
		Check("Engagement_Present", m_Engagement != null, "EngagementDecisionController missing");
		Check("Selector_Present", m_Selector != null, "TargetSelector missing");

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

		m_Processor.ApplyMemoryCalibrationBaseline();
		m_Processor.ApplyIdentityCalibrationBaseline();

		RunCase("SelfDefense_Hostile_NoThreat", UseOfForceLevel.SelfDefense, ObservableAffiliation.Hostile,
			false, false, ForcePermissionReason.SelfDefenseNoImmediateThreat);
		RunCase("SelfDefense_Hostile_Threat", UseOfForceLevel.SelfDefense, ObservableAffiliation.Hostile,
			true, true, ForcePermissionReason.SelfDefenseImmediateThreat);
		RunCase("RestrictedDefense_Hostile", UseOfForceLevel.RestrictedDefense, ObservableAffiliation.Hostile,
			false, true, ForcePermissionReason.PolicyAllowsHostile);
		RunCase("MissionCombat_Hostile", UseOfForceLevel.MissionCombat, ObservableAffiliation.Hostile,
			false, true, ForcePermissionReason.PolicyAllowsHostile);
		RunCase("FullEngagement_Hostile", UseOfForceLevel.FullEngagement, ObservableAffiliation.Hostile,
			false, true, ForcePermissionReason.PolicyAllowsHostile);
		RunCase("NoFriendlyFire_Hostile", UseOfForceLevel.NoFriendlyFire, ObservableAffiliation.Hostile,
			false, true, ForcePermissionReason.NonFriendly);
		RunCase("NoFriendlyFire_Neutral", UseOfForceLevel.NoFriendlyFire, ObservableAffiliation.Neutral,
			false, true, ForcePermissionReason.NonFriendly);
		RunCase("NoFriendlyFire_Unknown", UseOfForceLevel.NoFriendlyFire, ObservableAffiliation.Unknown,
			false, true, ForcePermissionReason.NonFriendly);

		RunFriendlyDenied(UseOfForceLevel.SelfDefense);
		RunFriendlyDenied(UseOfForceLevel.RestrictedDefense);
		RunFriendlyDenied(UseOfForceLevel.MissionCombat);
		RunFriendlyDenied(UseOfForceLevel.FullEngagement);
		RunFriendlyDenied(UseOfForceLevel.NoFriendlyFire);

		yield return RunTwoObservers();

		if (m_WorldTeam != null)
			m_WorldTeam.SetTeam(m_WorldTeamAtStart);
		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		m_Processor.ClearAffiliationCue(m_Target);
		if (m_Selector != null)
			m_Selector.ClearSelection(false);

		Finish();
		yield return null;
	}

	private void RunFriendlyDenied(UseOfForceLevel _level)
	{
		RunCase("Friendly_" + _level, _level, ObservableAffiliation.Friendly,
			false, false, ForcePermissionReason.FriendlyProtected);
	}

	private void RunCase(
		string _name,
		UseOfForceLevel _level,
		ObservableAffiliation _cue,
		bool _immediateThreat,
		bool _wantAllowed,
		ForcePermissionReason _wantReason)
	{
		AppendLine("---");
		ResetSim();
		m_Controller.TrySetUseOfForcePolicy(_level);
		m_Controller.ImmediateThreat = _immediateThreat;
		ApplyAffiliationCue(_cue);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);

		AIContactKnowledge k = SnapshotTarget();
		PerceivedRelationship expectedRelationship = ExpectedRelationship(_cue);
		Check(_name + "_HasContact", k.Target != null,
			k.Target == null ? "no AI contact after observe" : k.Target.name);
		Check(_name + "_Relationship", k.Relationship == expectedRelationship,
			$"got={k.Relationship} want={expectedRelationship} identity={k.Identity}");

		ForcePermission perm = m_Controller.EvaluateForce(k);
		AppendPolicyLog(_level, k, perm);

		Check(_name + "_Allowed", perm.Allowed == _wantAllowed,
			$"allowed={perm.Allowed} want={_wantAllowed} reason={perm.Reason}");
		Check(_name + "_Reason", perm.Reason == _wantReason,
			$"reason={perm.Reason} want={_wantReason}");
		Check(_name + "_StateUnchanged", m_Controller.CurrentState == UnitAIState.Idle,
			m_Controller.CurrentState.ToString());

		HandoffG6(_name, perm);
	}

	private void HandoffG6(string _name, ForcePermission _perm)
	{
		if (m_Engagement == null || m_Selector == null)
		{
			Check(_name + "_G6_Handoff", false, "missing engagement/selector");
			return;
		}

		m_Selector.SetSelectedTargetForDiagnostics(m_Target, m_Target.position);
		m_Engagement.RefreshDecisionNow();
		Check(_name + "_G6_Handoff", m_Engagement.ForceGateApplied, "gate not applied");
		if (m_Engagement.ForceGateApplied)
			AppendLine("G6_Handoff=received");
		Check(_name + "_G6_PermissionMatch",
			m_Engagement.LastForcePermission.Allowed == _perm.Allowed,
			$"eng={m_Engagement.LastForcePermission} ai={_perm}");
		if (!_perm.Allowed)
		{
			Check(_name + "_G6_NoFire",
				m_Engagement.CurrentDecision != EngagementDecision.Fire &&
				m_Engagement.CurrentDecision != EngagementDecision.Aim,
				m_Engagement.CurrentDecision.ToString());
		}

		AppendLine($"G6_OBSERVE decision={m_Engagement.CurrentDecision} (not AI-1A acceptance)");
	}

	private IEnumerator RunTwoObservers()
	{
		AppendLine("---");
		AppendLine("[TwoObservers] independent policies");
		ResetSim();
		DetectionProcessor observerB = CreateObserverB();
		Check("TwoObs_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
			yield break;

		if (!observerB.TryGetComponent(out UnitAIController aiB) || aiB == null)
			aiB = observerB.gameObject.AddComponent<UnitAIController>();
		aiB.EnsureStarted();
		m_Controller.TrySetUseOfForcePolicy(UseOfForceLevel.SelfDefense);
		m_Controller.ImmediateThreat = false;
		aiB.TrySetUseOfForcePolicy(UseOfForceLevel.FullEngagement);
		aiB.ImmediateThreat = false;

		UnitVision visionB = observerB.GetComponent<UnitVision>();
		if (visionB != null)
			visionB.enabled = false;
		observerB.ApplyMemoryCalibrationBaseline();
		observerB.ApplyIdentityCalibrationBaseline();
		observerB.ClearContacts();
		observerB.SetSimulatedTime(m_SimTime);

		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		observerB.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveBoth(m_Processor, observerB, m_Target.position, c_ObserveSeconds);

		AIContactKnowledge a = SnapshotFrom(m_Processor);
		AIContactKnowledge b = SnapshotFrom(observerB);
		ForcePermission permA = m_Controller.EvaluateForce(a);
		ForcePermission permB = aiB.EvaluateForce(b);
		Check("TwoObs_A_Denied", !permA.Allowed, permA.ToString());
		Check("TwoObs_B_Allowed", permB.Allowed, permB.ToString());
		Check("TwoObs_Independent", permA.Allowed != permB.Allowed, $"A={permA} B={permB}");
		Check("TwoObs_StateUntouched",
			m_Controller.CurrentState == UnitAIState.Idle && aiB.CurrentState == UnitAIState.Idle,
			m_Controller.CurrentState + "/" + aiB.CurrentState);
		DestroyObserverB();
		yield return null;
	}

	private void AppendPolicyLog(UseOfForceLevel _level, in AIContactKnowledge _k, in ForcePermission _perm)
	{
		AppendLine("POLICY");
		AppendLine($"state={m_Controller.CurrentState}");
		AppendLine($"policy={_level}");
		AppendLine("CONTACT");
		AppendLine($"identity={_k.Identity}");
		AppendLine($"relationship={_k.Relationship}");
		AppendLine($"threat={_k.Threat}");
		AppendLine("DECISION");
		AppendLine($"forceAllowed={_perm.Allowed}");
		AppendLine($"reason={_perm.Reason}");
	}

	private void ApplyAffiliationCue(ObservableAffiliation _cue)
	{
		if (_cue == ObservableAffiliation.Unknown)
		{
			m_Processor.ClearAffiliationCue(m_Target);
			if (m_Target.TryGetComponent(out VisualIdentityEvidence unknownLook))
				unknownLook.SetPrimaryAffiliation(VisualAffiliation.Unknown);
			return;
		}

		m_Processor.SetAffiliationCue(m_Target, _cue);
		if (m_Target.TryGetComponent(out VisualIdentityEvidence look) &&
		    _cue == ObservableAffiliation.Hostile)
			look.SetPrimaryAffiliation(VisualAffiliation.Enemy);
	}

	private static PerceivedRelationship ExpectedRelationship(ObservableAffiliation _cue)
	{
		switch (_cue)
		{
			case ObservableAffiliation.Friendly:
				return PerceivedRelationship.Friendly;
			case ObservableAffiliation.Neutral:
				return PerceivedRelationship.Neutral;
			case ObservableAffiliation.Hostile:
				return PerceivedRelationship.Hostile;
			default:
				return PerceivedRelationship.Unknown;
		}
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

	private AIContactKnowledge SnapshotTarget()
	{
		return SnapshotFrom(m_Processor);
	}

	private AIContactKnowledge SnapshotFrom(DetectionProcessor _processor)
	{
		AIPerceptionFrame frame = AIPerceptionFrameBuilder.Build(_processor);
		if (frame.TryGetContact(m_Target, out AIContactKnowledge knowledge))
			return knowledge;
		return default;
	}

	private DetectionProcessor CreateObserverB()
	{
		DestroyObserverB();
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner != null)
		{
			m_ObserverBRoot = spawner.SpawnAdditionalPlayer("AI1A_ObserverB");
			if (m_ObserverBRoot != null)
			{
				if (!m_ObserverBRoot.TryGetComponent(out DetectionProcessor dp))
					dp = m_ObserverBRoot.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_ObserverBRoot = new GameObject("AI1A_ObserverB_Minimal");
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

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "UseOfForcePolicy_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[UseOfForcePolicyRuntimeSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunUseOfForcePolicy;
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
			Debug.LogError($"[UseOfForcePolicyRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
