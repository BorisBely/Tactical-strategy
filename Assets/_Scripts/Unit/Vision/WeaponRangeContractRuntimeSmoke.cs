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
/// Vision Stage 9: baked weapon/ammo EffectiveRange contract.
/// Does not retune Q, ScopeVisionRange, BaseDamage, or recoil.
/// Writes Assets/_Docs/Logs/Tests/WeaponRangeContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(64)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class WeaponRangeContractRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private const float c_WaitYes = 3.5f;
	private const float c_WaitNo = 1.6f;
	private const float c_Pulse = 0.2f;
	private const string c_VortexPath = "Assets/GameData/Shooting/M4/Attachment_M4_Vortex_Razor.asset";
	private const string c_AimpointPath = "Assets/GameData/Shooting/M4/Attachment_M4_Aimpoint.asset";
	private const string c_Scope9Path = "Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset";
	private const string c_M4Path = "Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset";
	private const string c_Ak74uPath = "Assets/GameData/Shooting/AK/Weapon_AK74U.asset";
	private const string c_Mk12Path = "Assets/GameData/Shooting/M4/Weapon_MK12.asset";
	private const string c_SniperPath = "Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset";
	private const string c_M2Path = "Assets/GameData/Shooting/Turret/Weapon_M2Browning_127.asset";
	private const string c_Mk19Path = "Assets/GameData/Shooting/Turret/Weapon_MK19.asset";
	private const string c_Ammo556Path = "Assets/GameData/Shooting/Ammo_556x45mmNATO.asset";
	private const string c_Ammo545Path = "Assets/GameData/Shooting/Ammo_545x39mm.asset";
	private const string c_Ammo51Path = "Assets/GameData/Shooting/Ammo_762x51mmNATO.asset";
	private const string c_Ammo127Path = "Assets/GameData/Shooting/Turret/Ammo_127x99.asset";
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	#endregion

	#region Private Fields
	private DetectionTestController m_Harness;
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private int m_PassCount;
	private int m_FailCount;
	private WeaponAttachmentDefinition m_Vortex;
	private WeaponAttachmentDefinition m_Aimpoint;
	private WeaponAttachmentDefinition m_Scope9;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunWeaponRangeContract;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		m_Harness = GetComponent<DetectionTestController>();
		if (!WillRunOnStart)
			return;

		Debug.Log("[WeaponRangeContractRuntimeSmoke] weapon range contract starting.", this);
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyClones();
		if (DetectionHarnessPlayMode.RunWeaponRangeContract)
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
		Append("Vision Stage 9 WeaponRangeContract");
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

