using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase B13 short SIM_PLAY sanity after B12 N7 sheet. Spot-check M4 / AK-47 / M249 / PKM / MK12.
/// Does not retune assets. Not a full Phase A baseline rerun.
/// </summary>
public static class RecoilPlayB13SanityRunner
{
	#region Constants
	public const string Ak47WeaponAssetName = "Weapon_AK47";
	public const string Mk12WeaponAssetName = "Weapon_MK12";

	private static readonly int[] s_InstanceHashes = { 11, 29, 47 };
	private static readonly int[] s_RandomSeeds = { 101, 202, 303 };

	private const float c_AnchorEpsilonDegrees = 0.015f;
	private const float c_M4Group5CmAnchor = 11.8f;
	private const float c_GroupCmWarnRatio = 0.20f;
	private const float c_Mk12SemiMaxOff5Degrees = 0.15f;
	#endregion

	#region Nested Types
	private struct SpotCheck
	{
		public string Name;
		public WeaponFireMode FireMode;
		public float MathOff5;
		public float SimOffBeforeShot5Deg;
		public float Group5Cm;
		public int RecoilShotIndexAt5;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4,
		WeaponDefinition _ak47,
		WeaponDefinition _m249,
		WeaponDefinition _pkm,
		WeaponDefinition _mk12)
	{
		var sb = new StringBuilder(2048);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayB13Sanity SIM_PLAY spot-check");
		sb.AppendLine("After B12 N7 sheet. Not full Phase A baseline. MATH Off5 vs SIM offset + group cm @50m.");
		sb.AppendLine("Group cm = hitscan cone. M4 anchor group 5 ≈ 11.8 cm (Phase A).");
		sb.AppendLine();

		SpotCheck m4 = Sample(_m4, WeaponFireMode.FullAuto);
		SpotCheck ak47 = Sample(_ak47, WeaponFireMode.FullAuto);
		SpotCheck m249 = Sample(_m249, WeaponFireMode.FullAuto);
		SpotCheck pkm = Sample(_pkm, WeaponFireMode.FullAuto);
		SpotCheck mk12 = Sample(_mk12, WeaponFireMode.SemiAuto);
		AppendSpot(sb, in m4, culture);
		AppendSpot(sb, in ak47, culture);
		AppendSpot(sb, in m249, culture);
		AppendSpot(sb, in pkm, culture);
		AppendSpot(sb, in mk12, culture);

		sb.AppendLine("Form checks:");
		AppendCheck(sb, "M4 Math Off5 ≈ 0.313° (B0)", Mathf.Abs(m4.MathOff5 - 0.313f) <= c_AnchorEpsilonDegrees);
		AppendCheck(sb, "M249 Math Off5 ≈ 0.254° (B8)", Mathf.Abs(m249.MathOff5 - 0.254f) <= c_AnchorEpsilonDegrees);
		AppendCheck(sb, "PKM Math Off5 ≈ 0.296° (B8)", Mathf.Abs(pkm.MathOff5 - 0.296f) <= c_AnchorEpsilonDegrees);
		AppendCheck(sb, "M4 Off5 < AK-47 Off5", m4.MathOff5 < ak47.MathOff5);
		AppendCheck(sb, "M249 Off5 < PKM Off5", m249.MathOff5 < pkm.MathOff5);
		AppendCheck(
			sb,
			"M4 group5 near Phase A 11.8 cm",
			Mathf.Abs(m4.Group5Cm - c_M4Group5CmAnchor) / c_M4Group5CmAnchor <= c_GroupCmWarnRatio);
		AppendCheck(sb, "MK12 Semi Off5 quiet (no fake stack)", mk12.MathOff5 < c_Mk12SemiMaxOff5Degrees);
		AppendCheck(sb, "Math Off5 > offset before 5th shot (positive drift)", m4.MathOff5 > m4.SimOffBeforeShot5Deg);
		AppendCheck(sb, "RecoilShotIndex@5 not snap 0 (M4)", m4.RecoilShotIndexAt5 == 4);
		sb.AppendLine("  Assets not changed. B12 sheet is canonical N7 reference.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static SpotCheck Sample(WeaponDefinition _weapon, WeaponFireMode _fireMode)
	{
		var spot = new SpotCheck
		{
			Name = _weapon != null ? _weapon.name : "?",
			FireMode = _fireMode
		};
		if (_weapon == null)
			return spot;

		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			_fireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);
		spot.MathOff5 = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, 5);

		RecoilPlayBaselineSimulator.BurstResult burst = MedianBurst(_weapon, _fireMode, 5);
		spot.SimOffBeforeShot5Deg = burst.RecoilOffsetAtLastShotDeg.magnitude;
		spot.Group5Cm = burst.CenterAbsCm;
		spot.RecoilShotIndexAt5 = burst.RecoilShotIndexAtLastShot;
		return spot;
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

	private static void AppendSpot(StringBuilder _sb, in SpotCheck _spot, CultureInfo _culture)
	{
		_sb.AppendLine(_spot.Name + " (" + _spot.FireMode + ")");
		_sb.AppendLine(
			"  Math Off@5=" + _spot.MathOff5.ToString("F3", _culture) +
			"°  SIM before shot5=" + _spot.SimOffBeforeShot5Deg.ToString("F3", _culture) +
			"°  group5=" + _spot.Group5Cm.ToString("F1", _culture) +
			" cm  ShotIndex@5=" + _spot.RecoilShotIndexAt5);
		_sb.AppendLine();
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
