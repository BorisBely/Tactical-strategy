using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Block B runtime M1–M10. Simulated clock — 0–60 s memory arc does not take a real minute.
/// Does not retune Detection Q. Does not extend memory for reload / danger / player.
/// Report: Assets/_Docs/Logs/Tests/MemoryCalibrationRuntime_LAST.txt
/// </summary>
[DefaultExecutionOrder(57)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class MemoryCalibrationRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_ObserveSeconds = 2.4f;
	private const float c_SimDt = 0.05f;
	private const float c_PosTolSq = 0.01f;
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
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunMemoryCalibration) &&
		!DetectionHarnessPlayMode.RunCalibrationRuntime &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
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
		Debug.Log("[MemoryCalibrationRuntimeSmoke] Block B runtime M1–M10 starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyObserverB();
		if (DetectionHarnessPlayMode.RunMemoryCalibration)
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
		AppendLine("BLOCK B — MEMORY CALIBRATION RUNTIME");
		AppendLine("====================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("B1 CONTRACT RecentlyLost=5 Horizon=30 Shape=1.5 Stale=0.25");
		AppendLine("simulated time; UnitVision disabled; no Q retune; no Search/Hunt");
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
		if (m_Processor == null || m_Target == null)
		{
			Finish();
			yield break;
		}

		m_Processor.ApplyMemoryCalibrationBaseline();
		Check("B1_RuntimeRecentlyLost",
			Mathf.Abs(m_Processor.RecentlyLostDurationSeconds - 5f) < 0.0001f,
			$"RecentlyLost={m_Processor.RecentlyLostDurationSeconds:F2}");
		Check("B1_RuntimeHorizon",
			Mathf.Abs(m_Processor.MemoryHorizonSeconds - 30f) < 0.0001f,
			$"Horizon={m_Processor.MemoryHorizonSeconds:F2}");
		Check("B1_RuntimeShape",
			Mathf.Abs(m_Processor.MemoryShapeExponent - 1.5f) < 0.0001f,
			$"Shape={m_Processor.MemoryShapeExponent:F2}");
		Check("B1_RuntimeStale",
			Mathf.Abs(m_Processor.MemoryStaleThreshold - 0.25f) < 0.0001f,
			$"Stale={m_Processor.MemoryStaleThreshold:F2}");

		m_VisionWasEnabled = m_Vision != null && m_Vision.enabled;
		if (m_Vision != null)
			m_Vision.enabled = false;

		yield return RunM1M2Lifecycle();
		yield return null;
		yield return RunM5FrozenLastKnown();
		yield return null;
		yield return RunM6Timeline();
		yield return null;
		yield return RunM7Reacquire();
		yield return null;
		yield return RunM8LongLoss();
		yield return null;
		yield return RunM9DualObservers();
		yield return null;
		yield return RunM10ReacquireAfterForgotten();

		if (m_Vision != null)
			m_Vision.enabled = m_VisionWasEnabled;
		m_Processor.ClearSimulatedTime();
		DestroyObserverB();
		Finish();
	}

	private IEnumerator RunM1M2Lifecycle()
	{
		AppendLine("[M1/M2] Observed → RecentlyLost → Lost");
		ResetSim();
		Vector3 p0 = m_Target.position;
		Observe(p0, c_ObserveSeconds);

		PerceivedContact contact;
		Check("M1_HasContactBeforeLoss",
			m_Processor.TryGetContact(m_Target, out contact) && contact != null,
			"observe failed");
		if (contact == null)
			yield break;

		Check("M1_Observed",
			contact.ObservationState == ObservationState.Observed &&
			Mathf.Abs(contact.LastSeenConfidence - 1f) < 0.001f,
			$"obs={contact.ObservationState} conf={F(contact.LastSeenConfidence, 3)}");

		LoseLos();
		m_Processor.TryGetContact(m_Target, out contact);
		Check("M1_RecentlyLostImmediate",
			contact != null && contact.ObservationState == ObservationState.RecentlyLost,
			contact != null ? contact.ObservationState.ToString() : "null");
		Check("M1_ConfidenceNearOne",
			contact != null && contact.LastSeenConfidence > 0.98f,
			contact != null ? $"conf={F(contact.LastSeenConfidence, 3)}" : "null");
		Check("M1_LastKnownEqualsLastSeen",
			contact != null && (contact.LastKnownPosition - contact.LastSeenPosition).sqrMagnitude < c_PosTolSq,
			contact != null
				? $"known={contact.LastKnownPosition} seen={contact.LastSeenPosition}"
				: "null");

		bool hasStamp = m_Processor.TryGetLostSinceTime(m_Target, out float lostSince);
		Check("M1_LostSinceStamped", hasStamp, "lostSince missing after LOS");
		Check("M1_LostSinceOnSimClock",
			hasStamp && Mathf.Abs(lostSince - m_SimTime) < 0.02f,
			hasStamp
				? $"lostSince={F(lostSince, 3)} sim={F(m_SimTime, 3)} unity={F(Time.time, 3)}"
				: "no stamp");

		float lossTime = m_SimTime;
		AdvanceTo(lossTime + 4.95f);
		m_Processor.TryGetContact(m_Target, out contact);
		Check("M2_Before5s_RecentlyLost",
			contact != null && contact.ObservationState == ObservationState.RecentlyLost,
			contact != null
				? $"t={F(m_SimTime - lossTime, 2)} obs={contact.ObservationState}"
				: "null");

		AdvanceTo(lossTime + 5.00f);
		m_Processor.TryGetContact(m_Target, out contact);
		Check("M2_At5s_Lost",
			contact != null && contact.ObservationState == ObservationState.Lost,
			contact != null
				? $"t={F(m_SimTime - lossTime, 2)} obs={contact.ObservationState}"
				: "null");
		yield return null;
	}

	private IEnumerator RunM5FrozenLastKnown()
	{
		AppendLine("[M5] LastKnown frozen while target moves P0→P3");
		ResetSim();
		Vector3 p0 = m_Target.position;
		Observe(p0, c_ObserveSeconds);
		LoseLos();

		Vector3 p1 = p0 + Vector3.right * 8f;
		Vector3 p2 = p0 + Vector3.right * 16f;
		Vector3 p3 = p0 + Vector3.forward * 12f;
		m_Target.position = p1;
		AdvanceBy(1f);
		m_Target.position = p2;
		AdvanceBy(1f);
		m_Target.position = p3;
		AdvanceBy(1f);

		m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
		Check("M5_ContactKept", contact != null, "contact missing");
		Check("M5_LastKnownFrozenAtP0",
			contact != null && (contact.LastKnownPosition - p0).sqrMagnitude < c_PosTolSq,
			contact != null ? $"known={contact.LastKnownPosition} p0={p0} live={m_Target.position}" : "null");
		Check("M5_LastSeenFrozenAtP0",
			contact != null && (contact.LastSeenPosition - p0).sqrMagnitude < c_PosTolSq,
			contact != null ? contact.LastSeenPosition.ToString() : "null");
		Check("M5_NotLiveTransform",
			contact != null && (contact.LastKnownPosition - m_Target.position).sqrMagnitude > 1f,
			contact != null ? $"known={contact.LastKnownPosition} live={m_Target.position}" : "null");
		m_Target.position = p0;
		yield return null;
	}

	private IEnumerator RunM6Timeline()
	{
		AppendLine("[M6] 0–60 s memory timeline (main Block B sheet)");
		ResetSim();
		Vector3 p0 = m_Target.position;
		Observe(p0, c_ObserveSeconds);
		LoseLos();
		float lossTime = m_SimTime;

		m_Processor.TryGetContact(m_Target, out PerceivedContact first);
		object contactRef = first;
		AppendLine("time  ObsState  DetState  conf  HasMemory  IsMemoryStale  LastKnown  zone");

		float[] samples = MemoryCalibrationScenarios.TimelineSampleSeconds;
		for (int i = 0; i < samples.Length; i++)
		{
			AdvanceTo(lossTime + samples[i]);
			m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
			if (contact == null)
			{
				Check($"M6_t{MemoryCalibrationScenarios.F(samples[i], 0)}_Contact", false, "contact deleted");
				continue;
			}

			Check($"M6_t{MemoryCalibrationScenarios.F(samples[i], 0)}_SameContact",
				ReferenceEquals(contact, contactRef),
				"identity must survive timeline");

			float elapsed = samples[i];
			ObservationState expectedObs = MemoryCalibrationScenarios.ExpectedObservationState(elapsed);
			Check($"M6_t{MemoryCalibrationScenarios.F(elapsed, 1)}_Obs",
				contact.ObservationState == expectedObs,
				$"expected={expectedObs} actual={contact.ObservationState}");

			float expectedConf = MemoryDecayMath.Evaluate(elapsed, 1f);
			Check($"M6_t{MemoryCalibrationScenarios.F(elapsed, 1)}_Conf",
				Mathf.Abs(contact.LastSeenConfidence - expectedConf) < 0.02f,
				$"expected={F(expectedConf, 3)} actual={F(contact.LastSeenConfidence, 3)}");

			Check($"M6_t{MemoryCalibrationScenarios.F(elapsed, 1)}_LastKnown",
				(contact.LastKnownPosition - p0).sqrMagnitude < c_PosTolSq,
				contact.LastKnownPosition.ToString());

			if (elapsed >= 8f)
			{
				Check($"M6_t{MemoryCalibrationScenarios.F(elapsed, 1)}_MemoryNeDetection",
					Mathf.Abs(contact.LastSeenConfidence - contact.DetectionProgress) > 0.01f ||
					contact.LastSeenConfidence <= 0f,
					$"mem={F(contact.LastSeenConfidence, 3)} det={F(contact.DetectionProgress, 3)}");
			}

			AppendLine(
				$"{F(elapsed, 1)}  {contact.ObservationState}  {contact.State}  {F(contact.LastSeenConfidence, 3)}  " +
				$"{contact.HasMemory}  {contact.IsMemoryStale(m_Processor.MemoryStaleThreshold)}  " +
				$"{contact.LastKnownPosition}  {MemoryCalibrationScenarios.FeelZone(elapsed)}");
		}

		yield return null;
	}

	private IEnumerator RunM7Reacquire()
	{
		AppendLine("[M7] Reacquire after 15 s Lost");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Vector3 first = m_Target.position;
		Observe(first, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact before);
		object contactRef = before;
		PerceivedIdentity idBefore = before != null ? before.Identity : PerceivedIdentity.Unknown;
		float idConf = before != null ? before.IdentityConfidence : 0f;

		LoseLos();
		AdvanceBy(15f);
		m_Processor.TryGetContact(m_Target, out PerceivedContact lost);
		Check("M7_LostBeforeReacquire",
			lost != null && lost.ObservationState == ObservationState.Lost,
			lost != null ? $"{lost.ObservationState} conf={F(lost.LastSeenConfidence, 3)}" : "null");
		Check("M7_IdentityHeldWhileLost",
			lost != null && lost.Identity == idBefore &&
			Mathf.Abs(lost.IdentityConfidence - idConf) < 0.001f,
			lost != null ? $"id={lost.Identity} C={F(lost.IdentityConfidence, 3)}" : "null");

		Vector3 second = first + Vector3.right * 6f;
		Observe(second, 0.4f);
		m_Processor.TryGetContact(m_Target, out PerceivedContact again);
		Check("M7_SameContact",
			again != null && ReferenceEquals(again, contactRef),
			"reacquire must keep instance");
		Check("M7_ConfidenceRestored",
			again != null && Mathf.Abs(again.LastSeenConfidence - 1f) < 0.001f,
			again != null ? $"conf={F(again.LastSeenConfidence, 3)}" : "null");
		Check("M7_LastSeenUpdated",
			again != null && (again.LastSeenPosition - second).sqrMagnitude < c_PosTolSq,
			again != null ? again.LastSeenPosition.ToString() : "null");
		Check("M7_LastKnownUpdated",
			again != null && (again.LastKnownPosition - second).sqrMagnitude < c_PosTolSq,
			again != null ? again.LastKnownPosition.ToString() : "null");
		Check("M7_IdentityPreserved",
			again != null && again.Identity == idBefore,
			again != null ? again.Identity.ToString() : "null");
		m_Processor.ClearAffiliationCue(m_Target);
		yield return null;
	}

	private IEnumerator RunM8LongLoss()
	{
		AppendLine("[M8] Long-loss / Forgotten ≠ Deleted");
		ResetSim();
		Observe(m_Target.position, c_ObserveSeconds);
		LoseLos();
		float lossTime = m_SimTime;
		float[] marks = { 30f, 45f, 60f };
		object contactRef = null;
		if (m_Processor.TryGetContact(m_Target, out PerceivedContact first))
			contactRef = first;

		for (int i = 0; i < marks.Length; i++)
		{
			AdvanceTo(lossTime + marks[i]);
			m_Processor.TryGetContact(m_Target, out PerceivedContact contact);
			Check($"M8_t{MemoryCalibrationScenarios.F(marks[i], 0)}_ContactKept",
				contact != null && (contactRef == null || ReferenceEquals(contact, contactRef)),
				contact != null ? "present" : "deleted");
			Check($"M8_t{MemoryCalibrationScenarios.F(marks[i], 0)}_ConfidenceZero",
				contact != null && contact.LastSeenConfidence <= 0.0001f,
				contact != null ? $"conf={F(contact.LastSeenConfidence, 3)}" : "null");
			Check($"M8_t{MemoryCalibrationScenarios.F(marks[i], 0)}_Forgotten",
				contact != null && contact.IsMemoryForgotten && !contact.HasMemory,
				contact != null ? $"forgotten={contact.IsMemoryForgotten}" : "null");
		}

		yield return null;
	}

	private IEnumerator RunM9DualObservers()
	{
		AppendLine("[M9] Dual observers — memory belongs to the observer");
		ResetSim();
		DetectionProcessor observerB = CreateObserverB();
		Check("M9_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
			yield break;

		observerB.ApplyMemoryCalibrationBaseline();
		observerB.SetSimulatedTime(0f);
		observerB.ClearContacts();
		if (observerB.TryGetComponent(out UnitVision visionB))
			visionB.enabled = false;

		Vector3 pos = m_Target.position;
		Observe(pos, c_ObserveSeconds);
		observerB.SetSimulatedTime(m_SimTime);
		observerB.ApplyEmptyObservationFrame();
		observerB.Advance(c_SimDt, m_SimTime);

		LoseLos();
		AdvanceBy(2f);
		observerB.SetSimulatedTime(m_SimTime);
		observerB.ApplyEmptyObservationFrame();
		observerB.Advance(2f, m_SimTime);

		bool hasA = m_Processor.TryGetContact(m_Target, out PerceivedContact contactA);
		bool hasB = observerB.TryGetContact(m_Target, out PerceivedContact contactB);
		Check("M9_A_HasMemory",
			hasA && contactA != null && contactA.HasMemory,
			hasA && contactA != null
				? $"obs={contactA.ObservationState} conf={F(contactA.LastSeenConfidence, 3)}"
				: "A missing");
		Check("M9_B_NoContact", !hasB, hasB ? "B must never have seen the target" : "B empty");
		Check("M9_IndependentInstances",
			!hasB || !ReferenceEquals(contactA, contactB),
			"A/B contacts must not be shared");

		observerB.ClearSimulatedTime();
		yield return null;
	}

	private IEnumerator RunM10ReacquireAfterForgotten()
	{
		AppendLine("[M10] Reacquire at t=60 after forgotten");
		ResetSim();
		m_Processor.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
		Vector3 first = m_Target.position;
		Observe(first, c_ObserveSeconds);
		m_Processor.TryGetContact(m_Target, out PerceivedContact before);
		object contactRef = before;
		LoseLos();
		AdvanceBy(60f);
		m_Processor.TryGetContact(m_Target, out PerceivedContact forgotten);
		Check("M10_ForgottenBeforeReacquire",
			forgotten != null && forgotten.IsMemoryForgotten,
			forgotten != null ? $"conf={F(forgotten.LastSeenConfidence, 3)}" : "null");

		Vector3 second = first + Vector3.forward * 5f;
		Observe(second, 0.4f);
		m_Processor.TryGetContact(m_Target, out PerceivedContact again);
		Check("M10_SameContact",
			again != null && ReferenceEquals(again, contactRef),
			"forgotten must not allocate a new contact");
		Check("M10_ConfidenceRestored",
			again != null && Mathf.Abs(again.LastSeenConfidence - 1f) < 0.001f,
			again != null ? $"conf={F(again.LastSeenConfidence, 3)}" : "null");
		Check("M10_LastKnownUpdated",
			again != null && (again.LastKnownPosition - second).sqrMagnitude < c_PosTolSq,
			again != null ? again.LastKnownPosition.ToString() : "null");
		m_Processor.ClearAffiliationCue(m_Target);
		yield return null;
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
		m_Processor.SetSimulatedTime(0f);
	}

	private void Observe(Vector3 _position, float _seconds)
	{
		float end = m_SimTime + Mathf.Max(c_SimDt, _seconds);
		while (m_SimTime < end - 0.0001f)
		{
			m_Processor.SetSimulatedTime(m_SimTime);
			m_Processor.ApplySyntheticObservation(m_Target, 15f, 0f, 1f, _position);
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

	private void AdvanceTo(float _absoluteTime)
	{
		float dt = _absoluteTime - m_SimTime;
		if (dt < -0.0001f)
			return;

		m_SimTime = _absoluteTime;
		m_Processor.SetSimulatedTime(m_SimTime);
		m_Processor.Advance(Mathf.Max(0f, dt), m_SimTime);
	}

	private DetectionProcessor CreateObserverB()
	{
		DestroyObserverB();
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner != null)
		{
			m_ObserverBRoot = spawner.SpawnAdditionalPlayer("MemoryCalib_ObserverB");
			if (m_ObserverBRoot != null)
			{
				if (!m_ObserverBRoot.TryGetComponent(out DetectionProcessor dp))
					dp = m_ObserverBRoot.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_ObserverBRoot = new GameObject("MemoryCalib_ObserverB_Minimal");
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
		string runtimePath = Path.Combine(dir, "MemoryCalibrationRuntime_LAST.txt");
		string combinedPath = Path.Combine(dir, "MemoryCalibration_LAST.txt");
		string runtimeBody = m_Report.ToString();
		File.WriteAllText(runtimePath, runtimeBody, Encoding.UTF8);

		MemoryCalibrationScenarios.ReportResult math = MemoryCalibrationScenarios.BuildReport();
		var combined = new StringBuilder(math.Body.Length + runtimeBody.Length + 64);
		combined.Append(math.Body);
		combined.AppendLine();
		combined.AppendLine("===== RUNTIME =====");
		combined.Append(runtimeBody);
		File.WriteAllText(combinedPath, combined.ToString(), Encoding.UTF8);

		Debug.Log(
			$"[MemoryCalibrationRuntimeSmoke] wrote {runtimePath} and {combinedPath} " +
			$"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunMemoryCalibration;
		DetectionHarnessPlayMode.ResetFlags();

#if UNITY_EDITOR
		if (exitPlay)
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
			Debug.LogError($"[MemoryCalibrationRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);

	private static string F(float _value, int _digits)
	{
		return _value.ToString("F" + _digits, CultureInfo.InvariantCulture);
	}
	#endregion
}
