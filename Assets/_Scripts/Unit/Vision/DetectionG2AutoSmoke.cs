using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// G2 dual-observer lifecycle smoke. Writes Assets/_Docs/Logs/Tests/DetectionG2_LAST.txt
/// Runs after G1 harness spawn (execution order 200).
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG2AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 5f;
	[SerializeField] private float m_SampleWaitSeconds = 0.35f;
	[SerializeField] private float m_RecentlyLostProbeSeconds = 0.25f;
	[SerializeField] private float m_GraceWaitSeconds = 3.2f;
	[SerializeField] private float m_ReacquireWaitSeconds = 0.35f;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_SpawnedObserverB;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G2"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (m_SpawnedObserverB != null)
			Destroy(m_SpawnedObserverB);
		if (DetectionHarnessPlayMode.RunGStage == "G2")
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
	public IEnumerator RunSuite()
	{
		float warmup = DetectionHarnessPlayMode.GWarmupSeconds(m_WarmupSeconds);
		if (warmup > 0f)
			yield return new WaitForSeconds(warmup);
		else
			yield return null;

		m_Report.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine($"DetectionG2 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		DetectionProcessor observerA = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		Check("G2_ObserverA", observerA != null, "observer A missing");
		Check("G2_Target", target != null, "target missing");
		if (observerA == null || target == null)
		{
			Finish();
			yield break;
		}

		UnitVision visionA = observerA.GetComponent<UnitVision>();
		bool visionAWas = visionA != null && visionA.enabled;
		if (visionA != null)
			visionA.enabled = false;

		DetectionProcessor observerB = CreateObserverB();
		Check("G2_ObserverB", observerB != null, "failed to create observer B");
		if (observerB == null)
		{
			if (visionA != null)
				visionA.enabled = visionAWas;
			Finish();
			yield break;
		}

		UnitVision visionB = observerB.GetComponent<UnitVision>();
		if (visionB != null)
			visionB.enabled = false;

		observerA.ClearContacts();
		observerB.ClearContacts();

		Vector3 pos = target.position;
		observerA.ApplySyntheticObservation(target, 15f, 0f, 1f, pos);
		observerB.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_SampleWaitSeconds);

		bool hasA = observerA.TryGetContact(target, out PerceivedContact contactA);
		bool hasB = observerB.TryGetContact(target, out PerceivedContact contactB);
		Check("G2_A_HasContact", hasA && contactA != null, "A should have contact");
		Check("G2_B_NoContact", !hasB, $"B should not have contact (has={hasB})");
		if (hasA && contactA != null)
		{
			Check("G2_A_Observed", contactA.ObservationState == ObservationState.Observed, $"A obs={contactA.ObservationState}");
			AppendLine($"  A: Det={contactA.State} Obs={contactA.ObservationState} P={contactA.DetectionProgress:F3}");
		}

		for (int i = 0; i < 15; i++)
		{
			observerA.ApplySyntheticObservation(target, 15f, 0f, 1f, pos);
			yield return new WaitForSeconds(0.05f);
		}

		observerA.TryGetContact(target, out contactA);
		Check("G2_A_Detected", contactA != null && contactA.State == DetectionState.Detected,
			contactA != null ? $"state={contactA.State} P={contactA.DetectionProgress:F3}" : "null");

		object contactRef = contactA;
		Vector3 lastSeen = contactA != null ? contactA.LastSeenPosition : Vector3.zero;
		float lastSeenTime = contactA != null ? contactA.LastSeenTime : -1f;

		observerA.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_RecentlyLostProbeSeconds);

		observerA.TryGetContact(target, out contactA);
		Check("G2_A_RecentlyLost", contactA != null && contactA.ObservationState == ObservationState.RecentlyLost,
			contactA != null ? $"obs={contactA.ObservationState}" : "null");
		Check("G2_A_LastSeenValid", contactA != null && (contactA.LastSeenPosition - lastSeen).sqrMagnitude < 0.01f,
			contactA != null ? $"LastSeen={contactA.LastSeenPosition}" : "null");
		Check("G2_A_StillDetectedOrDetecting",
			contactA != null && contactA.State != DetectionState.Undetected,
			contactA != null ? $"det={contactA.State}" : "null");
		Check("G2_A_SameContactInstance",
			contactA != null && ReferenceEquals(contactA, contactRef),
			"contact identity must survive soft lose");

		hasB = observerB.TryGetContact(target, out _);
		Check("G2_B_StillNoContact", !hasB, "B must remain without contact");

		float grace = Mathf.Max(m_GraceWaitSeconds, observerA.RecentlyLostDurationSeconds + 0.35f);
		yield return new WaitForSeconds(grace);
		observerA.ApplyEmptyObservationFrame();
		yield return null;
		observerA.TryGetContact(target, out contactA);
		Check("G2_A_ContactKeptAfterGrace", contactA != null, "Lost contact must stay in registry");
		Check("G2_A_LostAfterGrace",
			contactA != null && contactA.ObservationState == ObservationState.Lost,
			contactA != null ? $"obs={contactA.ObservationState}" : "null");
		Check("G2_A_LastSeenFrozenThroughGrace",
			contactA != null && (contactA.LastSeenPosition - lastSeen).sqrMagnitude < 0.01f,
			contactA != null ? $"LastSeen={contactA.LastSeenPosition}" : "null");
		Check("G2_A_SameContactThroughGrace",
			contactA != null && ReferenceEquals(contactA, contactRef),
			"contact identity must survive Lost");

		Vector3 reacquirePos = pos + Vector3.right * 2.5f;
		observerA.ApplySyntheticObservation(target, 15f, 0f, 1f, reacquirePos);
		yield return new WaitForSeconds(m_ReacquireWaitSeconds);
		observerA.TryGetContact(target, out contactA);
		Check("G2_A_ReacquireSameContact",
			contactA != null && ReferenceEquals(contactA, contactRef),
			"reacquire must not allocate a new contact");
		Check("G2_A_ReacquireObserved",
			contactA != null && contactA.ObservationState == ObservationState.Observed,
			contactA != null ? $"obs={contactA.ObservationState}" : "null");
		Check("G2_A_ReacquireLastSeenUpdated",
			contactA != null &&
			(contactA.LastSeenPosition - reacquirePos).sqrMagnitude < 0.01f &&
			contactA.LastSeenTime > lastSeenTime,
			contactA != null
				? $"pos={contactA.LastSeenPosition} t={contactA.LastSeenTime:F2}"
				: "null");

		hasB = observerB.TryGetContact(target, out _);
		Check("G2_B_StillIndependent", !hasB, "B must stay independent through A reacquire");

		if (visionA != null)
			visionA.enabled = visionAWas;

		Finish();
	}

	private DetectionProcessor CreateObserverB()
	{
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner != null)
		{
			m_SpawnedObserverB = spawner.SpawnAdditionalPlayer("G2_ObserverB");
			if (m_SpawnedObserverB != null)
			{
				DetectionTestController.DisableLethalFire(m_SpawnedObserverB.transform);
				if (!m_SpawnedObserverB.TryGetComponent(out DetectionProcessor dp))
					dp = m_SpawnedObserverB.AddComponent<DetectionProcessor>();
				return dp;
			}
		}

		m_SpawnedObserverB = new GameObject("G2_ObserverB_Minimal");
		m_SpawnedObserverB.AddComponent<UnitObservationSource>();
		m_SpawnedObserverB.AddComponent<UnitPerception>();
		return m_SpawnedObserverB.AddComponent<DetectionProcessor>();
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string body = m_Report.ToString();
		File.WriteAllText(Path.Combine(dir, $"DetectionG2_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG2_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG2AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
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
			Debug.LogError($"[DetectionG2AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
