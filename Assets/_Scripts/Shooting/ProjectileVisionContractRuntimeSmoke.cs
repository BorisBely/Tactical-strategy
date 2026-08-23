using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vision Stage 12: projectile assignment uses Observed VisionRange; life is not clipped.
/// Writes Assets/_Docs/Logs/Tests/ProjectileVisionContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class ProjectileVisionContractRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly float[] s_Distances =
	{
		50f, 100f, 149f, 150f, 151f, 200f, 250f, 300f
	};
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(16384);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunProjectileVisionContract;
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
		if (DetectionHarnessPlayMode.RunProjectileVisionContract)
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
		Append("Vision Stage 12 ProjectileVisionContract");
		Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("---");

		WeaponDefinition assault = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponDefinition mk19 = LoadWeapon("Assets/GameData/Shooting/Turret/Weapon_MK19.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");
		RocketLauncherData rockets = LoadRockets();
		GameObject mk19Prefab = LoadPrefab("Assets/Resources/Turret/Shell_40mm_Projectile.prefab");

		Check(
			"Assets",
			assault != null && sniper != null && mk19 != null && reddot != null &&
			scope9 != null && rockets != null,
			"load");
		if (assault == null || sniper == null || mk19 == null || reddot == null ||
		    scope9 == null || rockets == null)
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
		Check(
			"Frozen_Disposable_130_12",
			Near(rockets.GetMuzzleSpeed(RocketLauncherType.Disposable), 130f) &&
			Near(rockets.ProjectileLifetimeSeconds, 12f),
			rockets.GetMuzzleSpeed(RocketLauncherType.Disposable).ToString("0") + "/" +
			rockets.ProjectileLifetimeSeconds.ToString("0"));

		float mk19Life = ProjectileLaunchPermit.Mk19LifetimeSeconds;
		if (mk19Prefab != null &&
		    mk19Prefab.TryGetComponent(out VehicleTurretGrenadeProjectile grenade))
			mk19Life = grenade.MaxLifetimeSeconds;
		Check(
			"Frozen_Mk19_240_25",
			Near(ProjectileLaunchPermit.Mk19MuzzleSpeed, 240f) && Near(mk19Life, 25f),
			"240/" + mk19Life.ToString("0"));

		DumpMatrix(150f, "eye");
		DumpMatrix(300f, "scope9");

		for (int i = 0; i < s_Distances.Length; i++)
		{
			float distance = s_Distances[i];
			ProjectileLaunchDeny deny = AuthorizeObserved(distance, 150f);
			bool expectLaunch = distance <= 150.01f;
			Check(
				"Permit_Eye_" + distance.ToString("0"),
				expectLaunch
					? deny == ProjectileLaunchDeny.None
					: deny == ProjectileLaunchDeny.OutsideVision,
				ProjectileLaunchPermit.FormatResult(deny));
		}

		Check(
			"Permit_Scope9_250",
			AuthorizeObserved(250f, 300f) == ProjectileLaunchDeny.None,
			"Launch");
		Check(
			"Permit_Scope9_300",
			AuthorizeObserved(300f, 300f) == ProjectileLaunchDeny.None,
			"Launch");

		Vector3 origin = Vector3.zero;
		ProjectileLaunchPermit.TryAuthorize(
			false, origin, new Vector3(0f, 0f, 140f), 150f, true, true, false,
			out ProjectileLaunchDeny recentlyLost);
		Check(
			"Permit_RecentlyLost",
			recentlyLost == ProjectileLaunchDeny.NoAimPoint,
			ProjectileLaunchPermit.FormatResult(recentlyLost));

		ProjectileLaunchPermit.TryAuthorize(
			false, origin, new Vector3(0f, 0f, 300f), 150f, true, true, false,
			out ProjectileLaunchDeny lastKnown);
		Check(
			"Permit_LastKnown_300",
			lastKnown == ProjectileLaunchDeny.NoAimPoint,
			ProjectileLaunchPermit.FormatResult(lastKnown));

		Check(
			"Permit_NotG6Fire",
			Authorize(true, 80f, 150f, true, false, false) == ProjectileLaunchDeny.NotG6Fire,
			"Track");
		Check(
			"Permit_NoLOS",
			Authorize(true, 80f, 150f, true, true, true) == ProjectileLaunchDeny.NoLOS,
			"blocked");

		float rpgReach = ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(
			rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7),
			rockets.ProjectileLifetimeSeconds);
		Check(
			"FlightBeyondVision",
			rpgReach > UnitVisionProfile.BaseRangeMeters &&
			!Near(rockets.ProjectileLifetimeSeconds, UnitVisionProfile.BaseRangeMeters / 115f),
			rpgReach.ToString("0") + "m");

		float mk19Reach = ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(240f, mk19Life);
		Check(
			"Mk19_E_NotPhysicalLife",
			!Near(mk19.EffectiveRangeMeters, mk19Reach) && mk19Reach > mk19.EffectiveRangeMeters,
			"E=" + mk19.EffectiveRangeMeters.ToString("0") + " phys=" + mk19Reach.ToString("0"));

		GameObject probe = new GameObject("Stage12_RocketProbe");
		probe.AddComponent<Rigidbody>();
		RocketProjectile rocket = probe.AddComponent<RocketProjectile>();
		rocket.Launch(
			Vector3.forward,
			rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7),
			rockets.ProjectileLifetimeSeconds,
			9.81f,
			0.02f,
			rockets,
			probe,
			RocketLauncherType.Rpg7);
		Check(
			"Rocket_LifetimeUnclipped",
			Near(rocket.LifetimeSeconds, 12f) && rocket.IsLaunched,
			rocket.LifetimeSeconds.ToString("0") + "s");
		Object.Destroy(probe);

		Vector3 led = ProjectileLaunchPermit.ApplyRocketLead(
			new Vector3(0f, 0f, 230f),
			new Vector3(10f, 0f, 0f),
			230f,
			115f);
		Check("Lead_AfterPermit", Near(led.x, 15f), led.x.ToString("F1", CultureInfo.InvariantCulture));

		Check(
			"Architecture_NoSecondVision",
			typeof(ProjectileLaunchPermit).IsAbstract &&
			rpgReach > UnitVisionProfile.BaseRangeMeters,
			"static permit");

		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private void DumpMatrix(float _vision, string _label)
	{
		Append("");
		Append("MATRIX vision=" + _vision.ToString("0") + " (" + _label + ")");
		for (int i = 0; i < s_Distances.Length; i++)
		{
			ProjectileLaunchDeny deny = AuthorizeObserved(s_Distances[i], _vision);
			Append(
				"  " + s_Distances[i].ToString("0").PadLeft(5) +
				"  " + ProjectileLaunchPermit.FormatResult(deny));
		}
	}

	private static ProjectileLaunchDeny AuthorizeObserved(float _distance, float _vision)
	{
		return Authorize(true, _distance, _vision, true, true, false);
	}

	private static ProjectileLaunchDeny Authorize(
		bool _hasAim,
		float _distance,
		float _vision,
		bool _hasG6,
		bool _g6Fire,
		bool _lof)
	{
		Vector3 origin = Vector3.zero;
		Vector3 aim = _hasAim ? new Vector3(0f, 0f, _distance) : Vector3.zero;
		ProjectileLaunchPermit.TryAuthorize(
			_hasAim,
			origin,
			aim,
			_vision,
			_hasG6,
			_g6Fire,
			_lof,
			out ProjectileLaunchDeny reason);
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
		Debug.Log("[ProjectileVisionContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "ProjectileVisionContract_LAST.txt"), text);
	}
	#endregion
}
