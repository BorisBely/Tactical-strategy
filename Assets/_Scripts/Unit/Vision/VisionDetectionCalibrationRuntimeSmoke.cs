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
/// Stage 4 measurement and Stage 6 balance. Same sampler; Play flag selects the matrix.
/// Stage 4 → VisionDetectionCalibration_LAST.txt. Stage 6 → VisionDetectionBalance_LAST.txt.
/// </summary>
[DefaultExecutionOrder(61)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class VisionDetectionCalibrationRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_TimeoutSeconds = 8f;
	private const float c_SweepTimeoutSeconds = 12f;
	private const float c_BalanceTimeoutSeconds = 10f;
	private const float c_BalanceSweepTimeoutSeconds = 14f;
	private const float c_ScanPulseSeconds = 0.25f;
	private const float c_SectorTimeoutSeconds = 22f;
	private const float c_GateHoldSeconds = 0.45f;
	private const float c_FrozenNoLosAbortSeconds = 2f;
	private const float c_ExposureTolerance = 0.20f;
	private const int c_RepeatsDefault = 5;
	private const int c_RepeatsHighN = 20;
	private const float c_AcquireThreshold = 0.25f;
	#endregion

	#region Nested Types
	private struct SampleResult
	{
		public float TCandidate;
		public float TLos;
		public float TDetected;
		public float Q;
		public float DistanceFactor;
		public float FovFactor;
		public float ExposureFactor;
		public float MovementFactor;
		public float Progress;
		public float StagedExposure;
		public bool Detected;
		public string NeverReason;
		public int LosChecks;
		public int QualityEval;
		public int Candidates;
		public int CacheHits;
		public int SweepTicks;
	}

	private sealed class CellRuns
	{
		public string Group;
		public string Source;
		public float Distance;
		public float Yaw;
		public float Exposure;
		public string Movement;
		public readonly List<SampleResult> Runs = new List<SampleResult>(20);
	}
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(65536);
	private readonly List<CellRuns> m_Cells = new List<CellRuns>(80);
	private int m_PassCount;
	private int m_FailCount;
	private int m_CompletedSamples;
	private WeaponAttachmentDefinition m_TestOptic300;
	private int m_TotalLos;
	private int m_TotalQEval;
	private int m_TotalCandidates;
	private int m_TotalCache;
	private int m_ExposureStagingFailureCount;
	private bool m_FixtureInvalid;
	private string m_FixtureInvalidReason;
	private bool m_BalanceMode;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart ||
		DetectionHarnessPlayMode.RunVisionDetectionCalibration ||
		DetectionHarnessPlayMode.RunVisionDetectionBalance;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[VisionDetectionCalibration] measuring Q→time. No retune.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunVisionDetectionCalibration ||
		    DetectionHarnessPlayMode.RunVisionDetectionBalance)
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

	#region Suite
	private IEnumerator RunSuite()
	{
		yield return null;
		yield return null;

		m_Report.Clear();
		m_Cells.Clear();
		m_PassCount = 0;
		m_FailCount = 0;
		m_CompletedSamples = 0;
		m_TotalLos = 0;
		m_TotalQEval = 0;
		m_TotalCandidates = 0;
		m_TotalCache = 0;
		m_ExposureStagingFailureCount = 0;
		m_FixtureInvalid = false;
		m_FixtureInvalidReason = null;
		m_BalanceMode = DetectionHarnessPlayMode.RunVisionDetectionBalance;

		if (m_BalanceMode)
		{
			Append("VISION DETECTION BALANCE");
			Append("========================");
			Append("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
			Append(
				"Stage 6 acceptance — Q = D×F×E×M frozen; AcquireThreshold=0.25 AcquireTime=0.35 " +
				"exponent=" + DetectionQualityMath.DefaultAcquisitionExponent.ToString("0.0", CultureInfo.InvariantCulture) +
				" DistanceCurve edge=" + DetectionQualityMath.DefaultFarFactor.ToString("0.00", CultureInfo.InvariantCulture));
		}
		else
		{
			Append("VISION DETECTION CALIBRATION");
			Append("============================");
			Append("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
			Append("measure only — AcquireThreshold=0.25 AcquireTime=0.35 DistanceCurve frozen");
		}

		float freezeTimeout = m_BalanceMode ? c_BalanceTimeoutSeconds : c_TimeoutSeconds;
		float sweepTimeout = m_BalanceMode ? c_BalanceSweepTimeoutSeconds : c_SweepTimeoutSeconds;
		Append(
			"timeout=" + freezeTimeout.ToString("0", CultureInfo.InvariantCulture) +
			"s sweep=" + sweepTimeout.ToString("0", CultureInfo.InvariantCulture) +
			"s  NEVER = Q below gate or no Detected; gated abort after " +
			c_GateHoldSeconds.ToString("0.00", CultureInfo.InvariantCulture) +
			"s LOS; frozen abort " +
			c_FrozenNoLosAbortSeconds.ToString("0", CultureInfo.InvariantCulture) +
			"s only if never in cone");
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

		PrepareUnits(observer, target);
		bool observerReady = TryValidateCalibrationActor(observer, false, out string observerDetail);
		bool targetReady = TryValidateCalibrationActor(target, true, out string targetDetail);
		bool observerIsolated = TryValidateCalibrationIsolation(observer, out string observerIsolationDetail);
		bool targetIsolated = TryValidateCalibrationIsolation(target, out string targetIsolationDetail);
		Check("Fixture_ObserverReady", observerReady, observerDetail);
		Check("Fixture_TargetReady", targetReady, targetDetail);
		Check("Fixture_ObserverCombatIsolated", observerIsolated, observerIsolationDetail);
		Check("Fixture_TargetCombatIsolated", targetIsolated, targetIsolationDetail);
		if (!observerReady || !targetReady || !observerIsolated || !targetIsolated)
		{
			Finish("FAIL");
			yield break;
		}

		vision.SetVisionRange(UnitVisionProfile.BaseRangeMeters);
		EnsureTestOptics();

		if (m_BalanceMode)
			yield return RunBalanceMatrix(vision, observer, target);
		else
			yield return RunMeasureMatrix(vision, observer, target);

		vision.DebugClearVisionOverrides();
		if (vision.ScopeScan != null)
			vision.ScopeScan.SetFrozenForTest(false);
		m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();

		WriteAggregates();
		if (m_BalanceMode)
			EvaluateBalanceAcceptance();
		bool observerIntact = TryValidateCalibrationActor(observer, false, out observerDetail);
		bool targetIntact = TryValidateCalibrationActor(target, true, out targetDetail);
		Check(
			"Fixture_ActorsIntact",
			observerIntact && targetIntact && !m_FixtureInvalid,
			m_FixtureInvalid
				? m_FixtureInvalidReason
				: $"observer={observerDetail}; target={targetDetail}");
		observerIsolated = TryValidateCalibrationIsolation(observer, out observerIsolationDetail);
		targetIsolated = TryValidateCalibrationIsolation(target, out targetIsolationDetail);
		Check(
			"Fixture_CombatIsolationHeld",
			observerIsolated && targetIsolated,
			$"observer={observerIsolationDetail}; target={targetIsolationDetail}");
		Check(
			"ExposureStaging_Valid",
			m_ExposureStagingFailureCount == 0,
			$"outsideTolerance={m_ExposureStagingFailureCount}");
		int minSamples = m_BalanceMode ? 120 : 300;
		Check("Suite_Completed", m_CompletedSamples > minSamples && !m_FixtureInvalid,
			"samples=" + m_CompletedSamples.ToString(CultureInfo.InvariantCulture));
		string status = m_FailCount == 0 ? "PASS" : "FAIL";
		Finish(status);
	}

	private IEnumerator RunMeasureMatrix(UnitVision _vision, Transform _observer, Transform _target)
	{
		BeginGroup("A DISTANCE");
		float[] eyeDist = { 25f, 50f, 75f, 100f, 125f, 140f, 149f };
		float[] opticDist = { 150f, 175f, 200f, 225f, 250f, 275f, 299f };
		yield return RunCell("A", _vision, _observer, _target, false, eyeDist[0], 0f, 1f, "Static", c_RepeatsDefault, true);
		CellRuns baseline = FindCell("A", "Eye", eyeDist[0], 0f, 1f, "Static");
		bool baselineStable = CountDetected(baseline) == c_RepeatsDefault;
		Check(
			"Fixture_Baseline25",
			baselineStable,
			$"detected={CountDetected(baseline)}/{c_RepeatsDefault}");
		if (!baselineStable)
		{
			m_FixtureInvalid = true;
			m_FixtureInvalidReason = "25m open-field baseline was not detected in every repeat";
		}

		for (int i = 1; i < eyeDist.Length; i++)
			yield return RunCell("A", _vision, _observer, _target, false, eyeDist[i], 0f, 1f, "Static", c_RepeatsDefault, true);
		for (int i = 0; i < opticDist.Length; i++)
			yield return RunCell("A", _vision, _observer, _target, true, opticDist[i], 0f, 1f, "Static", c_RepeatsDefault, true);

		BeginGroup("E HIGH-N");
		yield return RunCell("E", _vision, _observer, _target, false, 75f, 0f, 1f, "Static", c_RepeatsHighN, true);
		yield return RunCell("E", _vision, _observer, _target, true, 225f, 0f, 1f, "Static", c_RepeatsHighN, true);

		BeginGroup("B FOV");
		float[] eyeFovDist = { 50f, 100f, 140f };
		float[] eyeFov = { 0f, 30f, 55f };
		float[] opticFovDist = { 150f, 225f, 290f };
		float[] opticFov = { 0f, 2f, 3.5f };
		for (int d = 0; d < eyeFovDist.Length; d++)
		{
			for (int f = 0; f < eyeFov.Length; f++)
				yield return RunCell("B", _vision, _observer, _target, false, eyeFovDist[d], eyeFov[f], 1f, "Static", c_RepeatsDefault, true);
		}

		for (int d = 0; d < opticFovDist.Length; d++)
		{
			for (int f = 0; f < opticFov.Length; f++)
				yield return RunCell("B", _vision, _observer, _target, true, opticFovDist[d], opticFov[f], 1f, "Static", c_RepeatsDefault, true);
		}

		BeginGroup("C EXPOSURE");
		float[] expDistEye = { 75f, 140f };
		float[] expDistOptic = { 200f, 290f };
		float[] exposures = { 1f, 0.5f, 0.25f };
		for (int d = 0; d < expDistEye.Length; d++)
		{
			for (int e = 0; e < exposures.Length; e++)
				yield return RunCell("C", _vision, _observer, _target, false, expDistEye[d], 0f, exposures[e], "Static", c_RepeatsDefault, true);
		}

		for (int d = 0; d < expDistOptic.Length; d++)
		{
			for (int e = 0; e < exposures.Length; e++)
				yield return RunCell("C", _vision, _observer, _target, true, expDistOptic[d], 0f, exposures[e], "Static", c_RepeatsDefault, true);
		}

		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();

		BeginGroup("D MOVEMENT");
		string[] moves = { "Static", "Walk", "Run" };
		for (int d = 0; d < expDistEye.Length; d++)
		{
			for (int m = 0; m < moves.Length; m++)
				yield return RunCell("D", _vision, _observer, _target, false, expDistEye[d], 0f, 1f, moves[m], c_RepeatsDefault, true);
		}

		for (int d = 0; d < expDistOptic.Length; d++)
		{
			for (int m = 0; m < moves.Length; m++)
				yield return RunCell("D", _vision, _observer, _target, true, expDistOptic[d], 0f, 1f, moves[m], c_RepeatsDefault, true);
		}

		m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);

		BeginGroup("F SWEEP");
		float[] sweepYaw = { -40f, 0f, 40f };
		for (int i = 0; i < sweepYaw.Length; i++)
			yield return RunCell("F", _vision, _observer, _target, true, 225f, sweepYaw[i], 1f, "Static", c_RepeatsDefault, false);

		BeginGroup("G SECTOR");
		yield return RunSectorPass(_vision, _observer, _target);
	}

	private IEnumerator RunBalanceMatrix(UnitVision _vision, Transform _observer, Transform _target)
	{
		BeginGroup("A DISTANCE");
		VisionDetectionTimingContract.TimingAnchor[] anchors = VisionDetectionTimingContract.FullStaticCenterAnchors;
		if (anchors.Length > 0)
		{
			VisionDetectionTimingContract.TimingAnchor first = anchors[0];
			yield return RunCell(
				"A", _vision, _observer, _target, first.Optic, first.DistanceMeters, 0f, 1f, "Static",
				first.Repeats, true);
			CellRuns baseline = FindCell("A", first.Optic ? "Optic" : "Eye", first.DistanceMeters, 0f, 1f, "Static");
			bool baselineStable = CountDetected(baseline) == first.Repeats;
			Check(
				"Fixture_Baseline25",
				baselineStable,
				$"detected={CountDetected(baseline)}/{first.Repeats}");
			if (!baselineStable)
			{
				m_FixtureInvalid = true;
				m_FixtureInvalidReason = "25m open-field baseline was not detected in every repeat";
			}

			for (int i = 1; i < anchors.Length; i++)
			{
				VisionDetectionTimingContract.TimingAnchor anchor = anchors[i];
				yield return RunCell(
					"A", _vision, _observer, _target, anchor.Optic, anchor.DistanceMeters, 0f, 1f, "Static",
					anchor.Repeats, true);
			}
		}

		BeginGroup("B FOV");
		yield return RunCell("B", _vision, _observer, _target, false, 100f, 0f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("B", _vision, _observer, _target, false, 100f, 30f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("B", _vision, _observer, _target, false, 100f, 59f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("B", _vision, _observer, _target, false, 100f, 61f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("B", _vision, _observer, _target, true, 225f, 0f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("B", _vision, _observer, _target, true, 225f, 3.99f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("B", _vision, _observer, _target, true, 225f, 4.01f, 1f, "Static", c_RepeatsDefault, true);

		BeginGroup("C EXPOSURE");
		float twoThirds = 2f / 3f;
		float oneThird = 1f / 3f;
		float[] exposures = { 1f, twoThirds, oneThird, 0f };
		for (int e = 0; e < exposures.Length; e++)
			yield return RunCell("C", _vision, _observer, _target, false, 75f, 0f, exposures[e], "Static", c_RepeatsDefault, true);
		for (int e = 0; e < exposures.Length; e++)
			yield return RunCell("C", _vision, _observer, _target, true, 225f, 0f, exposures[e], "Static", c_RepeatsDefault, true);
		yield return RunCell("C", _vision, _observer, _target, false, 149f, 0f, twoThirds, "Static", c_RepeatsDefault, true);
		yield return RunCell("C", _vision, _observer, _target, false, 149f, 0f, oneThird, "Static", c_RepeatsDefault, true);
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();

		BeginGroup("D MOVEMENT");
		string[] moves = { "Static", "Walk", "Run" };
		for (int m = 0; m < moves.Length; m++)
			yield return RunCell("D", _vision, _observer, _target, false, 140f, 0f, 1f, moves[m], c_RepeatsDefault, true);
		for (int m = 0; m < moves.Length; m++)
			yield return RunCell("D", _vision, _observer, _target, true, 275f, 0f, 1f, moves[m], c_RepeatsDefault, true);
		m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);

		BeginGroup("F SWEEP");
		float[] sweepYaw = { -40f, 0f, 40f };
		for (int i = 0; i < sweepYaw.Length; i++)
			yield return RunCell("F", _vision, _observer, _target, true, 225f, sweepYaw[i], 1f, "Static", c_RepeatsDefault, false);

		BeginGroup("G SECTOR");
		yield return RunSectorPass(_vision, _observer, _target);

		BeginGroup("H RANGE");
		yield return RunCell("H", _vision, _observer, _target, false, 151f, 0f, 1f, "Static", c_RepeatsDefault, true);
		yield return RunCell("H", _vision, _observer, _target, true, 301f, 0f, 1f, "Static", c_RepeatsDefault, true);
	}

	private IEnumerator RunCell(
		string _group,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		bool _optic,
		float _distance,
		float _yaw,
		float _exposure,
		string _movement,
		int _repeats,
		bool _freezeYaw)
	{
		if (m_FixtureInvalid)
			yield break;

		var cell = new CellRuns
		{
			Group = _group,
			Source = _optic ? "Optic" : "Eye",
			Distance = _distance,
			Yaw = _yaw,
			Exposure = _exposure,
			Movement = _movement
		};

		for (int r = 0; r < _repeats; r++)
		{
			Debug.Log(
				$"[VISION CAL] {_group} {cell.Source} {_distance:0}m yaw={_yaw:0.#} E={_exposure:0.00} {_movement} " +
				$"run {r + 1}/{_repeats} t={Time.time:F1}",
				this);
			yield return SampleOnce(
				_vision,
				_observer,
				_target,
				_optic,
				_distance,
				_yaw,
				_exposure,
				_movement,
				_freezeYaw,
				cell.Runs);
			if (m_FixtureInvalid)
				yield break;
			m_CompletedSamples++;
		}

		m_Cells.Add(cell);
		AppendCellBlock(cell);
	}

	private IEnumerator SampleOnce(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		bool _optic,
		float _distance,
		float _yaw,
		float _exposure,
		string _movement,
		bool _freezeYaw,
		List<SampleResult> _sink)
	{
		if (!TryValidateCalibrationActor(_observer, false, out string observerDetail))
		{
			InvalidateFixture("observer invalid before sample: " + observerDetail);
			yield break;
		}
		if (!TryValidateCalibrationActor(_target, true, out string targetDetail))
		{
			InvalidateFixture("target invalid before sample: " + targetDetail);
			yield break;
		}

		ApplyOptic(_vision, _optic);
		PrepareUnits(_observer, _target);

		float moveSpeed = 0f;
		if (_movement == "Walk")
			moveSpeed = DetectionCalibrationScenarios.WalkSpeedMeters;
		else if (_movement == "Run")
			moveSpeed = DetectionCalibrationScenarios.RunSpeedMeters;

		var scenario = new DetectionCalibrationScenarios.Scenario(
			"Cal",
			_distance,
			_exposure,
			_yaw,
			moveSpeed,
			"N");
		m_Harness.ApplyCalibrationScenario(scenario);
		PrepareUnits(_observer, _target);
		Physics.SyncTransforms();
		ApplyOptic(_vision, _optic);
		DetectionTestController.SnapCalibrationPose(_observer);
		DetectionTestController.SnapCalibrationPose(_target);
		yield return null;
		DetectionTestController.SnapCalibrationPose(_observer);
		DetectionTestController.SnapCalibrationPose(_target);
		Physics.SyncTransforms();

		if (_exposure < 0.999f && m_Harness.ExposureStaging != null)
		{
			// Cheap 0/1/2/3 is for optic and for Eye 1/3–2/3. Eye 0 must hide the
			// weighted grid: cheap Head/Chest/Abdomen 0 still leaves limb Observation.
			bool cheapCounts = m_BalanceMode &&
				(_optic || _exposure > 0.01f) &&
				Mathf.Abs(_exposure * 3f - Mathf.Round(_exposure * 3f)) < 0.02f;
			if (cheapCounts)
			{
				int desired = Mathf.Clamp(Mathf.RoundToInt(_exposure * 3f), 0, 3);
				for (int attempt = 0; attempt < 4; attempt++)
				{
					m_Harness.ExposureStaging.BeginScenario();
					m_Harness.ExposureStaging.TryApplyCheapVisibleCount(
						_observer, _target, desired, out _);
					Physics.SyncTransforms();
					yield return null;
					DetectionTestController.SnapCalibrationPose(_observer);
					DetectionTestController.SnapCalibrationPose(_target);
					if (Mathf.Abs(m_Harness.ExposureStaging.MeasuredExposure01 - _exposure) <= c_ExposureTolerance)
						break;
				}
			}
			else
			{
				for (int attempt = 0; attempt < 4; attempt++)
				{
					m_Harness.ExposureStaging.BeginScenario();
					if (!_optic && _exposure <= 0.01f)
						m_Harness.ExposureStaging.ApplyZeroExposureHide(_observer, _target);
					else
						m_Harness.ExposureStaging.Apply(_observer, _target, _exposure);
					Physics.SyncTransforms();
					yield return null;
					DetectionTestController.SnapCalibrationPose(_observer);
					DetectionTestController.SnapCalibrationPose(_target);
					if (Mathf.Abs(m_Harness.ExposureStaging.MeasuredExposure01 - _exposure) <= c_ExposureTolerance)
						break;
				}
			}
		}

		float stagedExposure = m_Harness.ExposureStaging != null
			? m_Harness.ExposureStaging.MeasuredExposure01
			: 1f;
		if (Mathf.Abs(stagedExposure - _exposure) > c_ExposureTolerance)
		{
			m_ExposureStagingFailureCount++;
			Debug.LogError(
				$"[VisionDetectionCalibration] exposure staging outside tolerance: " +
				$"design={_exposure:F2} measured={stagedExposure:F2}",
				this);
		}

		if (_freezeYaw)
		{
			if (_vision.ScopeScan != null)
			{
				_vision.ScopeScan.SetAssignedSector(0f, ScopeScanController.DefaultAssignedSectorHalfDegrees);
				FreezeScopeYaw(_vision, 0f);
			}
		}
		else
		{
			if (_vision.ScopeScan != null)
			{
				_vision.ScopeScan.SetAssignedSector(0f, ScopeScanController.DefaultAssignedSectorHalfDegrees);
				_vision.ScopeScan.SetFrozenForTest(false);
				_vision.ScopeScan.ResetSweep();
				_vision.ScopeScan.SetScanYawForTest(-60f);
				_vision.ScopeScan.SetDirectionForTest(1);
				_vision.ScopeScan.SetFrozenForTest(false);
			}
		}

		// Stage 5 optic FOV contract: 3.99 in / 4.01 out is cheap-aim yaw, not root.
		if (m_BalanceMode && _optic && _freezeYaw && Mathf.Abs(_yaw) > 1f)
		{
			AlignTargetToCheapAimYaw(_vision, _observer, _target, _distance, _yaw);
			DetectionTestController.SnapCalibrationPose(_target);
			Physics.SyncTransforms();
			yield return null;
		}

		ApplyMovement(_vision, _movement);

		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		_vision.DebugClearLosCache();
		_vision.ScanStats.Reset();
		yield return null;
		Physics.SyncTransforms();

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		DetectionProcessor processor = m_Harness.DetectionProcessor;
		float timeout = _freezeYaw
			? (m_BalanceMode ? c_BalanceTimeoutSeconds : c_TimeoutSeconds)
			: (m_BalanceMode ? c_BalanceSweepTimeoutSeconds : c_SweepTimeoutSeconds);
		if (_exposure <= 0.01f)
			timeout = Mathf.Min(timeout, 2.5f);
		float t0 = Time.time;
		float tCandidate = -1f;
		float tLos = -1f;
		float tDetected = -1f;
		bool detected = false;
		bool hasQ = false;
		float lastPulse = t0 - c_ScanPulseSeconds;
		SampleResult result = default;

		while (Time.time - t0 < timeout)
		{
			if (!TryValidateCalibrationActor(_observer, false, out observerDetail))
			{
				InvalidateFixture("observer invalid during sample: " + observerDetail);
				break;
			}
			if (!TryValidateCalibrationActor(_target, false, out targetDetail))
			{
				InvalidateFixture("target invalid during sample: " + targetDetail);
				break;
			}

			if (m_Harness.ExposureStaging != null)
				m_Harness.ExposureStaging.Follow();

			if (Time.time - lastPulse >= c_ScanPulseSeconds)
			{
				_vision.RequestImmediateScan();
				lastPulse = Time.time;
			}

			if (tCandidate < 0f && IsInCurrentCone(_vision, _target, _optic))
				tCandidate = Time.time - t0;

			if (perception != null &&
			    perception.TryGetObservation(_target, out VisionObservation obs) &&
			    obs.IsVisible)
			{
				if (tLos < 0f)
					tLos = Time.time - t0;
				FillQFromObservation(_vision, obs, _movement, ref result);
				hasQ = true;
			}

			if (processor != null &&
			    processor.TryGetContact(_target, out PerceivedContact contact) &&
			    contact != null)
			{
				result.Q = contact.CurrentEvaluation.VisibilityQuality;
				result.DistanceFactor = contact.CurrentEvaluation.DistanceFactor;
				result.FovFactor = contact.CurrentEvaluation.FovFactor;
				result.ExposureFactor = contact.CurrentEvaluation.ExposureFactor;
				result.MovementFactor = contact.CurrentEvaluation.MovementFactor;
				result.Progress = contact.DetectionProgress;
				hasQ = result.Q > 0f || hasQ;
				if (tLos < 0f && contact.LastObservation.IsVisible)
					tLos = Time.time - t0;
				if (!detected && contact.State == DetectionState.Detected)
				{
					detected = true;
					tDetected = Time.time - t0;
					break;
				}
			}

			if (tLos >= 0f &&
			    hasQ &&
			    result.Q <= c_AcquireThreshold + 0.0001f &&
			    Time.time - t0 - tLos >= c_GateHoldSeconds)
				break;

			if (_freezeYaw &&
			    tLos < 0f &&
			    tCandidate < 0f &&
			    Time.time - t0 >= c_FrozenNoLosAbortSeconds)
				break;

			yield return null;
		}

		if (m_FixtureInvalid)
		{
			if (m_Harness.DetectionProcessor != null)
			{
				m_Harness.DetectionProcessor.ClearContacts();
				m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
			}
			m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);
			yield break;
		}

		VisionScanStats stats = _vision.ScanStats;
		result.TCandidate = tCandidate;
		result.TLos = tLos;
		result.TDetected = detected ? tDetected : -1f;
		result.Detected = detected;
		result.StagedExposure = stagedExposure;
		result.LosChecks = stats.LosCheckCount;
		result.QualityEval = stats.QualityEvalCount;
		result.Candidates = stats.CandidateCount;
		result.CacheHits = stats.CachedLosCount;
		result.SweepTicks = stats.ScopeSweepScanCount;
		if (!detected)
			result.NeverReason = ResolveNeverReason(result, _vision);
		m_TotalLos += stats.LosCheckCount;
		m_TotalQEval += stats.QualityEvalCount;
		m_TotalCandidates += stats.CandidateCount;
		m_TotalCache += stats.CachedLosCount;
		_sink.Add(result);

		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);
	}

	private IEnumerator RunSectorPass(UnitVision _vision, Transform _observer, Transform _target)
	{
		if (m_FixtureInvalid)
			yield break;
		if (!TryValidateCalibrationActor(_observer, false, out string observerDetail))
		{
			InvalidateFixture("observer invalid before sector pass: " + observerDetail);
			yield break;
		}
		if (!TryValidateCalibrationActor(_target, true, out string targetDetail))
		{
			InvalidateFixture("target invalid before sector pass: " + targetDetail);
			yield break;
		}

		ApplyOptic(_vision, true);
		PrepareUnits(_observer, _target);
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);
		m_Harness.PinObserverPoseToCalibrationPad();
		Physics.SyncTransforms();
		ApplyOptic(_vision, true);
		DetectionTestController.SnapCalibrationPose(_observer);
		yield return null;
		DetectionTestController.SnapCalibrationPose(_observer);

		PlaceOnBodyAxis(_vision, _observer, _target, 225f, -50f);
		GameObject dummyB = SpawnDummyEnemy(_vision, _observer, 225f, -20f, "CalSector_B");
		GameObject dummyC = SpawnDummyEnemy(_vision, _observer, 225f, 10f, "CalSector_C");
		GameObject dummyD = SpawnDummyEnemy(_vision, _observer, 225f, 45f, "CalSector_D");
		Transform[] marks =
		{
			_target,
			dummyB != null ? dummyB.transform : null,
			dummyC != null ? dummyC.transform : null,
			dummyD != null ? dummyD.transform : null
		};
		string[] names = { "A-50", "B-20", "C+10", "D+45" };

		try
		{
			if (_vision.ScopeScan != null)
			{
				_vision.ScopeScan.SetAssignedSector(0f, 60f);
				_vision.ScopeScan.SetFrozenForTest(false);
				_vision.ScopeScan.ResetSweep();
				_vision.ScopeScan.SetScanYawForTest(-60f);
				_vision.ScopeScan.SetDirectionForTest(1);
			}

			if (m_Harness.DetectionProcessor != null)
			{
				m_Harness.DetectionProcessor.ClearContacts();
				m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
			}

			yield return null;
			float t0 = Time.time;
			float[] first = { -1f, -1f, -1f, -1f };
			float[] markYaw = { -50f, -20f, 10f, 45f };
			while (Time.time - t0 < c_SectorTimeoutSeconds)
			{
				if (!TryValidateCalibrationActor(_observer, false, out observerDetail))
				{
					InvalidateFixture("observer invalid during sector pass: " + observerDetail);
					yield break;
				}
				if (!TryValidateCalibrationActor(_target, false, out targetDetail))
				{
					InvalidateFixture("target invalid during sector pass: " + targetDetail);
					yield break;
				}

				for (int i = 0; i < marks.Length; i++)
				{
					if (first[i] >= 0f || marks[i] == null)
						continue;
					if (m_Harness.DetectionProcessor != null &&
					    m_Harness.DetectionProcessor.TryGetContact(marks[i], out PerceivedContact c) &&
					    c != null &&
					    c.State == DetectionState.Detected)
					{
						first[i] = Time.time - t0;
						ParkFar(_vision, _observer, marks[i]);
					}
				}

				if (first[0] >= 0f && first[1] >= 0f && first[2] >= 0f && first[3] >= 0f)
					break;
				yield return null;
			}

			var sb = new StringBuilder();
			sb.Append("first:");
			for (int i = 0; i < names.Length; i++)
				sb.Append(' ').Append(names[i]).Append('=').Append(FmtTime(first[i]));
			Append("[SECTOR] " + sb);
			Debug.Log("[VISION CAL] " + sb, this);

			m_Harness.PinObserverPoseToCalibrationPad();
			Physics.SyncTransforms();
			for (int i = 0; i < marks.Length; i++)
			{
				if (marks[i] != null)
					PlaceOnBodyAxis(_vision, _observer, marks[i], 225f, markYaw[i]);
			}

			if (m_Harness.DetectionProcessor != null)
			{
				m_Harness.DetectionProcessor.ClearContacts();
				m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
			}

			if (_vision.ScopeScan != null)
			{
				_vision.ScopeScan.SetFrozenForTest(false);
				_vision.ScopeScan.ResetSweep();
				_vision.ScopeScan.SetScanYawForTest(-60f);
				_vision.ScopeScan.SetDirectionForTest(1);
			}

			yield return null;
			float t1 = Time.time;
			float[] second = { -1f, -1f, -1f, -1f };
			var parkedSecond = new bool[marks.Length];
			bool sawGhost = false;
			while (Time.time - t1 < c_SectorTimeoutSeconds)
			{
				if (!TryValidateCalibrationActor(_observer, false, out observerDetail))
				{
					InvalidateFixture("observer invalid during sector repeat: " + observerDetail);
					yield break;
				}
				if (!TryValidateCalibrationActor(_target, false, out targetDetail))
				{
					InvalidateFixture("target invalid during sector repeat: " + targetDetail);
					yield break;
				}

				float yaw = _vision.ScopeScan != null ? _vision.ScopeScan.ScanYawDegrees : 0f;
				for (int i = 0; i < marks.Length; i++)
				{
					if (marks[i] == null)
						continue;
					bool inCone = IsTargetInScopeCone(_vision, marks[i]);
					bool det = m_Harness.DetectionProcessor != null &&
						m_Harness.DetectionProcessor.TryGetContact(marks[i], out PerceivedContact c2) &&
						c2 != null &&
						c2.State == DetectionState.Detected;
					if (det && second[i] < 0f)
					{
						second[i] = Time.time - t1;
						ParkFar(_vision, _observer, marks[i]);
						parkedSecond[i] = true;
					}

					if (det && !inCone && !parkedSecond[i] && Mathf.Abs(yaw) > 12f)
						sawGhost = true;
				}

				if (second[0] >= 0f && second[1] >= 0f && second[2] >= 0f && second[3] >= 0f)
					break;
				yield return null;
			}

			sb.Length = 0;
			sb.Append("second:");
			for (int i = 0; i < names.Length; i++)
				sb.Append(' ').Append(names[i]).Append('=').Append(FmtTime(second[i]));
			sb.Append(" ghostOutside8=").Append(sawGhost ? "1" : "0");
			Append("[SECTOR] " + sb);
			Debug.Log("[VISION CAL] " + sb, this);
		}
		finally
		{
			if (dummyB != null)
				Destroy(dummyB);
			if (dummyC != null)
				Destroy(dummyC);
			if (dummyD != null)
				Destroy(dummyD);
		}
	}
	#endregion

	#region Report
	private void AppendCellBlock(CellRuns _cell)
	{
		List<float> detected = CollectTimes(_cell, r => r.TDetected);
		List<float> los = CollectTimes(_cell, r => r.TLos);
		List<float> cand = CollectTimes(_cell, r => r.TCandidate);
		List<float> stagedExposure = CollectTimes(_cell, r => r.StagedExposure);
		List<float> detOnly = CollectTimes(_cell, r =>
			r.Detected && r.TLos >= 0f ? r.TDetected - r.TLos : -1f);
		int never = 0;
		SampleResult last = _cell.Runs.Count > 0 ? _cell.Runs[_cell.Runs.Count - 1] : default;
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			if (!_cell.Runs[i].Detected)
				never++;
		}

		Append("");
		Append("[VISION CALIBRATION]");
		Append($"Source: {_cell.Source}");
		Append($"Range: {_cell.Distance:0}m");
		Append($"FOV: {_cell.Yaw:0.#}°");
		Append($"Exposure: {_cell.Exposure:0.00}");
		Append("StagedExposure: " + FactorStatsLine(stagedExposure));
		Append($"Movement: {_cell.Movement}");
		Append($"N={_cell.Runs.Count} never={never}");
		if (never > 0)
			Append("NeverReason: " + SummarizeNeverReasons(_cell));
		Append("Candidate: " + StatsLine(cand));
		Append("LOS: " + StatsLine(los));
		if (_cell.Group == "F")
		{
			Append("SweepTime(LOS): " + StatsLine(los));
			Append("DetectionTime(Detected-LOS): " + StatsLine(detOnly));
			Append("Total: " + StatsLine(detected));
		}
		else
		{
			Append("Detected: " + StatsLine(detected));
			Append("DetectionTime(Detected-LOS): " + StatsLine(detOnly));
		}

		Append(
			$"Q last={last.Q:F3}  D={last.DistanceFactor:F3} F={last.FovFactor:F3} " +
			$"E={last.ExposureFactor:F3} M={last.MovementFactor:F3} P={last.Progress:F3}");
	}

	private void WriteAggregates()
	{
		Append("");
		Append("=== DISTANCE ===");
		WriteGroupSummary("A", true);
		Append("");
		Append("=== FOV ===");
		WriteGroupSummary("B", false);
		Append("");
		Append("=== EXPOSURE ===");
		WriteGroupSummary("C", false);
		Append("");
		Append("=== MOVEMENT ===");
		WriteGroupSummary("D", false);
		Append("");
		Append("=== HIGH-N ===");
		WriteGroupSummary("E", false);
		Append("");
		Append("=== SWEEP ===");
		WriteGroupSummary("F", false);

		Append("");
		Append("=== RELATIVE 50% ===");
		CellRuns eye75 = FindCell("A", "Eye", 75f, 0f, 1f, "Static");
		CellRuns optic150 = FindCell("A", "Optic", 150f, 0f, 1f, "Static");
		float eyeDet = MedianDetected(eye75);
		float opticDet = MedianDetected(optic150);
		Append($"Eye 75/150 median Detected={FmtTime(eyeDet)}");
		Append($"Optic 150/300 median Detected={FmtTime(opticDet)}");
		Append("expect similar T_detection if DistanceFactor matches (optics stretch range only)");

		Append("");
		Append("=== EDGE GATE ===");
		AppendEdge("A", "Eye", 140f);
		AppendEdge("A", "Eye", 149f);
		AppendEdge("A", "Optic", 275f);
		AppendEdge("A", "Optic", 299f);

		Append("");
		Append("=== PERF (measure only) ===");
		Append($"samples={m_CompletedSamples} LOS={m_TotalLos} QualityEval={m_TotalQEval} " +
			$"Candidates={m_TotalCandidates} CacheHits={m_TotalCache}");

		Append("");
		Append("=== NEXT ACTION (hints only, no retune this stage) ===");
		if (m_BalanceMode)
		{
			Append("Stage 6 Play acceptance is in ACCEPTANCE below. Sweep is T_LOS + T_accumulate; do not retune sweep.");
		}
		else
		{
			Append("DistanceCurve: MEASURED");
			Append("FOV curve: MEASURED");
			Append("Exposure: MEASURED");
			Append("Movement: MEASURED");
			Append("Sweep delay: MEASURED");
			bool deadEdge = IsMostlyNever(FindCell("A", "Eye", 149f, 0f, 1f, "Static")) ||
				IsMostlyNever(FindCell("A", "Optic", 299f, 0f, 1f, "Static"));
			Append(deadEdge
				? "Edge 140–149 / 275–299: dead or gated (Q often <= AcquireThreshold 0.25). Do not retune yet."
				: "Edge 140–149 / 275–299: live. Do not retune yet.");
			Append("Closer should be faster; FOV edge slower; low Exposure slower; Run faster than Static.");
		}
	}

	private void EvaluateBalanceAcceptance()
	{
		Append("");
		Append("=== ACCEPTANCE ===");
		VisionDetectionTimingContract.TimingAnchor[] anchors = VisionDetectionTimingContract.FullStaticCenterAnchors;
		for (int i = 0; i < anchors.Length; i++)
		{
			VisionDetectionTimingContract.TimingAnchor anchor = anchors[i];
			CellRuns cell = FindCell(
				"A",
				anchor.Optic ? "Optic" : "Eye",
				anchor.DistanceMeters,
				0f,
				1f,
				"Static");
			CheckDetectedBand(
				anchor.Id,
				cell,
				anchor.Repeats,
				anchor.MinDetectedSeconds,
				anchor.MaxDetectedWithSlack);
		}

		for (int i = 0; i < VisionDetectionTimingContract.RelativePairs.Length; i++)
		{
			(string eyeId, string opticId) = VisionDetectionTimingContract.RelativePairs[i];
			if (!VisionDetectionTimingContract.TryFindAnchor(eyeId, out var eye) ||
			    !VisionDetectionTimingContract.TryFindAnchor(opticId, out var optic))
				continue;
			float tEye = MedianDetected(FindCell("A", "Eye", eye.DistanceMeters, 0f, 1f, "Static"));
			float tOptic = MedianDetected(FindCell("A", "Optic", optic.DistanceMeters, 0f, 1f, "Static"));
			Check(
				"Relative_" + eyeId + "_" + opticId,
				VisionDetectionTimingContract.RelativeTimesMatch(tEye, tOptic),
				$"eye={FmtTime(tEye)} optic={FmtTime(tOptic)} tol={VisionDetectionTimingContract.RelativeToleranceSeconds(tEye, tOptic):0.00}s");
		}

		float prev = -1f;
		float[] eyeDist = { 25f, 50f, 75f, 100f, 140f, 149f };
		bool monotone = true;
		for (int i = 0; i < eyeDist.Length; i++)
		{
			float t = MedianDetected(FindCell("A", "Eye", eyeDist[i], 0f, 1f, "Static"));
			if (t < 0f || (prev >= 0f && t + 0.05f < prev))
				monotone = false;
			prev = t;
		}

		Check("Monotone_EyeDistance", monotone, "25 < 50 < 75 < 100 < 140 < 149");

		CellRuns fov0 = FindCell("B", "Eye", 100f, 0f, 1f, "Static");
		CellRuns fov30 = FindCell("B", "Eye", 100f, 30f, 1f, "Static");
		CellRuns fov59 = FindCell("B", "Eye", 100f, 59f, 1f, "Static");
		CellRuns fov61 = FindCell("B", "Eye", 100f, 61f, 1f, "Static");
		Check("Fov_Eye100_0_Detected", CountDetected(fov0) == c_RepeatsDefault, $"detected={CountDetected(fov0)}/{c_RepeatsDefault}");
		Check(
			"Fov_Eye100_30_Slower",
			CountDetected(fov30) == c_RepeatsDefault && MedianDetected(fov30) + 0.02f >= MedianDetected(fov0),
			$"0={FmtTime(MedianDetected(fov0))} 30={FmtTime(MedianDetected(fov30))}");
		Check("Fov_Eye100_59_Observation", CountObserved(fov59) == c_RepeatsDefault, $"los={CountObserved(fov59)}/{c_RepeatsDefault}");
		Check("Fov_Eye100_61_NoObservation", CountObserved(fov61) == 0 && CountDetected(fov61) == 0,
			$"los={CountObserved(fov61)} det={CountDetected(fov61)}");

		CellRuns optic0 = FindCell("B", "Optic", 225f, 0f, 1f, "Static");
		CellRuns optic399 = FindCell("B", "Optic", 225f, 3.99f, 1f, "Static");
		CellRuns optic401 = FindCell("B", "Optic", 225f, 4.01f, 1f, "Static");
		Check("Fov_Optic225_0_Detected", CountDetected(optic0) == c_RepeatsDefault, $"detected={CountDetected(optic0)}/{c_RepeatsDefault}");
		Check("Fov_Optic225_3.99_Observation", CountObserved(optic399) == c_RepeatsDefault, $"los={CountObserved(optic399)}/{c_RepeatsDefault}");
		Check("Fov_Optic225_4.01_NoObservation", CountObserved(optic401) == 0 && CountDetected(optic401) == 0,
			$"los={CountObserved(optic401)} det={CountDetected(optic401)}");

		float twoThirds = 2f / 3f;
		float oneThird = 1f / 3f;
		CellRuns e1 = FindCell("C", "Eye", 75f, 0f, 1f, "Static");
		CellRuns e23 = FindCell("C", "Eye", 75f, 0f, twoThirds, "Static");
		CellRuns e13 = FindCell("C", "Eye", 75f, 0f, oneThird, "Static");
		CellRuns e0 = FindCell("C", "Eye", 75f, 0f, 0f, "Static");
		Check("Exp_Eye75_1_Detected", CountDetected(e1) == c_RepeatsDefault, $"detected={CountDetected(e1)}");
		Check(
			"Exp_Eye75_2/3_Slower",
			CountDetected(e23) == c_RepeatsDefault && MedianDetected(e23) + 0.02f >= MedianDetected(e1),
			$"E1={FmtTime(MedianDetected(e1))} E2/3={FmtTime(MedianDetected(e23))}");
		Check(
			"Exp_Eye75_1/3_NotFaster",
			CountDetected(e13) == 0 || MedianDetected(e13) + 0.02f >= MedianDetected(e23),
			$"E1/3={FmtTime(MedianDetected(e13))} detected={CountDetected(e13)}");
		Check("Exp_Eye75_0_NoObservation", CountObserved(e0) == 0 && CountDetected(e0) == 0,
			$"los={CountObserved(e0)} det={CountDetected(e0)}");

		CellRuns o1 = FindCell("C", "Optic", 225f, 0f, 1f, "Static");
		CellRuns o23 = FindCell("C", "Optic", 225f, 0f, twoThirds, "Static");
		CellRuns o13 = FindCell("C", "Optic", 225f, 0f, oneThird, "Static");
		CellRuns o0 = FindCell("C", "Optic", 225f, 0f, 0f, "Static");
		Check("Exp_Optic225_1_Detected", CountDetected(o1) == c_RepeatsDefault, $"detected={CountDetected(o1)}");
		Check(
			"Exp_Optic225_2/3_Slower",
			CountDetected(o23) == c_RepeatsDefault && MedianDetected(o23) + 0.02f >= MedianDetected(o1),
			$"E1={FmtTime(MedianDetected(o1))} E2/3={FmtTime(MedianDetected(o23))}");
		Check(
			"Exp_Optic225_1/3_MayGate",
			CountDetected(o13) == 0 || MedianDetected(o13) + 0.02f >= MedianDetected(o23),
			$"E1/3={FmtTime(MedianDetected(o13))} detected={CountDetected(o13)}");
		Check("Exp_Optic225_0_NoObservation", CountObserved(o0) == 0 && CountDetected(o0) == 0,
			$"los={CountObserved(o0)} det={CountDetected(o0)}");
		Check(
			"Exp_Eye149_2/3_Gated",
			IsMostlyNever(FindCell("C", "Eye", 149f, 0f, twoThirds, "Static")),
			"partial Exposure at live edge may stay gated");

		CellRuns mvS = FindCell("D", "Eye", 140f, 0f, 1f, "Static");
		CellRuns mvW = FindCell("D", "Eye", 140f, 0f, 1f, "Walk");
		CellRuns mvR = FindCell("D", "Eye", 140f, 0f, 1f, "Run");
		Check("Move_Eye140_AllDetected",
			CountDetected(mvS) == c_RepeatsDefault &&
			CountDetected(mvW) == c_RepeatsDefault &&
			CountDetected(mvR) == c_RepeatsDefault,
			$"S={CountDetected(mvS)} W={CountDetected(mvW)} R={CountDetected(mvR)}");
		Check(
			"Move_Eye140_StaticSlowest",
			MedianDetected(mvS) + 0.02f >= MedianDetected(mvW) &&
			MedianDetected(mvW) + 0.02f >= MedianDetected(mvR),
			$"S={FmtTime(MedianDetected(mvS))} W={FmtTime(MedianDetected(mvW))} R={FmtTime(MedianDetected(mvR))}");

		float losN = MedianLos(FindCell("F", "Optic", 225f, -40f, 1f, "Static"));
		float los0 = MedianLos(FindCell("F", "Optic", 225f, 0f, 1f, "Static"));
		float losP = MedianLos(FindCell("F", "Optic", 225f, 40f, 1f, "Static"));
		Check(
			"Sweep_TLos_Order",
			losN >= 0f && los0 >= 0f && losP >= 0f && losN <= los0 + 0.05f && los0 <= losP + 0.05f,
			$"T_LOS -40={FmtTime(losN)} 0={FmtTime(los0)} +40={FmtTime(losP)} (not a sweep-speed retune)");
		float accN = MedianAccumulate(FindCell("F", "Optic", 225f, -40f, 1f, "Static"));
		float acc0 = MedianAccumulate(FindCell("F", "Optic", 225f, 0f, 1f, "Static"));
		float accP = MedianAccumulate(FindCell("F", "Optic", 225f, 40f, 1f, "Static"));
		Append(
			"Sweep accumulate -40=" + FmtTime(accN) +
			" 0=" + FmtTime(acc0) +
			" +40=" + FmtTime(accP));

		Check("Range_Eye151_NoObservation",
			CountObserved(FindCell("H", "Eye", 151f, 0f, 1f, "Static")) == 0,
			"150+ must not create Observation");
		Check("Range_Optic301_NoObservation",
			CountObserved(FindCell("H", "Optic", 301f, 0f, 1f, "Static")) == 0,
			"300+ must not create Observation");
	}

	private void CheckDetectedBand(
		string _id,
		CellRuns _cell,
		int _expectedDetected,
		float _minSeconds,
		float _maxSeconds)
	{
		int detected = CountDetected(_cell);
		int n = _cell != null ? _cell.Runs.Count : 0;
		Check(
			_id + "_Detected",
			detected == _expectedDetected && n == _expectedDetected,
			$"detected={detected}/{n}");
		float median = MedianDetected(_cell);
		Check(
			_id + "_Band",
			VisionDetectionTimingContract.FitsBand(median, _minSeconds, _maxSeconds),
			$"median={FmtTime(median)} band={_minSeconds:0.00}-{_maxSeconds:0.00}s Q={LastQ(_cell):0.000}");
	}

	private void WriteGroupSummary(string _group, bool _listEach)
	{
		for (int i = 0; i < m_Cells.Count; i++)
		{
			CellRuns cell = m_Cells[i];
			if (cell.Group != _group)
				continue;
			List<float> times = CollectTimes(cell, r => r.TDetected);
			int never = 0;
			for (int r = 0; r < cell.Runs.Count; r++)
			{
				if (!cell.Runs[r].Detected)
					never++;
			}

			string line =
				$"{cell.Source} {cell.Distance:0}m yaw={cell.Yaw:0.#} E={cell.Exposure:0.00} {cell.Movement}  " +
				StatsLine(times) +
				$"  never={never}/{cell.Runs.Count}";
			Append(line);
			if (!_listEach)
				continue;
		}
	}

	private void AppendEdge(string _group, string _source, float _distance)
	{
		CellRuns cell = FindCell(_group, _source, _distance, 0f, 1f, "Static");
		if (cell == null)
		{
			Append($"{_source} {_distance:0}m  (missing)");
			return;
		}

		SampleResult last = cell.Runs.Count > 0 ? cell.Runs[cell.Runs.Count - 1] : default;
		bool dead = IsMostlyNever(cell) || last.Q <= c_AcquireThreshold + 0.001f && !HasAnyDetected(cell);
		Append(
			$"{_source} {_distance:0}m  {StatsLine(CollectTimes(cell, r => r.TDetected))}  " +
			$"Q={last.Q:F3}  {(dead ? "DEAD_OR_GATED" : "LIVE")}");
	}

	private CellRuns FindCell(
		string _group,
		string _source,
		float _distance,
		float _yaw,
		float _exposure,
		string _movement)
	{
		for (int i = 0; i < m_Cells.Count; i++)
		{
			CellRuns c = m_Cells[i];
			if (c.Group == _group &&
			    c.Source == _source &&
			    Mathf.Abs(c.Distance - _distance) < 0.1f &&
			    Mathf.Abs(c.Yaw - _yaw) < 0.005f &&
			    Mathf.Abs(c.Exposure - _exposure) < 0.01f &&
			    c.Movement == _movement)
				return c;
		}

		return null;
	}
	#endregion

	#region Stats
	private static List<float> CollectTimes(CellRuns _cell, Func<SampleResult, float> _pick)
	{
		var list = new List<float>(_cell.Runs.Count);
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			float v = _pick(_cell.Runs[i]);
			if (v >= 0f)
				list.Add(v);
		}

		list.Sort();
		return list;
	}

	private static float MedianDetected(CellRuns _cell)
	{
		if (_cell == null)
			return -1f;
		List<float> times = CollectTimes(_cell, r => r.TDetected);
		return Percentile(times, 0.5f);
	}

	private static float MedianLos(CellRuns _cell)
	{
		if (_cell == null)
			return -1f;
		List<float> times = CollectTimes(_cell, r => r.TLos);
		return Percentile(times, 0.5f);
	}

	private static float MedianAccumulate(CellRuns _cell)
	{
		if (_cell == null)
			return -1f;
		var list = new List<float>(_cell.Runs.Count);
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			SampleResult r = _cell.Runs[i];
			if (r.Detected && r.TDetected >= 0f && r.TLos >= 0f)
				list.Add(Mathf.Max(0f, r.TDetected - r.TLos));
		}

		list.Sort();
		return Percentile(list, 0.5f);
	}

	private static int CountObserved(CellRuns _cell)
	{
		if (_cell == null)
			return 0;
		int count = 0;
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			if (_cell.Runs[i].TLos >= 0f)
				count++;
		}

		return count;
	}

	private static float LastQ(CellRuns _cell)
	{
		if (_cell == null || _cell.Runs.Count == 0)
			return 0f;
		return _cell.Runs[_cell.Runs.Count - 1].Q;
	}

	private static bool IsMostlyNever(CellRuns _cell)
	{
		if (_cell == null || _cell.Runs.Count == 0)
			return true;
		int never = 0;
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			if (!_cell.Runs[i].Detected)
				never++;
		}

		return never * 2 >= _cell.Runs.Count;
	}

	private static int CountDetected(CellRuns _cell)
	{
		if (_cell == null)
			return 0;

		int count = 0;
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			if (_cell.Runs[i].Detected)
				count++;
		}

		return count;
	}

	private static bool HasAnyDetected(CellRuns _cell)
	{
		if (_cell == null)
			return false;
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			if (_cell.Runs[i].Detected)
				return true;
		}

		return false;
	}

	private static string StatsLine(List<float> _sorted)
	{
		if (_sorted == null || _sorted.Count == 0)
			return "min=NEVER median=NEVER p10=NEVER p90=NEVER max=NEVER";
		return
			$"min={FmtTime(_sorted[0])} median={FmtTime(Percentile(_sorted, 0.5f))} " +
			$"p10={FmtTime(Percentile(_sorted, 0.1f))} p90={FmtTime(Percentile(_sorted, 0.9f))} " +
			$"max={FmtTime(_sorted[_sorted.Count - 1])}";
	}

	private static string FactorStatsLine(List<float> _sorted)
	{
		if (_sorted == null || _sorted.Count == 0)
			return "missing";
		return
			$"min={_sorted[0]:F3} median={Percentile(_sorted, 0.5f):F3} " +
			$"max={_sorted[_sorted.Count - 1]:F3}";
	}

	private static string SummarizeNeverReasons(CellRuns _cell)
	{
		if (_cell == null)
			return "missing";

		var counts = new Dictionary<string, int>();
		for (int i = 0; i < _cell.Runs.Count; i++)
		{
			SampleResult run = _cell.Runs[i];
			if (run.Detected)
				continue;
			string reason = string.IsNullOrWhiteSpace(run.NeverReason)
				? "UNKNOWN"
				: run.NeverReason;
			counts.TryGetValue(reason, out int count);
			counts[reason] = count + 1;
		}

		var summary = new StringBuilder(128);
		foreach (KeyValuePair<string, int> pair in counts)
		{
			if (summary.Length > 0)
				summary.Append(", ");
			summary.Append(pair.Key).Append('=').Append(pair.Value);
		}

		return summary.Length > 0 ? summary.ToString() : "none";
	}

	private static string ResolveNeverReason(SampleResult _result, UnitVision _vision)
	{
		if (_result.TCandidate < 0f)
			return "OUT_OF_CONE";
		if (_result.TLos < 0f)
		{
			if (_result.Candidates <= 0)
				return "NO_CANDIDATE";
			if (_result.LosChecks <= 0)
				return "NO_LOS_QUERY";

			string blocker = _vision != null ? _vision.DebugLastLosBlocker : null;
			return string.IsNullOrWhiteSpace(blocker)
				? "NO_LOS"
				: "NO_LOS(" + blocker + ")";
		}
		if (_result.Q <= c_AcquireThreshold + 0.0001f)
			return "Q_GATED";
		return "TIMEOUT";
	}

	private static float Percentile(List<float> _sorted, float _p)
	{
		if (_sorted == null || _sorted.Count == 0)
			return -1f;
		if (_sorted.Count == 1)
			return _sorted[0];
		float u = Mathf.Clamp01(_p) * (_sorted.Count - 1);
		int lo = Mathf.FloorToInt(u);
		int hi = Mathf.CeilToInt(u);
		if (lo == hi)
			return _sorted[lo];
		float t = u - lo;
		return Mathf.Lerp(_sorted[lo], _sorted[hi], t);
	}

	private static string FmtTime(float _seconds)
	{
		if (_seconds < 0f)
			return "NEVER";
		return _seconds.ToString("F2", CultureInfo.InvariantCulture) + "s";
	}
	#endregion

	#region Setup helpers
	private static bool TryValidateCalibrationActor(
		Transform _unit,
		bool _requireVisionHitZones,
		out string _detail)
	{
		if (_unit == null)
		{
			_detail = "missing transform";
			return false;
		}
		if (!_unit.gameObject.activeInHierarchy)
		{
			_detail = _unit.name + " inactive";
			return false;
		}
		if (_unit.TryGetComponent(out DamageableTarget damageable))
		{
			if (!damageable.IsAlive)
			{
				_detail = _unit.name + " dead";
				return false;
			}
			if (damageable.CurrentHealth + 0.01f < damageable.MaxHealth)
			{
				_detail = _unit.name + " damaged (" +
					damageable.CurrentHealth.ToString("F1", CultureInfo.InvariantCulture) + "/" +
					damageable.MaxHealth.ToString("F1", CultureInfo.InvariantCulture) + " HP)";
				return false;
			}
		}
		if (_unit.TryGetComponent(out UnitHealth health))
		{
			if (health.IsDead)
			{
				_detail = _unit.name + " dead (UnitHealth)";
				return false;
			}
			if (health.HasInjuries)
			{
				_detail = _unit.name + " injured; injuries=" +
					health.InjuryCount.ToString(CultureInfo.InvariantCulture);
				return false;
			}
		}
		if (_unit.TryGetComponent(out UnitConsciousness consciousness) && !consciousness.IsConscious)
		{
			_detail = _unit.name + " unconscious/non-targetable";
			return false;
		}

		if (!_requireVisionHitZones)
		{
			_detail = _unit.name + " active/alive/conscious";
			return true;
		}

		if (!_unit.TryGetComponent(out UnitVision vision))
		{
			_detail = _unit.name + " missing UnitVision";
			return false;
		}

		vision.RefreshBodyHitZones();
		int usableZones = 0;
		IReadOnlyList<UnitBodyHitZone> zones = vision.BodyHitZones;
		for (int i = 0; i < zones.Count; i++)
		{
			if (UnitBodyHitZoneVisionUtility.IsUsableVisionZone(zones[i], out Collider collider) &&
			    collider != null)
				usableZones++;
		}

		if (usableZones <= 0)
		{
			_detail = _unit.name + " has no usable vision hit-zones";
			return false;
		}

		_detail = _unit.name + " active/alive/conscious; usableZones=" +
			usableZones.ToString(CultureInfo.InvariantCulture);
		return true;
	}

	private static bool TryValidateCalibrationIsolation(Transform _unit, out string _detail)
	{
		if (_unit == null)
		{
			_detail = "missing transform";
			return false;
		}

		if (_unit.TryGetComponent(out UnitWeaponFireDisciplineController discipline) && discipline.enabled)
		{
			_detail = _unit.name + " fire discipline still enabled";
			return false;
		}
		if (_unit.TryGetComponent(out UnitWeaponAutoFireWhenAimed autoFire) && autoFire.enabled)
		{
			_detail = _unit.name + " auto fire still enabled";
			return false;
		}
		if (_unit.TryGetComponent(out UnitWeaponFireController fireController) && fireController.enabled)
		{
			_detail = _unit.name + " fire controller still enabled";
			return false;
		}
		if (_unit.TryGetComponent(out TargetSelector selector) && selector.enabled)
		{
			_detail = _unit.name + " target selector still enabled";
			return false;
		}
		if (_unit.TryGetComponent(out EngagementDecisionController engagement) && engagement.enabled)
		{
			_detail = _unit.name + " engagement controller still enabled";
			return false;
		}
		if (_unit.TryGetComponent(out UnitClickToMove clickToMove) && clickToMove.enabled)
		{
			_detail = _unit.name + " click locomotion still enabled";
			return false;
		}
		if (_unit.TryGetComponent(out UnitNavLocomotionDriver locomotion) && locomotion.enabled)
		{
			_detail = _unit.name + " nav locomotion still enabled";
			return false;
		}

		_detail = _unit.name + " combat/locomotion isolated";
		return true;
	}

	private void InvalidateFixture(string _reason)
	{
		if (m_FixtureInvalid)
			return;

		m_FixtureInvalid = true;
		m_FixtureInvalidReason = string.IsNullOrWhiteSpace(_reason)
			? "unknown fixture failure"
			: _reason;
		Check("Fixture_RuntimeIntegrity", false, m_FixtureInvalidReason);
	}

	private void EnsureTestOptics()
	{
		if (m_TestOptic300 != null)
			return;
		m_TestOptic300 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestOptic300.name = "Attachment_CalScope_300";
		m_TestOptic300.SetScopeVisionRangeMeters(300f);
	}

	private static void FillQFromObservation(
		UnitVision _vision,
		in VisionObservation _obs,
		string _movement,
		ref SampleResult _result)
	{
		float distance = Mathf.Sqrt(Mathf.Max(0f, _obs.DistanceSq));
		float range = _vision != null ? _vision.ResolvedMaxRange : DetectionQualityMath.DefaultFarMeters;
		_result.DistanceFactor = DetectionQualityMath.DistanceFactor(distance, range);
		float fovHalf = DetectionQualityMath.DefaultFovHalfDegrees;
		if (_vision != null)
		{
			ResolvedVisionProfile profile = _vision.CurrentVisionProfile;
			fovHalf = _obs.Source == VisionObservationSource.Optic
				? profile.ScopeHalfFovDegrees
				: profile.EyeHalfFovDegrees;
		}

		_result.FovFactor = DetectionQualityMath.FovFactor(_obs.FovOffsetDegrees, fovHalf);
		_result.ExposureFactor = Mathf.Clamp01(_obs.Exposure01);
		float speed = 0f;
		if (_movement == "Walk")
			speed = DetectionCalibrationScenarios.WalkSpeedMeters;
		else if (_movement == "Run")
			speed = DetectionCalibrationScenarios.RunSpeedMeters;
		_result.MovementFactor = DetectionQualityMath.MovementFactor(speed);
		_result.Q = DetectionQualityMath.VisibilityQuality(
			_result.DistanceFactor,
			_result.FovFactor,
			_result.ExposureFactor,
			_result.MovementFactor);
	}

	private void ApplyOptic(UnitVision _vision, bool _useOptic)
	{
		if (!_useOptic)
		{
			_vision.DebugClearVisionOverrides();
			_vision.DebugSetVisionPoseOverride(WeaponPoseState.HipFire, true);
			return;
		}

		EnsureTestOptics();
		m_TestOptic300.SetScopeVisionRangeMeters(300f);
		_vision.DebugSetVisionOpticOverride(m_TestOptic300);
		_vision.DebugSetVisionPoseOverride(WeaponPoseState.Aiming, true);
	}

	private static void FreezeScopeYaw(UnitVision _vision, float _yawDegrees)
	{
		if (_vision == null || _vision.ScopeScan == null)
			return;
		_vision.ScopeScan.SetFrozenForTest(true);
		_vision.ScopeScan.SetScanYawForTest(_yawDegrees);
	}

	private void ApplyMovement(UnitVision _vision, string _movement)
	{
		Vector3 forward = _vision != null
			? _vision.GetGameplayVisionForwardXZ()
			: Vector3.forward;
		Vector3 strafeAxis = Vector3.Cross(Vector3.up, forward);
		if (strafeAxis.sqrMagnitude < 1e-6f)
			strafeAxis = Vector3.right;

		if (_movement == "Walk")
		{
			m_Harness.SetTargetMoveMode(
				DetectionTestController.MoveMode.Walk,
				strafeAxis);
		}
		else if (_movement == "Run")
		{
			m_Harness.SetTargetMoveMode(
				DetectionTestController.MoveMode.Run,
				strafeAxis);
		}
		else
		{
			m_Harness.SetTargetMoveMode(DetectionTestController.MoveMode.Idle, Vector3.forward);
		}
	}

	private static void PrepareUnits(Transform _observer, Transform _target)
	{
		DetectionTestController.PrepareCalibrationUnit(_observer);
		DetectionTestController.PrepareCalibrationUnit(_target);
	}

	private static void ParkFar(UnitVision _vision, Transform _observer, Transform _target)
	{
		if (_vision == null || _observer == null || _target == null)
			return;
		PlaceOnBodyAxis(_vision, _observer, _target, 400f, 80f);
	}

	private static void PlaceOnBodyAxis(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yawDegrees)
	{
		if (_observer == null || _target == null)
			return;

		_ = _vision;
		DetectionTestController.SnapCalibrationPose(_observer);
		DetectionTestController.SnapCalibrationPose(_target);
		Physics.SyncTransforms();
		Vector3 fwd = _observer.forward;
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 1e-6f)
			fwd = Vector3.forward;
		fwd.Normalize();
		Vector3 dir = Quaternion.AngleAxis(_yawDegrees, Vector3.up) * fwd;
		Vector3 pos = _observer.position + dir * _distance;
		pos.y = _observer.position.y;
		_target.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up));
		DetectionTestController.SnapCalibrationPose(_target);
		Physics.SyncTransforms();
	}

	private static void AlignTargetToCheapAimYaw(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yawDegrees)
	{
		if (_vision == null || _observer == null || _target == null)
			return;

		float placeYaw = _yawDegrees;
		for (int pass = 0; pass < 5; pass++)
		{
			Vector3 origin = _vision.GetGameplayVisionOriginWorld();
			Vector3 fwd = ResolvePlaceForward(_vision);
			Vector3 dir = Quaternion.AngleAxis(placeYaw, Vector3.up) * fwd;
			Vector3 pos = origin + dir * _distance;
			pos.y = _target.position.y;
			_target.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up));
			DetectionTestController.SnapCalibrationPose(_target);
			Physics.SyncTransforms();

			float aimYaw = SignedHorizontalYaw(fwd, GetCheapAimWorld(_target) - origin);
			float error = _yawDegrees - aimYaw;
			if (Mathf.Abs(error) < 0.005f)
				break;
			placeYaw += error;
		}
	}

	private static Vector3 ResolvePlaceForward(UnitVision _vision)
	{
		Vector3 fwd = _vision.GetGameplayVisionForwardXZ();
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 1e-6f)
			fwd = Vector3.forward;
		fwd.Normalize();
		if (_vision.CurrentVisionProfile.IsScopeActive && _vision.ScopeScan != null)
		{
			fwd = _vision.ScopeScan.GetSweepForwardXZ(fwd);
			fwd.y = 0f;
			if (fwd.sqrMagnitude < 1e-6f)
				fwd = Vector3.forward;
			fwd.Normalize();
		}

		return fwd;
	}

	private static Vector3 GetCheapAimWorld(Transform _target)
	{
		if (_target == null)
			return Vector3.zero;

		UnitBodyHitZone[] zones = _target.GetComponentsInChildren<UnitBodyHitZone>(true);
		Collider col = UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(zones, BodyPartType.Chest)
			?? UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(zones, BodyPartType.Head)
			?? UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(zones, BodyPartType.Abdomen);
		return col != null ? col.bounds.center : _target.position;
	}

	private static float SignedHorizontalYaw(Vector3 _forwardXZ, Vector3 _toPoint)
	{
		Vector3 forward = VisionGeometry.FlattenNormalized(_forwardXZ, Vector3.forward);
		Vector3 to = _toPoint;
		to.y = 0f;
		if (to.sqrMagnitude < 0.0001f)
			return 0f;
		return Vector3.SignedAngle(forward, to.normalized, Vector3.up);
	}

	private static bool IsInCurrentCone(UnitVision _vision, Transform _target, bool _opticPreferred)
	{
		if (_vision == null || _target == null)
			return false;
		Vector3 origin = _vision.GetGameplayVisionOriginWorld();
		Vector3 body = _vision.GetGameplayVisionForwardXZ();
		ResolvedVisionProfile profile = _vision.CurrentVisionProfile;
		Vector3 testPoint = _opticPreferred ? GetCheapAimWorld(_target) : _target.position;
		Vector3 to = testPoint - origin;
		to.y = 0f;
		float dist = to.magnitude;
		if (dist < 0.05f)
			return true;

		float eyeAng = Vector3.Angle(body, to);
		bool inEye = dist <= profile.EyeRangeMeters + 0.05f &&
			eyeAng <= profile.EyeHalfFovDegrees + 0.05f;
		if (!_opticPreferred)
			return inEye;
		if (!profile.IsScopeActive)
			return inEye;

		Vector3 query = _vision.ScopeScan != null
			? _vision.ScopeScan.GetQueryForwardXZ(body, origin)
			: body;
		float scopeAng = Vector3.Angle(query, to);
		// No +0.05° slack: Stage 5 optic contract is 3.99 in / 4.01 out.
		bool inScope = dist <= profile.ScopeRangeMeters + 0.05f &&
			scopeAng <= profile.ScopeHalfFovDegrees + 0.001f;
		return inScope || inEye;
	}

	private static bool IsTargetInScopeCone(UnitVision _vision, Transform _target)
	{
		if (_vision == null || _target == null || _vision.ScopeScan == null)
			return false;
		ResolvedVisionProfile profile = _vision.CurrentVisionProfile;
		if (!profile.IsScopeActive)
			return false;
		Vector3 origin = _vision.GetGameplayVisionOriginWorld();
		Vector3 query = _vision.ScopeScan.GetQueryForwardXZ(_vision.GetGameplayVisionForwardXZ(), origin);
		Vector3 to = _target.position - origin;
		to.y = 0f;
		if (to.sqrMagnitude < 1e-6f)
			return true;
		return Vector3.Angle(query, to) <= profile.ScopeHalfFovDegrees + 0.5f;
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

		Vector3 origin = _observer != null ? _observer.position : _observerVision.GetGameplayVisionOriginWorld();
		Vector3 fwd = _observer != null ? _observer.forward : _observerVision.GetGameplayVisionForwardXZ();
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
		Debug.Log("[VisionDetectionCalibration] " + line + $"  (pass={m_PassCount} fail={m_FailCount})", this);
	}

	private void BeginGroup(string _title)
	{
		Append("");
		Append(_title);
		Debug.Log(
			$"[VisionDetectionCalibration] {_title} t={Time.time:F1} samples={m_CompletedSamples} fail={m_FailCount}",
			this);
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
		Append("samples=" + m_CompletedSamples);
		string body = m_Report.ToString();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, m_BalanceMode
			? "VisionDetectionBalance_LAST.txt"
			: "VisionDetectionCalibration_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[VisionDetectionCalibration] wrote {latest}\n{body}", this);

#if UNITY_EDITOR
		if (m_ExitPlayModeWhenDone ||
		    DetectionHarnessPlayMode.RunVisionDetectionCalibration ||
		    DetectionHarnessPlayMode.RunVisionDetectionBalance)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
