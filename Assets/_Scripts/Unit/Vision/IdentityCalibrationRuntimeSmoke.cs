using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Block C CLOSED / VERIFIED. Runtime C3–C14. Simulated clock. Does not retune Q / Memory.
/// Does not drive TargetSelector / Engagement / Combat.
/// Report: Assets/_Docs/Logs/Tests/IdentityCalibrationRuntime_LAST.txt
/// </summary>
[DefaultExecutionOrder(60)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class IdentityCalibrationRuntimeSmoke : MonoBehaviour
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
	private float m_SimTime;
	private DetectionProcessor m_Processor;
	private Transform m_Target;
	private UnitVision m_Vision;
	private bool m_VisionWasEnabled;
	private GameObject m_ObserverBRoot;
	private UnitTeam m_WorldTeam;
	private UnitTeamId m_WorldTeamAtStart;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunIdentityCalibration) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.IsGRegressionPlay &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.RunCombatEngageExecution &&
		!DetectionHarnessPlayMode.RunSearchExecution &&
		!DetectionHarnessPlayMode.RunTacticalNavigationExecution &&
		!DetectionHarnessPlayMode.RunTacticalCommandContract &&
		!DetectionHarnessPlayMode.RunGameCommandSource &&
		!DetectionHarnessPlayMode.RunGameCommandInput &&
		!DetectionHarnessPlayMode.RunGameCommandLayer;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		LockObserverClock();
		Debug.Log("[IdentityCalibrationRuntimeSmoke] Block C runtime C3–C14 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyObserverB();
		ResetWorldEvidence();
		if (DetectionHarnessPlayMode.RunIdentityCalibration)
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
		LockObserverClock();
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
		AppendLine("BLOCK C — IDENTITY CALIBRATION RUNTIME");
		AppendLine("======================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("C1 IdentifyTime=4.0 Commit=0.50  Threat High≤25 Medium≤80");
		AppendLine("simulated time; UnitVision disabled; no Q/Memory retune; no Selector/Combat");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		m_Processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		m_Target = m_Harness != null ? m_Harness.Target : null;
		m_Vision = null;
		if (m_Processor != null)
			m_Processor.TryGetComponent(out m_Vision);

		Check("Harness_Processor", m_Processor != null, "DetectionProcessor missing");
		Check("Harness_Target", m_Target != null, "Target missing");
		Check("Isolation_SelectorHasNoIdentityFields",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedIdentity)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedRelationship)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(ThreatLevel)),
			"TargetSelector must not hold G3 identity types");
		Check("Isolation_ObservationHasNoKnowledgeFields",
			!TypeHasFieldOf(typeof(VisionObservation), typeof(PerceivedIdentity)) &&
			!TypeHasFieldOf(typeof(VisionObservation), typeof(PerceivedRelationship)),
			"VisionObservation must stay physical-only");

		if (m_Processor == null || m_Target == null)
		{
			Finish();
			yield break;
		}

		m_Processor.ApplyIdentityCalibrationBaseline();
		Check("C1_RuntimeIdentifyTime",
			Mathf.Abs(m_Processor.IdentifyTimeSeconds - 4f) < 0.0001f,
			$"IdentifyTime={m_Processor.IdentifyTimeSeconds:F2}");

		m_WorldTeam = m_Target.GetComponent<UnitTeam>() ?? m_Target.GetComponentInParent<UnitTeam>();
		Check("C0_WorldTeamPresent", m_WorldTeam != null, "target has no UnitTeam");
		if (m_WorldTeam != null)
		{
			m_WorldTeam.SetTeam(UnitTeamId.Neutral);
			m_WorldTeamAtStart = m_WorldTeam.Team;
		}

		ResetWorldEvidence();

		m_VisionWasEnabled = m_Vision != null && m_Vision.enabled;
		if (m_Vision != null)
			m_Vision.enabled = false;

		yield return RunC3Timeline();
		yield return null;
		RunC4Cues();
		yield return null;
		RunC5Unknown();
		yield return null;
		RunC7C8Threat();
		yield return null;
		RunC9Loss();
		yield return null;
		RunC10Reacquire();
		yield return null;
		RunC11CueFlip();
		yield return null;
		yield return RunC12Dual();
		yield return null;
		RunC13Appearance();

		Check("C0_WorldTeamUntouched",
			m_WorldTeam != null && m_WorldTeam.Team == m_WorldTeamAtStart,
			m_WorldTeam != null ? m_WorldTeam.Team.ToString() : "null");

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		m_Processor.ClearAffiliationCue(m_Target);
		ResetWorldEvidence();
		DestroyObserverB();
		Finish();
	}

	private IEnumerator RunC3Timeline()
	{
		AppendLine("[C3] Hostile cue timeline");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Vector3 pos = m_Target.position;

		float[] samples = IdentityCalibrationScenarios.IdentityTimelineSeconds;
		float cursor = 0f;
		for (int i = 0; i < samples.Length; i++)
		{
			float t = samples[i];
			if (t <= 0f)
			{
				AppendLine("  t=0.00  conf=0.000  identity=Unknown  rel=Unknown  threat=None");
				continue;
			}

			if (t > cursor)
			{
				Observe(pos, t - cursor);
				cursor = t;
			}

			m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
			string line = contact == null
				? $"t={F(t, 2)}  contact=null"
				: $"t={F(t, 2)}  conf={F(contact.IdentityConfidence, 3)}  identity={contact.Identity}  rel={contact.Relationship}  threat={contact.Threat}  detP={F(contact.DetectionProgress, 3)}";
			AppendLine("  " + line);

			if (Mathf.Abs(t - 0.5f) < 0.001f)
			{
				Check("C3_T0_5StillUnknown",
					contact != null && contact.Identity == PerceivedIdentity.Unknown &&
					!IdentityKnowledgeMath.HasReachedCommitThreshold(contact.IdentityConfidence),
					contact != null ? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}" : "null");
				Check("C3_DetectionAheadOfIdentity",
					contact != null && contact.DetectionProgress > contact.IdentityConfidence,
					contact != null
						? $"P={F(contact.DetectionProgress, 3)} C={F(contact.IdentityConfidence, 3)}"
						: "null");
			}

			if (Mathf.Abs(t - 1f) < 0.001f)
			{
				Check("C3_T1StillUnknown",
					contact != null && contact.Identity == PerceivedIdentity.Unknown &&
					!IdentityKnowledgeMath.HasReachedCommitThreshold(contact.IdentityConfidence),
					contact != null ? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}" : "null");
			}

			if (Mathf.Abs(t - 2f) < 0.001f)
			{
				Check("C3_T2NearCommitBand",
					contact != null &&
					contact.IdentityConfidence >= 0.45f &&
					contact.IdentityConfidence <= 0.55f &&
					IdentityKnowledgeMath.HasReachedCommitThreshold(contact.IdentityConfidence) &&
					contact.Identity == PerceivedIdentity.Hostile,
					contact != null
						? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)} (commit at 0.50 ≈ 2.0 s)"
						: "null");
			}

			if (Mathf.Abs(t - 2.5f) < 0.001f)
			{
				Check("C3_T2_5Hostile",
					contact != null && contact.Identity == PerceivedIdentity.Hostile &&
					contact.Relationship == PerceivedRelationship.Hostile,
					contact != null ? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}" : "null");
			}

			if (Mathf.Abs(t - 4f) < 0.001f)
			{
				Check("C3_T4Full",
					contact != null &&
					contact.Identity == PerceivedIdentity.Hostile &&
					contact.IdentityConfidence >= 0.99f &&
					contact.Relationship == PerceivedRelationship.Hostile,
					contact != null
						? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)} rel={contact.Relationship}"
						: "null");
			}
		}

		yield return null;
	}

	private void RunC4Cues()
	{
		AppendLine("[C4] cues");
		RunOneCue(ObservableAffiliation.Hostile, PerceivedIdentity.Hostile, PerceivedRelationship.Hostile);
		RunOneCue(ObservableAffiliation.Friendly, PerceivedIdentity.Friendly, PerceivedRelationship.Friendly);
		RunOneCue(ObservableAffiliation.Neutral, PerceivedIdentity.Neutral, PerceivedRelationship.Neutral);
	}

	private void RunOneCue(
		ObservableAffiliation _cue,
		PerceivedIdentity _identity,
		PerceivedRelationship _relationship)
	{
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, _cue);
		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
		Check($"C4_{_cue}_Identity",
			contact != null && contact.Identity == _identity && contact.Relationship == _relationship,
			contact != null
				? $"id={contact.Identity} rel={contact.Relationship} conf={F(contact.IdentityConfidence, 3)}"
				: "null");
	}

	private void RunC5Unknown()
	{
		AppendLine("[C5] Unknown cue — see someone, not know who");
		ResetSim();
		m_Processor.ClearAffiliationCue(m_Target);
		ResetWorldEvidence();
		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
		Check("C5_HasContact", contact != null, "detected contact missing");
		Check("C5_IdentityUnknown",
			contact != null && contact.Identity == PerceivedIdentity.Unknown,
			contact != null ? contact.Identity.ToString() : "null");
		Check("C5_ConfidenceZero",
			contact != null && contact.IdentityConfidence <= 0.0001f,
			contact != null ? $"conf={F(contact.IdentityConfidence, 3)}" : "null");
		Check("C5_RelationshipUnknown",
			contact != null && contact.Relationship == PerceivedRelationship.Unknown,
			contact != null ? contact.Relationship.ToString() : "null");
		Check("C5_ThreatNone",
			contact != null && contact.Threat == ThreatLevel.None,
			contact != null ? contact.Threat.ToString() : "null");
		Check("C5_DetectedWithoutIdentity",
			contact != null && contact.DetectionProgress >= 0.99f && contact.Identity == PerceivedIdentity.Unknown,
			contact != null
				? $"P={F(contact.DetectionProgress, 3)} id={contact.Identity}"
				: "null");
	}

	private void RunC7C8Threat()
	{
		AppendLine("[C7/C8] Threat vs distance (Hostile)");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Vector3 pos = m_Target.position;
		ObserveAt(pos, 10f, c_ObserveSeconds);
		AssertThreat("C7_10mHigh", ThreatLevel.High, 10f);
		ObserveAt(pos, 25f, 0.2f);
		AssertThreat("C7_25mHigh", ThreatLevel.High, 25f);
		ObserveAt(pos, 50f, 0.2f);
		AssertThreat("C8_50mMedium", ThreatLevel.Medium, 50f);
		ObserveAt(pos, 100f, 0.2f);
		AssertThreat("C8_100mLow", ThreatLevel.Low, 100f);
		ObserveAt(pos, 400f, 0.2f);
		AssertThreat("C8_400mLow", ThreatLevel.Low, 400f);

		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Friendly);
		ObserveAt(pos, 10f, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact friendly);
		Check("C7_FriendlyThreatNone",
			friendly != null &&
			friendly.Identity == PerceivedIdentity.Friendly &&
			friendly.Threat == ThreatLevel.None,
			friendly != null ? $"id={friendly.Identity} threat={friendly.Threat}" : "null");
	}

	private void AssertThreat(string _name, ThreatLevel _expected, float _meters)
	{
		m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
		float dist = contact != null
			? Mathf.Sqrt(Mathf.Max(0f, contact.LastObservation.DistanceSq))
			: -1f;
		Check(_name,
			contact != null &&
			contact.Relationship == PerceivedRelationship.Hostile &&
			contact.Threat == _expected,
			contact != null
				? $"threat={contact.Threat} dist={F(dist, 0)} (want {_expected} at {_meters:F0})"
				: "null");
	}

	private void RunC9Loss()
	{
		AppendLine("[C9] LOS loss holds Identity");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact before);
		float confBefore = before != null ? before.IdentityConfidence : 0f;
		object contactRef = before;
		LoseLos();
		AdvanceBy(1f);
		m_Processor.TryGetContact(m_Target, out PerceivedContact after);
		Check("C9_SameContact",
			after != null && ReferenceEquals(after, contactRef),
			"loss must not allocate");
		Check("C9_IdentityHeld",
			after != null && after.Identity == PerceivedIdentity.Hostile,
			after != null ? after.Identity.ToString() : "null");
		Check("C9_ConfidenceHeld",
			after != null && Mathf.Abs(after.IdentityConfidence - confBefore) < 0.02f,
			after != null
				? $"before={F(confBefore, 3)} after={F(after.IdentityConfidence, 3)}"
				: "null");
		Check("C9_RelationshipHeld",
			after != null && after.Relationship == PerceivedRelationship.Hostile,
			after != null ? after.Relationship.ToString() : "null");
		Check("C9_ThreatNotCleared",
			after != null && after.Threat != ThreatLevel.None,
			after != null ? after.Threat.ToString() : "null");
		Check("C9_ObservationNotObserved",
			after != null && after.ObservationState != ObservationState.Observed,
			after != null ? after.ObservationState.ToString() : "null");
	}

	private void RunC10Reacquire()
	{
		AppendLine("[C10] Reacquire preserves Identity");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Vector3 first = m_Target.position;
		Observe(first, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact before);
		object contactRef = before;
		LoseLos();
		AdvanceBy(2f);
		Observe(first + Vector3.forward * 2f, 0.4f);
		m_Processor.TryGetContact(m_Target, out PerceivedContact again);
		Check("C10_SameContact",
			again != null && ReferenceEquals(again, contactRef),
			"reacquire must not allocate");
		Check("C10_IdentityPreserved",
			again != null && again.Identity == PerceivedIdentity.Hostile,
			again != null ? again.Identity.ToString() : "null");
		Check("C10_ConfidenceHigh",
			again != null && again.IdentityConfidence >= 0.99f,
			again != null ? $"conf={F(again.IdentityConfidence, 3)}" : "null");
		Check("C10_RelationshipPreserved",
			again != null && again.Relationship == PerceivedRelationship.Hostile,
			again != null ? again.Relationship.ToString() : "null");
	}

	private void RunC11CueFlip()
	{
		AppendLine("[C11] cue change is not an instant team teleport");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
		Check("C11_StartedHostile",
			contact != null && contact.Identity == PerceivedIdentity.Hostile,
			contact != null ? contact.Identity.ToString() : "null");

		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Friendly);
		Observe(m_Target.position, 0.15f);
		m_Processor.TryGetContact(m_Target, out contact);
		Check("C11_NotInstantFriendly",
			contact != null && contact.Identity != PerceivedIdentity.Friendly &&
			contact.IdentityConfidence < IdentityKnowledgeMath.DefaultCommitThreshold,
			contact != null ? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}" : "null");

		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out contact);
		Check("C11_ReaccumulatedFriendly",
			contact != null && contact.Identity == PerceivedIdentity.Friendly &&
			contact.Relationship == PerceivedRelationship.Friendly,
			contact != null
				? $"id={contact.Identity} rel={contact.Relationship} conf={F(contact.IdentityConfidence, 3)}"
				: "null");
	}

	private IEnumerator RunC12Dual()
	{
		AppendLine("[C12] dual observers, same world object");
		ResetSim();
		DetectionProcessor observerB = CreateObserverB();
		Check("C12_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
			yield break;

		UnitVision visionB = observerB.GetComponent<UnitVision>();
		if (visionB != null)
			visionB.enabled = false;
		observerB.ApplyIdentityCalibrationBaseline();
		observerB.ClearContacts();
		observerB.SetSimulatedTime(m_SimTime);

		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		observerB.ClearAffiliationCue(m_Target);

		ObserveBoth(m_Processor, observerB, m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact contactA);
		observerB.TryGetContact(m_Target, out PerceivedContact contactB);
		Check("C12_A_Hostile",
			contactA != null && contactA.Identity == PerceivedIdentity.Hostile,
			contactA != null ? contactA.Identity.ToString() : "null");
		Check("C12_B_Unknown",
			contactB != null && contactB.Identity == PerceivedIdentity.Unknown,
			contactB != null ? $"id={contactB.Identity} conf={F(contactB.IdentityConfidence, 3)}" : "null");
		Check("C12_IdentitiesDiffer",
			contactA != null && contactB != null && contactA.Identity != contactB.Identity,
			contactA != null && contactB != null ? $"A={contactA.Identity} B={contactB.Identity}" : "null");
		Check("C12_WorldStillNeutral",
			m_WorldTeam != null && m_WorldTeam.Team == UnitTeamId.Neutral,
			m_WorldTeam != null ? m_WorldTeam.Team.ToString() : "null");
		DestroyObserverB();
		yield return null;
	}

	private void RunC13Appearance()
	{
		AppendLine("[C13] VisualIdentityEvidence world-look cue");
		ResetSim();
		m_Processor.ClearAffiliationCue(m_Target);
		UnitTeamId observerSideBefore = ReadObserverSide();
		EnsureObserverSide(UnitTeamId.Player);
		VisualIdentityEvidence evidence = VisualIdentityEvidence.GetOrCreate(m_Target.gameObject);
		evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
		Check("C13_AppearanceHostile",
			contact != null && contact.Identity == PerceivedIdentity.Hostile,
			contact != null ? contact.Identity.ToString() : "null");
		Check("C13_WorldTeamUnchanged",
			m_WorldTeam != null && m_WorldTeam.Team == UnitTeamId.Neutral,
			m_WorldTeam != null ? m_WorldTeam.Team.ToString() : "null");

		ResetSim();
		m_Processor.ClearAffiliationCue(m_Target);
		evidence.SetPrimaryAffiliation(VisualAffiliation.Unknown);
		Observe(m_Target.position, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out contact);
		Check("C13_AppearanceUnknown",
			contact != null && contact.Identity == PerceivedIdentity.Unknown &&
			contact.IdentityConfidence <= 0.0001f,
			contact != null ? $"id={contact.Identity} conf={F(contact.IdentityConfidence, 3)}" : "null");
		ResetWorldEvidence();
		EnsureObserverSide(observerSideBefore);
	}

	private void LockObserverClock()
	{
		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();
		if (m_Harness == null)
			return;

		m_Processor = m_Harness.DetectionProcessor;
		m_Target = m_Harness.Target;
		if (m_Processor == null)
			return;

		if (m_Processor.TryGetComponent(out m_Vision) && m_Vision != null)
		{
			m_VisionWasEnabled = m_Vision.enabled;
			m_Vision.enabled = false;
		}

		m_Processor.SetSimulatedTime(0f);
	}

	private void ResetSim()
	{
		m_SimTime = 0f;
		m_Processor.ClearContacts();
		m_Processor.ApplyIdentityCalibrationBaseline();
		m_Processor.SetSimulatedTime(0f);
		m_Processor.ClearAffiliationCue(m_Target);
	}

	private void Observe(Vector3 _position, float _seconds)
	{
		ObserveAt(_position, 15f, _seconds);
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

	private void ObserveBoth(
		DetectionProcessor _a,
		DetectionProcessor _b,
		Vector3 _position,
		float _seconds)
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

	private DetectionProcessor CreateObserverB()
	{
		DestroyObserverB();
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner != null)
		{
			m_ObserverBRoot = spawner.SpawnAdditionalPlayer("IdentityCalib_ObserverB");
			if (m_ObserverBRoot != null)
			{
				if (!m_ObserverBRoot.TryGetComponent(out DetectionProcessor dp))
					dp = m_ObserverBRoot.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_ObserverBRoot = new GameObject("IdentityCalib_ObserverB_Minimal");
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

	private void ResetWorldEvidence()
	{
		if (m_Target == null)
			return;
		if (m_Target.TryGetComponent(out VisualIdentityEvidence evidence) && evidence != null)
			evidence.SetPrimaryAffiliation(VisualAffiliation.Unknown);
	}

	private UnitTeamId ReadObserverSide()
	{
		if (m_Processor != null && m_Processor.TryGetComponent(out UnitTeam observerTeam) && observerTeam != null)
			return observerTeam.Team;
		return UnitTeamId.Neutral;
	}

	private void EnsureObserverSide(UnitTeamId _side)
	{
		if (m_Processor == null)
			return;
		if (!m_Processor.TryGetComponent(out UnitTeam observerTeam) || observerTeam == null)
			observerTeam = m_Processor.gameObject.AddComponent<UnitTeam>();
		observerTeam.SetTeam(_side);
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string runtimePath = Path.Combine(dir, "IdentityCalibrationRuntime_LAST.txt");
		string combinedPath = Path.Combine(dir, "IdentityCalibration_LAST.txt");
		string runtimeBody = m_Report.ToString();
		File.WriteAllText(runtimePath, runtimeBody, Encoding.UTF8);

		IdentityCalibrationScenarios.ReportResult math = IdentityCalibrationScenarios.BuildReport();
		var combined = new StringBuilder(math.Body.Length + runtimeBody.Length + 64);
		combined.Append(math.Body);
		combined.AppendLine();
		combined.AppendLine("===== RUNTIME =====");
		combined.Append(runtimeBody);
		File.WriteAllText(combinedPath, combined.ToString(), Encoding.UTF8);

		Debug.Log(
			$"[IdentityCalibrationRuntimeSmoke] wrote {runtimePath} and {combinedPath} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunIdentityCalibration;
#if UNITY_EDITOR
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}

	private static bool TypeHasFieldOf(Type _type, Type _needle)
	{
		FieldInfo[] fields = _type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			if (fields[i].FieldType == _needle)
				return true;
		}

		return false;
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
			Debug.LogError($"[IdentityCalibrationRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);

	private static string F(float _value, int _decimals)
	{
		return _value.ToString("F" + _decimals, CultureInfo.InvariantCulture);
	}
	#endregion
}
