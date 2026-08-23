using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase C2 MATH pose form for reference weapons M4 / AK-47 / M249 / PKM.
/// Standing idle only. Does not retune assets or BaseShotDispersion.
/// </summary>
public static class RecoilPlayC2PoseFormRunner
{
	#region Constants
	public const string Ak47WeaponAssetName = "Weapon_AK47";

	private const float c_B0RegressionEpsilonDegrees = 0.015f;
	private const float c_MinHipFireOverAimingRatio = 1.25f;
	#endregion

	#region Nested Types
	private struct PoseRow
	{
		public string Name;
		public float Aiming5;
		public float PointAim5;
		public float PreAim5;
		public float HipFire5;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4,
		WeaponDefinition _ak47,
		WeaponDefinition _m249,
		WeaponDefinition _pkm)
	{
		var sb = new StringBuilder(2560);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayC2PoseForm MATH");
		sb.AppendLine("Phase C2: pose |Offset|@5. Standing idle, FullAuto baseline mode, InstanceHash=0.");
		sb.AppendLine(
			"Pose kick/recovery: Aiming 1/1, PointAim 1.10/0.90, PreAim 1.15/0.85, HipFire 1.35/0.70.");
		sb.AppendLine("Form: Aiming < PointAim < PreAim < HipFire. Spread θ not evaluated here.");
		sb.AppendLine();

		PoseRow m4 = Sample(_m4);
		PoseRow ak47 = Sample(_ak47);
		PoseRow m249 = Sample(_m249);
		PoseRow pkm = Sample(_pkm);

		AppendWeaponBlock(sb, in m4, culture);
		AppendWeaponBlock(sb, in ak47, culture);
		AppendWeaponBlock(sb, in m249, culture);
		AppendWeaponBlock(sb, in pkm, culture);

		sb.AppendLine("Form checks:");
		AppendPoseChecks(sb, in m4, "M4");
		AppendPoseChecks(sb, in ak47, "AK-47");
		AppendPoseChecks(sb, in m249, "M249");
		AppendPoseChecks(sb, in pkm, "PKM");
		AppendCheck(sb, "M4 Aiming @5 ≈ 0.313° (B0 frozen)",
			Mathf.Abs(m4.Aiming5 - 0.313f) <= c_B0RegressionEpsilonDegrees);
		AppendCheck(sb, "M4 HipFire/Aiming ratio readable (≥1.25)",
			m4.HipFire5 / Mathf.Max(m4.Aiming5, 1e-4f) >= c_MinHipFireOverAimingRatio);
		sb.AppendLine(
			"  NOTE: HipFire/Aiming > kick×1.35 when recovery×0.70 applies between shots — expected.");
		sb.AppendLine("  Assets not changed. Stance matrix frozen (C1). N14 distance curve not here.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static PoseRow Sample(WeaponDefinition _weapon)
	{
		var row = new PoseRow { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return row;

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		row.Aiming5 = Off5(_weapon, fireMode, WeaponPoseState.Aiming);
		row.PointAim5 = Off5(_weapon, fireMode, WeaponPoseState.PointAim);
		row.PreAim5 = Off5(_weapon, fireMode, WeaponPoseState.PreAim);
		row.HipFire5 = Off5(_weapon, fireMode, WeaponPoseState.HipFire);
		return row;
	}

	private static float Off5(WeaponDefinition _weapon, WeaponFireMode _fireMode, WeaponPoseState _pose)
	{
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			_fireMode,
			_pose,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);
		return WeaponRecoilMath.PredictOffsetAfterShots(in context, 5).magnitude;
	}

	private static void AppendWeaponBlock(StringBuilder _sb, in PoseRow _row, CultureInfo _culture)
	{
		float aiming = Mathf.Max(_row.Aiming5, 1e-4f);
		_sb.AppendLine(_row.Name + " (FullAuto standing)");
		_sb.AppendLine(
			"  Aiming=" + _row.Aiming5.ToString("F3", _culture) +
			"°  PointAim=" + _row.PointAim5.ToString("F3", _culture) +
			"°  PreAim=" + _row.PreAim5.ToString("F3", _culture) +
			"°  HipFire=" + _row.HipFire5.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  ratios Point/Aim=" + (_row.PointAim5 / aiming).ToString("F2", _culture) +
			"  Pre/Aim=" + (_row.PreAim5 / aiming).ToString("F2", _culture) +
			"  Hip/Aim=" + (_row.HipFire5 / aiming).ToString("F2", _culture));
		_sb.AppendLine();
	}

	private static void AppendPoseChecks(StringBuilder _sb, in PoseRow _row, string _label)
	{
		AppendCheck(_sb, _label + " PointAim > Aiming", _row.PointAim5 > _row.Aiming5);
		AppendCheck(_sb, _label + " PreAim > PointAim", _row.PreAim5 > _row.PointAim5);
		AppendCheck(_sb, _label + " HipFire > PreAim", _row.HipFire5 > _row.PreAim5);
		AppendCheck(_sb, _label + " HipFire > Aiming", _row.HipFire5 > _row.Aiming5);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
