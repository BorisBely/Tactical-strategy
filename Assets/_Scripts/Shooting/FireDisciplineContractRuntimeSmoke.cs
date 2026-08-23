using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vision Stage 11: Fire Discipline character of fire. Does not retune Q, E, ScopeVisionRange, Accuracy.
/// Writes Assets/_Docs/Logs/Tests/FireDisciplineContract_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class FireDisciplineContractRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly float[] s_Distances =
	{
		10f, 25f, 50f, 100f, 150f, 200f, 225f, 250f, 300f
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
		m_RunOnStart || DetectionHarnessPlayMode.RunFireDisciplineContract;
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
		if (DetectionHarnessPlayMode.RunFireDisciplineContract)
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
		Append("Vision Stage 11 FireDisciplineContract");
		Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		Append("---");

		WeaponDefinition cqb = LoadWeapon("Assets/GameData/Shooting/AK/Weapon_AK74U.asset");
		WeaponDefinition assault = LoadWeapon("Assets/GameData/Shooting/M4/Weapon_M4_ModA_1.asset");
		WeaponDefinition lmg = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_M249.asset");
		WeaponDefinition marksman = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_SVD.asset");
		WeaponDefinition sniper = LoadWeapon("Assets/GameData/Shooting/Standalone/Weapon_Sniper762x51.asset");
		WeaponAttachmentDefinition reddot = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Reddot1.asset");
		WeaponAttachmentDefinition scope9 = LoadOptic("Assets/GameData/Shooting/M4/Attachment_M4_Scope9.asset");

		Check("Assets", cqb != null && assault != null && lmg != null && marksman != null &&
		                sniper != null && reddot != null && scope9 != null, "load");
		if (cqb == null || assault == null || lmg == null || marksman == null ||
		    sniper == null || reddot == null || scope9 == null)
		{
			Finish("FAIL");
			yield break;
		}

		Check("Frozen_E_M4", Near(assault.EffectiveRangeMeters, 140f), assault.EffectiveRangeMeters.ToString("0"));
		Check("Frozen_E_Sniper", Near(sniper.EffectiveRangeMeters, 225f), sniper.EffectiveRangeMeters.ToString("0"));
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
			"Profile_FiveClasses",
			WeaponFireDisciplineProfile.ResolveKind(cqb) == WeaponFireDisciplineProfileKind.Cqb &&
			WeaponFireDisciplineProfile.ResolveKind(assault) == WeaponFireDisciplineProfileKind.Assault &&
			WeaponFireDisciplineProfile.ResolveKind(lmg) == WeaponFireDisciplineProfileKind.Lmg &&
			WeaponFireDisciplineProfile.ResolveKind(marksman) == WeaponFireDisciplineProfileKind.Marksman &&
			WeaponFireDisciplineProfile.ResolveKind(sniper) == WeaponFireDisciplineProfileKind.Sniper,
			"cqb/assault/lmg/marksman/sniper");
		Check(
			"Working_NotVisionOrE",
			!Near(WeaponFireDisciplineProfile.GetWorkingRangeMeters(assault), assault.EffectiveRangeMeters) &&
			!Near(WeaponFireDisciplineProfile.GetWorkingRangeMeters(assault), reddot.ScopeVisionRangeMeters) &&
			!Near(WeaponFireDisciplineProfile.GetWorkingRangeMeters(sniper), sniper.EffectiveRangeMeters),
			"range≠E≠V");

		WeaponFireDisciplinePlan cqb10 = Plan(cqb, 10f);
		Check(
			"CQB_10_Aggressive",
			cqb10.EffectiveFireMode == WeaponFireMode.FullAuto &&
			cqb10.SeriesShotCount >= 3 &&
			cqb10.RequiredAimProgress01 < 0.70f,
			FormatPlan(cqb10));

		WeaponFireDisciplinePlan assault100 = Plan(assault, 100f);
		Check(
			"Assault_100_Universal",
			(assault100.EffectiveFireMode == WeaponFireMode.Burst ||
			 assault100.EffectiveFireMode == WeaponFireMode.FullAuto) &&
			assault100.SeriesShotCount >= 2 &&
			assault100.SeriesShotCount <= 4,
			FormatPlan(assault100));

		WeaponFireDisciplinePlan assault150 = Plan(assault, 150f);
		Check(
			"Assault_150_Controlled",
			assault150.EffectiveFireMode == WeaponFireMode.SemiAuto &&
			assault150.SeriesShotCount <= 2 &&
			assault150.RequiredAimProgress01 >= 0.82f,
			FormatPlan(assault150));

		WeaponFireDisciplinePlan lmg150 = Plan(lmg, 150f);
		Check(
			"LMG_150_Support",
			lmg150.EffectiveFireMode == WeaponFireMode.FullAuto &&
			lmg150.SeriesShotCount >= 5 &&
			lmg150.SeriesShotCount > assault150.SeriesShotCount,
			FormatPlan(lmg150));

		WeaponFireDisciplinePlan marksman150 = Plan(marksman, 150f);
		Check(
			"Marksman_150_Precision",
			marksman150.EffectiveFireMode == WeaponFireMode.SemiAuto &&
			marksman150.SeriesShotCount <= 2,
			FormatPlan(marksman150));

		bool sniperOk = true;
		for (int i = 0; i < s_Distances.Length; i++)
		{
			WeaponFireDisciplinePlan sniperPlan = Plan(sniper, s_Distances[i]);
			if (sniperPlan.EffectiveFireMode != WeaponFireMode.SemiAuto || sniperPlan.SeriesShotCount != 1)
				sniperOk = false;
		}

		Check("Sniper_NeverSpray", sniperOk, "semi×1");

		WeaponDefinition[] matrix = { cqb, assault, lmg, marksman, sniper };
		bool seriesOk = true;
		bool farFires = true;
		for (int w = 0; w < matrix.Length; w++)
		{
			DumpMatrix(matrix[w]);
			for (int i = 0; i < s_Distances.Length; i++)
			{
				WeaponFireDisciplinePlan plan = Plan(matrix[w], s_Distances[i]);
				if (plan.SeriesShotCount < 1)
					seriesOk = false;
			}

			WeaponFireDisciplinePlan at300 = Plan(matrix[w], 300f);
			if (at300.SeriesShotCount < 1)
				farFires = false;
		}

		Check("Series_NeverZero", seriesOk, "all cells");
		Check("FarStillFires", farFires, "300m");

		bool hysteresis =
			WeaponFireDisciplineProfile.ResolveBand(0.20f, null) == WeaponFireDisciplineDistanceBand.Near &&
			WeaponFireDisciplineProfile.ResolveBand(0.19f, WeaponFireDisciplineDistanceBand.Near) ==
			WeaponFireDisciplineDistanceBand.Near &&
			WeaponFireDisciplineProfile.ResolveBand(0.11f, WeaponFireDisciplineDistanceBand.Near) ==
			WeaponFireDisciplineDistanceBand.Close &&
			WeaponFireDisciplineProfile.ResolveBand(0.40f, WeaponFireDisciplineDistanceBand.Mid) ==
			WeaponFireDisciplineDistanceBand.Mid;
		Check("Hysteresis", hysteresis, "enter/exit");

		WeaponFireDisciplinePlan assault69 = Plan(assault, 69f);
		WeaponFireDisciplinePlan assault71 = Plan(assault, 71f);
		Check(
			"Old70m_NotAnEdge",
			assault69.DistanceBand == assault71.DistanceBand,
			assault69.DistanceBand.ToString());

		Check(
			"AmmoEconomy_3s",
			EstimateShotsInSeconds(assault, 10f, 3f) < 30f &&
			EstimateShotsInSeconds(lmg, 10f, 3f) < 80f,
			"mag");

		WeaponFireDisciplinePlan assault25 = Plan(assault, 25f);
		Check(
			"Aim_RisesWithDistance",
			assault150.RequiredAimProgress01 > assault25.RequiredAimProgress01,
			assault25.RequiredAimProgress01.ToString("F2") + "→" + assault150.RequiredAimProgress01.ToString("F2"));

		Finish(m_FailCount == 0 ? "PASS" : "FAIL");
		yield return null;
	}

	private void DumpMatrix(WeaponDefinition _weapon)
	{
		Append("");
		Append(
			"MATRIX " + _weapon.name +
			" profile=" + WeaponFireDisciplineProfile.ResolveKind(_weapon) +
			" range=" + WeaponFireDisciplineProfile.GetWorkingRangeMeters(_weapon).ToString("0"));
		for (int i = 0; i < s_Distances.Length; i++)
		{
			WeaponFireDisciplinePlan plan = Plan(_weapon, s_Distances[i]);
			Append(
				$"  {s_Distances[i],5:0}  band={plan.DistanceBand,-7} n={plan.NormalizedDistance01:F2}  " +
				$"mode={plan.EffectiveFireMode,-8} series={plan.SeriesShotCount,2}  " +
				$"pause={plan.SeriesPauseSeconds:F2}  needAim={plan.RequiredAimProgress01:F2}  " +
				$"disc={plan.EffectiveDiscipline}");
		}
	}

	private static WeaponFireDisciplinePlan Plan(WeaponDefinition _weapon, float _distanceMeters)
	{
		return WeaponFireDisciplinePlanner.CreatePlan(
			_weapon,
			WeaponFireMode.Auto,
			WeaponFireDisciplineMode.Auto,
			_distanceMeters,
			null,
			null,
			null,
			true);
	}

	private static float EstimateShotsInSeconds(WeaponDefinition _weapon, float _distance, float _seconds)
	{
		WeaponFireDisciplinePlan plan = Plan(_weapon, _distance);
		float rpm = plan.EffectiveFireMode == WeaponFireMode.SemiAuto
			? Mathf.Max(1f, _weapon.SemiAutoFireRateRpm)
			: Mathf.Max(1f, _weapon.FireRateRpm);
		float seriesTime = plan.SeriesShotCount * (60f / rpm) + plan.SeriesPauseSeconds;
		return _seconds / Mathf.Max(0.05f, seriesTime) * plan.SeriesShotCount;
	}

	private static string FormatPlan(WeaponFireDisciplinePlan _plan)
	{
		return _plan.EffectiveFireMode + " s=" + _plan.SeriesShotCount +
		       " a=" + _plan.RequiredAimProgress01.ToString("F2", CultureInfo.InvariantCulture) +
		       " " + _plan.DistanceBand;
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
		Debug.Log("[FireDisciplineContract] " + text, this);
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "FireDisciplineContract_LAST.txt"), text);
	}
	#endregion
}
