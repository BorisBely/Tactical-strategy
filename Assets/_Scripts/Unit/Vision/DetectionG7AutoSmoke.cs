using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// G7 sound / shared perception smoke. Writes Assets/_Docs/Logs/Tests/DetectionG7_LAST.txt
/// Runs after G6 (execution order 700, warmup 80s).
/// </summary>
[DefaultExecutionOrder(700)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG7AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 80f;
	[SerializeField] private float m_ObserveSeconds = 2.2f;
	[SerializeField] private float m_RecentlyLostProbeSeconds = 0.25f;
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
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G7"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunGStage == "G7")
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
		AppendLine($"DetectionG7 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		RunPureMathChecks();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		TargetSelector selector = processor != null ? processor.GetComponent<TargetSelector>() : null;
		EngagementDecisionController engagement =
			processor != null ? processor.GetComponent<EngagementDecisionController>() : null;

		Check("G7_Processor", processor != null, "DetectionProcessor missing");
		Check("G7_Target", target != null, "target missing");
		Check("G7_Selector", selector != null, "TargetSelector missing");
		Check("G7_Engagement", engagement != null, "EngagementDecisionController missing");
		Check("Isolation_SelectorNoSoundFields",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(SoundObservation)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(SharedObservation)),
			"TargetSelector must not store sound/shared types");
		Check("Isolation_EngagementNoSoundFields",
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(SoundObservation)) &&
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(SharedObservation)),
			"Engagement must not store sound/shared types");
		Check("Isolation_SelectorNoSoundMath",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(SoundKnowledgeMath)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(SharedKnowledgeMath)),
			"Selector must not own sound/shared math types");

		if (processor == null || target == null || selector == null || engagement == null)
		{
			Finish();
			yield break;
		}

		m_Harness.ResetPairToIdleCalibrationPad();
		processor = m_Harness.DetectionProcessor;
		target = m_Harness.Target;
		selector = processor != null ? processor.GetComponent<TargetSelector>() : null;
		engagement = processor != null ? processor.GetComponent<EngagementDecisionController>() : null;
		if (processor == null || target == null || selector == null || engagement == null)
		{
			Finish();
			yield break;
		}

		UnitVision vision = processor.GetComponent<UnitVision>();
		bool visionWas = vision != null && vision.enabled;
		if (vision != null)
			vision.enabled = false;

		processor.ClearContacts();
		selector.ForcedPriorityTarget = null;
		selector.ClearSelection(true);
		selector.ClearLineOfFireSuppression();
		yield return null;

		Vector3 seenPos = target.position;
		yield return ObserveFor(processor, target, seenPos, 15f, m_ObserveSeconds);
		yield return null;

		processor.TryGetContact(target, out PerceivedContact contact);
		EngagementDecision observedDecision = engagement.CurrentDecision;
		bool observedOk = observedDecision == EngagementDecision.Aim ||
		                  observedDecision == EngagementDecision.Fire;
		Check("G7_ObserveAimOrFire",
			selector.SelectedTarget == target && selector.HasSelectedAimPoint && observedOk,
			$"sel={selector.SelectedTarget} aim={selector.HasSelectedAimPoint} d={observedDecision}");

		processor.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_RecentlyLostProbeSeconds);
		yield return null;
		processor.TryGetContact(target, out contact);
		float memoryAfterHide = contact != null ? contact.LastSeenConfidence : 0f;
		Vector3 lastSeenFrozen = contact != null ? contact.LastSeenPosition : Vector3.zero;
		Check("G7_HiddenIsTrack",
			engagement.CurrentDecision == EngagementDecision.Track &&
			!selector.HasSelectedAimPoint,
			$"d={engagement.CurrentDecision} aim={selector.HasSelectedAimPoint}");
		Check("G7_HiddenMemoryLive",
			contact != null && memoryAfterHide > 0f && memoryAfterHide < 1f,
			contact != null ? $"conf={memoryAfterHide:F2}" : "null");

		Vector3 heardPos = seenPos + Vector3.forward * 4f;
		processor.ApplySyntheticSound(target, heardPos, 1f);
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G7_SoundKeepsContact",
			contact != null && contact.HasSoundEvidence && selector.SelectedTarget == target,
			contact != null ? $"sound={contact.SoundConfidence:F2}" : "null");
		Check("G7_SoundNotObserved",
			contact != null && contact.ObservationState != ObservationState.Observed,
			contact != null ? contact.ObservationState.ToString() : "null");
		Check("G7_SoundNoAim",
			!selector.HasSelectedAimPoint && selector.GetEngageableSelectedTarget() == null,
			$"aim={selector.HasSelectedAimPoint}");
		Check("G7_SoundDecisionTrackNotFire",
			engagement.CurrentDecision == EngagementDecision.Track &&
			engagement.CurrentDecision != EngagementDecision.Fire,
			engagement.CurrentDecision.ToString());
		Check("G7_SoundDoesNotResetVisionMemory",
			contact != null &&
			(contact.LastSeenPosition - lastSeenFrozen).sqrMagnitude < 0.01f &&
			contact.LastSeenConfidence <= memoryAfterHide + 0.02f &&
			contact.LastSeenConfidence < 1f,
			contact != null
				? $"seen={contact.LastSeenPosition} conf={contact.LastSeenConfidence:F2}"
				: "null");

		yield return new WaitForSeconds(processor.SoundHorizonSeconds + 0.45f);
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G7_SoundTtlExpired",
			contact != null && !contact.HasSoundEvidence,
			contact != null ? $"sound={contact.SoundConfidence:F2}" : "null");
		Check("G7_G4MemoryStillDecaying",
			contact != null && contact.LastSeenConfidence > 0f && contact.LastSeenConfidence < 1f,
			contact != null ? $"lastSeen={contact.LastSeenConfidence:F2}" : "null");
		Check("G7_AfterSoundTtlStillTrack",
			selector.SelectedTarget == target && engagement.CurrentDecision == EngagementDecision.Track,
			$"sel={selector.SelectedTarget} d={engagement.CurrentDecision}");

		selector.ClearLineOfFireSuppression();
		yield return ObserveFor(processor, target, target.position, 15f, m_ReacquireWaitSeconds);
		selector.SelectFromContacts();
		yield return null;
		EngagementDecision reacquired = engagement.CurrentDecision;
		Check("G7_ReacquireAimOrFire",
			selector.SelectedTarget == target && selector.HasSelectedAimPoint &&
			(reacquired == EngagementDecision.Aim || reacquired == EngagementDecision.Fire),
			$"{DetectionHarnessPlayMode.FormatSelectorProbe(processor, selector, target)} d={reacquired}");

		processor.ClearContacts();
		selector.ClearSelection(true);
		selector.ClearLineOfFireSuppression();
		yield return null;

		processor.ApplySyntheticSound(target, target.position, 1f);
		selector.SelectFromContacts();
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G7_SoundOnlySelectedTrack",
			contact != null &&
			selector.SelectedTarget == target &&
			!selector.HasSelectedAimPoint &&
			engagement.CurrentDecision == EngagementDecision.Track,
			$"{DetectionHarnessPlayMode.FormatSelectorProbe(processor, selector, target)} d={engagement.CurrentDecision}");
		Check("G7_SoundOnlyNotFire",
			engagement.CurrentDecision != EngagementDecision.Fire &&
			selector.GetEngageableSelectedTarget() == null,
			engagement.CurrentDecision.ToString());
		Check("G7_SoundOnlyUnknownIdentity",
			contact != null && contact.Identity == PerceivedIdentity.Unknown,
			contact != null ? contact.Identity.ToString() : "null");

		processor.ApplySyntheticShared(target, target.position + Vector3.right, 1f);
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G7_FusionOneContact",
			processor.Contacts.Count == 1 &&
			contact != null &&
			contact.HasSoundEvidence &&
			contact.HasSharedEvidence,
			$"count={processor.Contacts.Count} sound={contact != null && contact.HasSoundEvidence} shared={contact != null && contact.HasSharedEvidence}");

		if (vision != null)
			vision.enabled = visionWas;

		Finish();
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		Check("Math_SoundHorizonZero",
			Mathf.Approximately(SoundKnowledgeMath.Evaluate(SoundKnowledgeMath.DefaultHorizonSeconds, 1f), 0f),
			"Sound TTL → 0");
		Check("Math_SharedHorizonZero",
			Mathf.Approximately(SharedKnowledgeMath.Evaluate(SharedKnowledgeMath.DefaultHorizonSeconds, 1f), 0f),
			"Shared TTL → 0");

		ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
		var dummy = new GameObject("G7MathDummy");
		try
		{
			var soundOnly = new PerceivedContact
			{
				Target = dummy.transform,
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				SoundConfidence = 0.9f,
				LastKnownPosition = new Vector3(2f, 0f, 0f)
			};
			Check("Math_SoundOnlyEligible",
				ContactSelectionEligibility.Evaluate(soundOnly, true, policy, out _),
				"Sound-only selectable");
			Check("Math_SoundOnlyNoAim",
				!TargetSelectionMath.TryGetObservedAimPoint(soundOnly, out _),
				"Sound is not aim");

			var forgotten = new PerceivedContact
			{
				Target = dummy.transform,
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0f
			};
			Check("Math_ForgottenRejected",
				!ContactSelectionEligibility.Evaluate(forgotten, true, policy, out _),
				"No channel → forgotten");
		}
		finally
		{
			Destroy(dummy);
		}

		EngagementDecisionContext knowledge = new EngagementDecisionContext
		{
			HasSelectedTarget = true,
			HasContact = true,
			HasKnowledge = true,
			LastSeenConfidence = 0f,
			HasLosConfirmedAim = false,
			IsWorldEngageable = true,
			ObservationState = ObservationState.NotObserved,
			Identity = PerceivedIdentity.Unknown,
			Relationship = PerceivedRelationship.Unknown
		};
		Check("Math_KnowledgeTrackNotFire",
			EngagementDecisionMath.Evaluate(knowledge) == EngagementDecision.Track,
			"Non-visual knowledge → Track");
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
			Vector3 pos = _target != null ? _target.position : _pos;
			_processor.ApplySyntheticObservation(_target, _distanceMeters, 0f, 1f, pos);
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
		File.WriteAllText(Path.Combine(dir, $"DetectionG7_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG7_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG7AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
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
			Debug.LogError($"[DetectionG7AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}