#if UNITY_EDITOR
		WeaponDefinition m4 = LoadWeapon(c_M4Path, "M4");
		WeaponDefinition ak74u = LoadWeapon(c_Ak74uPath, "AK74U");
		WeaponDefinition mk12 = LoadWeapon(c_Mk12Path, "MK12");
		WeaponDefinition sniper = LoadWeapon(c_SniperPath, "Sniper");
		WeaponDefinition m2 = LoadWeapon(c_M2Path, "M2");
		WeaponDefinition mk19 = LoadWeapon(c_Mk19Path, "MK19");
		AmmoDefinition ammo556 = LoadAmmo(c_Ammo556Path, "556");
		AmmoDefinition ammo545 = LoadAmmo(c_Ammo545Path, "545");
		AmmoDefinition ammo51 = LoadAmmo(c_Ammo51Path, "762x51");
		AmmoDefinition ammo127 = LoadAmmo(c_Ammo127Path, "127");
		m_Vortex = CloneOptic(c_VortexPath, "Vortex");
		m_Aimpoint = CloneOptic(c_AimpointPath, "Aimpoint");
		m_Scope9 = CloneOptic(c_Scope9Path, "Scope9");
		if (m4 == null || ak74u == null || mk12 == null || sniper == null || m2 == null || mk19 == null ||
		    ammo556 == null || ammo545 == null || ammo51 == null || ammo127 == null ||
		    m_Vortex == null || m_Aimpoint == null || m_Scope9 == null)
		{
			Finish("FAIL");
			yield break;
		}

		BeginGroup("A ASSETS");
		CheckFalloffAsset("A_M4", m4, ammo556, 140f, 200f, 0.57f);
		CheckFalloffAsset("A_AK74U", ak74u, ammo545, 100f, 150f, 0.50f);
		CheckFalloffAsset("A_MK12", mk12, ammo556, 200f, 250f, 0.75f);
		CheckFalloffAsset("A_Sniper", sniper, ammo51, 225f, 300f, 0.667f);
		CheckFalloffAsset("A_M2", m2, ammo127, 225f, 300f, 0.667f);
		Check("A_MK19Class", mk19.WeaponClass == WeaponClassType.AutomaticGrenadeLauncher,
			mk19.WeaponClass.ToString());
		Check("A_MK19Ceiling", Near(mk19.EffectiveRangeMeters, 300f),
			$"range={mk19.EffectiveRangeMeters:0}");

		BeginGroup("B OPTICS");
		Check("B_VortexRangeX", Near(m_Vortex.EffectiveRangeModifier, 1f),
			$"x={m_Vortex.EffectiveRangeModifier:F2}");
		Check("B_Scope9RangeX", Near(m_Scope9.EffectiveRangeModifier, 1f),
			$"x={m_Scope9.EffectiveRangeModifier:F2}");
		Check("B_Scope9Vision", Near(m_Scope9.ScopeVisionRangeMeters, 300f),
			$"vision={m_Scope9.ScopeVisionRangeMeters:0}");
		Check("B_AimpointVision", Near(m_Aimpoint.ScopeVisionRangeMeters, 175f),
			$"vision={m_Aimpoint.ScopeVisionRangeMeters:0}");

		vision.SetVisionRange(UnitVisionProfile.BaseRangeMeters);
		vision.DebugClearVisionOverrides();
		PrepareUnits(observer, target);
		ResetWorld(vision);
		FreezeScopeYaw(vision, 0f);
		ForceAimingReady(observer);
		yield return WaitPoseReady(observer);

		BeginGroup("C CQB EDGE");
		ApplyCombatOptic(vision, m_Aimpoint, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("C_Aimpoint150", vision, observer, target, 150f, true);
		Check("C_AK74UDamageAt150",
			Falloff(ak74u, ammo545, 150f) >= 0.49f,
			$"mult={Falloff(ak74u, ammo545, 150f):F2}");

		BeginGroup("D ASSAULT EDGE");
		ApplyCombatOptic(vision, m_Vortex, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("D_Vortex200", vision, observer, target, 200f, true);
		Check("D_Vision250", Near(vision.ResolvedMaxRange, 250f), vision.FormatVisionProfileLog());
		float cap200 = GetHitscanCap(observer);
		Check("D_HitscanFollowsVision",
			cap200 <= vision.ResolvedMaxRange + 0.05f && cap200 >= 249f,
			$"cap={cap200:F1} vision={vision.ResolvedMaxRange:F1}");
		Check("D_M4DamageAt200",
			Falloff(m4, ammo556, 200f) >= 0.56f,
			$"mult={Falloff(m4, ammo556, 200f):F2}");
		if (observer.TryGetComponent(out UnitWeaponFireController fire200))
		{
			WeaponShotAttemptResult shot200 = fire200.TryFireSingleShot();
			Check("D_CanAttemptFireAt200",
				shot200 != WeaponShotAttemptResult.NoVisibleTarget &&
				shot200 != WeaponShotAttemptResult.NoWeapon,
				shot200.ToString());
		}
		else
			Check("D_CanAttemptFireAt200", false, "UnitWeaponFireController missing");

		BeginGroup("E MARKSMAN / SNIPER");
		yield return ProbeObservation("E_Vortex250Mk12", vision, observer, target, 250f, true);
		Check("E_MK12DamageAt250",
			Falloff(mk12, ammo556, 250f) >= 0.74f,
			$"mult={Falloff(mk12, ammo556, 250f):F2}");
		ApplyCombatOptic(vision, m_Scope9, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("E_Scope9_299", vision, observer, target, 299f, true);
		Check("E_SniperDamageAt300",
			Falloff(sniper, ammo51, 300f) >= 0.66f,
			$"mult={Falloff(sniper, ammo51, 300f):F2}");
		Check("E_M2DamageAt300",
			Falloff(m2, ammo127, 300f) >= 0.66f,
			$"mult={Falloff(m2, ammo127, 300f):F2}");

		BeginGroup("F VISION CAP UNCHANGED");
		ApplyCombatOptic(vision, m_Vortex, WeaponPoseState.Aiming, true);
		yield return ProbeObservation("F_Vortex251NoSee", vision, observer, target, 251f, false);
		float cap251 = GetHitscanCap(observer);
		Check("F_HitscanCappedAtVision",
			cap251 <= 250.05f && Near(vision.ResolvedMaxRange, 250f),
			$"cap={cap251:F1} vision={vision.ResolvedMaxRange:F1}");
		if (observer.TryGetComponent(out UnitWeaponFireController fire251))
		{
			WeaponShotAttemptResult shot251 = fire251.TryFireSingleShot();
			Check("F_NoVisibleTargetAt251",
				shot251 != WeaponShotAttemptResult.Success,
				shot251.ToString());
		}
		else
			Check("F_NoVisibleTargetAt251", false, "UnitWeaponFireController missing");

		Check("G_M4ZeroAt280", Near(Falloff(m4, ammo556, 280f), 0f),
			$"mult={Falloff(m4, ammo556, 280f):F2}");
		Check("G_M4FullAt140", Near(Falloff(m4, ammo556, 140f), 1f),
			$"mult={Falloff(m4, ammo556, 140f):F2}");
#else
		Check("EditorAssets", false, "Editor-only asset load");
#endif

		vision.DebugClearVisionOverrides();
		DestroyClones();
		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
	}

#if UNITY_EDITOR
	private WeaponDefinition LoadWeapon(string _path, string _label)
	{
		WeaponDefinition asset = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(_path);
		Check("LoadWeapon_" + _label, asset != null, _path);
		return asset;
	}

	private AmmoDefinition LoadAmmo(string _path, string _label)
	{
		AmmoDefinition asset = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(_path);
		Check("LoadAmmo_" + _label, asset != null, _path);
		return asset;
	}

	private WeaponAttachmentDefinition CloneOptic(string _path, string _label)
	{
		WeaponAttachmentDefinition source =
			AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_path);
		Check("LoadOptic_" + _label, source != null, _path);
		if (source == null)
			return null;
		WeaponAttachmentDefinition clone = Instantiate(source);
		clone.name = source.name + "_WeaponRangeClone";
		return clone;
	}
#endif

	private void CheckFalloffAsset(
		string _id,
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		float _full,
		float _edge,
		float _minAtEdge)
	{
		float e = WeaponDamageRangeMath.ResolveEffectiveRangeMeters(
			_weapon.EffectiveRangeMeters,
			WeaponDamageRangeMath.ProposedOpticEffectiveRangeModifier,
			_ammo.EffectiveRangeMeters);
		float atFull = WeaponDamageRangeMath.ComputeFalloffMultiplier(_full, e);
		float atEdge = WeaponDamageRangeMath.ComputeFalloffMultiplier(_edge, e);
		float atZero = WeaponDamageRangeMath.ComputeFalloffMultiplier(e * 2f, e);
		Check(_id + "_Full", Near(e, _full) && Near(atFull, 1f), $"E={e:0} full={atFull:F2}");
		Check(_id + "_Edge", atEdge + 0.005f >= _minAtEdge, $"edge={atEdge:F3} min={_minAtEdge:F3}");
		Check(_id + "_Zero", Near(atZero, 0f), $"zero={atZero:F2}");
	}

	private static float Falloff(WeaponDefinition _weapon, AmmoDefinition _ammo, float _distance)
	{
		float e = WeaponDamageRangeMath.ResolveEffectiveRangeMeters(
			_weapon.EffectiveRangeMeters,
			WeaponDamageRangeMath.ProposedOpticEffectiveRangeModifier,
			_ammo.EffectiveRangeMeters);
		return WeaponDamageRangeMath.ComputeFalloffMultiplier(_distance, e);
	}

	private void DestroyClones()
	{
		DestroyIfNotNull(m_Vortex);
		DestroyIfNotNull(m_Aimpoint);
		DestroyIfNotNull(m_Scope9);
		m_Vortex = null;
		m_Aimpoint = null;
		m_Scope9 = null;
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
			$"seen={lastSeen} expect={_expectObs} max={_vision.ResolvedMaxRange:0}");
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
		return Mathf.Abs(_a - _b) < 0.02f;
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		string line = (_ok ? "PASS " : "FAIL ") + _name + " | " + _detail;
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append(line);
		Debug.Log("[WeaponRangeContract] " + line + $"  (pass={m_PassCount} fail={m_FailCount})", this);
	}

	private void BeginGroup(string _title)
	{
		Append("");
		Append(_title);
		Debug.Log(
			$"[WeaponRangeContractRuntimeSmoke] {_title} t={Time.time:F1} pass={m_PassCount} fail={m_FailCount}",
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
		Debug.Log("[WeaponRangeContract] " + text, this);
		WriteReport(text);
	}

	private static void WriteReport(string _text)
	{
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "WeaponRangeContract_LAST.txt"), _text);
	}
	#endregion
}
