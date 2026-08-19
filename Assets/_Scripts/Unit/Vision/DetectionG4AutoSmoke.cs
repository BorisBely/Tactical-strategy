using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// G4 memory decay smoke. Writes Assets/_Docs/Logs/Tests/DetectionG4_LAST.txt
/// Runs after G3 (execution order 400). Does not feed Combat.
/// </summary>
[DefaultExecutionOrder(400)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG4AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 22f;
	[SerializeField] private float m_ObserveSeconds = 2.2f;
	[SerializeField] private float m_RecentlyLostProbeSeconds = 0.25f;
	[SerializeField] private float m_GraceWaitSeconds = 3.2f;
	[SerializeField] private float m_MidDecayWaitSeconds = 4f;
	[SerializeField] private float m_ReacquireWaitSeconds = 0.35f;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G4"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunGStage == "G4")
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
		AppendLine($"DetectionG4 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		RunPureMathChecks();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		TargetSelector selector = processor != null ? processor.GetComponent<TargetSelector>() : null;

		Check("G4_Processor", processor != null, "DetectionProcessor missing");
		Check("G4_Target", target != null, "target missing");
		Check("Isolation_SelectorHasNoPerceivedContactFields",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedContact)),
			"TargetSelector must not hold PerceivedContact");
		Check("Isolation_ObservationHasNoMemoryFields",
			!VisionObservationHasMemoryFields(),
			"VisionObservation must stay physical-only");

		if (processor == null || target == null)
		{
			Finish();
			yield break;
		}

		UnitVision vision = processor.GetComponent<UnitVision>();
		bool visionWas = vision != null && vision.enabled;
		if (vision != null)
			vision.enabled = false;

		processor.ClearContacts();
		processor.SetAffiliationCue(target, ObservableAffiliation.Hostile);
		Vector3 seenPos = target.position;
		yield return ObserveFor(processor, target, seenPos, 15f, m_ObserveSeconds);

		processor.TryGetContact(target, out PerceivedContact contact);
		Check("G4_HasContact", contact != null, "contact missing after observe");
		if (contact == null)
		{
			if (vision != null)
				vision.enabled = visionWas;
			Finish();
			yield break;
		}

		object contactRef = contact;
		Check("G4_ObservedConfidenceFull",
			contact.ObservationState == ObservationState.Observed &&
			Mathf.Abs(contact.LastSeenConfidence - 1f) < 0.001f,
			$"obs={contact.ObservationState} conf={contact.LastSeenConfidence:F3}");
		Check("G4_LastKnownEqualsLastSeen",
			(contact.LastKnownPosition - contact.LastSeenPosition).sqrMagnitude < 0.01f,
			$"known={contact.LastKnownPosition} seen={contact.LastSeenPosition}");
		Check("G4_Detected", contact.State == DetectionState.Detected, contact.State.ToString());
		Check("G4_IdentityHostile",
			contact.Identity == PerceivedIdentity.Hostile,
			$"id={contact.Identity} C={contact.IdentityConfidence:F3}");
		PerceivedIdentity identityBefore = contact.Identity;
		float identityConfBefore = contact.IdentityConfidence;

		processor.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_RecentlyLostProbeSeconds);
		processor.TryGetContact(target, out contact);
		Check("G4_RecentlyLost",
			contact != null && contact.ObservationState == ObservationState.RecentlyLost,
			contact != null ? contact.ObservationState.ToString() : "null");
		Check("G4_LastSeenFrozenOnSoftLose",
			contact != null && (contact.LastSeenPosition - seenPos).sqrMagnitude < 0.01f,
			contact != null ? contact.LastSeenPosition.ToString() : "null");
		Check("G4_ConfidenceStillHighOnSoftLose",
			contact != null && contact.LastSeenConfidence > 0.7f,
			contact != null ? $"conf={contact.LastSeenConfidence:F3}" : "null");
		Check("G4_SameContactOnSoftLose",
			contact != null && ReferenceEquals(contact, contactRef),
			"contact identity");

		target.position = seenPos + Vector3.right * 40f;
		float grace = Mathf.Max(m_GraceWaitSeconds, processor.RecentlyLostDurationSeconds + 0.35f);
		yield return new WaitForSeconds(grace);
		processor.ApplyEmptyObservationFrame();
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G4_LostAfterGrace",
			contact != null && contact.ObservationState == ObservationState.Lost,
			contact != null ? contact.ObservationState.ToString() : "null");
		Check("G4_LastSeenNotLiveTransform",
			contact != null && (contact.LastSeenPosition - target.position).sqrMagnitude > 1f,
			contact != null ? $"seen={contact.LastSeenPosition} live={target.position}" : "null");
		Check("G4_LastKnownFrozen",
			contact != null && (contact.LastKnownPosition - seenPos).sqrMagnitude < 0.01f,
			contact != null ? contact.LastKnownPosition.ToString() : "null");
		Check("G4_ConfidenceDecayed",
			contact != null && contact.LastSeenConfidence < 0.85f && contact.LastSeenConfidence > 0f,
			contact != null ? $"conf={contact.LastSeenConfidence:F3}" : "null");
		Check("G4_ConfidenceIndependentFromDetection",
			contact != null && Mathf.Abs(contact.LastSeenConfidence - contact.DetectionProgress) > 0.01f,
			contact != null ? $"mem={contact.LastSeenConfidence:F3} det={contact.DetectionProgress:F3}" : "null");
		Check("G4_IdentityHeld",
			contact != null && contact.Identity == identityBefore &&
			Mathf.Abs(contact.IdentityConfidence - identityConfBefore) < 0.001f,
			contact != null ? $"id={contact.Identity} C={contact.IdentityConfidence:F3}" : "null");

		float elapsedSinceSeen = contact != null
			? Mathf.Max(0f, Time.time - contact.LastSeenTime)
			: 0f;
		float tStale = MemoryDecayMath.ElapsedSecondsForConfidence(
			processor.MemoryStaleThreshold,
			processor.MemoryHorizonSeconds,
			processor.MemoryShapeExponent);
		float waitStale = Mathf.Max(m_MidDecayWaitSeconds, tStale - elapsedSinceSeen + 0.45f);
		float maxBeforeForgotten = Mathf.Max(0.25f, processor.MemoryHorizonSeconds - elapsedSinceSeen - 0.75f);
		yield return new WaitForSeconds(Mathf.Min(waitStale, maxBeforeForgotten));
		processor.ApplyEmptyObservationFrame();
		yield return null;
		processor.TryGetContact(target, out contact);
		if (contact != null &&
		    contact.LastSeenConfidence > processor.MemoryStaleThreshold)
		{
			yield return new WaitForSeconds(1.5f);
			processor.ApplyEmptyObservationFrame();
			yield return null;
			processor.TryGetContact(target, out contact);
		}

		Check("G4_MemoryStaleBeforeForgotten",
			contact != null &&
			contact.LastSeenConfidence > 0f &&
			contact.LastSeenConfidence <= processor.MemoryStaleThreshold,
			contact != null
				? $"conf={contact.LastSeenConfidence:F3} staleThr={processor.MemoryStaleThreshold:F2}"
				: "null");

		elapsedSinceSeen = contact != null
			? Mathf.Max(0f, Time.time - contact.LastSeenTime)
			: processor.MemoryHorizonSeconds;
		float remaining = Mathf.Max(0.5f, processor.MemoryHorizonSeconds - elapsedSinceSeen + 0.4f);
		yield return new WaitForSeconds(remaining);
		processor.ApplyEmptyObservationFrame();
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G4_ContactKeptWhenForgotten", contact != null, "Lost contact must stay");
		Check("G4_ConfidenceAtHorizon",
			contact != null && contact.LastSeenConfidence <= 0.05f,
			contact != null ? $"conf={contact.LastSeenConfidence:F3}" : "null");

		Vector3 reacquirePos = seenPos + Vector3.forward * 8f;
		yield return ObserveFor(processor, target, reacquirePos, 15f, m_ReacquireWaitSeconds);
		processor.TryGetContact(target, out contact);
		Check("G4_ReacquireSameContact",
			contact != null && ReferenceEquals(contact, contactRef),
			"reacquire must keep instance");
		Check("G4_ReacquireConfidenceFull",
			contact != null && Mathf.Abs(contact.LastSeenConfidence - 1f) < 0.001f,
			contact != null ? $"conf={contact.LastSeenConfidence:F3}" : "null");
		Check("G4_ReacquireLastKnownUpdated",
			contact != null && (contact.LastKnownPosition - reacquirePos).sqrMagnitude < 0.01f,
			contact != null ? contact.LastKnownPosition.ToString() : "null");
		Check("G4_ReacquireIdentityPreserved",
			contact != null && contact.Identity == identityBefore,
			contact != null ? contact.Identity.ToString() : "null");

		Transform selectedBefore = selector != null ? selector.SelectedTarget : null;
		processor.ClearContacts();
		Transform selectedAfter = selector != null ? selector.SelectedTarget : null;
		Check("G5_ClearContactsDeselects",
			selector == null || selectedAfter == null,
			$"before={(selectedBefore != null ? selectedBefore.name : "null")} after={(selectedAfter != null ? selectedAfter.name : "null")}");

		if (vision != null)
			vision.enabled = visionWas;

		Finish();
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		Check("Math_ZeroTimeInitial",
			Mathf.Abs(MemoryDecayMath.Evaluate(0f, 1f) - 1f) < 0.0001f,
			"t=0");
		Check("Math_HorizonZero",
			MemoryDecayMath.Evaluate(MemoryDecayMath.DefaultHorizonSeconds, 1f) <= 0.0001f,
			"horizon");
		float early = MemoryDecayMath.Evaluate(1f, 1f);
		float late = MemoryDecayMath.Evaluate(7f, 1f);
		Check("Math_Monotone", early > late, $"1s={early:F3} 7s={late:F3}");
		float half = MemoryDecayMath.Evaluate(3f, 0.5f);
		float full = MemoryDecayMath.Evaluate(3f, 1f);
		Check("Math_InitialScales", Mathf.Abs(half - full * 0.5f) < 0.0001f, $"half={half:F3} full={full:F3}");
		Check("Math_StaleNotForgotten",
			MemoryDecayMath.IsStale(0.2f) && !MemoryDecayMath.IsForgotten(0.2f),
			"stale band");
	}

	private IEnumerator ObserveFor(
		DetectionProcessor _processor,
		Transform _target,
		Vector3 _pos,
		float _distanceMeters,
		float _seconds)
	{
		float elapsed = 0f;
		const float step = 0.05f;
		while (elapsed < _seconds)
		{
			_processor.ApplySyntheticObservation(_target, _distanceMeters, 0f, 1f, _pos);
			yield return new WaitForSeconds(step);
			elapsed += step;
		}
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string body = m_Report.ToString();
		File.WriteAllText(Path.Combine(dir, $"DetectionG4_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG4_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG4AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
	}

	private static bool TypeHasFieldOf(Type _type, Type _needle)
	{
		FieldInfo[] fields = _type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			Type ft = fields[i].FieldType;
			if (ft == _needle)
				return true;
			if (!ft.IsGenericType)
				continue;
			Type[] args = ft.GetGenericArguments();
			for (int a = 0; a < args.Length; a++)
			{
				if (args[a] == _needle)
					return true;
			}
		}

		return false;
	}

	private static bool VisionObservationHasMemoryFields()
	{
		FieldInfo[] fields = typeof(VisionObservation).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			string name = fields[i].Name;
			if (name.IndexOf("LastKnown", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("LastSeenConfidence", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("Identity", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("Threat", StringComparison.OrdinalIgnoreCase) >= 0)
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
			Debug.LogError($"[DetectionG4AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
