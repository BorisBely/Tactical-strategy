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
/// Vision Stage 8: real combat optic ScopeVisionRange contract.
/// Does not retune Q, damage, or EffectiveRange of weapons.
/// Writes Assets/_Docs/Logs/Tests/OpticRangeContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(63)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class VisionOpticRangeContractRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_WaitYes = 3.5f;
	private const float c_WaitNo = 1.6f;
	private const float c_Pulse = 0.2f;
	private const string c_VortexPath = "Assets/GameData/Shooting/M4/Attachment_M4_Vortex_Razor.asset";
	private const string c_G33Path = "Assets/GameData/Shooting/M4/Attachment_M4_EOTech_G33.asset";
	private const string c_AcogPath = "Assets/GameData/Shooting/M4/Attachment_M4_ACOG.asset";
	private const string c_ReddotPath = "Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset";
	private const string c_Scope9Path = "Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset";
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
	private WeaponAttachmentDefinition m_Vortex;
	private WeaponAttachmentDefinition m_G33;
	private WeaponAttachmentDefinition m_Acog;
	private WeaponAttachmentDefinition m_Reddot;
	private WeaponAttachmentDefinition m_Scope9;
	private WeaponAttachmentDefinition m_ModA;
	private WeaponAttachmentDefinition m_ModB;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunVisionOpticRangeContract;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[VisionOpticRangeContractRuntimeSmoke] optic range contract starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyClones();
		if (DetectionHarnessPlayMode.RunVisionOpticRangeContract)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		m_PassCount = 0;
		m_FailCount = 0;
		m_Report.Length = 0;
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		m_Harness = GetComponent<DetectionTestController>();
		Append("Vision Stage 8 OpticRangeContract");
		Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("---");

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
		if (!LoadCombatOptics())
		{
			Finish("FAIL");
			yield break;
		}

		vision.DebugClearVisionOverrides();
		PrepareUnits(observer, target);
		ResetWorld(vision);
		FreezeScopeYaw(vision, 0f);
		ForceAimingReady(observer);
		yield return WaitPoseReady(observer);

		BeginGroup("A CATALOG");
		Check("A_VortexHigh", Near(m_Vortex.ScopeVisionRangeMeters, 250f) && m_Vortex.HasVariableMagnification,
			$"high={m_Vortex.ScopeVisionRangeMeters:0} var={m_Vortex.HasVariableMagnification}");
		Check("A_G33High", Near(m_G33.ScopeVisionRangeMeters, 200f) && m_G33.HasVariableMagnification,
			$"high={m_G33.ScopeVisionRangeMeters:0}");
		Check("A_Acog", Near(m_Acog.ScopeVisionRangeMeters, 220f) && !m_Acog.HasVariableMagnification,
			$"range={m_Acog.ScopeVisionRangeMeters:0}");
		Check("A_Reddot150", Near(m_Reddot.ScopeVisionRangeMeters, 150f),
			$"range={m_Reddot.ScopeVisionRangeMeters:0}");
		Check("A_Scope9_300", Near(m_Scope9.ScopeVisionRangeMeters, 300f),
			$"range={m_Scope9.ScopeVisionRangeMeters:0}");

		BeginGroup("B 1X NO BONUS");
		ApplyCombatOptic(vision, m_Reddot, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("B_Reddot175", vision, observer, target, 175f, false);
		ApplyCombatOptic(vision, m_Vortex, WeaponPoseState.Aiming, false);
		yield return ProbeObservation("B_Vortex1x175", vision, observer, target, 175f, false);
		Check("B_Vortex1xInactive",
			!vision.CurrentVisionProfile.IsScopeActive &&
			Near(vision.ResolvedMaxRange, UnitVisionProfile.BaseRangeMeters),
			vision.FormatVisionProfileLog());

		BeginGroup("C VARIABLE HIGH");
		ApplyCombatOptic(vision, m_Vortex, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("C_Vortex6x175", vision, observer, target, 175f, true);
		Check("C_Vortex6xActive",
			vision.CurrentVisionProfile.IsScopeActive &&
			Near(vision.ResolvedMaxRange, 250f),
			vision.FormatVisionProfileLog());
		yield return ProbeObservation("C_Vortex6x251", vision, observer, target, 251f, false);

		ApplyCombatOptic(vision, m_G33, WeaponPoseState.Aiming, false);
		yield return ProbeObservation("C_G33_1x175", vision, observer, target, 175f, false);
		ApplyCombatOptic(vision, m_G33, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("C_G33_3x175", vision, observer, target, 175f, true);

		BeginGroup("D CLASS RANGES");
		ApplyCombatOptic(vision, m_Acog, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("D_Acog219", vision, observer, target, 219f, true);
		yield return ProbeObservation("D_Acog221", vision, observer, target, 221f, false);
		ApplyCombatOptic(vision, m_Scope9, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("D_Scope9_299", vision, observer, target, 299f, true);
		yield return ProbeObservation("D_Scope9_301", vision, observer, target, 301f, false);

		BeginGroup("E MODIFIER ISOLATION");
		float rawA = UnitVisionProfile.ReadRawScopeRange(new[] { m_ModA });
		float rawB = UnitVisionProfile.ReadRawScopeRange(new[] { m_ModB });
		Check("E_SameVisionRange",
			Near(rawA, 250f) && Near(rawB, 250f) &&
			!Near(m_ModA.EffectiveRangeModifier, m_ModB.EffectiveRangeModifier),
			$"rawA={rawA:0} rawB={rawB:0} modA={m_ModA.EffectiveRangeModifier:F1} " +
			$"modB={m_ModB.EffectiveRangeModifier:F1}");

		ApplyCombatOptic(vision, m_ModA, WeaponPoseState.Aiming, true);
		float visionA = vision.ResolvedMaxRange;
		float hitscanA = GetHitscanCap(observer);
		ApplyCombatOptic(vision, m_ModB, WeaponPoseState.Aiming, true);
		float visionB = vision.ResolvedMaxRange;
		float hitscanB = GetHitscanCap(observer);
		Check("E_VisionIgnoresModifier",
			Near(visionA, 250f) && Near(visionB, 250f),
			$"visionA={visionA:0} visionB={visionB:0}");
		Check("E_HitscanIgnoresModifier",
			Near(hitscanA, hitscanB) && hitscanA <= 250.05f,
			$"hitscanA={hitscanA:F1} hitscanB={hitscanB:F1}");

		BeginGroup("F HITSCAN CAP");
		ApplyCombatOptic(vision, m_Vortex, WeaponPoseState.Aiming, true);
		ResetWorld(vision);
		PlaceOnVisionAxis(vision, observer, target, 251f, 0f);
		yield return new WaitForSeconds(0.35f);
		float cap = GetHitscanCap(observer);
		Check("F_HitscanFollowsVision",
			cap <= vision.ResolvedMaxRange + 0.05f && Near(vision.ResolvedMaxRange, 250f),
			$"cap={cap:F1} vision={vision.ResolvedMaxRange:F1}");
		if (observer.TryGetComponent(out UnitWeaponFireController fire))
		{
			WeaponShotAttemptResult result = fire.TryFireSingleShot();
			Check("F_NoShotAt251", result != WeaponShotAttemptResult.Success, result.ToString());
		}
		else
			Check("F_NoShotAt251", false, "UnitWeaponFireController missing");

		vision.DebugClearVisionOverrides();
		DestroyClones();
		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
	}

	private bool LoadCombatOptics()
	{
#if UNITY_EDITOR
		m_Vortex = CloneAsset(c_VortexPath, "Vortex");
		m_G33 = CloneAsset(c_G33Path, "G33");
		m_Acog = CloneAsset(c_AcogPath, "ACOG");
		m_Reddot = CloneAsset(c_ReddotPath, "Reddot");
		m_Scope9 = CloneAsset(c_Scope9Path, "Scope9");
		if (m_Vortex == null || m_G33 == null || m_Acog == null || m_Reddot == null || m_Scope9 == null)
			return false;

		m_ModA = Instantiate(m_Vortex);
		m_ModA.name = "OpticRange_Mod10";
		m_ModA.SetEffectiveRangeModifier(1f);
		m_ModA.SetVariableMagnificationActive(true);
		m_ModB = Instantiate(m_Vortex);
		m_ModB.name = "OpticRange_Mod15";
		m_ModB.SetEffectiveRangeModifier(1.5f);
		m_ModB.SetVariableMagnificationActive(true);
		return true;
#else
		Check("LoadCombatOptics", false, "Editor-only asset load");
		return false;
#endif
	}

#if UNITY_EDITOR
	private WeaponAttachmentDefinition CloneAsset(string _path, string _label)
	{
		WeaponAttachmentDefinition source =
			AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_path);
		Check("Load_" + _label, source != null, _path);
		if (source == null)
			return null;
		WeaponAttachmentDefinition clone = Instantiate(source);
		clone.name = source.name + "_OpticRangeClone";
		return clone;
	}
#endif

	private void DestroyClones()
	{
		DestroyIfNotNull(m_Vortex);
		DestroyIfNotNull(m_G33);
		DestroyIfNotNull(m_Acog);
		DestroyIfNotNull(m_Reddot);
		DestroyIfNotNull(m_Scope9);
		DestroyIfNotNull(m_ModA);
		DestroyIfNotNull(m_ModB);
		m_Vortex = null;
		m_G33 = null;
		m_Acog = null;
		m_Reddot = null;
		m_Scope9 = null;
		m_ModA = null;
		m_ModB = null;
	}

	private static void DestroyIfNotNull(ScriptableObject _asset)
	{
		if (_asset != null)
			Destroy(_asset);
	}

	private void ApplyCombatOptic(
		UnitVision _vision,
		WeaponAttachmentDefinition _optic,
		WeaponPoseState _pose,
		bool _highMagnification)
	{
		if (_optic != null && _optic.HasVariableMagnification)
			_optic.SetVariableMagnificationActive(_highMagnification);
		_vision.DebugSetVisionOpticOverride(_optic);
		_vision.DebugSetVisionPoseOverride(_pose, true);
		_vision.NotifyVisionProfileDirty();
		FreezeScopeYaw(_vision, 0f);
	}

	private IEnumerator ProbeObservation(
		string _id,
		UnitVision _vision,
		Transform _observer,
		Transform _target,
		float _distance,
		bool _expectObs)
	{
		ForceAimingReady(_observer);
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
		DetectionTestController.DisableLethalFire(_observer);
		DetectionTestController.DisableLethalFire(_target);
		DetectionTestController.EnablePerceptionCombat(_observer);
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
		DisableLocomotion(_observer);
		DisableLocomotion(_target);
		Physics.SyncTransforms();
		for (int pass = 0; pass < 2; pass++)
		{
			Vector3 origin = _vision.GetGameplayVisionOriginWorld();
			Vector3 fwd = _vision.GetGameplayVisionForwardXZ();
			if (_vision.CurrentVisionProfile.IsScopeActive && _vision.ScopeScan != null)
				fwd = _vision.ScopeScan.GetSweepForwardXZ(fwd);
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

	private static float MeasureHorizontalDistance(UnitVision _vision, Transform _target)
	{
		if (_vision == null || _target == null)
			return -1f;
		return Mathf.Sqrt(VisionGeometry.HorizontalDistanceSq(
			_vision.GetGameplayVisionOriginWorld(), _target.position));
	}

	private static float GetHitscanCap(Transform _observer)
	{
		if (_observer != null && _observer.TryGetComponent(out UnitWeaponHitscanShooting hitscan))
			return hitscan.GetCappedMaxDistance();
		return -1f;
	}

	private static bool Near(float _a, float _b)
	{
		return Mathf.Abs(_a - _b) < 0.01f;
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		string line = (_ok ? "PASS " : "FAIL ") + _name + " | " + _detail;
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append(line);
		Debug.Log("[OpticRangeContract] " + line + $"  (pass={m_PassCount} fail={m_FailCount})", this);
	}

	private void BeginGroup(string _title)
	{
		Append("");
		Append(_title);
		Debug.Log(
			$"[VisionOpticRangeContractRuntimeSmoke] {_title} t={Time.time:F1} pass={m_PassCount} fail={m_FailCount}",
			this);
	}

	private void Append(string _line)
	{
		m_Report.AppendLine(_line);
	}

	private void Finish(string _result)
	{
		Append("");
		Append($"RESULT={_result}  PASS={m_PassCount} FAIL={m_FailCount}");
		string text = m_Report.ToString();
		Debug.Log("[OpticRangeContract] " + text, this);
		WriteReport(text);
	}

	private static void WriteReport(string _text)
	{
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "OpticRangeContract_LAST.txt"), _text);
	}
	#endregion
}
