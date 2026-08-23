using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase B10 MATH + short SIM_PLAY: FullAuto character on M4 / AK-74 / AK-47 / M249 / PKM.
/// Tunes nothing. Only AutoRecoilMultiplier is the balance knob for this phase.
/// Does not retune Vertical / Horizontal / Recovery / Semi× / planner.
/// </summary>
public static class RecoilPlayAutoFormRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ak74WeaponAssetName = "Weapon_AK74";
	public const string Ak47WeaponAssetName = "Weapon_AK47";

	private static readonly int[] s_InstanceHashes = { 11, 29, 47 };
	private static readonly int[] s_RandomSeeds = { 101, 202, 303 };

	private const float c_B8RegressionEpsilonDegrees = 0.015f;
	private const float c_M4AnchorOffset5Degrees = 0.313f;
	private const float c_M4AnchorEpsilonDegrees = 0.01f;
	#endregion

	#region Nested Types
	private struct WeaponForm
	{
		public string Name;
		public float Vertical;
		public float Horizontal;
		public float Recovery;
		public float Rpm;
		public float AutoMultiplier;
		public WeaponFireMode FireMode;
		public Vector2 After3;
		public Vector2 After5;
		public Vector2 After8;
		public Vector2 After10;
		public float NetDriftPerShot;
		public float AbsXOverAbsYAfter5;
		public float MaxAbsX;
		public float Pause02;
		public float Pause04;
		public float Pause08;
		public float Group5Cm;
		public float Group10Cm;
		public int RecoilShotIndexAtShot5;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4,
		WeaponDefinition _ak74,
		WeaponDefinition _ak47,
		WeaponDefinition _m249,
		WeaponDefinition _pkm)
	{
		var sb = new StringBuilder(4096);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayAutoForm MATH + SIM_PLAY");
		sb.AppendLine("Phase B10 Auto×: M4 / AK-74 / AK-47 / M249 / PKM. FullAuto kick character only.");
		sb.AppendLine("Offset 3/5/8/10 + NetDrift 5→10. Auto× does not fix LMG horizontal (B7 closed).");
		sb.AppendLine("Aiming, stand, no attachments, RecoilControl 50, 50 m. Group cm = hitscan cone.");
		sb.AppendLine();

		WeaponForm m4 = Sample(_m4);
		WeaponForm ak74 = Sample(_ak74);
		WeaponForm ak47 = Sample(_ak47);
		WeaponForm m249 = Sample(_m249);
		WeaponForm pkm = Sample(_pkm);
		AppendWeapon(sb, in m4, culture);
		AppendWeapon(sb, in ak74, culture);
		AppendWeapon(sb, in ak47, culture);
		AppendWeapon(sb, in m249, culture);
		AppendWeapon(sb, in pkm, culture);

		sb.AppendLine("Form checks (FullAuto, not cm):");
		AppendCheck(sb, "Rifle |Offset|5: M4 < AK-74 < AK-47",
			m4.After5.magnitude < ak74.After5.magnitude &&
			ak74.After5.magnitude < ak47.After5.magnitude);
		AppendCheck(sb, "Rifle |Offset|10: M4 < AK-74 < AK-47",
			m4.After10.magnitude < ak74.After10.magnitude &&
			ak74.After10.magnitude < ak47.After10.magnitude);
		AppendCheck(sb, "LMG PKM heavier than M249 after 5", pkm.After5.magnitude > m249.After5.magnitude);
		AppendCheck(sb, "LMG PKM heavier than M249 after 10", pkm.After10.magnitude > m249.After10.magnitude);
		AppendCheck(sb, "M4 anchor @5 ≈ 0.313° (B0)", Mathf.Abs(m4.After5.magnitude - c_M4AnchorOffset5Degrees) <= c_M4AnchorEpsilonDegrees);
		AppendCheck(sb, "M249 B8 regression @5 (0.254°)", Mathf.Abs(m249.After5.magnitude - 0.254f) <= c_B8RegressionEpsilonDegrees);
		AppendCheck(sb, "PKM B8 regression @5 (0.296°)", Mathf.Abs(pkm.After5.magnitude - 0.296f) <= c_B8RegressionEpsilonDegrees);
		AppendCheck(sb, "M4 |Offset|10 > |Offset|5", m4.After10.magnitude > m4.After5.magnitude);
		AppendCheck(sb, "M249 |Offset|10 > |Offset|5", m249.After10.magnitude > m249.After5.magnitude);
		AppendCheck(sb, "PKM |Offset|10 > |Offset|5", pkm.After10.magnitude > pkm.After5.magnitude);
		AppendCheck(sb, "M4 NetDrift 5→10 > 0", m4.NetDriftPerShot > 0f);
		AppendCheck(sb, "M249 pause 0.4 does not wipe (B8)", m249.Pause04 >= 0.02f);
		AppendCheck(sb, "PKM pause 0.4 does not wipe (B8)", pkm.Pause04 >= 0.02f);
		sb.AppendLine(
			"  RecoilShotIndex@5 (must not snap 0): M4=" + m4.RecoilShotIndexAtShot5 +
			" M249=" + m249.RecoilShotIndexAtShot5 + " PKM=" + pkm.RecoilShotIndexAtShot5);
		sb.AppendLine("  Assets not changed this pass. V/H/Rec/Semi× frozen.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static WeaponForm Sample(WeaponDefinition _weapon)
	{
		var form = new WeaponForm { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return form;

		form.Vertical = _weapon.VerticalRecoil;
		form.Horizontal = _weapon.HorizontalRecoil;
		form.Recovery = _weapon.RecoilRecoveryPerSecond;
		form.Rpm = _weapon.FireRateRpm;
		form.AutoMultiplier = _weapon.AutoRecoilMultiplier;
		form.FireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);

		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			form.FireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);

		form.After3 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 3);
		form.After5 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 5);
		form.After8 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 8);
		form.After10 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 10);
		form.NetDriftPerShot = (form.After10.magnitude - form.After5.magnitude) / 5f;
		form.MaxAbsX = 0f;
		for (int n = 1; n <= 10; n++)
			form.MaxAbsX = Mathf.Max(form.MaxAbsX, Mathf.Abs(WeaponRecoilMath.PredictOffsetAfterShots(in context, n).x));
		form.AbsXOverAbsYAfter5 = Mathf.Abs(form.After5.y) > 1e-4f
			? Mathf.Abs(form.After5.x) / Mathf.Abs(form.After5.y)
			: 0f;
		form.Pause02 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.2f).magnitude;
		form.Pause04 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.4f).magnitude;
		form.Pause08 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.8f).magnitude;

		RecoilPlayBaselineSimulator.BurstResult burst5 = MedianBurst(_weapon, form.FireMode, 5);
		RecoilPlayBaselineSimulator.BurstResult burst10 = MedianBurst(_weapon, form.FireMode, 10);
		form.Group5Cm = burst5.CenterAbsCm;
		form.Group10Cm = burst10.CenterAbsCm;
		form.RecoilShotIndexAtShot5 = burst5.RecoilShotIndexAtLastShot;
		return form;
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

	private static void AppendWeapon(StringBuilder _sb, in WeaponForm _form, CultureInfo _culture)
	{
		_sb.AppendLine(_form.Name + " (" + _form.FireMode + " Auto×=" + _form.AutoMultiplier.ToString("F2", _culture) + ")");
		_sb.AppendLine(
			"  V=" + _form.Vertical.ToString("F3", _culture) +
			"° H=" + _form.Horizontal.ToString("F3", _culture) +
			"° Rec=" + _form.Recovery.ToString("F2", _culture) +
			"°/s RPM=" + _form.Rpm.ToString("F0", _culture));
		AppendOffsetLine(_sb, 3, _form.After3, _culture);
		AppendOffsetLine(_sb, 5, _form.After5, _culture);
		AppendOffsetLine(_sb, 8, _form.After8, _culture);
		AppendOffsetLine(_sb, 10, _form.After10, _culture);
		_sb.AppendLine(
			"  |X|/|Y|@5: " + _form.AbsXOverAbsYAfter5.ToString("F2", _culture) +
			"  max |X| 1-10: " + _form.MaxAbsX.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  NetDriftPerShot 5→10: " + _form.NetDriftPerShot.ToString("F4", _culture) + "°/shot");
		_sb.AppendLine(
			"  Pause A after 5: 0.2s=" + _form.Pause02.ToString("F3", _culture) +
			"° 0.4s=" + _form.Pause04.ToString("F3", _culture) +
			"° 0.8s=" + _form.Pause08.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  SIM_PLAY group@50m: 5=" + _form.Group5Cm.ToString("F1", _culture) +
			" cm  10=" + _form.Group10Cm.ToString("F1", _culture) +
			" cm  RecoilShotIndex@5=" + _form.RecoilShotIndexAtShot5);
		_sb.AppendLine();
	}

	private static void AppendOffsetLine(StringBuilder _sb, int _n, Vector2 _offset, CultureInfo _culture)
	{
		_sb.AppendLine(
			"  after " + _n + ": X=" + _offset.x.ToString("F3", _culture) +
			"° Y=" + _offset.y.ToString("F3", _culture) +
			"° |Offset|=" + _offset.magnitude.ToString("F3", _culture) + "°");
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
