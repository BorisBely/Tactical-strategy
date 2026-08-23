using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase F1 MATH regression for Benelli / M2 / MK19 (B4 anchors). No infantry stance matrix.
/// </summary>
public static class RecoilPlayF1SpecialFormRunner
{
	#region Constants
	public const string BenelliWeaponAssetName = "Weapon_BenelliM4";
	public const string M2WeaponAssetName = "Weapon_M2Browning_127";
	public const string Mk19WeaponAssetName = "Weapon_MK19";

	private const float c_RegressionEpsilonDegrees = 0.02f;
	#endregion

	#region Nested Types
	private struct SpecialRow
	{
		public string Name;
		public WeaponFireMode FireMode;
		public float Off3;
		public float Off5;
		public float Off10;
		public float NetDrift;
		public float Pause04After5;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _benelli,
		WeaponDefinition _m2,
		WeaponDefinition _mk19)
	{
		var sb = new StringBuilder(2048);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayF1SpecialForm MATH");
		sb.AppendLine("Phase F1: Benelli / M2 / MK19 baseline regression (B4). Aiming stand, InstanceHash=0.");
		sb.AppendLine("No infantry stance/pose matrix on turret weapons. Shotgun pellet spread = F2.");
		sb.AppendLine();

		SpecialRow benelli = Sample(_benelli);
		SpecialRow m2 = Sample(_m2);
		SpecialRow mk19 = Sample(_mk19);

		AppendRow(sb, in benelli, culture);
		AppendRow(sb, in m2, culture);
		AppendRow(sb, in mk19, culture);

		sb.AppendLine("Form checks:");
		AppendCheck(sb, "Benelli Off5 > 0", benelli.Off5 > 0.01f);
		AppendCheck(sb, "Benelli Off5 ≈ 0.352° (B4)", Mathf.Abs(benelli.Off5 - 0.352f) <= c_RegressionEpsilonDegrees);
		AppendCheck(sb, "M2 Off5 > Benelli Off5", m2.Off5 > benelli.Off5);
		AppendCheck(sb, "M2 Off5 ≈ 0.838° (B4)", Mathf.Abs(m2.Off5 - 0.838f) <= c_RegressionEpsilonDegrees);
		AppendCheck(sb, "MK19 Off5 > M2 Off5", mk19.Off5 > m2.Off5);
		AppendCheck(sb, "MK19 Off5 ≈ 1.122° (B4)", Mathf.Abs(mk19.Off5 - 1.122f) <= c_RegressionEpsilonDegrees);
		AppendCheck(sb, "M2 NetDrift 5→10 > 0", m2.NetDrift > 0f);
		AppendCheck(sb, "MK19 NetDrift 5→10 > 0", mk19.NetDrift > 0f);
		AppendCheck(sb, "MK19 pause 0.4 after 5 heavy", mk19.Pause04After5 > 0.5f);
		sb.AppendLine("  Assets not changed. E phase frozen. F2 Benelli spread @15/40 m next.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static SpecialRow Sample(WeaponDefinition _weapon)
	{
		var row = new SpecialRow { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return row;

		row.FireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			row.FireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);

		row.Off3 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 3).magnitude;
		row.Off5 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 5).magnitude;
		row.Off10 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 10).magnitude;
		row.NetDrift = (row.Off10 - row.Off5) / 5f;
		row.Pause04After5 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.4f).magnitude;
		return row;
	}

	private static void AppendRow(StringBuilder _sb, in SpecialRow _row, CultureInfo _culture)
	{
		_sb.AppendLine(_row.Name + " (" + _row.FireMode + ")");
		_sb.AppendLine(
			"  Off3=" + _row.Off3.ToString("F3", _culture) +
			"°  Off5=" + _row.Off5.ToString("F3", _culture) +
			"°  Off10=" + _row.Off10.ToString("F3", _culture) +
			"°  nd=" + _row.NetDrift.ToString("F4", _culture) +
			"  pause0.4=" + _row.Pause04After5.ToString("F3", _culture) + "°");
		_sb.AppendLine();
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
