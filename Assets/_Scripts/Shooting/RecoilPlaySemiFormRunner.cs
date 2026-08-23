using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase B9 MATH + short SIM_PLAY: Semi vs Auto on M4 / AK-47 / AK-74 / MK12 / SVD.
/// Tunes nothing. Does not retune Vertical / Horizontal / Recovery / Auto× / planner.
/// Does not replace RecoilPlayBaseline_LAST or RecoilPlayLmgForm_LAST.
/// </summary>
public static class RecoilPlaySemiFormRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ak47WeaponAssetName = "Weapon_AK47";
	public const string Ak74WeaponAssetName = "Weapon_AK74";
	public const string Mk12WeaponAssetName = "Weapon_MK12";
	public const string SvdWeaponAssetName = "Weapon_SVD";

	private static readonly int[] s_InstanceHashes = { 11, 29, 47 };
	private static readonly int[] s_RandomSeeds = { 101, 202, 303 };
	private const float c_ReadableSemiAfter5Degrees = 0.08f;
	private const float c_SvdDrainNoteDegrees = 0.05f;
	private const float c_SemiOverAutoMin = 0.30f;
	private const float c_SemiOverAutoMax = 0.85f;
	#endregion

	#region Nested Types
	private struct ModeSample
	{
		public WeaponFireMode FireMode;
		public float Multiplier;
		public float Kick1;
		public float IntervalSeconds;
		public float RecoveryPerShotWhileFiring;
		public Vector2 After1;
		public Vector2 After3;
		public Vector2 After5;
		public float Pause02;
		public float Pause04;
		public float Pause08;
		public int RecoilShotIndexAtShot5;
		public float Group5Cm;
	}

	private struct WeaponRow
	{
		public string Name;
		public float Vertical;
		public float Horizontal;
		public float Recovery;
		public float Rpm;
		public float SemiMultiplier;
		public float AutoMultiplier;
		public bool HasAutomaticMode;
		public ModeSample Semi;
		public ModeSample Auto;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4,
		WeaponDefinition _ak47,
		WeaponDefinition _ak74,
		WeaponDefinition _mk12,
		WeaponDefinition _svd)
	{
		var sb = new StringBuilder(4096);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlaySemiForm MATH + SIM_PLAY");
		sb.AppendLine("Phase B9 Semi×: M4 / AK-47 / AK-74 / MK12 / SVD. Metric is Semi vs Auto, not |Offset| size.");
		sb.AppendLine("Same FireRateRpm cap for both modes (SemiAutoFireRateRpm=0 on rifles). InstanceHash=0 MATH.");
		sb.AppendLine("Aiming, stand, no attachments, RecoilControl 50. Do not retune V/H/Rec/Auto×.");
		sb.AppendLine("SVD: full drain between shots at 150 RPM is allowed. Group cm is hitscan cone — ignore vs M4 11.8 cm.");
		sb.AppendLine();

		WeaponRow m4 = Sample(_m4);
		WeaponRow ak47 = Sample(_ak47);
		WeaponRow ak74 = Sample(_ak74);
		WeaponRow mk12 = Sample(_mk12);
		WeaponRow svd = Sample(_svd);
		AppendWeapon(sb, in m4, culture);
		AppendWeapon(sb, in ak47, culture);
		AppendWeapon(sb, in ak74, culture);
		AppendWeapon(sb, in mk12, culture);
		AppendWeapon(sb, in svd, culture);

		sb.AppendLine("Form checks (Semi vs Auto, not cm):");
		AppendCheck(sb, "M4 Semi |Offset|5 < Auto |Offset|5", m4.Semi.After5.magnitude < m4.Auto.After5.magnitude);
		AppendCheck(sb, "AK-74 Semi |Offset|5 < Auto |Offset|5", ak74.Semi.After5.magnitude < ak74.Auto.After5.magnitude);
		AppendCheck(sb, "AK-47 Semi |Offset|5 < Auto |Offset|5", ak47.Semi.After5.magnitude < ak47.Auto.After5.magnitude);
		AppendCheck(sb, "MK12 Semi |Offset|5 < Auto |Offset|5", mk12.Semi.After5.magnitude < mk12.Auto.After5.magnitude);
		AppendCheck(
			sb,
			"Semi class M4 < AK-74 < AK-47 after 5",
			m4.Semi.After5.magnitude < ak74.Semi.After5.magnitude &&
			ak74.Semi.After5.magnitude < ak47.Semi.After5.magnitude);
		AppendCheck(
			sb,
			"M4 Semi after 5 readable (not AR drain)",
			m4.Semi.After5.magnitude >= c_ReadableSemiAfter5Degrees);
		AppendCheck(sb, "M4 Semi/Auto |Offset|@5 in 0.30–0.85", RatioInBand(in m4));
		AppendCheck(sb, "AK-74 Semi/Auto |Offset|@5 in 0.30–0.85", RatioInBand(in ak74));
		AppendCheck(sb, "AK-47 Semi/Auto |Offset|@5 in 0.30–0.85", RatioInBand(in ak47));
		AppendCheck(
			sb,
			"MK12 Semi quieter than M4 Semi after 5 (DMR)",
			mk12.Semi.After5.magnitude < m4.Semi.After5.magnitude);
		bool svdDrain = svd.Semi.After5.magnitude < c_SvdDrainNoteDegrees;
		sb.AppendLine(
			"  " + (svdDrain ? "NOTE" : "OK  ") +
			"  SVD Semi after 5 drain allowed (low RPM): |Offset|=" +
			svd.Semi.After5.magnitude.ToString("F3", culture) + "°");
		sb.AppendLine(
			"  RecoilShotIndex@5 Semi (must not snap 0 on rifles): M4=" +
			m4.Semi.RecoilShotIndexAtShot5 + " AK-47=" + ak47.Semi.RecoilShotIndexAtShot5 +
			" AK-74=" + ak74.Semi.RecoilShotIndexAtShot5);
		sb.AppendLine("  Assets not changed this pass. V/H/Rec/Auto× frozen. LMG not in this set.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static WeaponRow Sample(WeaponDefinition _weapon)
	{
		var row = new WeaponRow { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return row;

		row.Vertical = _weapon.VerticalRecoil;
		row.Horizontal = _weapon.HorizontalRecoil;
		row.Recovery = _weapon.RecoilRecoveryPerSecond;
		row.Rpm = _weapon.FireRateRpm;
		row.SemiMultiplier = _weapon.SemiAutoRecoilMultiplier;
		row.AutoMultiplier = _weapon.AutoRecoilMultiplier;
		row.HasAutomaticMode = HasAutomaticMode(_weapon);
		row.Semi = SampleMode(_weapon, WeaponFireMode.SemiAuto);
		WeaponFireMode autoMode = row.HasAutomaticMode
			? WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon)
			: WeaponFireMode.FullAuto;
		row.Auto = SampleMode(_weapon, autoMode);
		return row;
	}

	private static ModeSample SampleMode(WeaponDefinition _weapon, WeaponFireMode _fireMode)
	{
		var sample = new ModeSample { FireMode = _fireMode };
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			_fireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);
		sample.Multiplier = WeaponRecoilMath.ResolveFireModeMultiplier(_weapon, _fireMode);
		sample.Kick1 = WeaponRecoilMath.ComputeKick(in context, 1, 0f).Delta.magnitude;
		sample.IntervalSeconds = 60f / Mathf.Max(1f, _weapon.FireRateRpm);
		sample.RecoveryPerShotWhileFiring =
			WeaponRecoilMath.ComposeRecoveryPerSecond(in context, true, true) * sample.IntervalSeconds;
		sample.After1 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 1);
		sample.After3 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 3);
		sample.After5 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 5);
		sample.Pause02 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.2f).magnitude;
		sample.Pause04 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.4f).magnitude;
		sample.Pause08 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.8f).magnitude;

		RecoilPlayBaselineSimulator.BurstResult burst5 = MedianBurst(_weapon, _fireMode, 5);
		sample.Group5Cm = burst5.CenterAbsCm;
		sample.RecoilShotIndexAtShot5 = burst5.RecoilShotIndexAtLastShot;
		return sample;
	}

	private static RecoilPlayBaselineSimulator.BurstResult MedianBurst(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		int _shotCount)
	{
		RecoilPlayBaselineSimulator.BurstResult a = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon,
			RecoilPlayBaselineProtocol.CaseId.A1AimingStand50,
			_shotCount,
			s_InstanceHashes[0],
			s_RandomSeeds[0],
			_fireMode);
		RecoilPlayBaselineSimulator.BurstResult b = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon,
			RecoilPlayBaselineProtocol.CaseId.A1AimingStand50,
			_shotCount,
			s_InstanceHashes[1],
			s_RandomSeeds[1],
			_fireMode);
		RecoilPlayBaselineSimulator.BurstResult c = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon,
			RecoilPlayBaselineProtocol.CaseId.A1AimingStand50,
			_shotCount,
			s_InstanceHashes[2],
			s_RandomSeeds[2],
			_fireMode);
		float median = RecoilPlayBaselineProtocol.Median3(a.CenterAbsCm, b.CenterAbsCm, c.CenterAbsCm);
		if (Mathf.Approximately(median, b.CenterAbsCm))
			return b;
		if (Mathf.Approximately(median, a.CenterAbsCm))
			return a;
		return c;
	}

	private static bool HasAutomaticMode(WeaponDefinition _weapon)
	{
		WeaponFireMode[] modes = _weapon != null ? _weapon.AvailableFireModes : null;
		if (modes == null)
			return false;
		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] == WeaponFireMode.FullAuto ||
			    modes[i] == WeaponFireMode.Auto ||
			    modes[i] == WeaponFireMode.Burst)
				return true;
		}

		return false;
	}

	private static bool RatioInBand(in WeaponRow _row)
	{
		float autoMag = _row.Auto.After5.magnitude;
		if (autoMag < 1e-4f)
			return false;
		float ratio = _row.Semi.After5.magnitude / autoMag;
		return ratio >= c_SemiOverAutoMin && ratio <= c_SemiOverAutoMax;
	}

	private static void AppendWeapon(StringBuilder _sb, in WeaponRow _row, CultureInfo _culture)
	{
		_sb.AppendLine(_row.Name);
		_sb.AppendLine(
			"  Asset V=" + _row.Vertical.ToString("F3", _culture) +
			"° H=" + _row.Horizontal.ToString("F3", _culture) +
			"° Rec=" + _row.Recovery.ToString("F2", _culture) +
			"°/s RPM=" + _row.Rpm.ToString("F0", _culture) +
			" Semi×=" + _row.SemiMultiplier.ToString("F2", _culture) +
			" Auto×=" + _row.AutoMultiplier.ToString("F2", _culture) +
			(_row.HasAutomaticMode ? "" : " (Auto MATH only, mode not on weapon)"));
		AppendMode(_sb, "Semi", in _row.Semi, _culture);
		AppendMode(_sb, "Auto", in _row.Auto, _culture);
		float auto5 = _row.Auto.After5.magnitude;
		float ratio = auto5 > 1e-4f ? _row.Semi.After5.magnitude / auto5 : 0f;
		_sb.AppendLine(
			"  Semi/Auto |Offset|@5=" + ratio.ToString("F2", _culture) +
			"  kick× ratio=" + (_row.AutoMultiplier > 1e-4f
				? (_row.SemiMultiplier / _row.AutoMultiplier).ToString("F2", _culture)
				: "0"));
		_sb.AppendLine();
	}

	private static void AppendMode(StringBuilder _sb, string _label, in ModeSample _sample, CultureInfo _culture)
	{
		_sb.AppendLine(
			"  " + _label + " (" + _sample.FireMode + " ×" +
			_sample.Multiplier.ToString("F2", _culture) +
			") kick1=" + _sample.Kick1.ToString("F3", _culture) +
			"° interval=" + _sample.IntervalSeconds.ToString("F3", _culture) +
			"s rec/shot=" + _sample.RecoveryPerShotWhileFiring.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"    after 1: X=" + _sample.After1.x.ToString("F3", _culture) +
			"° Y=" + _sample.After1.y.ToString("F3", _culture) +
			"° |Offset|=" + _sample.After1.magnitude.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"    after 3: X=" + _sample.After3.x.ToString("F3", _culture) +
			"° Y=" + _sample.After3.y.ToString("F3", _culture) +
			"° |Offset|=" + _sample.After3.magnitude.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"    after 5: X=" + _sample.After5.x.ToString("F3", _culture) +
			"° Y=" + _sample.After5.y.ToString("F3", _culture) +
			"° |Offset|=" + _sample.After5.magnitude.ToString("F3", _culture) +
			"°  pause 0.2/0.4/0.8=" +
			_sample.Pause02.ToString("F3", _culture) + "/" +
			_sample.Pause04.ToString("F3", _culture) + "/" +
			_sample.Pause08.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"    SIM_PLAY group@50m cone (ignore vs M4): 5=" +
			_sample.Group5Cm.ToString("F1", _culture) +
			" cm  RecoilShotIndex@5=" + _sample.RecoilShotIndexAtShot5);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
