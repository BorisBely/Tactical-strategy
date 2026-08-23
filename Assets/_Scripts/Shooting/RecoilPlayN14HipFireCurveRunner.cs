using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Stage 1 N14: HipFire distance curve θ decomposition @5–100 m (M4).
/// </summary>
public static class RecoilPlayN14HipFireCurveRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ammo556AssetName = "Ammo_556x45mmNATO";

	private static readonly float[] s_DistancesMeters = { 5f, 10f, 15f, 25f, 50f, 100f };

	private static readonly (float Distance, float ExpectedMult)[] s_KeyDistanceMults =
	{
		(5f, 1.10f),
		(10f, 1.50f),
		(25f, 3.50f),
		(100f, 10f)
	};

	private const float c_KeyMultTolerance = 0.08f;
	private const float c_PoseMultHipFire = WeaponPoseCombatModifiers.HipFireSpreadMultiplier;
	#endregion

	#region Nested Types
	private struct CurveRow
	{
		public float DistanceMeters;
		public float BaseAimingTheta;
		public float PoseMult;
		public float DistanceMult;
		public float FinalTheta;
		public float SpreadDiameterMeters;
	}
	#endregion

	#region Public Methods
	public static string Run(WeaponDefinition _m4, AmmoDefinition _ammo)
	{
		var sb = new StringBuilder(4096);
		CultureInfo culture = CultureInfo.InvariantCulture;
		var rows = new CurveRow[s_DistancesMeters.Length];

		sb.AppendLine("RecoilPlayN14HipFireCurve MATH + SIM_PLAY");
		sb.AppendLine("Stage 1 N14: M4 HipFire θ vs distance. Decompose pose×distance (A3 lesson).");
		sb.AppendLine("Offset informational only — not a PASS criterion.");
		sb.AppendLine();

		sb.AppendLine("N14-MATH (HipFire):");
		sb.AppendLine("Distance | BaseAimingθ | PoseMult | DistanceMult | Finalθ | SpreadDiameter");
		for (int i = 0; i < s_DistancesMeters.Length; i++)
		{
			rows[i] = SampleCurve(_m4, _ammo, s_DistancesMeters[i]);
			CurveRow row = rows[i];
			sb.AppendLine(
				row.DistanceMeters.ToString("F0", culture) + " m" +
				" | " + row.BaseAimingTheta.ToString("F4", culture) +
				" | " + row.PoseMult.ToString("F2", culture) +
				" | " + row.DistanceMult.ToString("F2", culture) +
				" | " + row.FinalTheta.ToString("F4", culture) +
				" | " + row.SpreadDiameterMeters.ToString("F3", culture) + " m");
		}

		sb.AppendLine();
		sb.AppendLine("N14-PLAY validation:");
		for (int i = 0; i < s_DistancesMeters.Length; i++)
		{
			float aimingTheta = EvaluateTheta(_m4, _ammo, WeaponPoseState.Aiming, s_DistancesMeters[i]);
			float hipTheta = rows[i].FinalTheta;
			bool pass = hipTheta > aimingTheta + 0.0001f;
			sb.AppendLine(
				"@" + s_DistancesMeters[i].ToString("F0", culture) + " m Aimingθ=" +
				aimingTheta.ToString("F4", culture) + " HipFireθ=" + hipTheta.ToString("F4", culture) +
				" | " + (pass ? "PASS" : "FAIL"));
		}

		sb.AppendLine();
		sb.AppendLine("Form checks:");
		bool monotonic = true;
		for (int i = 1; i < rows.Length; i++)
		{
			if (rows[i].FinalTheta < rows[i - 1].FinalTheta - 0.0001f)
				monotonic = false;
		}

		AppendCheck(sb, "Finalθ monotonic with distance", monotonic);
		for (int i = 0; i < s_KeyDistanceMults.Length; i++)
		{
			(float distance, float expected) = s_KeyDistanceMults[i];
			float actual = WeaponPoseDistanceCurves.GetAccuracyMultiplier(WeaponPoseState.HipFire, distance);
			AppendCheck(
				sb,
				"DistanceMult @" + distance.ToString("F0", culture) + " m ≈ " + expected.ToString("F2", culture),
				Mathf.Abs(actual - expected) <= c_KeyMultTolerance);
		}

		AppendCheck(sb, "PoseMult logged as 2.5", Mathf.Abs(c_PoseMultHipFire - 2.5f) < 0.01f);
		AppendCheck(sb, "Finalθ matches evaluator (decomposition)", MatchDecomposition(rows));
		AppendCheck(sb, "Aiming vs HipFire @same distance (all distances)", AllAimingLessThanHip(_m4, _ammo, rows));
		sb.AppendLine();
		sb.AppendLine("FAIL triage: Pose × DistanceCurve × θ evaluator. Do not change Vertical/H/Recovery/Offset.");
		sb.AppendLine("Phase G not opened.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static CurveRow SampleCurve(WeaponDefinition _weapon, AmmoDefinition _ammo, float _distanceMeters)
	{
		float baseAiming = EvaluateTheta(_weapon, _ammo, WeaponPoseState.Aiming, _distanceMeters);
		float distanceMult = WeaponPoseDistanceCurves.GetAccuracyMultiplier(WeaponPoseState.HipFire, _distanceMeters);
		float finalTheta = EvaluateTheta(_weapon, _ammo, WeaponPoseState.HipFire, _distanceMeters);
		return new CurveRow
		{
			DistanceMeters = _distanceMeters,
			BaseAimingTheta = baseAiming,
			PoseMult = c_PoseMultHipFire,
			DistanceMult = distanceMult,
			FinalTheta = finalTheta,
			SpreadDiameterMeters = WeaponRecoilMath.SpreadDiameterMeters(_distanceMeters, finalTheta)
		};
	}

	private static float EvaluateTheta(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		WeaponPoseState _pose,
		float _distanceMeters)
	{
		var input = RecoilPlayShotAccuracyUtility.BuildAccuracyInput(
			_weapon,
			_ammo,
			_distanceMeters,
			_pose,
			WeaponFireMode.FullAuto,
			WeaponFireMode.FullAuto,
			WeaponAimMode.FullAim,
			WeaponAimMode.FullAim);
		return RecoilPlayShotAccuracyUtility.EvaluateHalfAngleDegrees(input);
	}

	private static bool AllAimingLessThanHip(WeaponDefinition _weapon, AmmoDefinition _ammo, CurveRow[] _rows)
	{
		for (int i = 0; i < _rows.Length; i++)
		{
			float aiming = EvaluateTheta(_weapon, _ammo, WeaponPoseState.Aiming, _rows[i].DistanceMeters);
			if (_rows[i].FinalTheta <= aiming + 0.0001f)
				return false;
		}

		return true;
	}

	private static bool MatchDecomposition(CurveRow[] _rows)
	{
		for (int i = 0; i < _rows.Length; i++)
		{
			CurveRow row = _rows[i];
			float composed = row.BaseAimingTheta * row.PoseMult * row.DistanceMult;
			if (Mathf.Abs(composed - row.FinalTheta) > row.FinalTheta * 0.15f + 0.01f)
				return false;
		}

		return true;
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _pass)
	{
		_sb.AppendLine("  [" + (_pass ? "PASS" : "FAIL") + "] " + _label);
	}
	#endregion
}
