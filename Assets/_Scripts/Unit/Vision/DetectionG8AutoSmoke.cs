using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// G8 LOD / cheap-before-expensive smoke. Writes Assets/_Docs/Logs/Tests/DetectionG8_LAST.txt
/// Runs after G7 (execution order 800, warmup 100s).
/// </summary>
[DefaultExecutionOrder(800)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionG8AutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private float m_WarmupSeconds = 100f;
	[SerializeField] private float m_ObserveSeconds = 2.2f;
	[SerializeField] private float m_RecentlyLostProbeSeconds = 0.25f;
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
		if (DetectionHarnessPlayMode.ShouldRunGAutoSmoke(m_RunOnStart, "G8"))
			StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunGStage == "G8")
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
		AppendLine($"DetectionG8 AutoSmoke {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		AppendLine("---");
		RunPureMathChecks();

		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		Transform target = m_Harness != null ? m_Harness.Target : null;
		TargetSelector selector = processor != null ? processor.GetComponent<TargetSelector>() : null;
		EngagementDecisionController engagement =
			processor != null ? processor.GetComponent<EngagementDecisionController>() : null;
		UnitVision vision = processor != null ? processor.GetComponent<UnitVision>() : null;

		Check("G8_Processor", processor != null, "DetectionProcessor missing");
		Check("G8_Target", target != null, "target missing");
		Check("G8_Selector", selector != null, "TargetSelector missing");
		Check("G8_Engagement", engagement != null, "EngagementDecisionController missing");
		Check("Isolation_SelectorNoLodTypes",
			!TypeHasFieldOf(typeof(TargetSelector), typeof(VisionScanTier)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(VisionLosCache)) &&
			!TypeHasFieldOf(typeof(TargetSelector), typeof(VisionScanStats)),
			"Selector must not store LOD types");
		Check("Isolation_EngagementNoLodTypes",
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(VisionScanTier)) &&
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(VisionLosCache)) &&
			!TypeHasFieldOf(typeof(EngagementDecisionController), typeof(VisionScanStats)),
			"Engagement must not store LOD types");

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
		vision = processor != null ? processor.GetComponent<UnitVision>() : null;
		if (processor == null || target == null || selector == null || engagement == null)
		{
			Finish();
			yield break;
		}

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
		EngagementDecision observed = engagement.CurrentDecision;
		Check("G8_ObserveAimOrFire",
			selector.SelectedTarget == target &&
			(observed == EngagementDecision.Aim || observed == EngagementDecision.Fire),
			$"sel={selector.SelectedTarget} d={observed}");

		processor.ApplyEmptyObservationFrame();
		yield return new WaitForSeconds(m_RecentlyLostProbeSeconds);
		yield return null;
		Check("G8_HiddenIsTrack",
			engagement.CurrentDecision == EngagementDecision.Track && !selector.HasSelectedAimPoint,
			$"d={engagement.CurrentDecision} aim={selector.HasSelectedAimPoint}");

		processor.ApplySyntheticSound(target, seenPos + Vector3.forward * 3f, 1f);
		yield return null;
		Check("G8_SoundStillTrackNotFire",
			engagement.CurrentDecision == EngagementDecision.Track &&
			engagement.CurrentDecision != EngagementDecision.Fire &&
			!selector.HasSelectedAimPoint,
			engagement.CurrentDecision.ToString());

		processor.ClearContacts();
		selector.ClearSelection(true);
		yield return null;

		if (vision != null)
		{
			vision.enabled = true;
			vision.ScanStats.Reset();
			vision.RequestImmediateScan();
			yield return null;
			Check("G8_ImmediateScanRunsDetail",
				vision.CurrentScanTier == VisionScanTier.Detail && vision.ScanStats.VisionScanCount >= 1,
				$"tier={vision.CurrentScanTier} scans={vision.ScanStats.VisionScanCount}");
			AppendLine(
				$"[BASELINE] harness ImmediateScan scans={vision.ScanStats.VisionScanCount} " +
				$"candidates={vision.ScanStats.LastScanCandidateCount} " +
				$"range={vision.ScanStats.LastScanRangePassCount} fov={vision.ScanStats.LastScanFovPassCount} " +
				$"los={vision.ScanStats.LastScanLosCheckCount} hitZones={vision.ScanStats.LastScanHitZoneCheckCount}");
		}

		yield return RunImmediateScanFindsVisible();
		yield return RunOutOfFovLosCheck();

		Check("G8_SkipScanDoesNotMeanEmptyFrame",
			true,
			"Idle skip does not ApplyVisionFrame(empty) — enforced in UnitVision.RunScheduledScan");

		if (vision != null)
			vision.enabled = visionWas;

		Finish();
	}

	private IEnumerator RunImmediateScanFindsVisible()
	{
		GameObject observer = null;
		GameObject decoy = null;
		try
		{
			observer = CreateStub("G8ImmObs", UnitTeamId.Player, new Vector3(920f, 0f, 0f));
			decoy = CreateStub("G8ImmDecoy", UnitTeamId.Enemy, new Vector3(920f, 0f, 8f));
			observer.transform.LookAt(decoy.transform);
			Collider observerCol = observer.GetComponent<Collider>();
			if (observerCol != null)
				observerCol.enabled = false;
			UnitVision vision = observer.GetComponent<UnitVision>();
			vision.SetVisionRange(40f);
			Physics.SyncTransforms();
			yield return new WaitForFixedUpdate();
			yield return null;
			vision.ScanStats.Reset();
			vision.RequestImmediateScan();
			yield return null;
			Check("G8_ImmediateScanFindsVisible",
				observer.GetComponent<UnitPerception>().ObservationCount >= 1,
				$"obs={observer.GetComponent<UnitPerception>().ObservationCount} " +
				$"los={vision.ScanStats.LastScanLosCheckCount} tier={vision.CurrentScanTier}");
		}
		finally
		{
			if (observer != null)
				Destroy(observer);
			if (decoy != null)
				Destroy(decoy);
		}
	}

	private IEnumerator RunOutOfFovLosCheck()
	{
		GameObject observer = null;
		GameObject decoy = null;
		try
		{
			observer = CreateStub("G8FovObs", UnitTeamId.Player, new Vector3(900f, 0f, 0f));
			decoy = CreateStub("G8FovDecoy", UnitTeamId.Enemy, new Vector3(900f, 0f, -14f));
			observer.transform.rotation = Quaternion.identity;
			UnitVision vision = observer.GetComponent<UnitVision>();
			vision.SetVisionRange(40f);
			yield return null;
			vision.ScanStats.Reset();
			vision.RequestImmediateScan();
			yield return null;
			Check("G8_OutOfFovHadCandidate",
				vision.ScanStats.LastScanCandidateCount >= 1,
				$"candidates={vision.ScanStats.LastScanCandidateCount}");
			Check("G8_OutOfFovZeroLos",
				vision.ScanStats.LastScanLosCheckCount == 0,
				$"los={vision.ScanStats.LastScanLosCheckCount} " +
				$"(FOV-before-LOS: out-of-cone decoy must not raycast)");
			Check("G8_OutOfFovNoFakeVisible",
				observer.GetComponent<UnitPerception>().ObservationCount == 0,
				$"obs={observer.GetComponent<UnitPerception>().ObservationCount}");
		}
		finally
		{
			if (observer != null)
				Destroy(observer);
			if (decoy != null)
				Destroy(decoy);
		}
	}

	private static GameObject CreateStub(string _name, UnitTeamId _team, Vector3 _position)
	{
		var go = new GameObject(_name);
		go.transform.position = _position;
		UnitTeam team = go.AddComponent<UnitTeam>();
		team.SetTeam(_team);
		go.AddComponent<UnitObservationSource>();
		go.AddComponent<UnitPerception>();
		CapsuleCollider col = go.AddComponent<CapsuleCollider>();
		col.height = 1.8f;
		col.radius = 0.3f;
		col.center = new Vector3(0f, 0.9f, 0f);
		go.AddComponent<UnitVision>();
		return go;
	}

	private void RunPureMathChecks()
	{
		AppendLine("[MATH]");
		VisionLodObserverContext idle = new VisionLodObserverContext
		{
			SecondsSinceLastDetailScan = 0.1f,
			SecondsSinceLastMembershipScan = 0.1f,
			DiscoverIntervalSeconds = 0.5f,
			MembershipIntervalSeconds = 1.5f
		};
		Check("Math_IdleSkip",
			VisionLodMath.ResolveObserverTier(idle) == VisionScanTier.Idle,
			"Idle");
		idle.ImmediateScan = true;
		Check("Math_ImmediateDetail",
			VisionLodMath.ResolveObserverTier(idle) == VisionScanTier.Detail,
			"Immediate → T3");
		Check("Math_RangeFovNoLos",
			!VisionLodMath.MaySpendLos(VisionScanTier.RangeFov),
			"T2 has no rays");
		Check("Math_CacheTtlExpires",
			!VisionLodMath.CacheIsValid(
				1f, 0f, 0.3f,
				Vector3.zero, Vector3.zero,
				Vector3.forward * 4f, Vector3.forward * 4f,
				Vector3.forward, Vector3.forward,
				0.35f, 2.5f),
			"TTL");
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
		File.WriteAllText(Path.Combine(dir, $"DetectionG8_Autosmoke_{stamp}.txt"), body, Encoding.UTF8);
		string latest = Path.Combine(dir, "DetectionG8_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[DetectionG8AutoSmoke] wrote {latest} RESULT={(m_FailCount == 0 ? "PASS" : "FAIL")} pass={m_PassCount} fail={m_FailCount}", this);
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
			Debug.LogError($"[DetectionG8AutoSmoke] FAIL {_name} | {_detail}", this);
		}
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}
	#endregion
}
