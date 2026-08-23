using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Play smoke A–P plus stage-3 sweep Q–W for the unified eye + optic sensor. Writes VisionEnvelope_LAST.txt.
/// Does not retune acquire/loss thresholds or FOV edge.
/// </summary>
[DefaultExecutionOrder(61)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class VisionEnvelopeRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_WaitYes = 3.5f;
	private const float c_WaitNo = 1.6f;
	private const float c_Pulse = 0.2f;
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
	private WeaponAttachmentDefinition m_TestOptic300;
	private WeaponAttachmentDefinition m_TestCollimator;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunVisionEnvelope;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[VisionEnvelopeRuntimeSmoke] eye+optic envelope starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunVisionEnvelope)
			DetectionHarnessPlayMode.ResetFlags();
		ScopeScanController.TestLogging = false;
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

	#region Suite
	private IEnumerator RunSuite()
	{
		yield return null;
		yield return null;

		m_Report.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		ScopeScanController.TestLogging = true;
		Append("VISION ENVELOPE");
		Append("===============");
		Append("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("eye=150/120  scope=150-300/8  assignedSector=120  aiming-only");
		Append("---");

		m_Harness = GetComponent<DetectionTestController>();
		Transform observer = m_Harness != null ? m_Harness.Observer : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		UnitVision vision = null;
		if (observer != null)
			observer.TryGetComponent(out vision);

		Check("Harness_Vision", vision != null && vision.enabled, "UnitVision");
		Check("Harness_Target", target != null, "Target");
		if (vision == null || observer == null || target == null || m_Harness == null)
		{
			Finish("FAIL");
			yield break;
		}

		vision.SetVisionRange(UnitVisionProfile.BaseRangeMeters);
		EnsureTestOptics();
		vision.DebugClearVisionOverrides();
		PrepareUnits(observer, target);
		ResetWorld(vision);
		FreezeScopeYaw(vision, 0f);

		BeginGroup("A EYE");
		yield return Probe("A_50", vision, observer, target, 50f, 0f, false, 0f, true);
		yield return Probe("A_149", vision, observer, target, 149f, 0f, false, 0f, true);
		yield return Probe("A_150", vision, observer, target, 150f, 0f, false, 0f, true);
		yield return Probe("A_151", vision, observer, target, 151f, 0f, false, 0f, false);
		yield return Probe("A_FOV59", vision, observer, target, 50f, 59f, false, 0f, true);
		yield return Probe("A_FOV61", vision, observer, target, 50f, 61f, false, 0f, false);

		BeginGroup("B OPTIC AIMING");
		yield return Probe("B_Hip250", vision, observer, target, 250f, 0f, true, 300f, false, WeaponPoseState.HipFire);
		yield return Probe("B_Aim250", vision, observer, target, 250f, 0f, true, 300f, true, WeaponPoseState.Aiming);
		yield return Probe("B_Aim301", vision, observer, target, 301f, 0f, true, 300f, false, WeaponPoseState.Aiming);
		yield return Probe("B_Scope150_200", vision, observer, target, 200f, 0f, true, 150f, false, WeaponPoseState.Aiming);

		BeginGroup("C 8 DEG");
		FreezeScopeYaw(vision, 0f);

		yield return Probe("C_0", vision, observer, target, 200f, 0f, true, 300f, true, WeaponPoseState.Aiming);
		yield return Probe("C_3", vision, observer, target, 200f, 3f, true, 300f, true, WeaponPoseState.Aiming);
		yield return Probe("C_4", vision, observer, target, 200f, 4f, true, 300f, true, WeaponPoseState.Aiming);
		yield return Probe("C_5", vision, observer, target, 200f, 5f, true, 300f, false, WeaponPoseState.Aiming);

		BeginGroup("D SWEEP");
		if (vision.ScopeScan != null)
		{
			vision.ScopeScan.SetAssignedSector(0f, 4f);
			vision.ScopeScan.SetFrozenForTest(false);
			vision.ScopeScan.ResetSweep();
			vision.ScopeScan.SetScanYawForTest(-4f);
			vision.ScopeScan.SetFrozenForTest(false);
		}

		yield return RunSweepD(vision, observer, target);

		BeginGroup("E LOS");
		yield return RunLosBlocked(vision, observer, target);

		BeginGroup("F NO DUPLICATE");
		yield return RunNoDuplicate(vision, observer, target);

		BeginGroup("G OPTIC PATH");
		yield return RunOpticPath(vision, observer, target);

		BeginGroup("H FIRE");
		yield return RunFireCap(vision, observer, target);

		BeginGroup("I CLAMP");
		Check("I_0", Near(UnitVisionProfile.ClampScopeRange(0f), 150f), "0→150");
		Check("I_149", Near(UnitVisionProfile.ClampScopeRange(149f), 150f), "149→150");
		Check("I_300", Near(UnitVisionProfile.ClampScopeRange(300f), 300f), "300");
		Check("I_350", Near(UnitVisionProfile.ClampScopeRange(350f), 300f), "350→300");
		Check("I_500", Near(UnitVisionProfile.ClampScopeRange(500f), 300f), "500→300");
		Check("I_Bonus0", !UnitVisionProfile.HasMagnifiedScopeBonus(0f), "0 no bonus");
		Check("I_Bonus150", !UnitVisionProfile.HasMagnifiedScopeBonus(150f), "150 no bonus");
		Check("I_Bonus300", UnitVisionProfile.HasMagnifiedScopeBonus(300f), "300 bonus");

		BeginGroup("J COLLIMATOR");
		yield return Probe("J_0_200", vision, observer, target, 200f, 0f, true, 0f, false, WeaponPoseState.Aiming);
		yield return Probe("J_150_200", vision, observer, target, 200f, 0f, true, 150f, false, WeaponPoseState.Aiming);

		BeginGroup("K DIRTY POSE");
		vision.DebugSetVisionOpticOverride(m_TestOptic300);
		vision.DebugSetVisionPoseOverride(WeaponPoseState.Aiming, true);
		yield return null;
		vision.RequestImmediateScan();
		Check("K_AimingActive", vision.CurrentVisionProfile.IsScopeActive, "Aiming+300");
		vision.DebugSetVisionPoseOverride(WeaponPoseState.HipFire, true);
		yield return null;
		vision.RequestImmediateScan();
		Check("K_HipInactive", !vision.CurrentVisionProfile.IsScopeActive, "HipFire must not keep 300");

		BeginGroup("M Q DISTANCE");
		RunQDistanceDiagnostics();

		BeginGroup("N Q RELATIVE");
		RunQRelativeChecks();

		BeginGroup("O Q BOUNDARY");
		yield return RunQBoundary(vision, observer, target);

		BeginGroup("P Q POSE");
		yield return RunQPose(vision, observer, target);

		BeginGroup("Q R SWEEP GRID");
		yield return RunSweepQR(vision, observer, target);

		BeginGroup("S HOLD CONTACT");
		yield return RunHoldContactS(vision, observer, target);

		BeginGroup("T SECTOR 20 DEG");
		yield return RunSectorPlus20(vision, observer, target);

		BeginGroup("U RESUME AFTER LOST");
		yield return RunResumeAfterLost(vision, observer, target);

		BeginGroup("V CACHE DUP");
		yield return RunCachedLos(vision, observer, target);

		BeginGroup("W BUDGET AND TWO TARGETS");
		yield return RunBudgetAndTwoTargets(vision, observer, target);

		BeginGroup("X EYE VS SWEEP");
		yield return RunEyeDoesNotSpendScopeLos(vision, observer, target);

		BeginGroup("L PERF");
		if (m_Harness.DetectionProcessor != null)
			m_Harness.DetectionProcessor.ClearContacts();
		FreezeScopeYaw(vision, 0f);
		ApplyOptic(vision, true, 300f, WeaponPoseState.Aiming);
		PlaceOnVisionAxis(vision, observer, target, 250f, 0f);
		vision.RequestImmediateScan();
		yield return null;
		vision.ScanStats.Reset();
		float perfT0 = Time.time;
		while (Time.time - perfT0 < 0.7f)
			yield return null;
		VisionScanStats stats = vision.ScanStats;
		Append($"[PERF] scans={stats.VisionScanCount} scopeQuery={stats.LastScanScopeDetailedQueryCount} skipDup={stats.SkippedDuplicateCount} los={stats.LosCheckCount} qEval={stats.QualityEvalCount} avgMs={stats.AverageFrameMs:F2} maxMs={stats.MaxFrameMs:F2}");
		Append($"[SCOPE PERF] frames={Time.frameCount} scopeTicks={stats.ScopeSweepScanCount} candidateQueries={stats.ScopeDetailedQueryCount} LOSQueries={stats.ScopeLiveLosCount} cachedObservations={stats.CachedLosCount} skippedDuplicateLOS={stats.SkippedDuplicateCount} maxLOSPerFrame={stats.MaxScopeLiveLosCount}");
		Check("L_StatsPresent", stats.VisionScanCount + stats.ScopeSweepScanCount > 0,
			$"scans={stats.VisionScanCount} sweeps={stats.ScopeSweepScanCount}");
		Check("L_QEvalPresent", stats.QualityEvalCount > 0, $"qEval={stats.QualityEvalCount}");
		Check("L_LosNotInflated",
			stats.MaxScopeLiveLosCount <= 6 &&
			(stats.CachedLosCount > 0 || stats.SkippedDuplicateCount > 0) &&
			stats.ScopeLiveLosCount <= 6,
			$"live={stats.ScopeLiveLosCount} max={stats.MaxScopeLiveLosCount} los={stats.LosCheckCount} cache={stats.CachedLosCount} skip={stats.SkippedDuplicateCount}");
		Check("W_ScopePerfBudget", stats.MaxScopeLiveLosCount <= 6, $"maxLiveLos={stats.MaxScopeLiveLosCount}");

		vision.DebugClearVisionOverrides();
		if (vision.ScopeScan != null)
			vision.ScopeScan.SetFrozenForTest(false);
		ScopeScanController.TestLogging = false;

		string status = m_FailCount == 0 ? "PASS" : "FAIL";
		Finish(status);
	}

	private IEnumerator Probe(
		string _id,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yaw,
		bool _useOptic,
		float _opticRange,
		bool _expectObs,
		WeaponPoseState _pose = WeaponPoseState.HipFire)
	{
		if (_useOptic && _pose == WeaponPoseState.Aiming)
			FreezeScopeYaw(_vision, 0f);
		ApplyOptic(_vision, _useOptic, _opticRange, _pose);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, _distance, _yaw);
		_vision.ScanStats.Reset();
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool lastSeen = false;
		int frames = 0;
		void OnFrame()
		{
			frames++;
			lastSeen = perception != null &&
				perception.TryGetObservation(_target, out VisionObservation obs) &&
				obs.IsVisible;
		}

		if (perception != null)
			perception.PerceptionFrameApplied += OnFrame;

		_vision.RequestImmediateScan();
		float timeout = _expectObs ? c_WaitYes : c_WaitNo;
		float t0 = Time.time;
		float nextPulse = t0;
		while (Time.time - t0 < timeout)
		{
			if (Time.time >= nextPulse)
			{
				_vision.RequestImmediateScan();
				nextPulse = Time.time + c_Pulse;
			}

			if (_expectObs && lastSeen)
				break;
			yield return null;
		}

		if (perception != null)
			perception.PerceptionFrameApplied -= OnFrame;

		bool seen = frames > 0 && lastSeen;
		Check(_id, seen == _expectObs,
			$"dist={_distance:0} yaw={_yaw:0} seen={seen} expect={_expectObs} frames={frames} " +
			$"scope={_vision.CurrentVisionProfile.IsScopeActive} max={_vision.ResolvedMaxRange:0}");
	}

	private IEnumerator RunSweepD(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 200f, 3f);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.SetScanYawForTest(-4f);
		}

		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		bool seen = false;
		float t0 = Time.time;
		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		while (Time.time - t0 < 2.5f)
		{
			_vision.RequestImmediateScan();
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				seen = true;
				break;
			}

			yield return null;
		}

		Check("D_SweepFindsPlus3", seen, $"yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1}");
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, ScopeScanController.DefaultAssignedSectorHalfDegrees);
			_vision.ScopeScan.SetFrozenForTest(true);
		}
	}

	private IEnumerator RunLosBlocked(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		var scenario = new DetectionCalibrationScenarios.Scenario("E", 80f, 0f, 0f, 0f, "N");
		m_Harness.ApplyCalibrationScenario(scenario);
		yield return null;
		Physics.SyncTransforms();
		if (m_Harness.DetectionProcessor != null)
			m_Harness.DetectionProcessor.ClearContacts();
		_vision.RequestImmediateScan();

		bool detected = false;
		float t0 = Time.time;
		while (Time.time - t0 < c_WaitNo)
		{
			_vision.RequestImmediateScan();
			if (m_Harness.DetectionProcessor != null &&
			    m_Harness.DetectionProcessor.TryGetContact(_target, out PerceivedContact c) &&
			    c != null &&
			    c.State == DetectionState.Detected)
			{
				detected = true;
				break;
			}

			yield return null;
		}

		Check("E_BlockedNoDetect", !detected, detected ? "Detected through cover" : "blocked");
		ResetWorld(_vision);
	}

	private IEnumerator RunNoDuplicate(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 100f, 0f);

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		float t0 = Time.time;
		bool eyeSeen = false;
		while (Time.time - t0 < c_WaitYes)
		{
			_vision.RequestImmediateScan();
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				eyeSeen = true;
				break;
			}

			yield return null;
		}

		Check("F_EyeSees100", eyeSeen, eyeSeen ? "Observed" : "eye missed 100m");
		_vision.ScanStats.Reset();
		_vision.RequestImmediateScan();
		yield return null;
		_vision.RequestImmediateScan();
		yield return new WaitForSeconds(0.15f);

		Check("F_SkipDuplicate",
			_vision.ScanStats.SkippedDuplicateCount > 0 || _vision.ScanStats.LastScanSkippedDuplicateCount > 0,
			$"skip={_vision.ScanStats.SkippedDuplicateCount} last={_vision.ScanStats.LastScanSkippedDuplicateCount} scopeQ={_vision.ScanStats.ScopeDetailedQueryCount}");
	}

	private IEnumerator RunOpticPath(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		VisionObservationSource source = VisionObservationSource.Eye;
		bool seen = false;
		float t0 = Time.time;
		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		while (Time.time - t0 < c_WaitYes)
		{
			_vision.RequestImmediateScan();
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				seen = true;
				source = obs.Source;
				break;
			}

			yield return null;
		}

		Check("G_250OpticSource", seen && source == VisionObservationSource.Optic,
			seen ? source.ToString() : "missing");
	}

	private IEnumerator RunFireCap(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, false, 0f, WeaponPoseState.HipFire);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		if (m_Harness.DetectionProcessor != null)
			m_Harness.DetectionProcessor.ClearContacts();
		_vision.RequestImmediateScan();
		yield return new WaitForSeconds(0.4f);

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool hasObs = perception != null &&
			perception.TryGetObservation(_target, out VisionObservation obs) &&
			obs.IsVisible;
		Check("H_NoObservation250", !hasObs, hasObs ? "saw 250 without optic" : "none");

		if (_observer.TryGetComponent(out UnitWeaponHitscanShooting hitscan))
		{
			float cap = hitscan.GetCappedMaxDistance();
			Check("H_HitscanCap", cap <= _vision.ResolvedMaxRange + 0.05f,
				$"cap={cap:F1} vision={_vision.ResolvedMaxRange:F1}");
		}
		else
			Check("H_HitscanCap", false, "UnitWeaponHitscanShooting missing");

		if (_observer.TryGetComponent(out UnitWeaponFireController fire))
		{
			WeaponShotAttemptResult result = fire.TryFireSingleShot();
			Check("H_NoFireWithoutObs", result != WeaponShotAttemptResult.Success,
				result.ToString());
		}
		else
			Check("H_NoFireWithoutObs", false, "UnitWeaponFireController missing");
	}

	private void RunQDistanceDiagnostics()
	{
		AppendQDistance("Eye", 0f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 25f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 50f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 75f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 100f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 125f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 140f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 149f, UnitVisionProfile.BaseRangeMeters);
		AppendQDistance("Eye", 150f, UnitVisionProfile.BaseRangeMeters);

		AppendQDistance("Scope", 150f, 300f);
		AppendQDistance("Scope", 175f, 300f);
		AppendQDistance("Scope", 200f, 300f);
		AppendQDistance("Scope", 225f, 300f);
		AppendQDistance("Scope", 250f, 300f);
		AppendQDistance("Scope", 275f, 300f);
		AppendQDistance("Scope", 290f, 300f);
		AppendQDistance("Scope", 300f, 300f);
	}

	private void AppendQDistance(string _label, float _distanceMeters, float _resolvedRange)
	{
		float d = DetectionQualityMath.DistanceFactor(_distanceMeters, _resolvedRange);
		Append($"[Q] {_label} {_distanceMeters:0}m D={d:F2}");
	}

	private void RunQRelativeChecks()
	{
		float halfA = DetectionQualityMath.DistanceFactor(75f, 150f);
		float halfB = DetectionQualityMath.DistanceFactor(150f, 300f);
		Check("N_RelativeHalf", Mathf.Abs(halfA - halfB) < 0.02f, $"A={halfA:F3} B={halfB:F3}");

		float edgeA = DetectionQualityMath.DistanceFactor(150f, 150f);
		float edgeB = DetectionQualityMath.DistanceFactor(300f, 300f);
		Check("N_RelativeEdge",
			Mathf.Abs(edgeA - DetectionQualityMath.DefaultFarFactor) < 0.001f &&
			Mathf.Abs(edgeB - DetectionQualityMath.DefaultFarFactor) < 0.001f,
			$"150={edgeA:F3} 300={edgeB:F3}");

		Check("N_CurveKey0", Mathf.Abs(DetectionQualityMath.EvaluateDistanceCurve(0f) - 1f) < 0.001f, "t=0");
		Check("N_CurveKey1", Mathf.Abs(DetectionQualityMath.EvaluateDistanceCurve(1f) - DetectionQualityMath.DefaultFarFactor) < 0.001f, "t=1");
	}

	private IEnumerator RunQBoundary(UnitVision _vision, Transform _observer, Transform _target)
	{
		const float epsilon = 1f;
		yield return ProbeQBoundary("O_EyeInside", _vision, _observer, _target,
			UnitVisionProfile.BaseRangeMeters - epsilon, false, 0f, WeaponPoseState.HipFire, true);
		yield return ProbeQBoundary("O_EyeOutside", _vision, _observer, _target,
			UnitVisionProfile.BaseRangeMeters + epsilon, false, 0f, WeaponPoseState.HipFire, false);
		yield return ProbeQBoundary("O_ScopeInside", _vision, _observer, _target,
			300f - epsilon, true, 300f, WeaponPoseState.Aiming, true);
		yield return ProbeQBoundary("O_ScopeOutside", _vision, _observer, _target,
			300f + epsilon, true, 300f, WeaponPoseState.Aiming, false);
	}

	private IEnumerator ProbeQBoundary(
		string _id,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		bool _useOptic,
		float _opticRange,
		WeaponPoseState _pose,
		bool _expectInside)
	{
		if (_useOptic && _pose == WeaponPoseState.Aiming)
			FreezeScopeYaw(_vision, 0f);
		ApplyOptic(_vision, _useOptic, _opticRange, _pose);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, _distance, 0f);
		yield return SampleRuntimeQ(_id, _vision, _observer, _target, _expectInside);
	}

	private IEnumerator RunQPose(UnitVision _vision, Transform _observer, Transform _target)
	{
		FreezeScopeYaw(_vision, 0f);
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return SampleRuntimeQ("P_Aiming250", _vision, _observer, _target, true);

		ApplyOptic(_vision, true, 300f, WeaponPoseState.HipFire);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return SampleRuntimeQ("P_Hip250", _vision, _observer, _target, false);

		FreezeScopeYaw(_vision, 0f);
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return SampleRuntimeQ("P_Aiming250Again", _vision, _observer, _target, true);
	}

	private IEnumerator SampleRuntimeQ(
		string _id,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		bool _expectPositiveQ)
	{
		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		if (processor != null)
		{
			processor.ClearContacts();
			processor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		_vision.RequestImmediateScan();
		float timeout = _expectPositiveQ ? c_WaitYes : c_WaitNo;
		float t0 = Time.time;
		bool seen = false;
		float q = -1f;
		while (Time.time - t0 < timeout)
		{
			_vision.RequestImmediateScan();
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				seen = true;
				q = EvaluateObservationQ(_vision, obs);
				break;
			}

			yield return null;
		}

		if (_expectPositiveQ)
			Check(_id, seen && q > 0f, seen ? $"Q={q:F3}" : "missing");
		else
			Check(_id, !seen, seen ? $"Q={q:F3}" : "gone");
	}

	private IEnumerator RunSweepQR(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, 4f);
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.ResetSweep();
			_vision.ScopeScan.SetScanYawForTest(-4f);
			_vision.ScopeScan.SetDirectionForTest(1);
			_vision.ScopeScan.SetFrozenForTest(false);
		}

		PlaceOnBodyAxis(_vision, _observer, _target, 400f, 0f);
		var seen = new HashSet<int>();
		float t0 = Time.time;
		while (Time.time - t0 < 0.9f && _vision.ScopeScan != null)
		{
			seen.Add(Mathf.RoundToInt(_vision.ScopeScan.ScanYawDegrees));
			yield return null;
		}

		Check("Q_VisitedMinus4", HasYaw(seen, -4), "yaw set=" + FormatYawSet(seen));
		Check("Q_VisitedMinus2", HasYaw(seen, -2), "yaw set=" + FormatYawSet(seen));
		Check("Q_Visited0", HasYaw(seen, 0), "yaw set=" + FormatYawSet(seen));
		Check("Q_VisitedPlus2", HasYaw(seen, 2), "yaw set=" + FormatYawSet(seen));
		Check("Q_VisitedPlus4", HasYaw(seen, 4), "yaw set=" + FormatYawSet(seen));

		bool reversed = false;
		t0 = Time.time;
		while (Time.time - t0 < 0.5f && _vision.ScopeScan != null)
		{
			if (_vision.ScopeScan.Direction < 0 && _vision.ScopeScan.ScanYawDegrees < 3.5f)
			{
				reversed = true;
				break;
			}

			yield return null;
		}

		Check("R_ReversedAfterPlus4", reversed, $"dir={(_vision.ScopeScan != null ? _vision.ScopeScan.Direction : 0)} yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1}");
		if (_vision.ScopeScan != null)
			_vision.ScopeScan.SetAssignedSector(0f, ScopeScanController.DefaultAssignedSectorHalfDegrees);
	}

	private IEnumerator RunHoldContactS(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, 4f);
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.ResetSweep();
			_vision.ScopeScan.SetScanYawForTest(-4f);
		}

		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 200f, 3f);
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool seen = false;
		float t0 = Time.time;
		while (Time.time - t0 < 2.5f)
		{
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				seen = true;
				break;
			}

			yield return null;
		}

		Check("S_FoundPlus3", seen, $"mode={(_vision.ScopeScan != null ? _vision.ScopeScan.Mode.ToString() : "?")}");
		int sweepsAtLock = _vision.ScanStats.ScopeSweepScanCount;
		float yawAtLock = _vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f;
		yield return new WaitForSeconds(0.35f);
		bool held = _vision.ScopeScan != null && _vision.ScopeScan.Mode == ScopeScanMode.TrackTarget;
		bool yawStable = _vision.ScopeScan != null && Mathf.Abs(_vision.ScopeScan.ScanYawDegrees - yawAtLock) < 2.5f;
		Check("S_HoldStopsSweep", held && yawStable,
			$"held={held} yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1} lockYaw={yawAtLock:F1} extraSweeps={_vision.ScanStats.ScopeSweepScanCount - sweepsAtLock}");
		if (_vision.ScopeScan != null)
			_vision.ScopeScan.SetAssignedSector(0f, ScopeScanController.DefaultAssignedSectorHalfDegrees);
	}

	private IEnumerator RunSectorPlus20(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, 60f);
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.ResetSweep();
			_vision.ScopeScan.SetScanYawForTest(-4f);
			_vision.ScopeScan.SetDirectionForTest(1);
		}

		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 250f, 20f);
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool seenEarly = false;
		float t0 = Time.time;
		while (Time.time - t0 < 2.5f && _vision.ScopeScan != null && _vision.ScopeScan.ScanYawDegrees < 15f)
		{
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation early) &&
			    early.IsVisible)
			{
				seenEarly = true;
				break;
			}

			yield return null;
		}

		Check("T_NotSeenBeforeSector", !seenEarly,
			$"early={seenEarly} yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1}");

		bool seenLate = seenEarly;
		t0 = Time.time;
		while (Time.time - t0 < 3.5f)
		{
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				seenLate = true;
				break;
			}

			yield return null;
		}

		Check("T_SeenAtPlus20", seenLate, $"yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1}");
	}

	private IEnumerator RunResumeAfterLost(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, 60f);
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.ResetSweep();
			_vision.ScopeScan.SetScanYawForTest(-4f);
			_vision.ScopeScan.SetDirectionForTest(1);
		}

		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 250f, 6f);
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		float t0 = Time.time;
		while (Time.time - t0 < 2.5f)
		{
			if (_vision.ScopeScan != null && _vision.ScopeScan.Mode == ScopeScanMode.TrackTarget)
				break;
			yield return null;
		}

		float yawAtContact = _vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f;
		PlaceOnBodyAxis(_vision, _observer, _target, 400f, 80f);
		if (m_Harness.DetectionProcessor != null)
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		yield return null;
		yield return null;

		t0 = Time.time;
		while (Time.time - t0 < 2.2f)
		{
			if (_vision.ScopeScan != null && _vision.ScopeScan.Mode == ScopeScanMode.Sweep)
				break;
			yield return null;
		}

		bool resumedSweep = _vision.ScopeScan != null && _vision.ScopeScan.Mode == ScopeScanMode.Sweep;
		bool notResetToEdge = _vision.ScopeScan != null && Mathf.Abs(_vision.ScopeScan.ScanYawDegrees + 60f) > 5f;
		Check("U_ResumeNotFromMinus60", resumedSweep && notResetToEdge,
			$"mode={(_vision.ScopeScan != null ? _vision.ScopeScan.Mode.ToString() : "?")} yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1} lockYaw={yawAtContact:F1}");
	}

	private IEnumerator RunCachedLos(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 250f, 0f);
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		_vision.RequestImmediateScan();
		yield return null;
		_vision.ScanStats.Reset();
		if (_vision.ScopeScan != null)
			_vision.ScopeScan.SetFrozenForTest(true);

		float t0 = Time.time;
		while (Time.time - t0 < 1.2f)
			yield return null;

		VisionScanStats stats = _vision.ScanStats;
		Check("V_CachedOrSkippedGrows",
			stats.CachedLosCount > 0 || stats.SkippedDuplicateCount > 0,
			$"cache={stats.CachedLosCount} skip={stats.SkippedDuplicateCount} liveLos={stats.ScopeLiveLosCount}");
		Check("V_LiveLosNotLinear",
			stats.ScopeLiveLosCount < 40,
			$"liveLos={stats.ScopeLiveLosCount} sweeps={stats.ScopeSweepScanCount}");
		if (_vision.ScopeScan != null)
			_vision.ScopeScan.SetFrozenForTest(true);
	}

	private IEnumerator RunBudgetAndTwoTargets(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 250f, 2f);

		var dummies = new List<GameObject>(24);
		try
		{
			for (int i = 0; i < 24; i++)
			{
				float yaw = -3f + i * 0.25f;
				dummies.Add(SpawnDummyEnemy(_vision, _observer, 230f + i * 1.5f, yaw, "EnvDummy_" + i));
			}

			yield return null;
			Physics.SyncTransforms();
			_vision.ScanStats.Reset();
			_vision.RequestImmediateScan();
			yield return null;
			Check("W_BudgetMax6",
				_vision.ScanStats.MaxScopeLiveLosCount <= 6,
				$"maxLive={_vision.ScanStats.MaxScopeLiveLosCount} last={_vision.ScanStats.LastScanScopeLiveLosCount} cand={_vision.ScanStats.LastScanCandidateCount}");
		}
		finally
		{
			for (int i = 0; i < dummies.Count; i++)
			{
				if (dummies[i] != null)
					Destroy(dummies[i]);
			}
		}

		GameObject extraA = SpawnDummyEnemy(_vision, _observer, 250f, -20f, "EnvTargetA");
		try
		{
			if (_vision.ScopeScan != null)
			{
				_vision.ScopeScan.SetAssignedSector(0f, 60f);
				_vision.ScopeScan.SetFrozenForTest(false);
				_vision.ScopeScan.ResetSweep();
				_vision.ScopeScan.SetScanYawForTest(-4f);
				_vision.ScopeScan.SetDirectionForTest(1);
			}

			PlaceOnBodyAxis(_vision, _observer, _target, 250f, 2f);
			if (m_Harness.DetectionProcessor != null)
			{
				m_Harness.DetectionProcessor.ClearContacts();
				m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
			}

			UnitPerception perception = _observer.GetComponent<UnitPerception>();
			bool seenB = false;
			bool seenADuringB = false;
			float t0 = Time.time;
			while (Time.time - t0 < 2.5f)
			{
				bool b = perception != null &&
					perception.TryGetObservation(_target, out VisionObservation obsB) &&
					obsB.IsVisible;
				bool a = extraA != null &&
					perception != null &&
					perception.TryGetObservation(extraA.transform, out VisionObservation obsA) &&
					obsA.IsVisible;
				if (b)
				{
					seenB = true;
					seenADuringB = a;
					break;
				}

				yield return null;
			}

			Check("W_BFoundFirst", seenB && !seenADuringB, $"B={seenB} A={seenADuringB}");

			PlaceOnBodyAxis(_vision, _observer, _target, 400f, 80f);
			t0 = Time.time;
			bool seenALater = false;
			while (Time.time - t0 < 8f)
			{
				if (extraA != null &&
				    perception != null &&
				    perception.TryGetObservation(extraA.transform, out VisionObservation obsA) &&
				    obsA.IsVisible)
				{
					seenALater = true;
					break;
				}

				yield return null;
			}

			Check("W_AFoundAfterSweep", seenALater,
				$"yaw={(_vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f):F1}");
		}
		finally
		{
			if (extraA != null)
				Destroy(extraA);
			if (_vision.ScopeScan != null)
				_vision.ScopeScan.SetAssignedSector(0f, ScopeScanController.DefaultAssignedSectorHalfDegrees);
		}
	}

	private IEnumerator RunEyeDoesNotSpendScopeLos(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, 60f);
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.ResetSweep();
			_vision.ScopeScan.SetScanYawForTest(0f);
		}

		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, 100f, 0f);
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool eyeSeen = false;
		VisionObservationSource source = VisionObservationSource.Optic;
		float t0 = Time.time;
		while (Time.time - t0 < c_WaitYes)
		{
			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible &&
			    obs.Source == VisionObservationSource.Eye)
			{
				eyeSeen = true;
				source = obs.Source;
				break;
			}

			yield return null;
		}

		Check("X_EyeSees100", eyeSeen && source == VisionObservationSource.Eye,
			eyeSeen ? source.ToString() : "missing");
		_vision.ScanStats.Reset();
		t0 = Time.time;
		while (Time.time - t0 < 0.6f)
			yield return null;

		Check("X_ScopeLosSkippedDuringSweep",
			_vision.ScanStats.SkippedDuplicateCount > 0,
			$"skip={_vision.ScanStats.SkippedDuplicateCount} scopeLive={_vision.ScanStats.ScopeLiveLosCount} sweeps={_vision.ScanStats.ScopeSweepScanCount}");
		if (_vision.ScopeScan != null)
			_vision.ScopeScan.SetFrozenForTest(true);
	}

	private static float EvaluateObservationQ(UnitVision _vision, in VisionObservation _obs)
	{
		float distance = Mathf.Sqrt(Mathf.Max(0f, _obs.DistanceSq));
		float range = _vision != null ? _vision.ResolvedMaxRange : DetectionQualityMath.DefaultFarMeters;
		float distanceFactor = DetectionQualityMath.DistanceFactor(distance, range);
		float fovHalf = DetectionQualityMath.DefaultFovHalfDegrees;
		if (_vision != null)
		{
			ResolvedVisionProfile profile = _vision.CurrentVisionProfile;
			fovHalf = _obs.Source == VisionObservationSource.Optic
				? profile.ScopeHalfFovDegrees
				: profile.EyeHalfFovDegrees;
		}

		float fovFactor = DetectionQualityMath.FovFactor(_obs.FovOffsetDegrees, fovHalf);
		return DetectionQualityMath.VisibilityQuality(
			distanceFactor, fovFactor, Mathf.Clamp01(_obs.Exposure01), 1f);
	}
	#endregion

	#region Helpers
	private void EnsureTestOptics()
	{
		m_TestOptic300 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestOptic300.name = "Attachment_TestScope_300";
		m_TestOptic300.SetScopeVisionRangeMeters(300f);
		m_TestCollimator = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestCollimator.name = "Attachment_TestCollimator";
		m_TestCollimator.SetScopeVisionRangeMeters(0f);
	}

	private void ApplyOptic(UnitVision _vision, bool _useOptic, float _range, WeaponPoseState _pose)
	{
		if (!_useOptic)
		{
			_vision.DebugClearVisionOverrides();
			_vision.DebugSetVisionPoseOverride(_pose, true);
			return;
		}

		WeaponAttachmentDefinition optic = _range > 150.01f ? m_TestOptic300 : m_TestCollimator;
		if (optic == null)
			EnsureTestOptics();
		optic = _range > 150.01f ? m_TestOptic300 : m_TestCollimator;
		optic.SetScopeVisionRangeMeters(_range);
		_vision.DebugSetVisionOpticOverride(optic);
		_vision.DebugSetVisionPoseOverride(_pose, true);
	}

	private static void FreezeScopeYaw(UnitVision _vision, float _yawDegrees)
	{
		if (_vision == null || _vision.ScopeScan == null)
			return;
		_vision.ScopeScan.SetFrozenForTest(true);
		_vision.ScopeScan.SetScanYawForTest(_yawDegrees);
	}

	private void ResetWorld(UnitVision _vision)
	{
		if (m_Harness != null && m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		if (m_Harness != null && m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		if (_vision != null && m_Harness != null)
		{
			PrepareUnits(m_Harness.Observer, m_Harness.Target);
			_vision.RequestImmediateScan();
		}
	}

	private static void PrepareUnits(Transform _observer, Transform _target)
	{
		DetectionTestController.PrepareCalibrationUnit(_observer);
		DetectionTestController.PrepareCalibrationUnit(_target);
		DisableLocomotion(_observer);
		DisableLocomotion(_target);
	}

	private static void DisableLocomotion(Transform _unit)
	{
		if (_unit == null)
			return;
		if (_unit.TryGetComponent(out NavMeshAgent agent))
			agent.enabled = false;
		if (_unit.TryGetComponent(out UnitClickToMove click))
			click.enabled = false;
		if (_unit.TryGetComponent(out UnitNavLocomotionDriver driver))
			driver.enabled = false;
	}

	private static void PlaceOnVisionAxis(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yawDegrees)
	{
		PlaceRelative(_vision, _observer, _target, _distance, _yawDegrees, true);
	}

	private static void PlaceOnBodyAxis(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yawDegrees)
	{
		PlaceRelative(_vision, _observer, _target, _distance, _yawDegrees, false);
	}

	private static void PlaceRelative(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yawDegrees,
		bool _useSweepForward)
	{
		DisableLocomotion(_observer);
		DisableLocomotion(_target);
		Physics.SyncTransforms();

		for (int pass = 0; pass < 2; pass++)
		{
			Vector3 origin = _vision.GetGameplayVisionOriginWorld();
			Vector3 fwd = _useSweepForward ? ResolvePlaceForward(_vision) : _vision.GetGameplayVisionForwardXZ();
			fwd.y = 0f;
			if (fwd.sqrMagnitude < 1e-6f)
				fwd = Vector3.forward;
			fwd.Normalize();
			Vector3 dir = Quaternion.AngleAxis(_yawDegrees, Vector3.up) * fwd;
			Vector3 pos = origin + dir * _distance;
			pos.y = _target.position.y;
			_target.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up));
			Physics.SyncTransforms();
		}
	}

	private static Vector3 ResolvePlaceForward(UnitVision _vision)
	{
		Vector3 fwd = _vision.GetGameplayVisionForwardXZ();
		if (_vision.CurrentVisionProfile.IsScopeActive && _vision.ScopeScan != null)
			fwd = _vision.ScopeScan.GetSweepForwardXZ(fwd);
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 1e-6f)
			fwd = Vector3.forward;
		return fwd.normalized;
	}

	private static bool HasYaw(HashSet<int> _seen, int _yaw)
	{
		return _seen.Contains(_yaw) || _seen.Contains(_yaw - 1) || _seen.Contains(_yaw + 1);
	}

	private static string FormatYawSet(HashSet<int> _seen)
	{
		var list = new List<int>(_seen);
		list.Sort();
		return string.Join(",", list);
	}

	private GameObject SpawnDummyEnemy(
		UnitVision _observerVision,
		Transform _observer,
		float _distance,
		float _yawDegrees,
		string _name)
	{
		var go = new GameObject(_name);
		UnitTeamId team = UnitTeamId.Enemy;
		if (_observer != null && _observer.TryGetComponent(out UnitTeam observerTeam) &&
		    observerTeam.Team == UnitTeamId.Enemy)
			team = UnitTeamId.Player;

		UnitTeam unitTeam = go.AddComponent<UnitTeam>();
		unitTeam.SetTeam(team);
		go.AddComponent<UnitObservationSource>();
		go.AddComponent<UnitPerception>();
		go.AddComponent<SphereCollider>().radius = 0.45f;
		go.AddComponent<UnitVision>();

		Vector3 origin = _observerVision.GetGameplayVisionOriginWorld();
		Vector3 fwd = _observerVision.GetGameplayVisionForwardXZ();
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 1e-6f)
			fwd = Vector3.forward;
		fwd.Normalize();
		Vector3 dir = Quaternion.AngleAxis(_yawDegrees, Vector3.up) * fwd;
		Vector3 pos = origin + dir * _distance;
		pos.y = origin.y;
		go.transform.position = pos;
		return go;
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		string line = (_ok ? "PASS " : "FAIL ") + _name + " | " + _detail;
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append(line);
		Debug.Log("[VisionEnvelope] " + line + $"  (pass={m_PassCount} fail={m_FailCount})", this);
	}

	private void BeginGroup(string _title)
	{
		Append("");
		Append(_title);
		Debug.Log(
			$"[VisionEnvelopeRuntimeSmoke] {_title} t={Time.time:F1} pass={m_PassCount} fail={m_FailCount}",
			this);
	}

	private static bool Near(float _a, float _b)
	{
		return Mathf.Abs(_a - _b) < 0.01f;
	}

	private void Append(string _line)
	{
		m_Report.AppendLine(_line);
	}

	private void Finish(string _status)
	{
		Append("---");
		Append("SYSTEM STATUS: " + _status);
		Append("pass=" + m_PassCount + " fail=" + m_FailCount);
		string body = m_Report.ToString();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "VisionEnvelope_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[VisionEnvelopeRuntimeSmoke] wrote {latest}\n{body}", this);

#if UNITY_EDITOR
		if (m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunVisionEnvelope)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
