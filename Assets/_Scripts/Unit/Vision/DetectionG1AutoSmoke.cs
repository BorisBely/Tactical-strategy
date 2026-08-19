using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// G1.1 AutoSmoke: math invariants + synthetic runtime + LastObservation preservation.
/// Report: Assets/_Docs/Logs/Tests/DetectionG1_LAST.txt
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG1AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	[SerializeField] private float m_SampleWaitSeconds = 0.35f;
	[SerializeField] private float m_SoftLoseProbeSeconds = 0.2f;
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
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G1"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunGStage == "G1")
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
		yield return null;
		yield return null;

		m_Report.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine($"DetectionG1.1 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine($"unity={Application.unityVersion} scene={gameObject.scene.name}");
		AppendLine("---");

		RunPureMathChecks();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : FindAnyObjectByType<DetectionProcessor>();
		Transform target = m_Harness != null ? m_Harness.Target : null;
		UnitVision vision = null;
		TargetSelector selector = null;

		if (processor != null)
		{
			processor.TryGetComponent(out vision);
			processor.TryGetComponent(out selector);
			if (target == null)
				target = FindEnemyTarget();
		}

		Check("Harness_ProcessorPresent", processor != null, "DetectionProcessor missing");
		Check("Harness_PerceptionPresent", processor != null && processor.GetComponent<UnitPerception>() != null, "UnitPerception missing");
		Check("Harness_TargetPresent", target != null, "Target missing");
		Check("Isolation_SelectorHasNoPerceivedContactFields",
			!TypeHasPerceivedContactField(typeof(TargetSelector)),
			"TargetSelector must not hold PerceivedContact");

		if (processor != null && target != null)
		{
			bool visionWasEnabled = vision != null && vision.enabled;
			if (vision != null)
				vision.enabled = false;

			yield return RunSyntheticRuntimeChecks(processor, target, selector);

			if (vision != null)
				vision.enabled = visionWasEnabled;
		}
		else
		{
			AppendLine("SKIP runtime synthetic checks (wiring incomplete)");
		}

		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string path = WriteReport("DetectionG1");
		Debug.Log($"[DetectionG1AutoSmoke] wrote {path} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);

#if UNITY_EDITOR
		if (m_ExitPlayModeWhenDone)
			EditorApplication.isPlaying = false;
#endif
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		float dNear = DetectionQualityMath.DistanceFactor(10f);
		float dFar = DetectionQualityMath.DistanceFactor(400f);
		Check("Invariant_Distance", dNear > dFar, $"near.Qfactor={dNear:F3} far={dFar:F3}");

		float fCenter = DetectionQualityMath.FovFactor(0f);
		float fEdge = DetectionQualityMath.FovFactor(50f);
		Check("Invariant_FOV", fCenter > fEdge, $"center={fCenter:F3} edge={fEdge:F3}");

		float qFull = DetectionQualityMath.VisibilityQuality(dNear, fCenter, 1f, 1f);
		float qPartial = DetectionQualityMath.VisibilityQuality(dNear, fCenter, 0.3f, 1f);
		Check("Invariant_Exposure", qFull > qPartial, $"full={qFull:F3} partial={qPartial:F3}");

		float qIdle = DetectionQualityMath.VisibilityQuality(dFar, fEdge, 0.1f, DetectionQualityMath.MovementFactor(0f));
		float qRun = DetectionQualityMath.VisibilityQuality(dFar, fEdge, 0.1f, DetectionQualityMath.MovementFactor(4.5f));
		Check("Invariant_Movement", qRun > qIdle && DetectionQualityMath.MovementFactor(0f) >= 1f - 1e-5f,
			$"idle={qIdle:F3} run={qRun:F3} movIdle={DetectionQualityMath.MovementFactor(0f):F3}");

		float mid = (DetectionQualityMath.DefaultLoseThreshold + DetectionQualityMath.DefaultAcquireThreshold) * 0.5f;
		float held = DetectionQualityMath.IntegrateProgress(0.55f, mid, 0.5f);
		Check("Invariant_HysteresisHold", Mathf.Abs(held - 0.55f) < 0.0001f, $"midQ={mid:F3} held={held:F3}");

		float acq = DetectionQualityMath.IntegrateProgress(0f, 1f, 0.1f);
		float loss = DetectionQualityMath.IntegrateProgress(1f, 0f, 0.1f);
		Check("Invariant_AcquireFasterThanLose", acq > (1f - loss), $"acq={acq:F3} loseDelta={1f - loss:F3}");
	}

	private IEnumerator RunSyntheticRuntimeChecks(
		DetectionProcessor _processor,
		Transform _target,
		TargetSelector _selector)
	{
		AppendLine("[RUNTIME]");
		_processor.ClearContacts();
		Vector3 pos = _target.position;

		_processor.ApplySyntheticObservation(_target, 10f, 0f, 1f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		_processor.TryGetContact(_target, out PerceivedContact near);
		float qNear = near != null ? near.VisibilityQuality : -1f;

		_processor.ClearContacts();
		_processor.ApplySyntheticObservation(_target, 400f, 50f, 0.1f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		_processor.TryGetContact(_target, out PerceivedContact far);
		float qFar = far != null ? far.VisibilityQuality : -1f;
		AppendLine($"  Distance: near.Q={qNear:F3} far.Q={qFar:F3}");
		Check("Runtime_Distance", qNear > qFar, $"near={qNear:F3} far={qFar:F3}");

		_processor.ClearContacts();
		_processor.ApplySyntheticObservation(_target, 80f, 0f, 1f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		_processor.TryGetContact(_target, out PerceivedContact center);
		float qCenter = center != null ? center.VisibilityQuality : -1f;

		_processor.ClearContacts();
		_processor.ApplySyntheticObservation(_target, 80f, 50f, 1f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		_processor.TryGetContact(_target, out PerceivedContact edge);
		float qEdge = edge != null ? edge.VisibilityQuality : -1f;
		AppendLine($"  FOV: center.Q={qCenter:F3} edge.Q={qEdge:F3}");
		Check("Runtime_FOV", qCenter > qEdge, $"center={qCenter:F3} edge={qEdge:F3}");

		_processor.ClearContacts();
		_processor.ApplySyntheticObservation(_target, 80f, 0f, 1f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		_processor.TryGetContact(_target, out PerceivedContact fullExp);
		float qFull = fullExp != null ? fullExp.VisibilityQuality : -1f;

		_processor.ClearContacts();
		_processor.ApplySyntheticObservation(_target, 80f, 0f, 0.25f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		_processor.TryGetContact(_target, out PerceivedContact partExp);
		float qPart = partExp != null ? partExp.VisibilityQuality : -1f;
		AppendLine($"  Exposure: full.Q={qFull:F3} partial.Q={qPart:F3}");
		Check("Runtime_Exposure", qFull > qPart, $"full={qFull:F3} partial={qPart:F3}");

		_processor.ClearContacts();
		_processor.ApplySyntheticObservation(_target, 10f, 0f, 1f, pos);
		yield return new WaitForSeconds(m_SampleWaitSeconds);
		bool has = _processor.TryGetContact(_target, out PerceivedContact contact);
		Check("Runtime_ContactCreated", has && contact != null, "missing contact");
		if (!has || contact == null)
			yield break;

		VisionObservation lastObs = contact.LastObservation;
		Vector3 lastAim = lastObs.AimPoint;
		float progressBefore = contact.DetectionProgress;
		AppendLine($"  sample P={progressBefore:F3} Obs={contact.ObservationState} Det={contact.State}");

		_processor.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_SoftLoseProbeSeconds);
		_processor.TryGetContact(_target, out PerceivedContact after);
		Check("Runtime_ContactSurvives", after != null, "contact deleted");
		if (after == null)
			yield break;

		AppendLine($"  SoftLose: before.P={progressBefore:F3} after.P={after.DetectionProgress:F3} Q={after.VisibilityQuality:F3}");
		Check("Runtime_SoftLose", after.DetectionProgress > progressBefore * 0.5f, $"before={progressBefore:F3} after={after.DetectionProgress:F3}");
		Check("Runtime_EmptySetsQ0", after.VisibilityQuality <= 0.0001f, $"Q={after.VisibilityQuality:F3}");
		Check("Runtime_LastObservationPreserved",
			after.LastObservation.IsVisible && (after.LastObservation.AimPoint - lastAim).sqrMagnitude < 0.0001f,
			"LastObservation must not be wiped on empty frame");
		Check("Runtime_ObservationStateRecentlyLostOrObserved",
			after.ObservationState == ObservationState.RecentlyLost || after.ObservationState == ObservationState.Observed,
			$"state={after.ObservationState}");

		Transform selectedBeforeClear = _selector != null ? _selector.SelectedTarget : null;
		_processor.ClearContacts();
		Transform selectedAfterClear = _selector != null ? _selector.SelectedTarget : null;
		Check("G5_ClearContactsDeselects",
			_selector == null || selectedAfterClear == null,
			$"before={(selectedBeforeClear != null ? selectedBeforeClear.name : "null")} after={(selectedAfterClear != null ? selectedAfterClear.name : "null")}");
	}

	private static Transform FindEnemyTarget()
	{
		UnitTeam[] teams = FindObjectsByType<UnitTeam>();
		for (int i = 0; i < teams.Length; i++)
		{
			if (teams[i] != null && teams[i].Team == UnitTeamId.Enemy)
				return teams[i].transform;
		}
		return null;
	}

	private static bool TypeHasPerceivedContactField(Type _type)
	{
		FieldInfo[] fields = _type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			Type ft = fields[i].FieldType;
			if (ft == typeof(PerceivedContact))
				return true;
			if (!ft.IsGenericType)
				continue;
			Type[] args = ft.GetGenericArguments();
			for (int a = 0; a < args.Length; a++)
			{
				if (args[a] == typeof(PerceivedContact))
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
			Debug.LogError($"[DetectionG1AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);

	private string WriteReport(string _prefix)
	{
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string stamped = Path.Combine(dir, $"{_prefix}_Autosmoke_{stamp}.txt");
		string latest = Path.Combine(dir, $"{_prefix}_LAST.txt");
		string body = m_Report.ToString();
		File.WriteAllText(stamped, body, Encoding.UTF8);
		File.WriteAllText(latest, body, Encoding.UTF8);
		return latest;
	}
	#endregion
}
