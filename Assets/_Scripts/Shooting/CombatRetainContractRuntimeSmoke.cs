using System;
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
/// Vision Stage 14: retain uses ResolvedMaxRange, not 18 m.
/// Writes Assets/_Docs/Logs/Tests/CombatRetainContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class CombatRetainContractRuntimeSmoke : MonoBehaviour
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
		(m_RunOnStart || DetectionHarnessPlayMode.RunCombatRetainContract) &&
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
		if (DetectionHarnessPlayMode.RunCombatRetainContract)
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
		Append("Vision Stage 14 CombatRetainContract");
		Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("---");

		WeaponDefinition assault = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponDefinition mk19 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_MK19.asset");
		WeaponDefinition m2 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_M2Browning_127.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");
		RocketLauncherData rockets = LoadRockets();

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
		Check("Frozen_Pose_0.35", Near(WeaponAimModeUtility.SnapShotAimProgress01, 0.35f), "0.35");
		Check("Frozen_Pose_0.68", Near(WeaponAimModeUtility.QuickAimProgress01, 0.68f), "0.68");
		Check("Frozen_Pose_1.00", Near(WeaponAimModeUtility.FullAimProgress01, 1f), "1.00");
		Check(
			"Frozen_Rpg_115_12",
			Near(rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7), 115f) &&
			Near(rockets.ProjectileLifetimeSeconds, 12f),
			"115/12");
		Check(
			"Frozen_Mk19_240_25",
			Near(ProjectileLaunchPermit.Mk19MuzzleSpeed, 240f) &&
			Near(ProjectileLaunchPermit.Mk19LifetimeSeconds, 25f),
			"240/25");
		Check("Frozen_M2_Optic_0", Near(m2.OpticVisionRangeMeters, 0f), "0");
		Check("Stage13_PassengerNot100", Near(PassengerRange(), 150f), "150");

		float eye = SourceRange(VisionSourceKind.InfantryEye, WeaponPoseState.PointAim, false, 0f, null);
		Check("Infantry_20", CombatRetainMath.CanRetainAtDistance(20f, eye), "retain");
		Check("Infantry_80", CombatRetainMath.CanRetainAtDistance(80f, eye), "retain");
		Check("Infantry_149", CombatRetainMath.CanRetainAtDistance(149f, eye), "retain");
		Check("Infantry_151", !CombatRetainMath.CanRetainAtDistance(151f, eye), "no retain");
		Check("Not18", !Near(eye, 18f) && Near(eye, 150f), eye.ToString("0"));

		m_InjectedScope9 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
		m_InjectedScope9.SetScopeVisionRangeMeters(300f);
		float scope = SourceRange(
			VisionSourceKind.InfantryEye,
			WeaponPoseState.Aiming,
			false,
			0f,
			new[] { m_InjectedScope9 });
		Check("Scope9_250", CombatRetainMath.CanRetainAtDistance(250f, scope), "retain");
		Check("Scope9_300", CombatRetainMath.CanRetainAtDistance(300f, scope), "retain");
		Check("Scope9_301", !CombatRetainMath.CanRetainAtDistance(301f, scope), "no retain");

		float passenger = PassengerRange();
		Check("Passenger_80", CombatRetainMath.CanRetainAtDistance(80f, passenger), "retain");

		float turret = SourceRange(VisionSourceKind.Turret, WeaponPoseState.PointAim, false, 0f, null);
		Check("Turret_149", CombatRetainMath.CanRetainAtDistance(149f, turret), "retain");
		Check("Turret_151", !CombatRetainMath.CanRetainAtDistance(151f, turret), "no retain");

		float turretOptic = SourceRange(VisionSourceKind.Turret, WeaponPoseState.PointAim, false, 250f, null);
		Check("TurretOptic_250", CombatRetainMath.CanRetainAtDistance(250f, turretOptic), "retain");

		Vector3 origin = Vector3.zero;
		ProjectileLaunchPermit.TryAuthorize(
			false, origin, new Vector3(0f, 0f, 80f), eye, true, true, false,
			out ProjectileLaunchDeny lastKnown);
		Check(
			"LastKnown_NoFire",
			lastKnown == ProjectileLaunchDeny.NoAimPoint,
			ProjectileLaunchPermit.FormatResult(lastKnown));

		Check(
			"Mk19_permit_inside",
			AuthorizeObserved(149f, 150f) == ProjectileLaunchDeny.None,
			"Launch");
		Check(
			"Rpg_permit_outside",
			AuthorizeObserved(151f, 150f) == ProjectileLaunchDeny.OutsideVision,
			"OutsideVision");

		FieldInfo cap = typeof(TargetSelector).GetField(
			"m_MaxEngageRange",
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		Check("Architecture_No18Field", cap == null, cap == null ? "field gone" : cap.Name);

		Type[] types = typeof(UnitVision).Assembly.GetTypes();
		int extraVision = 0;
		for (int i = 0; i < types.Length; i++)
		{
			if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
				extraVision++;
		}

		Check("Architecture_NoSecondVision", extraVision == 0, extraVision.ToString());

		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private static float PassengerRange()
	{
		return SourceRange(VisionSourceKind.Passenger, WeaponPoseState.PointAim, true, 0f, null);
	}

	private static float SourceRange(
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
			_turretOptic).MaxRangeMeters;
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
		Debug.Log("[CombatRetainContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "CombatRetainContract_LAST.txt"), text);
	}
	#endregion
}
