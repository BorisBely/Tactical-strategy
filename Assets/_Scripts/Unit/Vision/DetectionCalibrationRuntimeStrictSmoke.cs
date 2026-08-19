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
/// V1.9.4 strict runtime validation. Does not retune Q / FOV / thresholds.
/// Report: Assets/_Docs/Logs/Tests/DetectionCalibrationRuntimeStrict_LAST.txt
/// </summary>
[DefaultExecutionOrder(59)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionCalibrationRuntimeStrictSmoke : MonoBehaviour
{
	#region Constants
	private const float c_DetectTimeoutSeconds = 8f;
	private const float c_NoDetectTimeoutSeconds = 6f;
	private const float c_NegativeTimeoutSeconds = 3f;
	private const float c_ScanPulseSeconds = 0.25f;
	private const float c_DistanceTolMeters = 2.5f;
	private const float c_FovTolDegrees = 8f;
	private const float c_ExposureTol = 0.20f;
	private const float c_TimeAbsSeconds = 0.45f;
	private const float c_TimeRel = 0.40f;
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(24576);
	private readonly StringBuilder m_Summary = new StringBuilder(2048);
	private int m_PassCount;
	private int m_FailCount;
	private int m_ContractFailAtStart;
	private string m_ContractId = "";
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunCalibrationStrict) &&
		!DetectionHarnessPlayMode.RunMemoryCalibration &&
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

		Debug.Log("[DetectionCalibrationRuntimeStrictSmoke] V1.9.4 strict starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunCalibrationStrict)
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
		for (int i = 0; i < 6; i++)
			yield return null;

		m_Report.Clear();
		m_Summary.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine("VISION DETECTION STRICT VALIDATION");
		AppendLine("==================================");
		AppendLine($"stamp={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("V1.9.4 — no production retune; acceptance is distance/FOV/exposure/detection/time");
		AppendLine("F/G/H Q=0 is not a FAIL. Staging implementation is not an acceptance field.");
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
		Check("Harness_Target", target != null, "Target missing");
		if (processor == null || vision == null || target == null || m_Harness == null)
		{
			Finish();
			yield break;
		}

		vision.SetVisionRange(DetectionQualityMath.DefaultFarMeters);

		yield return RunDefaults(processor, vision);
		AppendLine("");
		AppendLine("A–H");
		AppendLine("-------");
		m_Summary.AppendLine("A–H");
		m_Summary.AppendLine("-------");

		DetectionCalibrationScenarios.Scenario[] scenarios = DetectionCalibrationScenarios.All;
		for (int i = 0; i < scenarios.Length; i++)
			yield return RunAhScenario(scenarios[i], processor, vision, observer, target);

		AppendLine("");
		AppendLine("NEGATIVE");
		AppendLine("--------");
		m_Summary.AppendLine("");
		m_Summary.AppendLine("NEGATIVE");
		m_Summary.AppendLine("--------");
		yield return RunNegatives(processor, vision, observer, target);

		AppendLine("");
		AppendLine("BOUNDARIES");
		AppendLine("----------");
		m_Summary.AppendLine("");
		m_Summary.AppendLine("BOUNDARIES");
		m_Summary.AppendLine("----------");
		yield return RunBoundaries(processor, vision, observer, target);

		AppendLine("");
		AppendLine("SCHEDULER");
		AppendLine("---------");
		m_Summary.AppendLine("");
		m_Summary.AppendLine("SCHEDULER");
		m_Summary.AppendLine("---------");
		yield return RunSkipNotEmpty(processor, vision, observer, target);

		AppendLine("");
		AppendLine("REGRESSION");
		AppendLine("----------");
		m_Summary.AppendLine("");
		m_Summary.AppendLine("REGRESSION");
		m_Summary.AppendLine("----------");
		AppendRegressionFromLastFiles();

		Finish();
	}

	private IEnumerator RunDefaults(DetectionProcessor _processor, UnitVision _vision)
	{
		BeginContract("Defaults");
		Check("Defaults_VisionRange",
			_vision != null && Mathf.Abs(_vision.VisionRange - DetectionQualityMath.DefaultFarMeters) < 0.5f,
			$"expected={DetectionQualityMath.DefaultFarMeters:0} runtime={(_vision != null ? _vision.VisionRange : 0f):0}");
		Check("Defaults_FovHalf",
			Mathf.Abs(DetectionQualityMath.DefaultFovHalfDegrees - 60f) < 0.01f &&
			Mathf.Abs(_processor.FovHalfReferenceDegrees - 60f) < 0.01f,
			$"math={DetectionQualityMath.DefaultFovHalfDegrees:0} runtime={_processor.FovHalfReferenceDegrees:0}");
		Check("Defaults_FovEdge",
			Mathf.Abs(DetectionQualityMath.DefaultFovEdgeFactor - 0.15f) < 0.001f &&
			Mathf.Abs(_processor.FovEdgeFactor - 0.15f) < 0.001f,
			$"math={DetectionQualityMath.DefaultFovEdgeFactor:0.00} runtime={_processor.FovEdgeFactor:0.00}");
		Check("Defaults_AcquireThreshold",
			Mathf.Abs(DetectionQualityMath.DefaultAcquireThreshold - 0.25f) < 0.001f &&
			Mathf.Abs(_processor.AcquireThreshold - 0.25f) < 0.001f,
			$"math={DetectionQualityMath.DefaultAcquireThreshold:0.00} runtime={_processor.AcquireThreshold:0.00}");
		Check("Defaults_LoseThreshold",
			Mathf.Abs(DetectionQualityMath.DefaultLoseThreshold - 0.20f) < 0.001f &&
			Mathf.Abs(_processor.LoseThreshold - 0.20f) < 0.001f,
			$"math={DetectionQualityMath.DefaultLoseThreshold:0.00} runtime={_processor.LoseThreshold:0.00}");
		Check("Defaults_AcquireTime",
			Mathf.Abs(DetectionQualityMath.DefaultAcquireTime - 0.35f) < 0.001f &&
			Mathf.Abs(_processor.AcquireTimeSeconds - 0.35f) < 0.001f,
			$"math={DetectionQualityMath.DefaultAcquireTime:0.00} runtime={_processor.AcquireTimeSeconds:0.00}");
		Check("Defaults_LossTime",
			Mathf.Abs(DetectionQualityMath.DefaultLossTime - 2.5f) < 0.001f &&
			Mathf.Abs(_processor.LossTimeSeconds - 2.5f) < 0.001f,
			$"math={DetectionQualityMath.DefaultLossTime:0.00} runtime={_processor.LossTimeSeconds:0.00}");
		EndContract(null, false);
		yield break;
	}

	private IEnumerator RunAhScenario(
		DetectionCalibrationScenarios.Scenario _scenario,
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target)
	{
		BeginContract(_scenario.Id);
		DetectionCalibrationScenarios.QSnapshot math = DetectionCalibrationScenarios.EvaluateQ(_scenario);
		DetectionCalibrationScenarios.ProgressRun mathRun = DetectionCalibrationScenarios.SimulateProgress(math.Q);
		bool expectDetected = mathRun.Detected;

		Sample sample = default;
		yield return SampleLayout(_scenario, _processor, _vision, _observer, _target,
			expectDetected ? c_DetectTimeoutSeconds : c_NoDetectTimeoutSeconds, sampleHolder => sample = sampleHolder);

		AppendLine($"RT {_scenario.Id} dist={F(sample.Distance, 1)} fov={F(sample.Fov, 1)} exp={F(sample.Exposure, 2)} " +
			$"detected={sample.Detected} t={FmtTime(sample.Detected, sample.TDetect)} " +
			$"det={sample.DetectionState} obs={sample.ObservationState} visible={sample.HasObservation}");

		Check($"{_scenario.Id}_Distance",
			Mathf.Abs(sample.Distance - _scenario.DistanceMeters) <= c_DistanceTolMeters,
			$"expected={F(_scenario.DistanceMeters, 1)} actual={F(sample.Distance, 1)}");

		if (expectDetected || sample.HasObservation)
		{
			Check($"{_scenario.Id}_Fov",
				sample.HasObservation && Mathf.Abs(sample.Fov - _scenario.FovOffsetDegrees) <= c_FovTolDegrees,
				sample.HasObservation
					? $"expected={F(_scenario.FovOffsetDegrees, 1)} actual={F(sample.Fov, 1)}"
					: "no VisionObservation");
			Check($"{_scenario.Id}_Exposure",
				sample.HasObservation && Mathf.Abs(sample.Exposure - _scenario.Exposure01) <= c_ExposureTol,
				sample.HasObservation
					? $"design={F(_scenario.Exposure01, 2)} runtime={F(sample.Exposure, 2)}"
					: "no VisionObservation");
		}

		if (expectDetected)
		{
			Check($"{_scenario.Id}_Detected", sample.Detected, sample.Detected ? $"tDetect={F(sample.TDetect, 2)}" : "timeout");
			if (sample.Detected)
			{
				float tol = Mathf.Max(c_TimeAbsSeconds, mathRun.TDetect * c_TimeRel);
				Check($"{_scenario.Id}_Time",
					Mathf.Abs(sample.TDetect - mathRun.TDetect) <= tol,
					$"math={F(mathRun.TDetect, 2)} runtime={F(sample.TDetect, 2)} tol={F(tol, 2)}");
			}
		}
		else
		{
			Check($"{_scenario.Id}_NoDetect", !sample.Detected && sample.DetectionState != DetectionState.Detected,
				sample.Detected ? $"false Detected t={F(sample.TDetect, 2)}" : $"state={sample.DetectionState}");
			Check($"{_scenario.Id}_ObservedIsNotDetected",
				sample.DetectionState != DetectionState.Detected,
				$"obs={sample.ObservationState} det={sample.DetectionState} (Observed must not imply Detected)");
		}

		EndContract();
	}

	private IEnumerator RunNegatives(
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target)
	{
		BeginContract("N1");
		yield return SampleCustom("N1", 30f, 90f, 1f, 0f, _processor, _vision, _observer, _target, c_NegativeTimeoutSeconds,
			sample =>
			{
		AppendLine($"N1 FOV>60 (90°) dist={F(sample.Distance, 1)} fov={F(sample.LookAngle, 1)} visible={sample.HasObservation} det={sample.Detected}");
				Check("N1_NoObservation", !sample.HasObservation, sample.HasObservation ? "VisionObservation present" : "none");
				Check("N1_NoDetect", !sample.Detected, sample.Detected ? "Detected" : "Undetected");
			});
		EndContract("N1 FOV");

		BeginContract("N2");
		yield return SampleCustom("N2", 510f, 0f, 1f, 0f, _processor, _vision, _observer, _target, c_NegativeTimeoutSeconds,
			sample =>
			{
				AppendLine($"N2 RANGE>500 dist={F(sample.WorldDistance, 1)} visible={sample.HasObservation} det={sample.Detected} range={F(_vision.VisionRange, 0)}");
				Check("N2_NoObservation", !sample.HasObservation,
					sample.HasObservation
						? $"perception present range={F(_vision.VisionRange, 0)} obsDist={F(sample.Distance, 1)}"
						: "no valid perception");
				Check("N2_NoDetect", !sample.Detected, sample.Detected ? "Detected" : "Undetected");
			});
		EndContract("N2 RANGE");

		BeginContract("N3");
		yield return SampleCustom("N3", 30f, 0f, 0f, 0f, _processor, _vision, _observer, _target, c_NegativeTimeoutSeconds,
			sample =>
			{
				AppendLine($"N3 LOS dist={F(sample.Distance, 1)} fov={F(sample.LookAngle, 1)} exp={F(sample.Exposure, 2)} visible={sample.HasObservation} det={sample.Detected}");
				Check("N3_InRangeFovLayout", sample.WorldDistance <= 35f && sample.LookAngle <= 8f,
					$"worldDist={F(sample.WorldDistance, 1)} look={F(sample.LookAngle, 1)}");
				Check("N3_NoDetect", !sample.Detected, sample.Detected ? "Detected through full blocker" : "Undetected");
				Check("N3_NoStrongExposure", !sample.HasObservation || sample.Exposure <= 0.15f,
					sample.HasObservation ? $"exp={F(sample.Exposure, 2)}" : "no observation");
			});
		EndContract("N3 LOS");

		BeginContract("N4");
		yield return SampleCustom("N4", 20f, 180f, 1f, 0f, _processor, _vision, _observer, _target, c_NegativeTimeoutSeconds,
			sample =>
			{
				AppendLine($"N4 BACK look={F(sample.LookAngle, 1)} visible={sample.HasObservation} det={sample.Detected}");
				Check("N4_Behind", sample.LookAngle >= 160f, $"look={F(sample.LookAngle, 1)}");
				Check("N4_NoObservation", !sample.HasObservation, sample.HasObservation ? "VisionObservation present" : "none");
				Check("N4_NoDetect", !sample.Detected, sample.Detected ? "Detected" : "Undetected");
			});
		EndContract("N4 BACK");

		BeginContract("N5");
		yield return SampleCustom("N5", 10f, 0f, 0f, DetectionCalibrationScenarios.WalkSpeedMeters,
			_processor, _vision, _observer, _target, c_NegativeTimeoutSeconds,
			sample =>
			{
				AppendLine($"N5 EXP0+WALK exp={F(sample.Exposure, 2)} visible={sample.HasObservation} det={sample.Detected}");
				Check("N5_NoDetect", !sample.Detected, sample.Detected ? "Movement pushed invisible target to Detected" : "Undetected");
				Check("N5_NoStrongExposure", !sample.HasObservation || sample.Exposure <= 0.15f,
					sample.HasObservation ? $"exp={F(sample.Exposure, 2)}" : "no observation");
			});
		EndContract("N5 EXPOSURE");
	}

	private IEnumerator RunBoundaries(
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target)
	{
		BeginContract("FOV59");
		yield return SampleCustom("B_FOV59", 30f, 59f, 1f, 0f, _processor, _vision, _observer, _target, 2f,
			sample =>
			{
				Check("Boundary_Fov59_Observation", sample.HasObservation, $"look={F(sample.LookAngle, 1)} visible={sample.HasObservation}");
			});
		EndContract(null, false);

		BeginContract("FOV60");
		yield return SampleCustom("B_FOV60", 30f, 60f, 1f, 0f, _processor, _vision, _observer, _target, 2f,
			sample =>
			{
				Check("Boundary_Fov60_Observation", sample.HasObservation, $"look={F(sample.LookAngle, 1)} visible={sample.HasObservation}");
			});
		EndContract(null, false);

		BeginContract("FOV61");
		yield return SampleCustom("B_FOV61", 30f, 61f, 1f, 0f, _processor, _vision, _observer, _target, 2f,
			sample =>
			{
				float half = _vision.ResolveHalfFovDegreesForScan();
				bool expectObs = 61f <= half + 0.05f;
				Check("Boundary_Fov61_MatchesLiveCone",
					sample.HasObservation == expectObs,
					$"half={F(half, 1)} look={F(sample.LookAngle, 1)} visible={sample.HasObservation} expectObs={expectObs}");
				Check("Boundary_Fov61_NoDetectRequired",
					expectObs || !sample.Detected,
					sample.Detected ? "Detected" : "Undetected");
			});
		EndContract(null, false);
		m_Summary.AppendLine($"FOV 59/60/61 {(ContractPassed("FOV59") && ContractPassed("FOV60") && ContractPassed("FOV61") ? "PASS" : "FAIL")}");

		BeginContract("Range499");
		yield return SampleCustom("B_R499", 499f, 0f, 1f, 0f, _processor, _vision, _observer, _target, 2f,
			sample =>
			{
				Check("Boundary_Range499_Observation", sample.HasObservation, $"dist={F(sample.WorldDistance, 1)} visible={sample.HasObservation}");
			});
		EndContract(null, false);

		BeginContract("Range500");
		yield return SampleCustom("B_R500", 500f, 0f, 1f, 0f, _processor, _vision, _observer, _target, 2f,
			sample =>
			{
				Check("Boundary_Range500_Observation", sample.HasObservation, $"dist={F(sample.WorldDistance, 1)} visible={sample.HasObservation}");
			});
		EndContract(null, false);

		BeginContract("Range501");
		yield return SampleCustom("B_R501", 501f, 0f, 1f, 0f, _processor, _vision, _observer, _target, 2f,
			sample =>
			{
				Check("Boundary_Range501_NoObservation", !sample.HasObservation,
					sample.HasObservation
						? $"perception beyond 500 range={F(_vision.VisionRange, 0)} dist={F(sample.WorldDistance, 1)}"
						: "beyond VisionRange");
				Check("Boundary_Range501_NoDetect", !sample.Detected, sample.Detected ? "Detected" : "Undetected");
			});
		EndContract(null, false);
		m_Summary.AppendLine($"Range 499/500/501 {(ContractPassed("Range499") && ContractPassed("Range500") && ContractPassed("Range501") ? "PASS" : "FAIL")}");

		BeginContract("Exp0");
		yield return SampleCustom("B_E0", 10f, 0f, 0f, 0f, _processor, _vision, _observer, _target, 2.5f,
			sample =>
			{
				Check("Boundary_Exp0_NoDetect", !sample.Detected, sample.Detected ? "Detected at Exposure 0" : "Undetected");
			});
		EndContract(null, false);

		BeginContract("Exp005");
		yield return SampleCustom("B_E005", 10f, 0f, 0.05f, 0f, _processor, _vision, _observer, _target, 2.5f,
			sample =>
			{
				Check("Boundary_Exp005_NoDetect", !sample.Detected, sample.Detected ? "Detected at Q≈0.05" : "Undetected");
			});
		EndContract(null, false);

		BeginContract("Exp010");
		yield return SampleCustom("B_E010", 10f, 0f, 0.10f, 0f, _processor, _vision, _observer, _target, 2.5f,
			sample =>
			{
				Check("Boundary_Exp010_NoDetect", !sample.Detected, sample.Detected ? "Detected at Q≈0.10" : "Undetected");
			});
		EndContract(null, false);
		m_Summary.AppendLine($"Exposure 0/0.05/0.10 {(ContractPassed("Exp0") && ContractPassed("Exp005") && ContractPassed("Exp010") ? "PASS" : "FAIL")}");
	}

	private IEnumerator RunSkipNotEmpty(
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target)
	{
		BeginContract("Skip");
		Check("Skip_MayApplyFrame_Idle", !VisionLodMath.MayApplyVisionFrame(VisionScanTier.Idle), "Idle must not apply vision frame");
		Check("Skip_MayApplyFrame_RangeFov", !VisionLodMath.MayApplyVisionFrame(VisionScanTier.RangeFov), "RangeFov must not apply vision frame");
		Check("Skip_MayApplyFrame_Cheap", !VisionLodMath.MayApplyVisionFrame(VisionScanTier.Cheap), "Cheap must not apply vision frame");
		Check("Skip_MayApplyFrame_Detail", VisionLodMath.MayApplyVisionFrame(VisionScanTier.Detail), "Detail may apply vision frame");

		DetectionCalibrationScenarios.Scenario close = DetectionCalibrationScenarios.All[0];
		Sample before = default;
		yield return SampleLayout(close, _processor, _vision, _observer, _target, c_DetectTimeoutSeconds, s => before = s);
		Check("Skip_DetectedBefore", before.Detected && before.HasContact, $"detected={before.Detected} contact={before.HasContact}");
		if (!before.HasContact)
		{
			EndContract("Skip != Empty");
			yield break;
		}

		VisionObservation lastObs = before.LastObservation;
		float progress = before.Progress;
		DetectionState det = before.DetectionState;
		ObservationState obs = before.ObservationState;

		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		if (selector != null)
		{
			selector.ForcedPriorityTarget = null;
			selector.ClearSelection(true);
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		int frames = 0;
		void OnFrame() => frames++;
		if (perception != null)
			perception.PerceptionFrameApplied += OnFrame;

		bool visionWas = _vision.enabled;
		_vision.enabled = false;
		yield return new WaitForSeconds(0.6f);
		_vision.enabled = visionWas;

		if (perception != null)
			perception.PerceptionFrameApplied -= OnFrame;

		_processor.TryGetContact(_target, out PerceivedContact after);
		Check("Skip_ContactSurvives", after != null, "contact missing after skip");
		if (after != null)
		{
			Check("Skip_LastObservationPreserved",
				after.LastObservation.IsVisible &&
				(after.LastObservation.AimPoint - lastObs.AimPoint).sqrMagnitude < 0.0001f,
				"LastObservation must not be wiped when observer skips");
			Check("Skip_NotForcedRecentlyLost",
				after.ObservationState != ObservationState.RecentlyLost || obs == ObservationState.RecentlyLost,
				$"before={obs} after={after.ObservationState}");
			Check("Skip_NotLostDetected",
				after.State == DetectionState.Detected || det != DetectionState.Detected,
				$"before={det} after={after.State} progress {progress:0.000}->{after.DetectionProgress:0.000}");
		}

		Check("Skip_NoEmptyFrameWhileDisabled", frames == 0, $"ApplyVisionFrame count while vision disabled={frames}");
		EndContract("Skip != Empty");
	}

	private IEnumerator SampleCustom(
		string _id,
		float _distance,
		float _fov,
		float _exposure,
		float _moveSpeed,
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _timeout,
		Action<Sample> _onDone)
	{
		var scenario = new DetectionCalibrationScenarios.Scenario(_id, _distance, _exposure, _fov, _moveSpeed, "N");
		Sample sample = default;
		yield return SampleLayout(scenario, _processor, _vision, _observer, _target, _timeout, s => sample = s);
		_onDone?.Invoke(sample);
	}

	private IEnumerator SampleLayout(
		DetectionCalibrationScenarios.Scenario _scenario,
		DetectionProcessor _processor,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _timeout,
		Action<Sample> _onDone)
	{
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
		float t0 = Time.time;
		float tDetect = -1f;
		bool detected = false;
		VisionObservation lastObs = default;
		float distAtFirstObs = -1f;
		DetectionState lastState = DetectionState.Undetected;
		ObservationState lastObsState = ObservationState.Lost;
		float lastProgress = 0f;
		PerceivedContact lastContact = null;
		float lastPulse = t0;

		while (Time.time - t0 < _timeout)
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
				if (distAtFirstObs < 0f)
					distAtFirstObs = Mathf.Sqrt(Mathf.Max(0f, frameObs.DistanceSq));
			}

			if (!_processor.TryGetContact(_target, out PerceivedContact contact) || contact == null)
				continue;

			lastContact = contact;
			lastState = contact.State;
			lastObsState = contact.ObservationState;
			lastProgress = contact.DetectionProgress;
			if (contact.LastObservation.IsVisible)
			{
				lastObs = contact.LastObservation;
				if (distAtFirstObs < 0f)
					distAtFirstObs = Mathf.Sqrt(Mathf.Max(0f, lastObs.DistanceSq));
			}

			if (!detected && contact.State == DetectionState.Detected)
			{
				detected = true;
				tDetect = Time.time - t0;
				break;
			}
		}

		bool hasObsNow = false;
		if (perception != null &&
		    perception.TryGetObservation(_target, out VisionObservation endObs) &&
		    endObs.IsVisible)
		{
			hasObsNow = true;
			lastObs = endObs;
		}

		Vector3 toTarget = _target.position - _observer.position;
		toTarget.y = 0f;
		float worldDist = toTarget.magnitude;
		Vector3 look = new Vector3(_observer.forward.x, 0f, _observer.forward.z);
		float lookAngle = Vector3.Angle(
			look.sqrMagnitude > 0.0001f ? look.normalized : Vector3.forward,
			toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward);

		_onDone?.Invoke(new Sample
		{
			Distance = distAtFirstObs >= 0f
				? distAtFirstObs
				: (hasObsNow ? Mathf.Sqrt(Mathf.Max(0f, lastObs.DistanceSq)) : worldDist),
			Fov = hasObsNow ? lastObs.FovOffsetDegrees : lookAngle,
			Exposure = hasObsNow ? lastObs.Exposure01 : 0f,
			LookAngle = lookAngle,
			WorldDistance = worldDist,
			HasObservation = hasObsNow,
			Detected = detected,
			TDetect = tDetect,
			DetectionState = lastState,
			ObservationState = lastObsState,
			Progress = lastProgress,
			HasContact = lastContact != null,
			LastObservation = lastObs
		});
	}

	private void AppendRegressionFromLastFiles()
	{
		AppendLast("G1", "DetectionG1_LAST.txt", 20);
		AppendLast("G2", "DetectionG2_LAST.txt", 20);
		AppendLast("G3", "DetectionG3_LAST.txt", 30);
		AppendLast("G4", "DetectionG4_LAST.txt", 32);
		AppendLast("G5", "DetectionG5_LAST.txt", 21);
		AppendLast("G6", "DetectionG6_LAST.txt", 26);
		AppendLast("G7", "DetectionG7_LAST.txt", 29);
		AppendLast("G8", "DetectionG8_LAST.txt", 19);
		AppendLast("G8 Stress", "DetectionG8_Stress_LAST.txt", 24);
		AppendLine("(G1–G8 LAST files are recorded, not re-run in this Play. Re-run Tools/Tests G menus before closing Block A.)");
	}

	private void AppendLast(string _label, string _fileName, int _expectedPass)
	{
		string path = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests", _fileName);
		if (!File.Exists(path))
		{
			AppendLine($"{_label} NOT RUN | missing {_fileName}");
			m_Summary.AppendLine($"{_label} NOT RUN");
			return;
		}

		string body = File.ReadAllText(path);
		int at = body.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string line = at >= 0 ? body.Substring(at).Trim().Split('\n')[0].Trim() : "RESULT=UNKNOWN";
		bool pass = line.StartsWith("RESULT=PASS", StringComparison.Ordinal) && line.Contains($"pass={_expectedPass}") && line.Contains("fail=0");
		AppendLine($"{_label} {(pass ? "PASS" : "SEE LAST")} | {line} (file, not this Play)");
		m_Summary.AppendLine($"{_label} {(pass ? "PASS" : line)}");
	}

	private void Finish()
	{
		AppendLine("");
		AppendLine("---");
		AppendLine(m_Summary.ToString().TrimEnd());
		AppendLine("");
		AppendLine($"RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}");
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionCalibrationRuntimeStrict_LAST.txt");
		File.WriteAllText(latest, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			$"[DetectionCalibrationRuntimeStrictSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}",
			this);

		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCalibrationStrict;
		DetectionHarnessPlayMode.ResetFlags();
#if UNITY_EDITOR
		if (exitPlay)
			EditorApplication.isPlaying = false;
#endif
	}

	private readonly System.Collections.Generic.Dictionary<string, bool> m_ContractPass =
		new System.Collections.Generic.Dictionary<string, bool>();

	private void BeginContract(string _id)
	{
		m_ContractId = _id;
		m_ContractFailAtStart = m_FailCount;
	}

	private void EndContract(string _summaryLabel = null, bool _writeSummary = true)
	{
		bool pass = m_FailCount == m_ContractFailAtStart;
		m_ContractPass[m_ContractId] = pass;
		if (!_writeSummary)
			return;
		string label = string.IsNullOrEmpty(_summaryLabel) ? m_ContractId : _summaryLabel;
		m_Summary.AppendLine($"{label} {(pass ? "PASS" : "FAIL")}");
	}

	private bool ContractPassed(string _id)
	{
		return m_ContractPass.TryGetValue(_id, out bool ok) && ok;
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
			Debug.LogError($"[DetectionCalibrationRuntimeStrictSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line) => m_Report.AppendLine(_line);

	private static string F(float _value, int _digits)
	{
		return _value.ToString("F" + _digits, CultureInfo.InvariantCulture);
	}

	private static string FmtTime(bool _detected, float _t)
	{
		return _detected ? F(_t, 2) : "timeout";
	}

	private struct Sample
	{
		public float Distance;
		public float Fov;
		public float Exposure;
		public float LookAngle;
		public float WorldDistance;
		public bool HasObservation;
		public bool Detected;
		public float TDetect;
		public DetectionState DetectionState;
		public ObservationState ObservationState;
		public float Progress;
		public bool HasContact;
		public VisionObservation LastObservation;
	}
	#endregion
}
