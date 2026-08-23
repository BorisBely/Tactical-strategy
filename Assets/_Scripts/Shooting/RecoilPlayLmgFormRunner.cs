using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase B short SIM_PLAY for LMG: 5→10 climb, pause A, Horizontal X/Y.
/// Not WeaponBalanceRunner. Does not retune assets. Does not replace M4 RecoilPlayBaseline_LAST.
/// </summary>
public static class RecoilPlayLmgFormRunner
{
	#region Constants
	private static readonly int[] s_InstanceHashes = { 11, 29, 47 };
	private static readonly int[] s_RandomSeeds = { 101, 202, 303 };
	private const float c_PauseWipeWarnDegrees = 0.02f;
	private const float c_ReadableSideRatio = 0.28f;
	#endregion

	#region Nested Types
	public struct WeaponForm
	{
		public string Name;
		public float VerticalRecoil;
		public float HorizontalRecoil;
		public float RecoveryPerSecond;
		public Vector2 OffsetAfter3;
		public Vector2 OffsetAfter5;
		public Vector2 OffsetAfter8;
		public Vector2 OffsetAfter10;
		public float NetDriftPerShot;
		public float MaxAbsX;
		public float AbsXOverAbsYAfter5;
		public float Pause02;
		public float Pause04;
		public float Pause08;
		public float Group5Cm;
		public float Group10Cm;
		public int RecoilShotIndexAtShot5;
	}
	#endregion

