using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// G6 engagement-decision smoke. Writes Assets/_Docs/Logs/Tests/DetectionG6_LAST.txt
/// Runs after G5 (execution order 600, warmup 60s).
/// </summary>
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG6AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 60f;
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
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G6"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunGStage == "G6")
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
		AppendLine($"DetectionG6 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");

		RunPureMathChecks();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		TargetSelector selector = processor != null ? processor.GetComponent<TargetSelector>() : null;
		EngagementDecisionController engagement =
			processor != null ? processor.GetComponent<EngagementDecisionController>() : null;

		Check("G6_Processor", processor != null, "DetectionProcessor missing");
		Check("G6_Target", target != null, "target missing");
		Check("G6_Selector", selector != null, "TargetSelector missing");
		Check("G6_Engagement", engagement != null, "EngagementDecisionController missing");
		Check("Isolation_NoPerceivedContactFields",
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(PerceivedContact)),
			"EngagementDecisionController must not store PerceivedContact fields");
		Check("Isolation_DoesNotOwnDetectionMath",
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(MemoryDecayMath)) &&
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(IdentityKnowledgeMath)),
			"Engagement must not own decay/identity math types");
		Check("Isolation_DoesNotReferenceHitscan",
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(UnitWeaponHitscanShooting)),
			"Engagement must not own hitscan");

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

		Check("G6_NoneWhenNoTarget",
			engagement.CurrentDecision == EngagementDecision.None,
			engagement.CurrentDecision.ToString());

		Vector3 seenPos = target.position;
		yield return ObserveFor(processor, target, seenPos, 15f, m_ObserveSeconds);
		yield return null;

		processor.TryGetContact(target, out PerceivedContact contact);
		EngagementDecision observedDecision = engagement.CurrentDecision;
		bool observedOk = observedDecision == EngagementDecision.Aim ||
		                  observedDecision == EngagementDecision.Fire;
		Check("G6_ObserveAimOrFire",
			selector.SelectedTarget == target && selector.HasSelectedAimPoint && observedOk,
			$"sel={selector.SelectedTarget} aim={selector.HasSelectedAimPoint} d={observedDecision}");
		Check("G6_UnknownAllowed",
			contact == null || contact.Identity == PerceivedIdentity.Unknown ||
			contact.Identity == PerceivedIdentity.Hostile,
			contact != null ? contact.Identity.ToString() : "null");
		Check("G6_ObservedNotTrack",
			observedDecision != EngagementDecision.Track &&
			observedDecision != EngagementDecision.Ignore &&
			observedDecision != EngagementDecision.None,
			observedDecision.ToString());

		processor.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_RecentlyLostProbeSeconds);
		yield return null;
		processor.TryGetContact(target, out contact);
		Check("G6_HiddenKeepsSelection",
			selector.SelectedTarget == target,
			selector.SelectedTarget != null ? selector.SelectedTarget.name : "null");
		Check("G6_HiddenIsTrack",
			engagement.CurrentDecision == EngagementDecision.Track,
			engagement.CurrentDecision.ToString());
		Check("G6_HiddenNoFire",
			engagement.CurrentDecision != EngagementDecision.Fire &&
			selector.GetEngageableSelectedTarget() == null,
			$"d={engagement.CurrentDecision} engage={selector.GetEngageableSelectedTarget()}");
		Check("G6_LastKnownNotAim",
			contact == null || selector.SelectedAimPointWorld == Vector3.zero ||
			(selector.SelectedAimPointWorld - contact.LastKnownPosition).sqrMagnitude > 0.01f,
			contact != null ? $"known={contact.LastKnownPosition} aim={selector.SelectedAimPointWorld}" : "null");

		float remaining = Mathf.Max(0.5f, processor.MemoryHorizonSeconds + 0.4f);
		yield return new WaitForSeconds(remaining);
		processor.ApplyEmptyObservationFrame();
		yield return null;
		Check("G6_ForgottenIsNone",
			selector.SelectedTarget == null && engagement.CurrentDecision == EngagementDecision.None,
			$"sel={selector.SelectedTarget} d={engagement.CurrentDecision}");

		selector.ClearLineOfFireSuppression();
		yield return ObserveFor(processor, target, target.position, 15f, m_ReacquireWaitSeconds);
		selector.SelectFromContacts();
		yield return null;
		EngagementDecision reacquired = engagement.CurrentDecision;
		Check("G6_ReacquireAimOrFire",
			selector.SelectedTarget == target && selector.HasSelectedAimPoint &&
			(reacquired == EngagementDecision.Aim || reacquired == EngagementDecision.Fire),
			$"{DetectionHarnessPlayMode.FormatSelectorProbe(processor, selector, target)} d={reacquired}");

		processor.ClearContacts();
		yield return null;
		Check("G6_ClearContactsNone",
			selector.SelectedTarget == null && engagement.CurrentDecision == EngagementDecision.None,
			$"sel={selector.SelectedTarget} d={engagement.CurrentDecision}");

		if (vision != null)
			vision.enabled = visionWas;

		Finish();
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		EngagementDecisionContext none = new EngagementDecisionContext();
		Check("Math_NoTargetNone",
			EngagementDecisionMath.Evaluate(none) == EngagementDecision.None,
			"No target → None");

		EngagementDecisionContext unknown = FireReadyContext();
		unknown.Identity = PerceivedIdentity.Unknown;
		Check("Math_UnknownCanFire",
			EngagementDecisionMath.Evaluate(unknown) == EngagementDecision.Fire,
			"Unknown may Fire");

		EngagementDecisionContext friendly = FireReadyContext();
		friendly.Identity = PerceivedIdentity.Friendly;
		friendly.Relationship = PerceivedRelationship.Friendly;
		Check("Math_FriendlyIgnore",
			EngagementDecisionMath.Evaluate(friendly) == EngagementDecision.Ignore,
			"Friendly → Ignore");

		EngagementDecisionContext forgotten = FireReadyContext();
		forgotten.LastSeenConfidence = 0f;
		forgotten.HasLosConfirmedAim = false;
		Check("Math_ForgottenIgnore",
			EngagementDecisionMath.Evaluate(forgotten) == EngagementDecision.Ignore,
			"Forgotten → Ignore");

		EngagementDecisionContext memory = FireReadyContext();
		memory.HasLosConfirmedAim = false;
		memory.ObservationState = ObservationState.Lost;
		memory.LastSeenConfidence = 0.5f;
		Check("Math_MemoryTrack",
			EngagementDecisionMath.Evaluate(memory) == EngagementDecision.Track,
			"Memory → Track");
		Check("Math_MemoryNotFire",
			EngagementDecisionMath.Evaluate(memory) != EngagementDecision.Fire,
			"Memory must not Fire");

		EngagementDecisionContext noAim = FireReadyContext();
		noAim.AimReadyToFire = false;
		Check("Math_NoAimProgressAim",
			EngagementDecisionMath.Evaluate(noAim) == EngagementDecision.Aim,
			"LOS without aim progress → Aim");

		Check("Math_FireWhenGatesPass",
			EngagementDecisionMath.Evaluate(FireReadyContext()) == EngagementDecision.Fire,
			"Gates pass → Fire");
	}

	private static EngagementDecisionContext FireReadyContext()
	{
		return new EngagementDecisionContext
		{
			HasSelectedTarget = true,
			HasContact = true,
			Identity = PerceivedIdentity.Unknown,
			Relationship = PerceivedRelationship.Unknown,
			Threat = ThreatLevel.None,
			ObservationState = ObservationState.Observed,
			LastSeenConfidence = 1f,
			IsWorldEngageable = true,
			HasLosConfirmedAim = true,
			WeaponCanFireEventually = true,
			AimReadyToFire = true
		};
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
		File.WriteAllText(Path.Combine(dir, $"DetectionG6_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG6_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG6AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
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
			Debug.LogError($"[DetectionG6AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}
