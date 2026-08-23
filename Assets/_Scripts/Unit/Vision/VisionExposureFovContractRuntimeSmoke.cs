using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Stage 5 Play: far-optic Exposure = visible/tested zones, plus clean Eye/Optic FOV.
/// Does not retune Q, DistanceCurve, AcquireThreshold, or AcquireTime.
/// Report: Assets/_Docs/Logs/Tests/VisionExposureFovContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(61)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class VisionExposureFovContractRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_WaitYes = 3.5f;
	private const float c_WaitNo = 1.6f;
	private const float c_Pulse = 0.2f;
	private const float c_ExposureTol = 0.02f;
	private const float c_OpticDistance = 225f;
	private const float c_EyeDistance = 100f;
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
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunVisionExposureFovContract;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[VisionExposureFovContract] Exposure fraction + FOV source contract.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunVisionExposureFovContract)
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
		Append("VISION EXPOSURE / FOV CONTRACT");
		Append("==============================");
		Append("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("Q frozen. Cheap optic Exposure = visibleZones/testedZones. Optic FOV at 225 m only.");
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

		Append("");
		Append("=== STAGE 5 EXPOSURE ===");
		yield return RunExposureCase("E0", observer, target, vision, 3, true);
		yield return RunExposureCase("E1", observer, target, vision, 2, true);
		yield return RunExposureCase("E2", observer, target, vision, 1, true);
		yield return RunExposureCase("E3", observer, target, vision, 0, false);

		Append("");
		Append("=== STAGE 5 FOV ===");
		yield return RunFovCase(
			"[OPTIC] 225m 0.00°", vision, observer, target, true, c_OpticDistance, 0f, true,
			VisionObservationSource.Optic);
		yield return RunFovCase(
			"[OPTIC] 225m 2.00°", vision, observer, target, true, c_OpticDistance, 2f, true,
			VisionObservationSource.Optic);
		yield return RunFovCase(
			"[OPTIC] 225m 3.00°", vision, observer, target, true, c_OpticDistance, 3f, true,
			VisionObservationSource.Optic);
		yield return RunFovCase(
			"[OPTIC] 225m 3.50°", vision, observer, target, true, c_OpticDistance, 3.5f, true,
			VisionObservationSource.Optic);
		yield return RunFovCase(
			"[OPTIC] 225m 3.99°", vision, observer, target, true, c_OpticDistance, 3.99f, true,
			VisionObservationSource.Optic);
		yield return RunFovCase(
			"[OPTIC] 225m 4.01°", vision, observer, target, true, c_OpticDistance, 4.01f, false,
			VisionObservationSource.Optic);
		yield return RunFovCase(
			"[OPTIC] 225m 5.00°", vision, observer, target, true, c_OpticDistance, 5f, false,
			VisionObservationSource.Optic);

		yield return RunFovCase(
			"[EYE] 100m 0°", vision, observer, target, false, c_EyeDistance, 0f, true,
			VisionObservationSource.Eye);
		yield return RunFovCase(
			"[EYE] 100m 30°", vision, observer, target, false, c_EyeDistance, 30f, true,
			VisionObservationSource.Eye);
		yield return RunFovCase(
			"[EYE] 100m 59°", vision, observer, target, false, c_EyeDistance, 59f, true,
			VisionObservationSource.Eye);
		yield return RunFovCase(
			"[EYE] 100m 61°", vision, observer, target, false, c_EyeDistance, 61f, false,
			VisionObservationSource.Eye);

		Append("");
		Append("=== STAGE 5 SOURCE / DUPLICATE ===");
		yield return RunFarWithoutOptic(vision, observer, target);
		yield return RunEyeDoesNotSpendScopeLos(vision, observer, target);

		vision.DebugClearVisionOverrides();
		if (vision.ScopeScan != null)
			vision.ScopeScan.SetFrozenForTest(false);
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		ScopeScanController.TestLogging = false;

		string status = m_FailCount == 0 ? "PASS" : "FAIL";
		Finish(status);
	}

	private IEnumerator RunExposureCase(
		string _id,
		Transform _observer,
		Transform _target,
		UnitVision _vision,
		int _desiredVisible,
		bool _expectObservation)
	{
		FreezeScopeYaw(_vision, 0f);
		ApplyOptic(_vision, true, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, c_OpticDistance, 0f);
		DetectionTestController.SnapCalibrationPose(_observer);
		DetectionTestController.SnapCalibrationPose(_target);
		Physics.SyncTransforms();

		DetectionCalibrationExposureStaging staging = m_Harness.ExposureStaging;
		VisibilityChecker.CheapExposureSample staged = default;
		bool stagedOk = staging != null &&
			staging.TryApplyCheapVisibleCount(_observer, _target, _desiredVisible, out staged);
		Physics.SyncTransforms();
		_vision.DebugClearLosCache();
		_vision.ScanStats.Reset();

		ProbeResult probe = default;
		yield return WaitObservation(_vision, _observer, _target, _expectObservation, _result =>
		{
			probe = _result;
		});

		VisibilityChecker.CheapExposureSample cheap = _vision.DebugLastCheapExposure;
		float expectedExposure = VisibilityChecker.CheapZoneExposure01(
			_desiredVisible, Mathf.Max(1, cheap.TestedZones > 0 ? cheap.TestedZones : 3));
		if (_desiredVisible <= 0)
			expectedExposure = 0f;

		bool sourceOk = !_expectObservation || probe.Source == VisionObservationSource.Optic;
		bool zonesOk = cheap.TestedZones >= 3 && cheap.VisibleZones == _desiredVisible;
		bool exposureOk = _expectObservation
			? probe.HasObservation &&
			  Mathf.Abs(probe.Exposure01 - expectedExposure) <= c_ExposureTol &&
			  Mathf.Abs(probe.Exposure01 - cheap.Exposure01) <= c_ExposureTol
			: !probe.HasObservation && cheap.VisibleZones == 0;
		bool pass = stagedOk && zonesOk && exposureOk && sourceOk &&
			(_expectObservation == probe.HasObservation);

		string label = _id == "E0" ? "Full" : _id == "E1" ? "Partial" : _id == "E2" ? "HeadOnly" : "Hidden";
		Append($"[{_id}] Optic {c_OpticDistance:0}m {label}");
		Append(
			$"[OPTIC EXPOSURE] Target={(_target != null ? _target.name : "?")} " +
			$"Distance={c_OpticDistance:0} Source={(probe.HasObservation ? probe.Source.ToString() : "none")}");
		Append(cheap.FormatZones());
		if (staging != null)
			Append($"staged={staging.Note} stagedZones={staged.VisibleZones}/{staged.TestedZones}");
		Check(
			_id,
			pass,
			$"zones={cheap.VisibleZones}/{cheap.TestedZones} exposure={(_expectObservation ? probe.Exposure01 : cheap.Exposure01):0.00} " +
			$"obs={(probe.HasObservation ? "yes" : "no")} source={(probe.HasObservation ? probe.Source.ToString() : "none")}");
	}

	private IEnumerator RunFovCase(
		string _id,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		bool _useOptic,
		float _distance,
		float _yaw,
		bool _expectObservation,
		VisionObservationSource _expectedSource)
	{
		if (_useOptic)
			FreezeScopeYaw(_vision, 0f);
		ApplyOptic(_vision, _useOptic, _useOptic ? WeaponPoseState.Aiming : WeaponPoseState.HipFire);
		if (_useOptic)
			FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, _distance, _yaw, _alignCheapAim: _useOptic);
		Physics.SyncTransforms();
		_vision.DebugClearLosCache();

		float rootAng = MeasureHorizontalAngle(_vision, _target.position);
		float aimAng = MeasureHorizontalAngle(_vision, GetCheapAimWorld(_target));
		ProbeResult probe = default;
		yield return WaitObservation(_vision, _observer, _target, _expectObservation, _result =>
		{
			probe = _result;
		});

		bool sourceOk = true;
		if (_expectObservation)
		{
			sourceOk = probe.HasObservation && probe.Source == _expectedSource;
			if (_useOptic && probe.HasObservation && probe.Source == VisionObservationSource.Eye)
				sourceOk = false;
		}
		else if (_useOptic && probe.HasObservation && probe.Source == VisionObservationSource.Eye)
		{
			sourceOk = false;
		}

		bool pass = _expectObservation
			? probe.HasObservation && sourceOk
			: !probe.HasObservation && sourceOk;

		string detail = _expectObservation
			? $"source={(probe.HasObservation ? probe.Source.ToString() : "none")} root={rootAng:0.00}° aim={aimAng:0.00}°"
			: $"candidate=false root={rootAng:0.00}° aim={aimAng:0.00}° obs={(probe.HasObservation ? probe.Source.ToString() : "none")}";
		Check(_id, pass, detail);
	}

	private IEnumerator RunFarWithoutOptic(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, false, WeaponPoseState.HipFire);
		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, c_OpticDistance, 0f);
		Physics.SyncTransforms();
		_vision.DebugClearLosCache();

		ProbeResult probe = default;
		yield return WaitObservation(_vision, _observer, _target, false, _result =>
		{
			probe = _result;
		});
		Check(
			"R_225m_NoOptic",
			!probe.HasObservation,
			probe.HasObservation ? $"candidate={probe.Source}" : "candidate=none");
	}

	private IEnumerator RunEyeDoesNotSpendScopeLos(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, WeaponPoseState.Aiming);
		if (_vision.ScopeScan != null)
		{
			_vision.ScopeScan.SetAssignedSector(0f, 60f);
			_vision.ScopeScan.SetFrozenForTest(false);
			_vision.ScopeScan.ResetSweep();
			_vision.ScopeScan.SetScanYawForTest(0f);
		}

		ResetWorld(_vision);
		PlaceOnBodyAxis(_vision, _observer, _target, c_EyeDistance, 0f);
		DetectionTestController.SnapCalibrationPose(_observer);
		DetectionTestController.SnapCalibrationPose(_target);
		Physics.SyncTransforms();

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

		Check(
			"X_100m_Eye",
			eyeSeen && source == VisionObservationSource.Eye,
			eyeSeen ? source.ToString() : "missing");
		_vision.ScanStats.Reset();
		t0 = Time.time;
		while (Time.time - t0 < 0.6f)
			yield return null;

		Check(
			"X_100m_OpticLosSkipped",
			_vision.ScanStats.SkippedDuplicateCount > 0,
			$"skip={_vision.ScanStats.SkippedDuplicateCount} scopeLive={_vision.ScanStats.ScopeLiveLosCount}");
		if (_vision.ScopeScan != null)
			_vision.ScopeScan.SetFrozenForTest(true);
	}

	private IEnumerator WaitObservation(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		bool _expectObs,
		Action<ProbeResult> _done)
	{
		if (m_Harness.DetectionProcessor != null)
		{
			m_Harness.DetectionProcessor.ClearContacts();
			m_Harness.DetectionProcessor.ApplyEmptyObservationFrame();
		}

		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool lastSeen = false;
		VisionObservation last = default;
		void OnFrame()
		{
			lastSeen = perception != null &&
				perception.TryGetObservation(_target, out last) &&
				last.IsVisible;
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

		if (!lastSeen && perception != null &&
		    perception.TryGetObservation(_target, out VisionObservation late) &&
		    late.IsVisible)
		{
			lastSeen = true;
			last = late;
		}

		_done(new ProbeResult
		{
			HasObservation = lastSeen,
			Source = last.Source,
			Exposure01 = last.Exposure01
		});
	}
	#endregion

	#region Helpers
	private struct ProbeResult
	{
		public bool HasObservation;
		public VisionObservationSource Source;
		public float Exposure01;
	}

	private void EnsureTestOptics()
	{
		m_TestOptic300 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestOptic300.name = "Attachment_TestScope_300";
		m_TestOptic300.SetScopeVisionRangeMeters(300f);
	}

	private void ApplyOptic(UnitVision _vision, bool _useOptic, WeaponPoseState _pose)
	{
		if (!_useOptic)
		{
			_vision.DebugClearVisionOverrides();
			_vision.DebugSetVisionPoseOverride(_pose, true);
			return;
		}

		EnsureTestOptics();
		m_TestOptic300.SetScopeVisionRangeMeters(300f);
		_vision.DebugSetVisionOpticOverride(m_TestOptic300);
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

		if (_vision != null)
			_vision.DebugClearLosCache();

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
		if (_observer != null && _observer.TryGetComponent(out UnitVision observerVision))
			observerVision.RefreshBodyHitZones();
		if (_target != null && _target.TryGetComponent(out UnitVision targetVision))
			targetVision.RefreshBodyHitZones();
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

	private static void PlaceOnBodyAxis(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		float _yawDegrees,
		bool _alignCheapAim = false)
	{
		DisableLocomotion(_observer);
		DisableLocomotion(_target);
		Physics.SyncTransforms();

		float placeYaw = _yawDegrees;
		int passes = _alignCheapAim ? 5 : 2;
		for (int pass = 0; pass < passes; pass++)
		{
			Vector3 origin = _vision.GetGameplayVisionOriginWorld();
			Vector3 fwd = ResolvePlaceForward(_vision);
			Vector3 dir = Quaternion.AngleAxis(placeYaw, Vector3.up) * fwd;
			Vector3 pos = origin + dir * _distance;
			pos.y = _target.position.y;
			_target.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up));
			Physics.SyncTransforms();

			if (!_alignCheapAim)
				continue;

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

	private static float MeasureHorizontalAngle(UnitVision _vision, Vector3 _worldPoint)
	{
		if (_vision == null)
			return 0f;
		Vector3 origin = _vision.GetGameplayVisionOriginWorld();
		Vector3 fwd = _vision.GetGameplayVisionForwardXZ();
		if (_vision.CurrentVisionProfile.IsScopeActive && _vision.ScopeScan != null)
			fwd = _vision.ScopeScan.GetQueryForwardXZ(fwd, origin);
		return Mathf.Abs(SignedHorizontalYaw(fwd, _worldPoint - origin));
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		string line = (_ok ? "PASS " : "FAIL ") + _name + " | " + _detail;
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append(line);
		Debug.Log("[VisionExposureFov] " + line + $"  (pass={m_PassCount} fail={m_FailCount})", this);
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
		string latest = Path.Combine(dir, "VisionExposureFovContract_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[VisionExposureFovContractRuntimeSmoke] wrote {latest}\n{body}", this);

#if UNITY_EDITOR
		if (m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunVisionExposureFovContract)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
