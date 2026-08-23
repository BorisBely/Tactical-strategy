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
/// Vision Stage 7: contact lifecycle + test optic envelopes. Does not retune Q or fill combat PSO/ACOG meters.
/// Writes Assets/_Docs/Logs/Tests/VisionContactLifecycle_LAST.txt
/// </summary>
[DefaultExecutionOrder(62)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class VisionContactLifecycleRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_WaitYes = 3.5f;
	private const float c_WaitDetect = 6.5f;
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
	private WeaponAttachmentDefinition m_TestOptic;
	private WeaponAttachmentDefinition m_TestCollimator;
	private WeaponAttachmentDefinition m_TestVariable;
	private WeaponAttachmentDefinition m_TestOpticModA;
	private WeaponAttachmentDefinition m_TestOpticModB;
	private GameObject m_ExtraTarget;
	private int m_ContactsChangedCount;
	private PerceivedContact m_TrackedContact;
	private EntityId m_TrackedTargetId;
	private EntityId m_VisionInstanceId;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunVisionContactLifecycle;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[VisionContactLifecycleRuntimeSmoke] contact lifecycle starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyExtraTarget();
		DestroyTestOptics();
		if (DetectionHarnessPlayMode.RunVisionContactLifecycle)
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
		m_PassCount = 0;
		m_FailCount = 0;
		m_TrackedContact = null;
		m_TrackedTargetId = default;
		Append("VISION CONTACT LIFECYCLE");
		Append("========================");
		Append("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("SEE ≠ KNOW ≠ SELECT ≠ ENGAGE ≠ AIM ≠ FIRE");
		Append("test optics: NoOptic 0 | Collimator 150 | 3x 200 | 6x 250 | LongRange 300");
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

		m_VisionInstanceId = vision.GetEntityId();
		EnsureTestOptics();
		PrepareCombatUnits(observer, target);
		ForceAimingReady(observer);
		vision.SetVisionRange(UnitVisionProfile.BaseRangeMeters);
		vision.DebugClearVisionOverrides();
		ResetWorld(vision);

		BeginGroup("P OPTIC ENVELOPE");
		yield return RunOpticEnvelope(vision, observer, target);

		BeginGroup("X EYE 100");
		yield return RunEye100(vision, observer, target);

		BeginGroup("A 250 OPTIC HOPS");
		yield return RunPipelineA(vision, observer, target);

		BeginGroup("HIPFIRE");
		yield return RunHipFireHold(vision, observer, target);

		BeginGroup("COL LONG→COLLIMATOR");
		yield return RunLongRangeToCollimator(vision, observer, target);

		BeginGroup("B LOS LOST");
		yield return RunLosLost(vision, observer, target);

		BeginGroup("C REACQUIRE");
		yield return RunReacquire(vision, observer, target);

		BeginGroup("G CIVILIAN");
		yield return RunCivilianLook(vision, observer, target);

		BeginGroup("H DUAL");
		yield return RunDualTarget(vision, observer, target);

		BeginGroup("D DEATH");
		yield return RunDeath(vision, observer, target);

		BeginGroup("E MEMORY");
		yield return RunMemoryExpiry(vision, observer, target);

		vision.DebugClearVisionOverrides();
		if (vision.ScopeScan != null)
			vision.ScopeScan.SetFrozenForTest(false);
		DestroyExtraTarget();
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();

		string status = m_FailCount == 0 ? "PASS" : "FAIL";
		Finish(status);
	}

	private IEnumerator RunOpticEnvelope(UnitVision _vision, Transform _observer, Transform _target)
	{
		Check("P_Clamp",
			Near(UnitVisionProfile.ClampScopeRange(0f), 150f) &&
			Near(UnitVisionProfile.ClampScopeRange(149f), 150f) &&
			Near(UnitVisionProfile.ClampScopeRange(300f), 300f) &&
			Near(UnitVisionProfile.ClampScopeRange(350f), 300f),
			"0/149→150 300 350→300");
		Check("P_Bonus",
			!UnitVisionProfile.HasMagnifiedScopeBonus(0f) &&
			!UnitVisionProfile.HasMagnifiedScopeBonus(150f) &&
			UnitVisionProfile.HasMagnifiedScopeBonus(300f),
			"0/150 none, 300 bonus");

		EntityId visionId = _vision.GetEntityId();
		ApplyOptic(_vision, false, 0f, WeaponPoseState.Aiming);
		yield return ProbeObservation("P_NoOptic200", _vision, _observer, _target, 200f, false);
		Check("P_NoOpticInactive",
			!_vision.CurrentVisionProfile.IsScopeActive &&
			Near(_vision.ResolvedMaxRange, UnitVisionProfile.BaseRangeMeters),
			_vision.FormatVisionProfileLog());

		ApplyOptic(_vision, true, 150f, WeaponPoseState.Aiming);
		yield return ProbeObservation("P_Collimator200", _vision, _observer, _target, 200f, false);
		Check("P_CollimatorNoBonus",
			!_vision.CurrentVisionProfile.IsScopeActive &&
			Near(_vision.ResolvedMaxRange, UnitVisionProfile.BaseRangeMeters),
			_vision.FormatVisionProfileLog());

		bool swapOk = true;
		string swapDetail = string.Empty;
		float[] ranges = { 200f, 250f, 300f };
		for (int i = 0; i < ranges.Length; i++)
		{
			float range = ranges[i];
			ApplyOptic(_vision, true, range, WeaponPoseState.Aiming);
			if (_vision.GetEntityId() != visionId)
			{
				swapOk = false;
				swapDetail = "UnitVision destroyed";
				break;
			}

			ResolvedVisionProfile profile = _vision.CurrentVisionProfile;
			float t = 210f / range;
			float d = DetectionQualityMath.DistanceFactor(210f, range);
			float hitscan = GetHitscanCap(_observer);
			Append(
				$"[SWAP] range={range:0} resolved={profile.MaxRangeMeters:0} active={profile.IsScopeActive} " +
				$"t210={t:F3} D={d:F3} hitscan={hitscan:F1} candCap={profile.MaxRangeMeters + 4f:0} " +
				_vision.FormatVisionProfileLog());
			if (!profile.IsScopeActive ||
			    !Near(profile.MaxRangeMeters, range) ||
			    hitscan > range + 0.05f)
			{
				swapOk = false;
				swapDetail = $"range={range:0} resolved={profile.MaxRangeMeters:0} hitscan={hitscan:F1}";
				break;
			}
		}

		Check("P_Swap200_250_300", swapOk && _vision.GetEntityId() == m_VisionInstanceId, swapDetail);
		Check("P_HitscanFollowsVision",
			GetHitscanCap(_observer) <= _vision.ResolvedMaxRange + 0.05f,
			$"cap={GetHitscanCap(_observer):F1} vision={_vision.ResolvedMaxRange:F1}");

		ApplyOptic(_vision, true, 200f, WeaponPoseState.Aiming);
		yield return ProbeObservation("P_3x199", _vision, _observer, _target, 199f, true);
		yield return ProbeObservation("P_3x210", _vision, _observer, _target, 210f, false);
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		yield return ProbeObservation("P_6x210", _vision, _observer, _target, 210f, true);

		float rawA = UnitVisionProfile.ReadRawScopeRange(new[] { m_TestOpticModA });
		float rawB = UnitVisionProfile.ReadRawScopeRange(new[] { m_TestOpticModB });
		Check("P_ModifierIsolation",
			Near(rawA, 250f) && Near(rawB, 250f) &&
			!Near(m_TestOpticModA.EffectiveRangeModifier, m_TestOpticModB.EffectiveRangeModifier),
			$"rawA={rawA:0} rawB={rawB:0} modA={m_TestOpticModA.EffectiveRangeModifier:F1} " +
			$"modB={m_TestOpticModB.EffectiveRangeModifier:F1}");

		m_TestVariable.ConfigureVariableMagnification(150f, 250f);
		m_TestVariable.SetVariableMagnificationActive(false);
		_vision.DebugSetVisionOpticOverride(m_TestVariable);
		_vision.DebugSetVisionPoseOverride(WeaponPoseState.Aiming, true);
		yield return ProbeObservation("P_Var1x200", _vision, _observer, _target, 200f, false);
		Check("P_Variable1xInactive",
			!_vision.CurrentVisionProfile.IsScopeActive &&
			Near(_vision.ResolvedMaxRange, UnitVisionProfile.BaseRangeMeters),
			_vision.FormatVisionProfileLog());
		m_TestVariable.SetVariableMagnificationActive(true);
		_vision.NotifyVisionProfileDirty();
		yield return ProbeObservation("P_Var6x200", _vision, _observer, _target, 200f, true);
		Check("P_Variable6xActive",
			_vision.CurrentVisionProfile.IsScopeActive &&
			Near(_vision.ResolvedMaxRange, 250f),
			_vision.FormatVisionProfileLog());
	}

	private IEnumerator RunEye100(UnitVision _vision, Transform _observer, Transform _target)
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
		PlaceOnVisionAxis(_vision, _observer, _target, 100f, 0f);
		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool eyeSeen = false;
		VisionObservationSource source = VisionObservationSource.Optic;
		float t0 = Time.time;
		while (Time.time - t0 < c_WaitYes)
		{
			_vision.RequestImmediateScan();
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

		_vision.ScanStats.Reset();
		t0 = Time.time;
		while (Time.time - t0 < 0.55f)
			yield return null;

		Check("X_Eye100NoScopeLosSpend",
			eyeSeen &&
			source == VisionObservationSource.Eye &&
			_vision.ScanStats.SkippedDuplicateCount > 0,
			$"src={source} skip={_vision.ScanStats.SkippedDuplicateCount} live={_vision.ScanStats.ScopeLiveLosCount}");
		FreezeScopeYaw(_vision, 0f);
	}

	private IEnumerator RunPipelineA(UnitVision _vision, Transform _observer, Transform _target)
	{
		ForceAimingReady(_observer);
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		if (_observer.TryGetComponent(out UnitWeaponRuntime weaponRuntime))
			weaponRuntime.SetAimProgress(0f);

		Append(_vision.FormatVisionProfileLog());
		Check("A_ProfileLog",
			_vision.CurrentVisionProfile.IsScopeActive &&
			Near(_vision.ResolvedMaxRange, 250f),
			_vision.FormatVisionProfileLog());

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		if (selector != null)
			selector.ClearSelection(true);

		bool sawCandidate = false;
		bool sawObservation = false;
		bool firstObsWasDetected = true;
		bool sawDetecting = false;
		bool sawDetected = false;
		bool sawSelected = false;
		bool sawEngageable = false;
		float tObs = -1f;
		float tDetecting = -1f;
		float tDetected = -1f;
		float tSelected = -1f;
		float tEngageable = -1f;
		VisionObservationSource obsSource = VisionObservationSource.Eye;
		float firstProgress = -1f;

		_vision.RequestImmediateScan();
		float t0 = Time.time;
		float nextPulse = t0;
		while (Time.time - t0 < c_WaitDetect)
		{
			if (Time.time >= nextPulse)
			{
				_vision.RequestImmediateScan();
				nextPulse = Time.time + c_Pulse;
			}

			if (_vision.ScanStats.LastScanCandidateCount > 0)
				sawCandidate = true;

			VisionObservation obs = default;
			bool visible = perception != null &&
				perception.TryGetObservation(_target, out obs) &&
				obs.IsVisible;
			processor.TryGetContact(_target, out PerceivedContact contact);
			if (selector != null)
				selector.SelectFromContacts();

			if (visible && !sawObservation)
			{
				sawObservation = true;
				tObs = Time.time - t0;
				obsSource = obs.Source;
				firstProgress = contact != null ? contact.DetectionProgress : 0f;
				firstObsWasDetected = contact != null && contact.State == DetectionState.Detected;
				sawDetecting = contact != null &&
					(contact.State == DetectionState.Detecting || contact.DetectionProgress > 0f) &&
					contact.State != DetectionState.Detected;
				if (sawDetecting)
					tDetecting = tObs;
				Append(
					$"[HOP] Observation t={tObs:F3}s src={obsSource} E={obs.Exposure01:F2} " +
					$"state={(contact != null ? contact.State.ToString() : "none")} prog={firstProgress:F3}");
			}

			if (contact != null && contact.State == DetectionState.Detecting && tDetecting < 0f)
			{
				sawDetecting = true;
				tDetecting = Time.time - t0;
				Append($"[HOP] Detecting t={tDetecting:F3}s prog={contact.DetectionProgress:F3}");
			}

			if (contact != null && contact.State == DetectionState.Detected && !sawDetected)
			{
				sawDetected = true;
				tDetected = Time.time - t0;
				m_TrackedContact = contact;
				m_TrackedTargetId = _target.GetEntityId();
				Append($"[HOP] Detected t={tDetected:F3}s know={contact.HasKnowledge}");
			}

			if (selector != null && selector.SelectedTarget == _target && !sawSelected)
			{
				sawSelected = true;
				tSelected = Time.time - t0;
				Append($"[HOP] Selected t={tSelected:F3}s");
			}

			if (selector != null && selector.GetEngageableSelectedTarget() == _target && !sawEngageable)
			{
				sawEngageable = true;
				tEngageable = Time.time - t0;
				Append($"[HOP] Engageable t={tEngageable:F3}s aim={selector.HasSelectedAimPoint}");
			}

			if (sawDetected && sawSelected && sawEngageable)
				break;
			yield return null;
		}

		Check("A_CandidateThenObservation",
			sawCandidate && sawObservation && obsSource == VisionObservationSource.Optic,
			$"cand={sawCandidate} obs={sawObservation} src={obsSource} tObs={tObs:F3}");
		Check("A_ObservationNotDetectedSameFrame",
			sawObservation && !firstObsWasDetected && firstProgress < 1f,
			$"detectedSame={firstObsWasDetected} prog={firstProgress:F3}");
		Check("A_DetectingThenDetected",
			sawDetecting && sawDetected && tDetected > tObs,
			$"tDet={tDetecting:F3} tDetected={tDetected:F3}");
		Check("A_SelectedBeforeEngageable",
			sawSelected && sawEngageable && tSelected <= tEngageable + 0.0001f,
			$"tSel={tSelected:F3} tEng={tEngageable:F3}");

		yield return WaitPoseReady(_observer);
		float aim0 = ReadAimProgress(_observer);
		float aimWaitT0 = Time.time;
		float aim1 = aim0;
		while (Time.time - aimWaitT0 < 1.4f)
		{
			ForceAimingReady(_observer);
			aim1 = ReadAimProgress(_observer);
			if (aim1 > aim0 + 0.02f || aim1 > 0.05f)
				break;
			yield return null;
		}

		Append($"[HOP] AimProgress {aim0:F3} → {aim1:F3} pose={ReadPose(_observer)}");
		Check("A_AimProgressGrows",
			aim1 > aim0 + 0.02f || aim1 > 0.05f,
			$"aim0={aim0:F3} aim1={aim1:F3} pose={ReadPose(_observer)}");
	}

	private IEnumerator RunHipFireHold(UnitVision _vision, Transform _observer, Transform _target)
	{
		DetectionProcessor processor = m_Harness.DetectionProcessor;
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		ForceAimingReady(_observer);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return WaitDetected(_vision, _observer, _target, c_WaitDetect);
		if (selector != null)
			selector.SelectFromContacts();
		EntityId targetId = _target.GetEntityId();

		ApplyOptic(_vision, true, 250f, WeaponPoseState.HipFire);
		yield return new WaitForSeconds(0.55f);
		_vision.RequestImmediateScan();
		yield return null;
		if (selector != null)
			selector.SelectFromContacts();

		bool hasObs = HasVisibleObservation(_observer, _target);
		processor.TryGetContact(_target, out PerceivedContact contact);
		Check("Hip_OpticGoneKnowledgeHeld",
			!hasObs &&
			contact != null &&
			contact.HasKnowledge &&
			selector != null &&
			selector.SelectedTarget == _target &&
			selector.GetEngageableSelectedTarget() == null,
			FormatProbe(processor, selector, _target, hasObs));

		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		ForceAimingReady(_observer);
		FreezeScopeYaw(_vision, 0f);
		float t0 = Time.time;
		bool restored = false;
		while (Time.time - t0 < c_WaitYes)
		{
			_vision.RequestImmediateScan();
			if (selector != null)
				selector.SelectFromContacts();
			if (selector != null &&
			    selector.GetEngageableSelectedTarget() == _target &&
			    _target.GetEntityId() == targetId)
			{
				restored = true;
				break;
			}

			yield return null;
		}

		Check("Hip_RestoreSameTarget",
			restored && _target.GetEntityId() == targetId && _target.GetEntityId() == m_TrackedTargetId,
			$"id={_target.GetEntityId()} tracked={m_TrackedTargetId}");
	}

	private IEnumerator RunLongRangeToCollimator(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 300f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return WaitDetected(_vision, _observer, _target, c_WaitDetect);

		ApplyOptic(_vision, true, 150f, WeaponPoseState.Aiming);
		yield return new WaitForSeconds(0.45f);
		_vision.RequestImmediateScan();
		yield return null;

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		if (selector != null)
			selector.SelectFromContacts();
		bool hasObs = HasVisibleObservation(_observer, _target);
		processor.TryGetContact(_target, out PerceivedContact contact);
		Check("Col_250CollimatorDropsOpticKeepsKnowledge",
			!hasObs &&
			contact != null &&
			contact.HasKnowledge &&
			selector != null &&
			selector.GetEngageableSelectedTarget() == null,
			FormatProbe(processor, selector, _target, hasObs));

		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		yield return WaitDetected(_vision, _observer, _target, c_WaitDetect);
	}

	private IEnumerator RunLosLost(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return WaitDetected(_vision, _observer, _target, c_WaitDetect);

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		if (selector != null)
			selector.SelectFromContacts();
		processor.TryGetContact(_target, out m_TrackedContact);

		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.ApplyZeroExposureHide(_observer, _target);
		Physics.SyncTransforms();
		yield return new WaitForSeconds(0.45f);
		_vision.RequestImmediateScan();
		yield return null;
		if (selector != null)
			selector.SelectFromContacts();

		float aimWait = Time.time;
		while (Time.time - aimWait < 0.55f)
			yield return null;

		bool hasObs = HasVisibleObservation(_observer, _target);
		processor.TryGetContact(_target, out PerceivedContact contact);
		float aim = ReadAimProgress(_observer);
		Check("B_LosLostKeepsSelected",
			!hasObs &&
			contact != null &&
			contact.HasKnowledge &&
			contact.ObservationState == ObservationState.RecentlyLost &&
			selector != null &&
			selector.SelectedTarget == _target &&
			selector.GetEngageableSelectedTarget() == null &&
			aim < 0.08f,
			FormatProbe(processor, selector, _target, hasObs) + $" aim={aim:F3} obsSt={contact.ObservationState}");
		Check("F_AimProgressClearsWhileSelectedHeld",
			selector != null && selector.SelectedTarget == _target && aim < 0.08f,
			$"sel={selector != null && selector.SelectedTarget == _target} aim={aim:F3}");
	}

	private IEnumerator RunReacquire(UnitVision _vision, Transform _observer, Transform _target)
	{
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		Physics.SyncTransforms();
		ForceAimingReady(_observer);
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		PerceivedContact before = m_TrackedContact;
		float aim0 = ReadAimProgress(_observer);
		float t0 = Time.time;
		bool sameContact = false;
		bool engageable = false;
		while (Time.time - t0 < c_WaitDetect)
		{
			_vision.RequestImmediateScan();
			if (selector != null)
				selector.SelectFromContacts();
			processor.TryGetContact(_target, out PerceivedContact contact);
			sameContact = before != null && ReferenceEquals(before, contact);
			engageable = selector != null && selector.GetEngageableSelectedTarget() == _target;
			if (engageable && sameContact)
				break;
			yield return null;
		}

		float aimWait = Time.time;
		while (Time.time - aimWait < 0.7f)
			yield return null;
		float aim1 = ReadAimProgress(_observer);
		Check("C_SameContactReengageable",
			sameContact &&
			_target.GetEntityId() == m_TrackedTargetId &&
			engageable,
			$"same={sameContact} id={_target.GetEntityId()} eng={engageable} aim {aim0:F3}→{aim1:F3}");
	}

	private IEnumerator RunCivilianLook(UnitVision _vision, Transform _observer, Transform _target)
	{
		VisualAffiliation previous = VisualAffiliation.Unknown;
		VisualIdentityEvidence look = VisualIdentityEvidence.GetOrCreate(_target.gameObject);
		if (look != null)
		{
			previous = look.PrimaryAffiliation;
			look.SetPrimaryAffiliation(VisualAffiliation.Civilian);
		}

		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		ApplyOptic(_vision, false, 0f, WeaponPoseState.HipFire);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 100f, 0f);
		yield return WaitDetected(_vision, _observer, _target, c_WaitYes);

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		bool observed = HasVisibleObservation(_observer, _target);
		processor.TryGetContact(_target, out PerceivedContact contact);
		Check("G_CivilianObserved",
			observed && contact != null && contact.HasKnowledge,
			$"obs={observed} look={look != null && look.PrimaryAffiliation == VisualAffiliation.Civilian}");

		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.ApplyZeroExposureHide(_observer, _target);
		yield return new WaitForSeconds(0.45f);
		_vision.RequestImmediateScan();
		yield return null;
		processor.TryGetContact(_target, out contact);
		Check("G_CivilianRecentlyLost",
			!HasVisibleObservation(_observer, _target) &&
			contact != null &&
			contact.ObservationState == ObservationState.RecentlyLost &&
			contact.HasKnowledge,
			contact != null ? contact.ObservationState.ToString() : "null");

		if (look != null)
			look.SetPrimaryAffiliation(previous);
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
	}

	private IEnumerator RunDualTarget(UnitVision _vision, Transform _observer, Transform _target)
	{
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);

		m_ExtraTarget = SpawnOpposingInfantry(_observer, _target);
		if (m_ExtraTarget != null)
		{
			DetectionTestController.DisableLethalFire(m_ExtraTarget.transform);
			if (m_ExtraTarget.TryGetComponent(out TargetSelector extraSelector))
				extraSelector.enabled = false;
			DisableLocomotion(m_ExtraTarget.transform);
			DetectionTestController.SnapCalibrationPose(m_ExtraTarget.transform);
			PlaceRelative(_vision, _observer, m_ExtraTarget.transform, 248f, 0f, true);
		}

		yield return null;
		Physics.SyncTransforms();
		float t0 = Time.time;
		DetectionProcessor processor = m_Harness.DetectionProcessor;
		bool sawPrimary = false;
		bool sawExtra = false;
		while (Time.time - t0 < c_WaitDetect)
		{
			_vision.RequestImmediateScan();
			sawPrimary = processor != null && processor.TryGetContact(_target, out _);
			sawExtra = m_ExtraTarget != null &&
				processor != null &&
				processor.TryGetContact(m_ExtraTarget.transform, out _);
			if (sawPrimary && sawExtra)
				break;
			yield return null;
		}

		Check("H_DualContacts",
			sawPrimary && sawExtra,
			$"primary={sawPrimary} extra={sawExtra} spawned={m_ExtraTarget != null}");
		DestroyExtraTarget();
	}

	private IEnumerator RunDeath(UnitVision _vision, Transform _observer, Transform _target)
	{
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		ForceAimingReady(_observer);
		ApplyOptic(_vision, true, 250f, WeaponPoseState.Aiming);
		FreezeScopeYaw(_vision, 0f);
		PlaceOnVisionAxis(_vision, _observer, _target, 250f, 0f);
		yield return WaitDetected(_vision, _observer, _target, c_WaitDetect);

		if (_target.TryGetComponent(out UnitConsciousness consciousness))
			consciousness.EnterUnconscious();
		if (_target.TryGetComponent(out UnitHealth health))
			health.EnterDead();

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		_vision.RequestImmediateScan();
		yield return null;
		if (processor != null)
			processor.ApplyEmptyObservationFrame();
		yield return null;
		yield return null;

		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		if (selector != null)
			selector.SelectFromContacts();
		yield return null;

		float aimWait = Time.time;
		while (Time.time - aimWait < 0.4f)
			yield return null;

		bool hasObs = HasVisibleObservation(_observer, _target);
		processor.TryGetContact(_target, out PerceivedContact contact);
		float aim = ReadAimProgress(_observer);
		bool recentlyLost = contact != null && contact.ObservationState == ObservationState.RecentlyLost;
		Check("D_DeadClearsSelection",
			!hasObs &&
			selector != null &&
			selector.SelectedTarget == null &&
			selector.GetEngageableSelectedTarget() == null &&
			aim < 0.08f &&
			!TargetEngageability.IsEngageable(_target),
			FormatProbe(processor, selector, _target, hasObs) + $" aim={aim:F3}");
		Check("D_DeadIsNotRecentlyLost",
			!recentlyLost,
			contact != null ? contact.ObservationState.ToString() : "contact-null");

		DetectionTestController.RestoreFixtureActor(_target);
		DetectionTestController.RestoreFixtureActor(_observer);
		PrepareCombatUnits(_observer, _target);
		ForceAimingReady(_observer);
	}

	private IEnumerator RunMemoryExpiry(UnitVision _vision, Transform _observer, Transform _target)
	{
		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
		ApplyOptic(_vision, false, 0f, WeaponPoseState.HipFire);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, 100f, 0f);
		yield return WaitDetected(_vision, _observer, _target, c_WaitYes);

		DetectionProcessor processor = m_Harness.DetectionProcessor;
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		m_ContactsChangedCount = 0;
		if (processor != null)
			processor.ContactsChanged += OnContactsChanged;

		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.ApplyZeroExposureHide(_observer, _target);
		float horizon = processor != null ? processor.MemoryHorizonSeconds : 30f;
		yield return new WaitForSeconds(horizon + 0.4f);
		if (processor != null)
			processor.ApplyEmptyObservationFrame();
		yield return null;
		if (selector != null)
			selector.SelectFromContacts();

		if (processor != null)
			processor.ContactsChanged -= OnContactsChanged;

		processor.TryGetContact(_target, out PerceivedContact contact);
		bool hasKnowledge = contact != null && contact.HasKnowledge;
		Check("E_MemoryExpiry",
			!hasKnowledge &&
			(selector == null || selector.SelectedTarget == null) &&
			m_ContactsChangedCount > 0,
			$"know={hasKnowledge} sel={(selector != null && selector.SelectedTarget != null)} changed={m_ContactsChangedCount}");

		if (m_Harness.ExposureStaging != null)
			m_Harness.ExposureStaging.Clear();
	}
	#endregion

	#region Helpers
	private void OnContactsChanged()
	{
		m_ContactsChangedCount++;
	}

	private IEnumerator ProbeObservation(
		string _id,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		bool _expectObs)
	{
		FreezeScopeYaw(_vision, 0f);
		ResetWorld(_vision);
		PlaceOnVisionAxis(_vision, _observer, _target, _distance, 0f);
		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		bool lastSeen = false;
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

			lastSeen = perception != null &&
				perception.TryGetObservation(_target, out VisionObservation obs) &&
				obs.IsVisible;
			if (_expectObs && lastSeen)
				break;
			yield return null;
		}

		Check(_id, lastSeen == _expectObs,
			$"want={_distance:0} placed={MeasureHorizontalDistance(_vision, _target):0.0} " +
			$"seen={lastSeen} expect={_expectObs} " +
			$"scope={_vision.CurrentVisionProfile.IsScopeActive} max={_vision.ResolvedMaxRange:0}");
	}

	private IEnumerator WaitDetected(
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _timeout)
	{
		DetectionProcessor processor = m_Harness != null ? m_Harness.DetectionProcessor : null;
		TargetSelector selector = _observer.GetComponent<TargetSelector>();
		float t0 = Time.time;
		while (Time.time - t0 < _timeout)
		{
			_vision.RequestImmediateScan();
			if (selector != null)
				selector.SelectFromContacts();
			if (processor != null &&
			    processor.TryGetContact(_target, out PerceivedContact contact) &&
			    contact != null &&
			    contact.State == DetectionState.Detected)
			{
				m_TrackedContact = contact;
				m_TrackedTargetId = _target.GetEntityId();
				yield break;
			}

			yield return null;
		}
	}

	private void EnsureTestOptics()
	{
		m_TestOptic = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestOptic.name = "Attachment_TestScope_Lifecycle";
		m_TestOptic.SetScopeVisionRangeMeters(300f);
		m_TestCollimator = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestCollimator.name = "Attachment_TestCollimator_Lifecycle";
		m_TestCollimator.SetScopeVisionRangeMeters(150f);
		m_TestVariable = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestVariable.name = "Attachment_TestVariable_Lifecycle";
		m_TestVariable.ConfigureVariableMagnification(150f, 250f);
		m_TestOpticModA = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestOpticModA.name = "Attachment_TestScope_Mod10";
		m_TestOpticModA.SetScopeVisionRangeMeters(250f);
		m_TestOpticModA.SetEffectiveRangeModifier(1f);
		m_TestOpticModB = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_TestOpticModB.name = "Attachment_TestScope_Mod16";
		m_TestOpticModB.SetScopeVisionRangeMeters(250f);
		m_TestOpticModB.SetEffectiveRangeModifier(1.6f);
	}

	private void DestroyTestOptics()
	{
		DestroyIfNotNull(m_TestOptic);
		DestroyIfNotNull(m_TestCollimator);
		DestroyIfNotNull(m_TestVariable);
		DestroyIfNotNull(m_TestOpticModA);
		DestroyIfNotNull(m_TestOpticModB);
		m_TestOptic = null;
		m_TestCollimator = null;
		m_TestVariable = null;
		m_TestOpticModA = null;
		m_TestOpticModB = null;
	}

	private static void DestroyIfNotNull(ScriptableObject _asset)
	{
		if (_asset != null)
			Destroy(_asset);
	}

	private void ApplyOptic(UnitVision _vision, bool _useOptic, float _range, WeaponPoseState _pose)
	{
		if (!_useOptic)
		{
			_vision.DebugClearVisionOverrides();
			_vision.DebugSetVisionPoseOverride(_pose, true);
			return;
		}

		WeaponAttachmentDefinition optic = _range > 150.01f ? m_TestOptic : m_TestCollimator;
		if (optic == null)
			EnsureTestOptics();
		optic = _range > 150.01f ? m_TestOptic : m_TestCollimator;
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
			PrepareCombatUnits(m_Harness.Observer, m_Harness.Target);
			_vision.RequestImmediateScan();
		}

		m_TrackedContact = null;
	}

	private static void PrepareCombatUnits(Transform _observer, Transform _target)
	{
		DetectionTestController.DisableLethalFire(_observer);
		DetectionTestController.DisableLethalFire(_target);
		DetectionTestController.EnablePerceptionCombat(_observer);
		DetectionTestController.SnapCalibrationPose(_observer);
		DetectionTestController.SnapCalibrationPose(_target);
		DisableLocomotion(_observer);
		DisableLocomotion(_target);
	}

	private static void ForceAimingReady(Transform _observer)
	{
		if (_observer == null)
			return;
		if (_observer.TryGetComponent(out UnitWeaponReadyHandsLayer ready))
			ready.SetPoseModeWanted(WeaponPoseMode.Aiming, true);
		if (_observer.TryGetComponent(out UnitWeaponAimProgressController aim))
			aim.enabled = true;
	}

	private static IEnumerator WaitPoseReady(Transform _observer)
	{
		float t0 = Time.time;
		while (Time.time - t0 < 1.2f)
		{
			ForceAimingReady(_observer);
			if (_observer != null &&
			    _observer.TryGetComponent(out UnitWeaponReadyHandsLayer ready) &&
			    ready.EffectivePoseState.CanAccumulateAimFromPose())
				yield break;
			yield return null;
		}
	}

	private static string ReadPose(Transform _observer)
	{
		if (_observer != null && _observer.TryGetComponent(out UnitWeaponReadyHandsLayer ready))
			return ready.EffectivePoseState.ToString();
		return "none";
	}

	private static float MeasureHorizontalDistance(UnitVision _vision, Transform _target)
	{
		if (_vision == null || _target == null)
			return -1f;
		return Mathf.Sqrt(VisionGeometry.HorizontalDistanceSq(
			_vision.GetGameplayVisionOriginWorld(), _target.position));
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

	private GameObject SpawnOpposingInfantry(Transform _observer, Transform _target)
	{
		UnitSceneSpawner spawner = FindAnyObjectByType<UnitSceneSpawner>();
		if (spawner == null)
			return null;

		GameObject extra = spawner.SpawnAdditionalPlayer("LifecycleExtraTarget");
		if (extra == null)
			return null;

		UnitTeamId team = UnitTeamId.Enemy;
		if (_target != null && _target.TryGetComponent(out UnitTeam targetTeam))
			team = targetTeam.Team;
		else if (_observer != null && _observer.TryGetComponent(out UnitTeam observerTeam) &&
		         observerTeam.Team == UnitTeamId.Enemy)
			team = UnitTeamId.Player;

		if (extra.TryGetComponent(out UnitTeam extraTeam))
			extraTeam.SetTeam(team);
		if (extra.TryGetComponent(out UnitVision extraVision))
		{
			UnitVisionRegistry registry = FindAnyObjectByType<UnitVisionRegistry>();
			if (registry != null)
				registry.Register(extraVision);
		}

		return extra;
	}

	private void DestroyExtraTarget()
	{
		if (m_ExtraTarget != null)
		{
			Destroy(m_ExtraTarget);
			m_ExtraTarget = null;
		}
	}

	private static bool HasVisibleObservation(Transform _observer, Transform _target)
	{
		if (_observer == null || _target == null)
			return false;
		UnitPerception perception = _observer.GetComponent<UnitPerception>();
		return perception != null &&
			perception.TryGetObservation(_target, out VisionObservation obs) &&
			obs.IsVisible;
	}

	private static float ReadAimProgress(Transform _observer)
	{
		if (_observer != null && _observer.TryGetComponent(out UnitWeaponRuntime runtime))
			return runtime.TransientState.AimProgress01;
		return 0f;
	}

	private static float GetHitscanCap(Transform _observer)
	{
		if (_observer != null && _observer.TryGetComponent(out UnitWeaponHitscanShooting hitscan))
			return hitscan.GetCappedMaxDistance();
		return -1f;
	}

	private static string FormatProbe(
		DetectionProcessor _processor,
		TargetSelector _selector,
		Transform _target,
		bool _hasObs)
	{
		return DetectionHarnessPlayMode.FormatSelectorProbe(_processor, _selector, _target) +
			" vis=" + _hasObs;
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		string line = (_ok ? "PASS " : "FAIL ") + _name + " | " + _detail;
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append(line);
		Debug.Log("[VisionContactLifecycle] " + line + $"  (pass={m_PassCount} fail={m_FailCount})", this);
	}

	private void BeginGroup(string _title)
	{
		Append("");
		Append(_title);
		Debug.Log(
			$"[VisionContactLifecycleRuntimeSmoke] {_title} t={Time.time:F1} pass={m_PassCount} fail={m_FailCount}",
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
		string latest = Path.Combine(dir, "VisionContactLifecycle_LAST.txt");
		File.WriteAllText(latest, body, Encoding.UTF8);
		Debug.Log($"[VisionContactLifecycleRuntimeSmoke] wrote {latest}\n{body}", this);

#if UNITY_EDITOR
		if (m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunVisionContactLifecycle)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
