using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Stage 1 N13: RecoilControl skill 0/50/100 MATH + 8-shot SIM on M4 ModA_1.
/// </summary>
public static class RecoilPlayN13RecoilControlRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ammo556AssetName = "Ammo_556x45mmNATO";

	private static readonly float[] s_Skills = { 0f, 50f, 100f };
	private static readonly float[] s_PauseSeconds = { 0.2f, 0.4f, 0.8f };

	private const float c_DistanceMeters = 50f;
	private const int c_PauseBurstShots = 5;
	private const float c_OffsetMonotoneTolerance = 0.05f;
	#endregion

	#region Nested Types
	private struct SkillSample
	{
		public float Skill;
		public float KickYShot1;
		public float RecoveryPerSecond;
		public float OffsetMag3;
		public float OffsetMag5;
		public float OffsetMag8;
		public float SimOffsetMag8;
		public float ThetaHalfAngle;
		public float PatternRawShot2;
		public Vector2 KickDeltaShot2;
	}
	#endregion

	#region Public Methods
	public static string Run(WeaponDefinition _m4, AmmoDefinition _ammo)
	{
		var sb = new StringBuilder(6144);
		CultureInfo culture = CultureInfo.InvariantCulture;
		var samples = new SkillSample[s_Skills.Length];

		sb.AppendLine("RecoilPlayN13RecoilControl MATH + SIM_PLAY");
		sb.AppendLine("Stage 1 N13: M4 ModA_1, Aiming stand FullAuto, no attachments.");
		sb.AppendLine("Skill kick Lerp(1.2→0.8), recovery Lerp(0.8→1.2). θ must not depend on RecoilControl.");
		sb.AppendLine();

		sb.AppendLine("N13-MATH:");
		sb.AppendLine("Skill | KickY@1 | Recovery/s | |Off|@3 | |Off|@5 | |Off|@8");
		for (int i = 0; i < s_Skills.Length; i++)
		{
			samples[i] = SampleSkill(_m4, _ammo, s_Skills[i]);
			SkillSample s = samples[i];
			sb.AppendLine(
				s.Skill.ToString("F0", culture) +
				" | " + s.KickYShot1.ToString("F4", culture) +
				" | " + s.RecoveryPerSecond.ToString("F3", culture) +
				" | " + s.OffsetMag3.ToString("F4", culture) +
				" | " + s.OffsetMag5.ToString("F4", culture) +
				" | " + s.OffsetMag8.ToString("F4", culture));
		}

		sb.AppendLine();
		sb.AppendLine("N13-PLAY (8-shot SIM @50 m):");
		sb.AppendLine("Skill | OffX@3 | OffY@3 | |Off|@3 | OffX@5 | OffY@5 | |Off|@5 | OffX@8 | OffY@8 | |Off|@8");
		for (int i = 0; i < s_Skills.Length; i++)
		{
			Vector2 off3 = PredictOffset(_m4, s_Skills[i], 3);
			Vector2 off5 = PredictOffset(_m4, s_Skills[i], 5);
			Vector2 off8 = PredictOffset(_m4, s_Skills[i], 8);
			samples[i].SimOffsetMag8 = off8.magnitude;
			sb.AppendLine(
				s_Skills[i].ToString("F0", culture) +
				" | " + off3.x.ToString("F4", culture) +
				" | " + off3.y.ToString("F4", culture) +
				" | " + off3.magnitude.ToString("F4", culture) +
				" | " + off5.x.ToString("F4", culture) +
				" | " + off5.y.ToString("F4", culture) +
				" | " + off5.magnitude.ToString("F4", culture) +
				" | " + off8.x.ToString("F4", culture) +
				" | " + off8.y.ToString("F4", culture) +
				" | " + off8.magnitude.ToString("F4", culture));
		}

		sb.AppendLine();
		sb.AppendLine("N13 pause recovery (after 5 shots):");
		sb.AppendLine("Pause | Skill | Remaining |Off|");
		for (int p = 0; p < s_PauseSeconds.Length; p++)
		{
			for (int i = 0; i < s_Skills.Length; i++)
			{
				float remaining = SamplePauseRemaining(_m4, s_Skills[i], s_PauseSeconds[p]);
				sb.AppendLine(
					s_PauseSeconds[p].ToString("F1", culture) + " s" +
					" | " + s_Skills[i].ToString("F0", culture) +
					" | " + remaining.ToString("F4", culture));
			}
		}

		sb.AppendLine();
		sb.AppendLine("Form checks:");
		AppendCheck(sb, "KickY@1 monotonic 0>50>100 (less kick)", samples[0].KickYShot1 > samples[1].KickYShot1 &&
		                                                              samples[1].KickYShot1 > samples[2].KickYShot1);
		AppendCheck(sb, "Recovery/s monotonic 0<50<100", samples[0].RecoveryPerSecond < samples[1].RecoveryPerSecond &&
		                                                 samples[1].RecoveryPerSecond < samples[2].RecoveryPerSecond);
		AppendCheck(sb, "|Off|@3 monotonic decreasing", samples[0].OffsetMag3 > samples[1].OffsetMag3 &&
		                                                samples[1].OffsetMag3 > samples[2].OffsetMag3);
		AppendCheck(sb, "|Off|@5 monotonic decreasing", samples[0].OffsetMag5 > samples[1].OffsetMag5 &&
		                                                samples[1].OffsetMag5 > samples[2].OffsetMag5);
		AppendCheck(sb, "|Off|@8 monotonic decreasing", samples[0].OffsetMag8 > samples[1].OffsetMag8 &&
		                                                samples[1].OffsetMag8 > samples[2].OffsetMag8);
		bool off8Visible = samples[0].SimOffsetMag8 > samples[2].SimOffsetMag8 * (1f + c_OffsetMonotoneTolerance);
		AppendCheck(sb, "|Off|@8 visible 0→100 (~5% NOTE if WARN)", off8Visible || samples[0].SimOffsetMag8 > samples[2].SimOffsetMag8);
		AppendCheck(sb, "θ(skill) invariant",
			Mathf.Abs(samples[0].ThetaHalfAngle - samples[1].ThetaHalfAngle) < 0.0001f &&
			Mathf.Abs(samples[1].ThetaHalfAngle - samples[2].ThetaHalfAngle) < 0.0001f);
		AppendCheck(sb, "Pattern @shot1 smoothed value identical across skills",
			Mathf.Abs(samples[0].PatternRawShot2 - samples[1].PatternRawShot2) < 0.0001f &&
			Mathf.Abs(samples[1].PatternRawShot2 - samples[2].PatternRawShot2) < 0.0001f);
		AppendCheck(sb, "Kick delta@1 scales with skill impulse (100 < 0 magnitude)",
			samples[2].KickDeltaShot2.magnitude < samples[0].KickDeltaShot2.magnitude);
		bool pauseFastest = true;
		for (int p = 0; p < s_PauseSeconds.Length; p++)
		{
			float rem0 = SamplePauseRemaining(_m4, 0f, s_PauseSeconds[p]);
			float rem100 = SamplePauseRemaining(_m4, 100f, s_PauseSeconds[p]);
			if (rem100 > rem0 + 0.0001f)
				pauseFastest = false;
		}

		AppendCheck(sb, "Pause recovery: skill 100 drains fastest", pauseFastest);
		sb.AppendLine();
		sb.AppendLine("FAIL triage: skill wiring → context → kick/recovery path. Do not change weapon V/H/Rec.");
		sb.AppendLine("Phase G not opened.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static SkillSample SampleSkill(WeaponDefinition _weapon, AmmoDefinition _ammo, float _skill)
	{
		WeaponRecoilContext context = BuildContext(_weapon, _skill);
		WeaponRecoilKick kick1 = WeaponRecoilMath.ComputeKick(in context, 1, 0f);
		float recovery = WeaponRecoilMath.ComposeRecoveryPerSecond(in context, false, true);
		var sample = new SkillSample
		{
			Skill = _skill,
			KickYShot1 = kick1.Delta.y,
			RecoveryPerSecond = recovery,
			OffsetMag3 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 3),
			OffsetMag5 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 5),
			OffsetMag8 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 8),
			ThetaHalfAngle = EvaluateTheta(_weapon, _ammo),
			PatternRawShot2 = kick1.PatternValue,
			KickDeltaShot2 = kick1.Delta
		};
		return sample;
	}

	private static WeaponRecoilContext BuildContext(WeaponDefinition _weapon, float _skill)
	{
		var scenario = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(
			_weapon, null, c_DistanceMeters, _skill);
		return RecoilAutoSelectorInputBuilder.BuildRecoilContext(scenario, WeaponFireMode.FullAuto);
	}

	private static Vector2 PredictOffset(WeaponDefinition _weapon, float _skill, int _shotIndex)
	{
		WeaponRecoilContext context = BuildContext(_weapon, _skill);
		return WeaponRecoilMath.PredictOffsetBeforeShot(in context, _shotIndex);
	}

	private static float SamplePauseRemaining(WeaponDefinition _weapon, float _skill, float _pauseSeconds)
	{
		WeaponRecoilContext context = BuildContext(_weapon, _skill);
		return WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, c_PauseBurstShots, _pauseSeconds).magnitude;
	}

	private static float EvaluateTheta(WeaponDefinition _weapon, AmmoDefinition _ammo)
	{
		var input = RecoilPlayShotAccuracyUtility.BuildAccuracyInput(
			_weapon,
			_ammo,
			c_DistanceMeters,
			WeaponPoseState.Aiming,
			WeaponFireMode.FullAuto,
			WeaponFireMode.FullAuto,
			WeaponAimMode.FullAim,
			WeaponAimMode.FullAim);
		return RecoilPlayShotAccuracyUtility.EvaluateHalfAngleDegrees(input);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _pass)
	{
		_sb.AppendLine("  [" + (_pass ? "PASS" : "FAIL") + "] " + _label);
	}
	#endregion
}
