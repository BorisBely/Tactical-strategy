using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase B11 MATH cross-class regression after Semi×/Auto× frozen.
/// Rifle: M4 → AK-74 → AK-47. LMG: M249 → PKM per B8 frozen anchors.
/// Does not retune assets.
/// </summary>
public static class RecoilPlayB11CrossClassRunner
{
	#region Constants
	private const float c_B8RegressionEpsilonDegrees = 0.015f;
	private const float c_ReadableLmgSideRatio = 0.28f;
	private const float c_PauseWipeWarnDegrees = 0.02f;
	#endregion

	#region Nested Types
	private struct ClassSample
	{
		public string Name;
		public float Vertical;
		public float Horizontal;
		public float Recovery;
		public float AutoMultiplier;
		public Vector2 After5;
		public Vector2 After10;
		public float NetDriftPerShot;
		public float AbsXOverAbsYAfter5;
		public float Pause04;
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
		var sb = new StringBuilder(3072);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayB11CrossClass MATH");
		sb.AppendLine("Phase B11: rifle + LMG class regression after B9 Semi× and B10 Auto× frozen.");
		sb.AppendLine("FullAuto, Aiming, stand, InstanceHash=0. Does not retune assets.");
		sb.AppendLine();

		ClassSample m4 = Sample(_m4);
		ClassSample ak74 = Sample(_ak74);
		ClassSample ak47 = Sample(_ak47);
		ClassSample m249 = Sample(_m249);
		ClassSample pkm = Sample(_pkm);

		sb.AppendLine("Rifle (FullAuto |Offset|):");
		AppendRow(sb, in m4, culture);
		AppendRow(sb, in ak74, culture);
		AppendRow(sb, in ak47, culture);
		sb.AppendLine();

		sb.AppendLine("LMG (B8 regression anchors):");
		AppendRow(sb, in m249, culture);
		AppendRow(sb, in pkm, culture);
		sb.AppendLine();

		sb.AppendLine("Form checks:");
		AppendCheck(sb, "Rifle @5: M4 < AK-74 < AK-47",
			m4.After5.magnitude < ak74.After5.magnitude &&
			ak74.After5.magnitude < ak47.After5.magnitude);
		AppendCheck(sb, "Rifle @10: M4 < AK-74 < AK-47",
			m4.After10.magnitude < ak74.After10.magnitude &&
			ak74.After10.magnitude < ak47.After10.magnitude);
		AppendCheck(sb, "LMG PKM |Offset|5 > M249", pkm.After5.magnitude > m249.After5.magnitude);
		AppendCheck(sb, "LMG PKM |Offset|10 > M249", pkm.After10.magnitude > m249.After10.magnitude);
		AppendCheck(sb, "LMG PKM side ratio > M249 (@5)", pkm.AbsXOverAbsYAfter5 > m249.AbsXOverAbsYAfter5);
		AppendCheck(sb, "M249 |X|/|Y|@5 readable LMG (≥0.28)", m249.AbsXOverAbsYAfter5 >= c_ReadableLmgSideRatio);
		AppendCheck(sb, "M249 NetDrift 5→10 > 0", m249.NetDriftPerShot > 0f);
		AppendCheck(sb, "PKM NetDrift 5→10 > 0", pkm.NetDriftPerShot > 0f);
		AppendCheck(sb, "M249 B8 @5 ≈ 0.254°", Mathf.Abs(m249.After5.magnitude - 0.254f) <= c_B8RegressionEpsilonDegrees);
		AppendCheck(sb, "PKM B8 @5 ≈ 0.296°", Mathf.Abs(pkm.After5.magnitude - 0.296f) <= c_B8RegressionEpsilonDegrees);
		AppendCheck(sb, "M249 pause 0.4 B7/B8 alive", m249.Pause04 >= c_PauseWipeWarnDegrees);
		AppendCheck(sb, "PKM pause 0.4 B7/B8 alive", pkm.Pause04 >= c_PauseWipeWarnDegrees);
		AppendCheck(sb, "M4 anchor @5 ≈ 0.313° (B0)", Mathf.Abs(m4.After5.magnitude - 0.313f) <= c_B8RegressionEpsilonDegrees);
		sb.AppendLine("  Assets not changed. Semi×/Auto×/V/H/Rec frozen.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static ClassSample Sample(WeaponDefinition _weapon)
	{
		var sample = new ClassSample { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return sample;

		sample.Vertical = _weapon.VerticalRecoil;
		sample.Horizontal = _weapon.HorizontalRecoil;
		sample.Recovery = _weapon.RecoilRecoveryPerSecond;
		sample.AutoMultiplier = _weapon.AutoRecoilMultiplier;

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			fireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);

		sample.After5 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 5);
		sample.After10 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 10);
		sample.NetDriftPerShot = (sample.After10.magnitude - sample.After5.magnitude) / 5f;
		sample.AbsXOverAbsYAfter5 = Mathf.Abs(sample.After5.y) > 1e-4f
			? Mathf.Abs(sample.After5.x) / Mathf.Abs(sample.After5.y)
			: 0f;
		sample.Pause04 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.4f).magnitude;
		return sample;
	}

	private static void AppendRow(StringBuilder _sb, in ClassSample _s, CultureInfo _culture)
	{
		_sb.AppendLine(
			"  " + _s.Name +
			"  V=" + _s.Vertical.ToString("F3", _culture) +
			" H=" + _s.Horizontal.ToString("F3", _culture) +
			" Rec=" + _s.Recovery.ToString("F2", _culture) +
			" Auto×=" + _s.AutoMultiplier.ToString("F2", _culture) +
			"  |Off|5=" + _s.After5.magnitude.ToString("F3", _culture) +
			"° 10=" + _s.After10.magnitude.ToString("F3", _culture) +
			"°  |X|/|Y|@5=" + _s.AbsXOverAbsYAfter5.ToString("F2", _culture) +
			"  nd=" + _s.NetDriftPerShot.ToString("F4", _culture) +
			"  pause0.4=" + _s.Pause04.ToString("F3", _culture) + "°");
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
