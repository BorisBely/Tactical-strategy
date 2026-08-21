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
/// V1.9.1 runtime A–H: production UnitVision → Perception → DetectionProcessor.
/// Physical cover staging only. Does not retune defaults. Does not inject synthetic Q.
/// Report: Assets/_Docs/Logs/Tests/DetectionCalibrationRuntime_LAST.txt
/// </summary>
[DefaultExecutionOrder(58)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionCalibrationRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_TimeoutSeconds = 12f;
	private const float c_ScanPulseSeconds = 0.25f;
	private const float c_DistanceTolMeters = 2.5f;
	private const float c_FovTolDegrees = 8f;
	private const float c_ExposureTol = 0.20f;
	private const float c_TimeAbsSeconds = 0.45f;
	private const float c_TimeRel = 0.40f;
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart = true;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunCalibrationRuntime) &&
		!DetectionHarnessPlayMode.RunCalibrationStrict &&
		!DetectionHarnessPlayMode.IsGRegressionPlay &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
		!DetectionHarnessPlayMode.RunIdentityCalibration &&
		!DetectionHarnessPlayMode.RunAIPerceptionHandoff &&
		!DetectionHarnessPlayMode.RunAITacticalState &&
		!DetectionHarnessPlayMode.RunUseOfForcePolicy &&
		!DetectionHarnessPlayMode.RunCombatEngageExecution &&
		!DetectionHarnessPlayMode.RunSearchExecution &&
		!DetectionHarnessPlayMode.RunTacticalNavigationExecution &&
		!DetectionHarnessPlayMode.RunTacticalCommandContract &&
		!DetectionHarnessPlayMode.RunGameCommandSource &&
		!DetectionHarnessPlayMode.RunGameCommandInput &&
		!DetectionHarnessPlayMode.RunGameCommandLayer &&
		!DetectionHarnessPlayMode.RunVisionEnvelope;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[DetectionCalibrationRuntimeSmoke] V1.9.1 runtime A–H starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunCalibrationRuntime)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
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
		AppendLine("VISION DETECTION CALIBRATION RUNTIME V1.9.1");
		AppendLine("===========================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("path=UnitVision → VisionObservation → UnitPerception → DetectionProcessor → PerceivedContact");
		AppendLine("defaults unchanged (FOV 60/0.15 AcquireThreshold=0.25 AcquireTime=0.35)");
		AppendLine("LOD/skip is not a Q penalty; no synthetic ApplySyntheticObservation");
		AppendLine("V1.9.1/V1.9.2: physical cover staging + yaw mirror if scene fully blocks LOS");
		AppendLine("---");

		if (m_Harness == null)
			m_Harness = GetComponent<DetectionTestController>();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform observer = m_Harness != null ? m_Harness.Observer : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		UnitVision vision = null;
		if (observer != null)
			observer.TryGetComponent(out vision);

		Check("Harness_Processor", processor != null, "DetectionProcessor missing");
		Check("Harness_Vision", vision != null && vision.enabled, "UnitVision missing or disabled");
		Check("Harness_Perception", processor != null && processor.GetComponent<UnitPerception>() != null,
			"UnitPerception missing");
		Check("Harness_Target", target != null, "Target missing");

		UnitVisionRegistry registry = UnityEngine.Object.FindAnyObjectByType<UnitVisionRegistry>();
		int enemyCount = registry != null ? registry.EnemyUnitCount : 0;
		Check("Harness_RegistryEnemy", enemyCount >= 1, $"EnemyUnitCount={enemyCount}");

		if (processor == null || vision == null || !vision.enabled || target == null || m_Harness == null)
		{
			Finish();
			yield break;
		}

		if (observer.TryGetComponent(out UnitVision observerVision))
			observerVision.RefreshBodyHitZones();
		if (target.TryGetComponent(out UnitVision targetVision))
			targetVision.RefreshBodyHitZones();

		DetectionCalibrationScenarios.Scenario[] scenarios = DetectionCalibrationScenarios.All;
		for (int i = 0; i < scenarios.Length; i++)
			yield return RunScenario(scenarios[i], processor, vision, observer, target);

		Finish();
	}

	private IEnumerator RunScenario(
		DetectionCalibrationScenarios.Scenario _scenario,
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target)
	{
		DetectionCalibrationScenarios.QSnapshot math = DetectionCalibrationScenarios.EvaluateQ(_scenario);
		DetectionCalibrationScenarios.ProgressRun mathRun = DetectionCalibrationScenarios.SimulateProgress(math.Q);
		bool expectDetected = mathRun.Detected;

		m_Harness.ApplyCalibrationScenario(_scenario);
		yield return new WaitForFixedUpdate();
		yield return null;
		Physics.SyncTransforms();

		if (_observer.TryGetComponent(out UnitVision observerVision))
			observerVision.RefreshBodyHitZones();
		if (_target.TryGetComponent(out UnitVision targetVision))
			targetVision.RefreshBodyHitZones();

		_processor.ClearContacts();
		_vision.ScanStats.Reset();
		_vision.RequestImmediateScan();

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		UnitVisionRegistry registry = UnityEngine.Object.FindAnyObjectByType<UnitVisionRegistry>();

		float t0 = Time.time;
		float tDetect = -1f;
		bool detected = false;
		float lastPulse = t0;
		DetectionCalibrationScenarios.QSnapshot runtime = default;
		VisionObservation lastObs = default;
		bool hasObs = false;
		float distAtFirstObs = -1f;
		DetectionState lastState = DetectionState.Undetected;
		ObservationState lastObsState = ObservationState.Lost;
		float lastProgress = 0f;

		while (Time.time - t0 < c_TimeoutSeconds)
		{
			if (Time.time - lastPulse >= c_ScanPulseSeconds)
			{
				_vision.RequestImmediateScan();
				lastPulse = Time.time;
			}

			yield return null;

			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation frameObs) &&
			    frameObs.IsVisible)
			{
				lastObs = frameObs;
				hasObs = true;
				if (distAtFirstObs < 0f)
					distAtFirstObs = Mathf.Sqrt(Mathf.Max(0f, frameObs.DistanceSq));
			}

			if (!_processor.TryGetContact(_target, out PerceivedContact contact) || contact == null)
				continue;

			lastState = contact.State;
			lastObsState = contact.ObservationState;
			lastProgress = contact.DetectionProgress;
			if (contact.LastObservation.IsVisible)
			{
				lastObs = contact.LastObservation;
				hasObs = true;
				if (distAtFirstObs < 0f)
					distAtFirstObs = Mathf.Sqrt(Mathf.Max(0f, lastObs.DistanceSq));
			}

			runtime.DistanceFactor = contact.CurrentEvaluation.DistanceFactor;
			runtime.FovFactor = contact.CurrentEvaluation.FovFactor;
			runtime.ExposureFactor = contact.CurrentEvaluation.ExposureFactor;
			runtime.MovementFactor = contact.CurrentEvaluation.MovementFactor;
			runtime.Q = contact.CurrentEvaluation.VisibilityQuality;

			if (!detected && contact.State == DetectionState.Detected)
			{
				detected = true;
				tDetect = Time.time - t0;
				break;
			}
		}

		VisionScanStats stats = _vision.ScanStats;
		Vector3 toTarget = _target.position - _observer.position;
		toTarget.y = 0f;
		float worldDist = toTarget.magnitude;
		float lookAngle = Vector3.Angle(
			new Vector3(_observer.forward.x, 0f, _observer.forward.z),
			toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward);

		float distActual = distAtFirstObs >= 0f
			? distAtFirstObs
			: (hasObs ? Mathf.Sqrt(Mathf.Max(0f, lastObs.DistanceSq)) : worldDist);
		float fovActual = hasObs ? lastObs.FovOffsetDegrees : -1f;
		float expActual = hasObs ? lastObs.Exposure01 : 0f;

		if (hasObs && runtime.Q <= 0.0001f)
		{
			float obsDist = Mathf.Sqrt(Mathf.Max(0f, lastObs.DistanceSq));
			float resolvedRange = _vision != null
				? _vision.ResolvedMaxRange
				: DetectionQualityMath.DefaultFarMeters;
			runtime.DistanceFactor = DetectionQualityMath.DistanceFactor(obsDist, resolvedRange);
			float fovHalf = DetectionQualityMath.DefaultFovHalfDegrees;
			if (_vision != null)
			{
				ResolvedVisionProfile profile = _vision.CurrentVisionProfile;
				fovHalf = lastObs.Source == VisionObservationSource.Optic
					? profile.ScopeHalfFovDegrees
					: profile.EyeHalfFovDegrees;
			}

			runtime.FovFactor = DetectionQualityMath.FovFactor(lastObs.FovOffsetDegrees, fovHalf);
			runtime.ExposureFactor = Mathf.Clamp01(lastObs.Exposure01);
			if (runtime.MovementFactor < 1f)
				runtime.MovementFactor = 1f;
			runtime.Q = DetectionQualityMath.VisibilityQuality(
				runtime.DistanceFactor,
				runtime.FovFactor,
				runtime.ExposureFactor,
				runtime.MovementFactor);
		}

		string tRuntime = detected ? F(tDetect, 2) : "timeout";
		string tMath = mathRun.Detected ? F(mathRun.TDetect, 2) : "timeout";

		AppendLine($"RT {_scenario.Id}");
		AppendLine(
			$"expected dist={F(_scenario.DistanceMeters, 1)} fov={F(_scenario.FovOffsetDegrees, 1)} " +
			$"exp={F(_scenario.Exposure01, 2)} move={F(math.MovementFactor, 2)} Q={F(math.Q, 4)} tDetect={tMath}");
		AppendLine(
			$"runtime   dist={F(distActual, 1)} fov={F(fovActual, 1)} " +
			$"exp={F(expActual, 2)} move={F(runtime.MovementFactor, 2)} Q={F(runtime.Q, 4)} tDetect={tRuntime}");
		AppendLine(
			$"factors   D={F(runtime.DistanceFactor, 3)} F={F(runtime.FovFactor, 3)} " +
			$"E={F(runtime.ExposureFactor, 3)} M={F(runtime.MovementFactor, 3)} " +
			$"(math D={F(math.DistanceFactor, 3)} F={F(math.FovFactor, 3)} E={F(math.ExposureFactor, 3)} M={F(math.MovementFactor, 3)})");
		AppendLine(
			$"state={lastState} progress={F(lastProgress, 3)} obs={lastObsState} " +
			$"visible={(hasObs ? "1" : "0")}");
		AppendLine(
			$"scan scans={stats.VisionScanCount} cand={stats.LastScanCandidateCount} " +
			$"range={stats.LastScanRangePassCount} fov={stats.LastScanFovPassCount} " +
			$"los={stats.LastScanLosCheckCount} hitZones={stats.LastScanHitZoneCheckCount} " +
			$"perception={(perception != null ? perception.ObservationCount : 0)} " +
			$"registryP/E={(registry != null ? registry.PlayerUnitCount : 0)}/{(registry != null ? registry.EnemyUnitCount : 0)} " +
			$"worldDist={F(worldDist, 1)} lookAngle={F(lookAngle, 1)} " +
			$"losBlocker={(_vision.DebugLastLosBlocker ?? "none")} " +
			$"staging={m_Harness.ExposureStaging.Note} cover={m_Harness.ExposureStaging.CoverName}");

		bool distOk = Mathf.Abs(distActual - _scenario.DistanceMeters) <= c_DistanceTolMeters;
		Check($"Runtime_{_scenario.Id}_Distance", distOk,
			$"expected={F(_scenario.DistanceMeters, 1)} actual={F(distActual, 1)}");

		bool fovOk = hasObs && Mathf.Abs(fovActual - _scenario.FovOffsetDegrees) <= c_FovTolDegrees;
		Check($"Runtime_{_scenario.Id}_Fov", fovOk,
			hasObs
				? $"expected={F(_scenario.FovOffsetDegrees, 1)} actual={F(fovActual, 1)}"
				: "no VisionObservation");

		bool exposureOk = hasObs && Mathf.Abs(expActual - _scenario.Exposure01) <= c_ExposureTol;
		Check($"Runtime_{_scenario.Id}_Exposure", exposureOk,
			hasObs
				? $"design={F(_scenario.Exposure01, 2)} hit-zones={F(expActual, 2)} (do not retune AcquireThreshold if this fails)"
				: "no VisionObservation");

		if (expectDetected)
		{
			Check($"Runtime_{_scenario.Id}_Detected", detected,
				detected ? $"tDetect={tRuntime}" : "timeout");
			if (detected && exposureOk)
			{
				float tol = Mathf.Max(c_TimeAbsSeconds, mathRun.TDetect * c_TimeRel);
				bool timeOk = Mathf.Abs(tDetect - mathRun.TDetect) <= tol;
				Check($"Runtime_{_scenario.Id}_Time", timeOk,
					$"math={tMath} runtime={tRuntime} tol={F(tol, 2)}");
			}
			else if (detected && !exposureOk)
			{
				AppendLine($"SKIP Runtime_{_scenario.Id}_Time (exposure mismatch — physical observation, not threshold)");
			}
		}
		else
		{
			Check($"Runtime_{_scenario.Id}_NoDetect", !detected,
				detected ? $"false Detected t={tRuntime}" : "timeout as math");
		}

		AppendLine("");
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionCalibrationRuntime_LAST.txt");
		File.WriteAllText(latest, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[DetectionCalibrationRuntimeSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCalibrationRuntime;
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
			Debug.LogError($"[DetectionCalibrationRuntimeSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);

	private static string F(float _value, int _digits)
	{
		return _value.ToString("F" + _digits, CultureInfo.InvariantCulture);
	}
	#endregion
}
