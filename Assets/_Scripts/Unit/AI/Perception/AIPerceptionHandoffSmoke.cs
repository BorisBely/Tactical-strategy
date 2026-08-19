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
/// AI-0 FROZEN. Play H1–H10. Vision → PerceivedContact → AIPerceptionFrame.
/// Simulated clock. Does not retune Q / Memory / Identity. Does not drive TargetSelector / Combat.
/// Report: Assets/_Docs/Logs/Tests/AIPerceptionHandoff_LAST.txt
/// </summary>
[DefaultExecutionOrder(60)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class AIPerceptionHandoffSmoke : MonoBehaviour
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
	private AIPerceptionSensor m_Sensor;
	private Transform m_Target;
	private UnitVision m_Vision;
	private bool m_VisionWasEnabled;
	private GameObject m_ObserverBRoot;
	private UnitTeam m_WorldTeam;
	private UnitTeamId m_WorldTeamAtStart;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunAIPerceptionHandoff) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.IsGRegressionPlay;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		LockObserverClock();
		Debug.Log("[AIPerceptionHandoffSmoke] AI-0 H1–H10 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyObserverB();
		if (DetectionHarnessPlayMode.RunAIPerceptionHandoff)
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
		AppendLine("AI-0 — PERCEPTION HANDOFF RUNTIME");
		AppendLine("=================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("Vision → PerceivedContact → AIPerceptionFrame");
		AppendLine("simulated time; UnitVision disabled; no Q/Memory/Identity retune; no Selector/Combat");
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
		Check("Contract_NoDetectionProgressOnKnowledge",
			typeof(AIContactKnowledge).GetField("DetectionProgress") == null,
			"AIContactKnowledge must omit DetectionProgress");
		Check("Contract_NoSelectedTargetOnFrame",
			typeof(AIPerceptionFrame).GetField("SelectedTarget") == null,
			"AIPerceptionFrame must not include TargetSelector");
		Check("Isolation_FrameHasNoUnitTeam",
			!TypeHasFieldOf(typeof(AIContactKnowledge), typeof(UnitTeam)) &&
			!TypeHasFieldOf(typeof(AIContactKnowledge), typeof(UnitTeamId)),
			"AI snapshot must not carry UnitTeam");

		if (m_Processor == null || m_Target == null)
		{
			Finish();
			yield break;
		}

		EnsureSensor();
		m_Processor.ApplyMemoryCalibrationBaseline();
		m_Processor.ApplyIdentityCalibrationBaseline();

		m_WorldTeam = m_Target.GetComponent<UnitTeam>() ?? m_Target.GetComponentInParent<UnitTeam>();
		Check("C0_WorldTeamPresent", m_WorldTeam != null, "target has no UnitTeam");
		if (m_WorldTeam != null)
		{
			m_WorldTeam.SetTeam(UnitTeamId.Neutral);
			m_WorldTeamAtStart = m_WorldTeam.Team;
		}

		m_VisionWasEnabled = m_Vision != null && m_Vision.enabled;
		if (m_Vision != null)
			m_Vision.enabled = false;

		RunH1VisibleHostile();
		yield return null;
		RunH2RecentlyLost();
		yield return null;
		RunH3LostUsefulMemory();
		yield return null;
		RunH4StaleMemory();
		yield return null;
		RunH5Unknown();
		yield return null;
		RunH6Friendly();
		yield return null;
		RunH7Neutral();
		yield return null;
		RunH8ThreatLevels();
		yield return null;
		RunH9Reacquire();
		yield return null;
		yield return RunH10TwoObservers();

		Check("C0_WorldTeamUntouched",
			m_WorldTeam != null && m_WorldTeam.Team == m_WorldTeamAtStart,
			m_WorldTeam != null ? m_WorldTeam.Team.ToString() : "null");

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		m_Processor.ClearAffiliationCue(m_Target);
		DestroyObserverB();
		Finish();
	}

	private void RunH1VisibleHostile()
	{
		AppendLine("[H1] visible hostile");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		AIContactKnowledge k = SnapshotTarget();
		Check("H1_VisibleNow", k.VisibleNow, $"VisibleNow={k.VisibleNow} obs={k.ObservationState}");
		Check("H1_IdentityKnown", k.IdentityKnown && k.Hostile, $"id={k.Identity} rel={k.Relationship}");
		Check("H1_ThreatHigh", k.ThreatHigh, $"threat={k.Threat}");
		Check("H1_NotUnknown", !k.IdentityUnknown, $"unknown={k.IdentityUnknown}");
	}

	private void RunH2RecentlyLost()
	{
		AppendLine("[H2] recently lost hostile");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		LoseLos();
		AIContactKnowledge k = SnapshotTarget();
		Check("H2_NotVisible", !k.VisibleNow, $"VisibleNow={k.VisibleNow}");
		Check("H2_RecentlyLost", k.RecentlyLost, $"obs={k.ObservationState}");
		Check("H2_UsefulMemory", k.HasUsefulMemory && !k.MemoryStale,
			$"useful={k.HasUsefulMemory} stale={k.MemoryStale} conf={F(k.LastSeenConfidence, 3)}");
		Check("H2_StillHostile", k.Hostile, $"rel={k.Relationship}");
	}

	private void RunH3LostUsefulMemory()
	{
		AppendLine("[H3] lost with useful memory");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		LoseLos();
		AdvanceBy(6f);
		AIContactKnowledge k = SnapshotTarget();
		Check("H3_Lost", k.Lost, $"obs={k.ObservationState}");
		Check("H3_NotVisible", !k.VisibleNow, $"VisibleNow={k.VisibleNow}");
		Check("H3_UsefulMemory", k.HasUsefulMemory && !k.MemoryStale,
			$"useful={k.HasUsefulMemory} stale={k.MemoryStale} conf={F(k.LastSeenConfidence, 3)}");
		Check("H3_IdentityHeld", k.Hostile && k.IdentityKnown, $"id={k.Identity} rel={k.Relationship}");
	}

	private void RunH4StaleMemory()
	{
		AppendLine("[H4] stale memory");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		LoseLos();
		AdvanceBy(20f);
		AIContactKnowledge k = SnapshotTarget();
		Check("H4_Lost", k.Lost, $"obs={k.ObservationState}");
		Check("H4_MemoryStale", k.MemoryStale && !k.HasUsefulMemory,
			$"stale={k.MemoryStale} useful={k.HasUsefulMemory} conf={F(k.LastSeenConfidence, 3)}");
		Check("H4_IdentityHeld", k.Hostile, $"rel={k.Relationship}");
	}

	private void RunH5Unknown()
	{
		AppendLine("[H5] unknown target");
		ResetSim();
		m_Processor.ClearAffiliationCue(m_Target);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		AIContactKnowledge k = SnapshotTarget();
		Check("H5_VisibleNow", k.VisibleNow, $"VisibleNow={k.VisibleNow} det={k.DetectionState}");
		Check("H5_IdentityUnknown", k.IdentityUnknown && !k.IdentityKnown,
			$"id={k.Identity} known={k.IdentityKnown}");
		Check("H5_NotHostile", !k.Hostile && k.ThreatNone,
			$"rel={k.Relationship} threat={k.Threat}");
	}

	private void RunH6Friendly()
	{
		AppendLine("[H6] friendly");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Friendly);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		AIContactKnowledge k = SnapshotTarget();
		Check("H6_Friendly", k.Friendly && k.IdentityKnown, $"id={k.Identity} rel={k.Relationship}");
		Check("H6_ThreatNone", k.ThreatNone, $"threat={k.Threat}");
		Check("H6_NotHostile", !k.Hostile, $"hostile={k.Hostile}");
	}

	private void RunH7Neutral()
	{
		AppendLine("[H7] neutral");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Neutral);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		AIContactKnowledge k = SnapshotTarget();
		Check("H7_Neutral", k.Neutral && k.IdentityKnown, $"id={k.Identity} rel={k.Relationship}");
		Check("H7_ThreatNone", k.ThreatNone, $"threat={k.Threat}");
	}

	private void RunH8ThreatLevels()
	{
		AppendLine("[H8] threat levels");
		AssertThreatAt("H8_High10m", 10f, ThreatLevel.High);
		AssertThreatAt("H8_Medium50m", 50f, ThreatLevel.Medium);
		AssertThreatAt("H8_Low100m", 100f, ThreatLevel.Low);
	}

	private void RunH9Reacquire()
	{
		AppendLine("[H9] reacquire");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, 15f, c_ObserveSeconds);
		LoseLos();
		AdvanceBy(6f);
		AIContactKnowledge lost = SnapshotTarget();
		Check("H9_WasLost", lost.Lost && !lost.VisibleNow, $"obs={lost.ObservationState}");
		ObserveAt(m_Target.position, 15f, 0.5f);
		AIContactKnowledge k = SnapshotTarget();
		Check("H9_VisibleAgain", k.VisibleNow, $"VisibleNow={k.VisibleNow} obs={k.ObservationState}");
		Check("H9_IdentityPreserved", k.Hostile && k.IdentityKnown, $"id={k.Identity}");
	}

	private IEnumerator RunH10TwoObservers()
	{
		AppendLine("[H10] two observers");
		ResetSim();
		DetectionProcessor observerB = CreateObserverB();
		Check("H10_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
			yield break;

		UnitVision visionB = observerB.GetComponent<UnitVision>();
		if (visionB != null)
			visionB.enabled = false;
		observerB.ApplyMemoryCalibrationBaseline();
		observerB.ApplyIdentityCalibrationBaseline();
		observerB.ClearContacts();
		observerB.SetSimulatedTime(m_SimTime);

		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		observerB.ClearAffiliationCue(m_Target);
		ObserveBoth(m_Processor, observerB, m_Target.position, c_ObserveSeconds);

		AIPerceptionFrame frameA = RebuildFrame(m_Processor);
		AIPerceptionFrame frameB = AIPerceptionFrameBuilder.Build(observerB);
		bool hasA = frameA.TryGetContact(m_Target, out AIContactKnowledge a);
		bool hasB = frameB.TryGetContact(m_Target, out AIContactKnowledge b);
		Check("H10_A_Hostile", hasA && a.Hostile && a.VisibleNow,
			hasA ? $"id={a.Identity} vis={a.VisibleNow}" : "A missing");
		Check("H10_B_Unknown", hasB && b.IdentityUnknown && !b.Hostile,
			hasB ? $"id={b.Identity} rel={b.Relationship}" : "B missing");
		Check("H10_FramesDiffer", hasA && hasB && a.Identity != b.Identity,
			hasA && hasB ? $"A={a.Identity} B={b.Identity}" : "null");
		Check("H10_WorldStillNeutral",
			m_WorldTeam != null && m_WorldTeam.Team == UnitTeamId.Neutral,
			m_WorldTeam != null ? m_WorldTeam.Team.ToString() : "null");
		DestroyObserverB();
		yield return null;
	}

	private void AssertThreatAt(string _name, float _meters, ThreatLevel _want)
	{
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		ObserveAt(m_Target.position, _meters, c_ObserveSeconds);
		AIContactKnowledge k = SnapshotTarget();
		Check(_name,
			k.Hostile && k.Threat == _want,
			$"threat={k.Threat} dist={_meters} (want {_want})");
	}

	private AIContactKnowledge SnapshotTarget()
	{
		AIPerceptionFrame frame = RebuildFrame(m_Processor);
		if (frame.TryGetContact(m_Target, out AIContactKnowledge knowledge))
			return knowledge;
		return default;
	}

	private AIPerceptionFrame RebuildFrame(DetectionProcessor _processor)
	{
		if (_processor == m_Processor && m_Sensor != null)
		{
			m_Sensor.Rebuild();
			return m_Sensor.CurrentFrame;
		}

		return AIPerceptionFrameBuilder.Build(_processor);
	}

	private void EnsureSensor()
	{
		if (m_Processor == null)
			return;
		if (!m_Processor.TryGetComponent(out m_Sensor) || m_Sensor == null)
			m_Sensor = m_Processor.gameObject.AddComponent<AIPerceptionSensor>();
		m_Sensor.Rebuild();
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
			m_ObserverBRoot = spawner.SpawnAdditionalPlayer("AI0_ObserverB");
			if (m_ObserverBRoot != null)
			{
				if (!m_ObserverBRoot.TryGetComponent(out DetectionProcessor dp))
					dp = m_ObserverBRoot.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_ObserverBRoot = new GameObject("AI0_ObserverB_Minimal");
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
		string path = Path.Combine(dir, "AIPerceptionHandoff_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[AIPerceptionHandoffSmoke] wrote {path} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunAIPerceptionHandoff;
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
			Debug.LogError($"[AIPerceptionHandoffSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);

	private static string F(float _value, int _decimals)
	{
		return _value.ToString("F" + _decimals, CultureInfo.InvariantCulture);
	}
	#endregion
}
