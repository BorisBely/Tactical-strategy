using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase C1 MATH stance/movement form for reference weapons M4 / AK-47 / M249 / PKM.
/// Aiming only. Does not retune assets or BaseShotDispersion.
/// </summary>
public static class RecoilPlayC1StanceFormRunner
{
	#region Constants
	public const string Ak47WeaponAssetName = "Weapon_AK47";

	private const float c_SprintKickMultiplier = 1.6f;
	private const float c_SprintRecoveryMultiplier = 0.5f;
	private const float c_B0RegressionEpsilonDegrees = 0.015f;
	#endregion

	#region Nested Types
	private struct StanceRow
	{
		public string Name;
		public float Stand5;
		public float Crouch5;
		public float Walk5;
		public float Sprint5;
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
		sb.AppendLine("RecoilPlayC1StanceForm MATH");
		sb.AppendLine("Phase C1: stance/movement |Offset|@5. Aiming, FullAuto baseline mode, InstanceHash=0.");
		sb.AppendLine("Kick/recovery: Stand 1/1, Crouch 0.95/1.10, Walk 1.25/0.85, Sprint 1.60/0.50.");
		sb.AppendLine("Relative form Walk>Stand, Crouch<Stand, Sprint>Stand. Assets not changed.");
		sb.AppendLine();

		StanceRow m4 = Sample(_m4);
		StanceRow ak47 = Sample(_ak47);
		StanceRow m249 = Sample(_m249);
		StanceRow pkm = Sample(_pkm);

		AppendWeaponBlock(sb, in m4, culture);
		AppendWeaponBlock(sb, in ak47, culture);
		AppendWeaponBlock(sb, in m249, culture);
		AppendWeaponBlock(sb, in pkm, culture);

		sb.AppendLine("Form checks:");
		AppendStanceChecks(sb, in m4, "M4");
		AppendStanceChecks(sb, in ak47, "AK-47");
		AppendStanceChecks(sb, in m249, "M249");
		AppendStanceChecks(sb, in pkm, "PKM");
		AppendCheck(sb, "M4 Stand @5 ≈ 0.313° (B0 frozen)",
			Mathf.Abs(m4.Stand5 - 0.313f) <= c_B0RegressionEpsilonDegrees);
		sb.AppendLine(
			"  NOTE: Walk/Stand ratio not monotonic with kick class (M4 " +
			(m4.Walk5 / Mathf.Max(m4.Stand5, 1e-4f)).ToString("F2", CultureInfo.InvariantCulture) +
			" > AK-47 " +
			(ak47.Walk5 / Mathf.Max(ak47.Stand5, 1e-4f)).ToString("F2", CultureInfo.InvariantCulture) +
			") — walk recovery×0.85 dominates on high-Rec weapons.");
		sb.AppendLine("  Assets not changed. Phase B numbers frozen. Pose matrix = C2.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static StanceRow Sample(WeaponDefinition _weapon)
	{
		var row = new StanceRow { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return row;

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		row.Stand5 = Off5(_weapon, fireMode,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);
		row.Crouch5 = Off5(_weapon, fireMode,
			RecoilPlayBaselineProtocol.CrouchKickMultiplier,
			RecoilPlayBaselineProtocol.CrouchRecoveryMultiplier);
		row.Walk5 = Off5(_weapon, fireMode,
			RecoilPlayBaselineProtocol.WalkKickMultiplier,
			RecoilPlayBaselineProtocol.WalkRecoveryMultiplier);
		row.Sprint5 = Off5(_weapon, fireMode, c_SprintKickMultiplier, c_SprintRecoveryMultiplier);
		return row;
	}

	private static float Off5(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		float _stanceKick,
		float _stanceRecovery)
	{
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			_fireMode,
			WeaponPoseState.Aiming,
			_stanceKick,
			_stanceRecovery);
		return WeaponRecoilMath.PredictOffsetAfterShots(in context, 5).magnitude;
	}

	private static void AppendWeaponBlock(StringBuilder _sb, in StanceRow _row, CultureInfo _culture)
	{
		float stand = Mathf.Max(_row.Stand5, 1e-4f);
		_sb.AppendLine(_row.Name + " (FullAuto Aiming)");
		_sb.AppendLine(
			"  Stand=" + _row.Stand5.ToString("F3", _culture) +
			"°  Crouch=" + _row.Crouch5.ToString("F3", _culture) +
			"°  Walk=" + _row.Walk5.ToString("F3", _culture) +
			"°  Sprint=" + _row.Sprint5.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  ratios Walk/Stand=" + (_row.Walk5 / stand).ToString("F2", _culture) +
			"  Crouch/Stand=" + (_row.Crouch5 / stand).ToString("F2", _culture) +
			"  Sprint/Stand=" + (_row.Sprint5 / stand).ToString("F2", _culture));
		_sb.AppendLine();
	}

	private static void AppendStanceChecks(StringBuilder _sb, in StanceRow _row, string _label)
	{
		AppendCheck(_sb, _label + " Walk > Stand", _row.Walk5 > _row.Stand5);
		AppendCheck(_sb, _label + " Crouch < Stand", _row.Crouch5 < _row.Stand5);
		AppendCheck(_sb, _label + " Sprint > Stand", _row.Sprint5 > _row.Stand5);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
