using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// G5 selector-from-contacts smoke. Writes Assets/_Docs/Logs/Tests/DetectionG5_LAST.txt
/// Runs after G4 (execution order 500).
/// </summary>
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG5AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 40f;
	[SerializeField] private float m_ObserveSeconds = 2.2f;
	[SerializeField] private float m_RecentlyLostProbeSeconds = 0.25f;
	[SerializeField] private float m_ReacquireWaitSeconds = 0.35f;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_ForcedDummy;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G5"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (m_ForcedDummy != null)
			Destroy(m_ForcedDummy);
		if (DetectionHarnessPlayMode.RunGStage == "G5")
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
		AppendLine($"DetectionG5 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		RunPureMathChecks();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		TargetSelector selector = processor != null ? processor.GetComponent<TargetSelector>() : null;

		Check("G5_Processor", processor != null, "DetectionProcessor missing");
		Check("G5_Target", target != null, "target missing");
		Check("G5_Selector", selector != null, "TargetSelector missing");
		Check("Isolation_SelectorHasNoPerceivedContactFields",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(PerceivedContact)),
			"TargetSelector must not store PerceivedContact fields");
		Check("Isolation_SelectorDoesNotCallDetectionMath",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(MemoryDecayMath)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(IdentityKnowledgeMath)),
			"Selector must not own decay/identity math types");

		if (processor == null || target == null || selector == null)
		{
			Finish();
			yield break;
		}

		m_Harness.ResetPairToIdleCalibrationPad();
		processor = m_Harness.DetectionProcessor;
		target = m_Harness.Target;
		selector = processor != null ? processor.GetComponent<TargetSelector>() : null;
		if (processor == null || target == null || selector == null)
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

		Vector3 seenPos = target.position;
		yield return ObserveFor(processor, target, seenPos, 15f, m_ObserveSeconds);

		processor.TryGetContact(target, out PerceivedContact contact);
		Check("G5_HasContact", contact != null, "contact missing after observe");
		Check("G5_SelectsFromContact",
			selector.SelectedTarget == target,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");
		Check("G5_ObservedHasAim",
			selector.HasSelectedAimPoint && selector.GetEngageableSelectedTarget() == target,
			$"aim={selector.HasSelectedAimPoint} engage={selector.GetEngageableSelectedTarget()}");
		Check("G5_UnknownAllowed",
			contact == null || contact.Identity == PerceivedIdentity.Unknown ||
			contact.Identity == PerceivedIdentity.Hostile,
			contact != null ? contact.Identity.ToString() : "null");

		processor.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_RecentlyLostProbeSeconds);
		processor.TryGetContact(target, out contact);
		Check("G5_HiddenKeepsSelection",
			selector.SelectedTarget == target,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");
		Check("G5_HiddenNoEngageableAim",
			!selector.HasSelectedAimPoint && selector.GetEngageableSelectedTarget() == null,
			$"aim={selector.HasSelectedAimPoint} engage={selector.GetEngageableSelectedTarget()}");
		Check("G5_LastKnownNotAim",
			contact == null || (selector.SelectedAimPointWorld - contact.LastKnownPosition).sqrMagnitude > 0.01f ||
			selector.SelectedAimPointWorld == Vector3.zero,
			contact != null ? $"known={contact.LastKnownPosition} aim={selector.SelectedAimPointWorld}" : "null");

		float remaining = Mathf.Max(0.5f, processor.MemoryHorizonSeconds + 0.4f);
		yield return new WaitForSeconds(remaining);
		processor.ApplyEmptyObservationFrame();
		yield return null;
		Check("G5_ForgottenDeselects",
			selector.SelectedTarget == null,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");

		selector.ClearLineOfFireSuppression();
		yield return ObserveFor(processor, target, target.position, 15f, m_ReacquireWaitSeconds);
		selector.SelectFromContacts();
		Check("G5_ReacquireSelects",
			selector.SelectedTarget == target && selector.HasSelectedAimPoint,
			DetectionHarnessPlayMode.FormatSelectorProbe(processor, selector, target));

		m_ForcedDummy = new GameObject("G5_ForcedDummy");
		m_ForcedDummy.transform.position = seenPos + Vector3.right * 80f;
		selector.ForcedPriorityTarget = m_ForcedDummy.transform;
		processor.Advance(0.05f, Time.time);
		Check("G5_ForcedWithoutContactIgnored",
			selector.SelectedTarget != m_ForcedDummy.transform,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");
		selector.ClearLineOfFireSuppression();
		selector.ForcedPriorityTarget = target;
		processor.Advance(0.05f, Time.time);
		Check("G5_ForcedWithContactWins",
			selector.SelectedTarget == target,
			DetectionHarnessPlayMode.FormatSelectorProbe(processor, selector, target));
		selector.ForcedPriorityTarget = null;

		processor.ClearContacts();
		Check("G5_ClearContactsDeselects",
			selector.SelectedTarget == null,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");

		if (vision != null)
			vision.enabled = visionWas;

		Finish();
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
		var unknown = new PerceivedContact
		{
			Target = transform,
			Identity = PerceivedIdentity.Unknown,
			LastSeenConfidence = 1f,
			ObservationState = ObservationState.Observed
		};
		Check("Math_UnknownEligible",
			ContactSelectionEligibility.Evaluate(unknown, true, policy, out _),
			"Unknown must be selectable");

		var friendly = new PerceivedContact
		{
			Target = transform,
			Identity = PerceivedIdentity.Friendly,
			Relationship = PerceivedRelationship.Friendly,
			LastSeenConfidence = 1f,
			ObservationState = ObservationState.Observed
		};
		Check("Math_FriendlyRejected",
			!ContactSelectionEligibility.Evaluate(friendly, true, policy, out _),
			"Friendly must not be selected");

		var forgotten = new PerceivedContact
		{
			Target = transform,
			LastSeenConfidence = 0f,
			ObservationState = ObservationState.Lost
		};
		Check("Math_ForgottenRejected",
			!ContactSelectionEligibility.Evaluate(forgotten, true, policy, out _),
			"Forgotten must not be selected");

		var observed = new PerceivedContact
		{
			ObservationState = ObservationState.Observed,
			LastSeenConfidence = 1f,
			LastKnownPosition = new Vector3(20f, 0f, 0f)
		};
		var stale = new PerceivedContact
		{
			ObservationState = ObservationState.Lost,
			LastSeenConfidence = 0.2f,
			LastKnownPosition = new Vector3(2f, 0f, 0f),
			Threat = ThreatLevel.High
		};
		Check("Math_ObservedBeatsStale",
			TargetSelectionMath.Score(observed, Vector3.zero, policy) >
			TargetSelectionMath.Score(stale, Vector3.zero, policy),
			"Observed must beat stale");
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
		File.WriteAllText(Path.Combine(dir, $"DetectionG5_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG5_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG5AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
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
			Debug.LogError($"[DetectionG5AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);
	#endregion
}
