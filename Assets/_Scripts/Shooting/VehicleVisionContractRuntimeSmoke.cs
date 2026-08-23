using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vision Stage 13: passenger = infantry envelope; turret optic or 150.
/// Writes Assets/_Docs/Logs/Tests/VehicleVisionContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class VehicleVisionContractRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private int m_PassCount;
	private int m_FailCount;
	private WeaponAttachmentDefinition m_InjectedScope9;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		(m_RunOnStart || DetectionHarnessPlayMode.RunVehicleVisionContract) &&
		!DetectionHarnessPlayMode.RunCombatRetainContract &&
		!DetectionHarnessPlayMode.RunAttentionFacingContract &&
		!DetectionHarnessPlayMode.RunSoundPerceptionContract &&
		!DetectionHarnessPlayMode.RunAllyReportContract &&
		!DetectionHarnessPlayMode.RunFinalPerceptionContract;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (m_InjectedScope9 != null)
			Destroy(m_InjectedScope9);
		if (DetectionHarnessPlayMode.RunVehicleVisionContract)
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
		Append("Vision Stage 13 VehicleVisionContract");
		Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("---");

		WeaponDefinition assault = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponDefinition mk19 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_MK19.asset");
		WeaponDefinition m2 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_M2Browning_127.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");
		RocketLauncherData rockets = LoadRockets();
		GameObject mk19Prefab = LoadPrefab("Assets/Resources/Turret/Shell_40mm_Projectile.prefab");

		Check(
			"Assets",
			assault != null && sniper != null && mk19 != null && m2 != null &&
			reddot != null && scope9 != null && rockets != null,
			"load");
		if (assault == null || sniper == null || mk19 == null || m2 == null ||
		    reddot == null || scope9 == null || rockets == null)
		{
			Finish("FAIL");
			yield break;
		}

		Check("Frozen_E_M4", Near(assault.EffectiveRangeMeters, 140f), assault.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_E_Sniper", Near(sniper.EffectiveRangeMeters, 225f), sniper.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_E_MK19", Near(mk19.EffectiveRangeMeters, 300f), mk19.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_V_Reddot", Near(reddot.ScopeVisionRangeMeters, 150f), reddot.ScopeVisionRangeMeters.ToString("0"));
		Check("Frozen_V_Scope9", Near(scope9.ScopeVisionRangeMeters, 300f), scope9.ScopeVisionRangeMeters.ToString("0"));
		Check("Frozen_AimTimeX_Scope9", Near(scope9.AimTimeModifier, 1.55f), scope9.AimTimeModifier.ToString("F2"));
		Check(
			"Frozen_PoseFloors",
			Near(WeaponAimModeUtility.SnapShotAimProgress01, 0.35f) &&
			Near(WeaponAimModeUtility.QuickAimProgress01, 0.68f) &&
			Near(WeaponAimModeUtility.FullAimProgress01, 1f),
			"0.35/0.68/1.0");
		Check(
			"Frozen_Rpg_115_12",
			Near(rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7), 115f) &&
			Near(rockets.ProjectileLifetimeSeconds, 12f),
			rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7).ToString("0") + "/" +
			rockets.ProjectileLifetimeSeconds.ToString("0"));

		float mk19Life = ProjectileLaunchPermit.Mk19LifetimeSeconds;
		if (mk19Prefab != null &&
		    mk19Prefab.TryGetComponent(out VehicleTurretGrenadeProjectile grenade))
			mk19Life = grenade.MaxLifetimeSeconds;
		Check(
			"Frozen_Mk19_240_25",
			Near(ProjectileLaunchPermit.Mk19MuzzleSpeed, 240f) && Near(mk19Life, 25f),
			"240/" + mk19Life.ToString("0"));
		Check("Frozen_M2_Optic_0", Near(m2.OpticVisionRangeMeters, 0f), m2.OpticVisionRangeMeters.ToString("0"));
		Check("Frozen_MK19_Optic_0", Near(mk19.OpticVisionRangeMeters, 0f), mk19.OpticVisionRangeMeters.ToString("0"));

		ResolvedVisionProfile infantryEye = Resolve(
			VisionSourceKind.InfantryEye, WeaponPoseState.PointAim, false, 0f, null);
		Check("InfantryEye_149", CanSee(149f, infantryEye), infantryEye.MaxRangeMeters.ToString("0"));
		Check("InfantryEye_151", !CanSee(151f, infantryEye), infantryEye.MaxRangeMeters.ToString("0"));

		ResolvedVisionProfile passenger = Resolve(
			VisionSourceKind.Passenger, WeaponPoseState.PointAim, false, 0f, null);
		Check(
			"Passenger_Not100",
			!Near(passenger.MaxRangeMeters, 100f) && Near(passenger.MaxRangeMeters, 150f),
			passenger.MaxRangeMeters.ToString("0"));
		Check("Passenger_99", CanSee(99f, passenger), "Visible");
		Check("Passenger_101", CanSee(101f, passenger), "Visible");
		Check("Passenger_149", CanSee(149f, passenger), "Visible");
		Check("Passenger_151", !CanSee(151f, passenger), "no Observation");
		Check(
			"InfantryBeside_101",
			CanSee(101f, infantryEye) && CanSee(101f, passenger),
			"both Visible");

		m_InjectedScope9 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_InjectedScope9.SetScopeVisionRangeMeters(300f);
		WeaponAttachmentDefinition[] scope9Arr = { m_InjectedScope9 };
		ResolvedVisionProfile passengerScopeReady = Resolve(
			VisionSourceKind.Passenger, WeaponPoseState.PointAim, true, 0f, scope9Arr);
		ResolvedVisionProfile passengerScopeIdle = Resolve(
			VisionSourceKind.Passenger, WeaponPoseState.PointAim, false, 0f, scope9Arr);
		Check(
			"Passenger_250_Scope9_ready",
			CanSee(250f, passengerScopeReady) && Near(passengerScopeReady.MaxRangeMeters, 300f),
			passengerScopeReady.MaxRangeMeters.ToString("0"));
		Check(
			"Passenger_250_notReady",
			!CanSee(250f, passengerScopeIdle) && Near(passengerScopeIdle.MaxRangeMeters, 150f),
			passengerScopeIdle.MaxRangeMeters.ToString("0"));

		ResolvedVisionProfile turret = Resolve(
			VisionSourceKind.Turret, WeaponPoseState.PointAim, false, 0f, null);
		ResolvedVisionProfile turretAiming = Resolve(
			VisionSourceKind.Turret, WeaponPoseState.Aiming, false, 0f, null);
		Check("Turret_noOptic_149", CanSee(149f, turret), "Visible");
		Check("Turret_noOptic_151", !CanSee(151f, turret), "no Observation");
		Check(
			"Turret_ignores_Aiming",
			Near(turret.MaxRangeMeters, turretAiming.MaxRangeMeters) && Near(turret.MaxRangeMeters, 150f),
			turret.MaxRangeMeters.ToString("0"));

		ResolvedVisionProfile turretOptic = Resolve(
			VisionSourceKind.Turret, WeaponPoseState.PointAim, false, 250f, null);
		Check("Turret_injected_250", CanSee(250f, turretOptic), turretOptic.MaxRangeMeters.ToString("0"));
		Check("Turret_injected_251", !CanSee(251f, turretOptic), "no Observation");

		Check(
			"M2_inside_permit",
			AuthorizeObserved(149f, turret.MaxRangeMeters) == ProjectileLaunchDeny.None,
			"Launch");
		Check(
			"M2_outside_permit",
			AuthorizeObserved(151f, turret.MaxRangeMeters) == ProjectileLaunchDeny.OutsideVision,
			"OutsideVision");
		Check(
			"MK19_inside_permit",
			AuthorizeObserved(149f, 150f) == ProjectileLaunchDeny.None,
			"Launch");
		Check(
			"MK19_outside_permit",
			AuthorizeObserved(151f, 150f) == ProjectileLaunchDeny.OutsideVision,
			"OutsideVision");

		Vector3 origin = Vector3.zero;
		ProjectileLaunchPermit.TryAuthorize(
			false, origin, new Vector3(0f, 0f, 140f), 150f, true, true, false,
			out ProjectileLaunchDeny lastKnown);
		Check(
			"Rpg_LastKnown",
			lastKnown == ProjectileLaunchDeny.NoAimPoint,
			ProjectileLaunchPermit.FormatResult(lastKnown));

		Check(
			"Mk19_life_unclipped",
			Near(mk19Life, 25f) &&
			ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(240f, mk19Life) > 300f,
			mk19Life.ToString("0") + "s");

		FieldInfo cap = typeof(VehiclePassengerFireValidator).GetField(
			"m_MaxFireRange",
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		Check("Architecture_NoPassenger100", cap == null, cap == null ? "field gone" : cap.Name);
		Check(
			"Architecture_NoSecondVision",
			typeof(UnitVision).IsSealed,
			typeof(UnitVision).IsSealed ? "one UnitVision" : "subclassable");

		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private static ResolvedVisionProfile Resolve(
		VisionSourceKind _source,
		WeaponPoseState _pose,
		bool _passengerReady,
		float _turretOptic,
		WeaponAttachmentDefinition[] _optics)
	{
		return UnitVisionProfile.ResolveForSource(
			_source,
			UnitVisionProfile.BaseRangeMeters,
			UnitVisionProfile.BaseFovDegrees,
			_pose,
			_optics,
			_passengerReady,
			_turretOptic);
	}

	private static bool CanSee(float _distance, ResolvedVisionProfile _profile)
	{
		return UnitVisionProfile.IsWithinResolvedRange(_distance, _profile.MaxRangeMeters);
	}

	private static ProjectileLaunchDeny AuthorizeObserved(float _distance, float _vision)
	{
		Vector3 origin = Vector3.zero;
		Vector3 aim = new Vector3(0f, 0f, _distance);
		ProjectileLaunchPermit.TryAuthorize(
			true, origin, aim, _vision, true, true, false, out ProjectileLaunchDeny reason);
		return reason;
	}

	private static WeaponDefinition LoadWeapon(string _path)
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<WeaponDefinition>(_path);
#else
		return null;
#endif
	}

	private static WeaponAttachmentDefinition LoadOptic(string _path)
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(_path);
#else
		return null;
#endif
	}

	private static RocketLauncherData LoadRockets()
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<RocketLauncherData>(
			"Assets/GameData/Combat/RocketLauncherData.asset");
#else
		return null;
#endif
	}

	private static GameObject LoadPrefab(string _path)
	{
#if UNITY_EDITOR
		return AssetDatabase.LoadAssetAtPath<GameObject>(_path);
#else
		return null;
#endif
	}

	private void Check(string _name, bool _ok, string _detail)
	{
		if (_ok)
			m_PassCount++;
		else
			m_FailCount++;
		Append((_ok ? "PASS  " : "FAIL  ") + _name + "  " + _detail);
	}

	private static bool Near(float _a, float _b) => Mathf.Abs(_a - _b) <= 0.011f;

	private void Append(string _line) => m_Report.AppendLine(_line);

	private void Finish(string _result)
	{
		Append("");
		Append($"RESULT={_result}  PASS={m_PassCount} FAIL={m_FailCount}");
		string text = m_Report.ToString();
		Debug.Log("[VehicleVisionContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "VehicleVisionContract_LAST.txt"), text);
	}
	#endregion
}