	#region Public Methods
	public static string Run(WeaponDefinition _m249, WeaponDefinition _pkm)
	{
		var sb = new StringBuilder(2048);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayLmgForm SIM_PLAY");
		sb.AppendLine("Phase B7 Horizontal: M249/PKM X/Y at 5 and 10, pause A. Not M4 cm baseline.");
		sb.AppendLine("Offset° matches RecoilContract PredictOffset. Group cm is hitscan cone — do not match to M4 11.8 cm.");
		sb.AppendLine("Aiming, stand, FullAuto, no attachments, RecoilControl 50, prone off, 50 m.");
		sb.AppendLine();

		WeaponForm m249 = Sample(_m249);
		WeaponForm pkm = Sample(_pkm);
		AppendWeapon(sb, in m249, culture);
		AppendWeapon(sb, in pkm, culture);

		sb.AppendLine("Form checks (not absolute cm):");
		AppendCheck(sb, "M249 |Offset|10 > |Offset|5 (no zero-drift)", m249.OffsetAfter10.magnitude > m249.OffsetAfter5.magnitude);
		AppendCheck(sb, "PKM |Offset|10 > |Offset|5", pkm.OffsetAfter10.magnitude > pkm.OffsetAfter5.magnitude);
		AppendCheck(sb, "PKM heavier than M249 after 5", pkm.OffsetAfter5.magnitude > m249.OffsetAfter5.magnitude);
		AppendCheck(sb, "M249 pause 0.4s does not wipe Offset", m249.Pause04 >= c_PauseWipeWarnDegrees);
		AppendCheck(sb, "PKM pause 0.4s does not wipe Offset", pkm.Pause04 >= c_PauseWipeWarnDegrees);
		AppendCheck(sb, "M249 |X|/|Y|@5 readable LMG side", m249.AbsXOverAbsYAfter5 >= c_ReadableSideRatio);
		AppendCheck(sb, "PKM side heavier than M249 (|X|/|Y|@5)", pkm.AbsXOverAbsYAfter5 > m249.AbsXOverAbsYAfter5);
		AppendCheck(
			sb,
			"After 5, |Y| still leads |X| (climb remains vertical-led)",
			Mathf.Abs(m249.OffsetAfter5.y) > Mathf.Abs(m249.OffsetAfter5.x) &&
			Mathf.Abs(pkm.OffsetAfter5.y) > Mathf.Abs(pkm.OffsetAfter5.x));
		sb.AppendLine(
			"  RecoilShotIndex at 5th hitscan (must not snap to 0): M249=" +
			m249.RecoilShotIndexAtShot5 + " PKM=" + pkm.RecoilShotIndexAtShot5);
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static WeaponForm Sample(WeaponDefinition _weapon)
	{
		var form = new WeaponForm { Name = _weapon != null ? _weapon.name : "?" };
		if (_weapon == null)
			return form;

		form.VerticalRecoil = _weapon.VerticalRecoil;
		form.HorizontalRecoil = _weapon.HorizontalRecoil;
		form.RecoveryPerSecond = _weapon.RecoilRecoveryPerSecond;

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_weapon,
			fireMode,
			WeaponPoseState.Aiming,
			RecoilPlayBaselineProtocol.StandingKickMultiplier,
			RecoilPlayBaselineProtocol.StandingRecoveryMultiplier);

		form.OffsetAfter3 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 3);
		form.OffsetAfter5 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 5);
		form.OffsetAfter8 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 8);
		form.OffsetAfter10 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 10);
		form.NetDriftPerShot = (form.OffsetAfter10.magnitude - form.OffsetAfter5.magnitude) / 5f;
		form.MaxAbsX = 0f;
		for (int n = 1; n <= 10; n++)
			form.MaxAbsX = Mathf.Max(form.MaxAbsX, Mathf.Abs(WeaponRecoilMath.PredictOffsetAfterShots(in context, n).x));
		form.AbsXOverAbsYAfter5 = Mathf.Abs(form.OffsetAfter5.y) > 1e-4f
			? Mathf.Abs(form.OffsetAfter5.x) / Mathf.Abs(form.OffsetAfter5.y)
			: 0f;
		form.Pause02 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.2f).magnitude;
		form.Pause04 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.4f).magnitude;
		form.Pause08 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.8f).magnitude;

		RecoilPlayBaselineSimulator.BurstResult burst5 = MedianBurst(_weapon, 5);
		RecoilPlayBaselineSimulator.BurstResult burst10 = MedianBurst(_weapon, 10);
		form.Group5Cm = burst5.CenterAbsCm;
		form.Group10Cm = burst10.CenterAbsCm;
		form.RecoilShotIndexAtShot5 = burst5.RecoilShotIndexAtLastShot;
		return form;
	}

	private static RecoilPlayBaselineSimulator.BurstResult MedianBurst(WeaponDefinition _weapon, int _shotCount)
	{
		RecoilPlayBaselineSimulator.BurstResult a = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, _shotCount, s_InstanceHashes[0], s_RandomSeeds[0]);
		RecoilPlayBaselineSimulator.BurstResult b = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, _shotCount, s_InstanceHashes[1], s_RandomSeeds[1]);
		RecoilPlayBaselineSimulator.BurstResult c = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, _shotCount, s_InstanceHashes[2], s_RandomSeeds[2]);
		float median = RecoilPlayBaselineProtocol.Median3(a.CenterAbsCm, b.CenterAbsCm, c.CenterAbsCm);
		if (Mathf.Approximately(median, b.CenterAbsCm))
			return b;
		if (Mathf.Approximately(median, a.CenterAbsCm))
			return a;
		return c;
	}

	private static void AppendWeapon(StringBuilder _sb, in WeaponForm _form, CultureInfo _culture)
	{
		_sb.AppendLine(_form.Name);
		_sb.AppendLine(
			"  A kick: V=" + _form.VerticalRecoil.ToString("F3", _culture) +
			"° H=" + _form.HorizontalRecoil.ToString("F3", _culture) +
			"° Rec=" + _form.RecoveryPerSecond.ToString("F2", _culture) + "°/s");
		_sb.AppendLine(
			"  After 3: X=" + _form.OffsetAfter3.x.ToString("F3", _culture) +
			"° Y=" + _form.OffsetAfter3.y.ToString("F3", _culture) +
			"° |Offset|=" + _form.OffsetAfter3.magnitude.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  After 5: X=" + _form.OffsetAfter5.x.ToString("F3", _culture) +
			"° Y=" + _form.OffsetAfter5.y.ToString("F3", _culture) +
			"° |Offset|=" + _form.OffsetAfter5.magnitude.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  After 8: X=" + _form.OffsetAfter8.x.ToString("F3", _culture) +
			"° Y=" + _form.OffsetAfter8.y.ToString("F3", _culture) +
			"° |Offset|=" + _form.OffsetAfter8.magnitude.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  After 10: X=" + _form.OffsetAfter10.x.ToString("F3", _culture) +
			"° Y=" + _form.OffsetAfter10.y.ToString("F3", _culture) +
			"° |Offset|=" + _form.OffsetAfter10.magnitude.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  |X|/|Y| after 5: " + _form.AbsXOverAbsYAfter5.ToString("F2", _culture) +
			"  max |X| 1-10: " + _form.MaxAbsX.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  NetDriftPerShot 5→10: " + _form.NetDriftPerShot.ToString("F4", _culture) + "°/shot");
		_sb.AppendLine(
			"  Pause A after 5: 0.2s=" + _form.Pause02.ToString("F3", _culture) +
			"° 0.4s=" + _form.Pause04.ToString("F3", _culture) +
			"° 0.8s=" + _form.Pause08.ToString("F3", _culture) + "°");
		_sb.AppendLine(
			"  SIM_PLAY group |center| @50m (cone, ignore vs M4): 5=" +
			_form.Group5Cm.ToString("F1", _culture) + " cm  10=" +
			_form.Group10Cm.ToString("F1", _culture) + " cm  RecoilShotIndex@5=" +
			_form.RecoilShotIndexAtShot5);
		_sb.AppendLine();
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
