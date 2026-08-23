using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// RecoilPlayBaseline_LAST.txt builder. MATH is filled here; PLAY comes from RecoilPlayBaselineAutoRunner (SIM_PLAY) or PLAY_PENDING.
/// </summary>
public static class RecoilPlayBaselineReport
{
	#region Public Methods
	public static string Build(
		WeaponDefinition _m4,
		WeaponDefinition _m249,
		WeaponDefinition _pkm,
		string _playSection,
		string _n8Section)
	{
		return Build(_m4, _m249, _pkm, _playSection, _n8Section, -1f);
	}

	public static string Build(
		WeaponDefinition _m4,
		WeaponDefinition _m249,
		WeaponDefinition _pkm,
		string _playSection,
		string _n8Section,
		float _playFiveShotGroupCm)
	{
		var sb = new StringBuilder(8192);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayBaseline");
		sb.AppendLine("Phase A / N4 + N8. Phase B CLOSED until this report is frozen.");
		sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", culture));
		sb.AppendLine("Weapon: " + RecoilPlayBaselineProtocol.ReferenceWeaponAssetName + " FullAuto, no attachments.");
		sb.AppendLine("RecoilControl: 50 (neutral). Prone: OFF. Rings: 10 / 25 / 50 / 100 cm.");
		sb.AppendLine("Group center = mean face XY of shots 1..N. Spread = max pairwise about that center, not about aim.");
		sb.AppendLine("PredictOffsetAfterShots(N) = aim center before shot N+1. Mean of hits 1..N is lower.");
		sb.AppendLine("Stop-list: no RecoilPenalty, no θ from recoil, no Offset snap on StopFiring, no Vertical retune, no G runner classes.");
		sb.AppendLine();

		if (_m4 == null)
		{
			sb.AppendLine("FAIL: Weapon_M4_ModA_1 not loaded.");
			return sb.ToString();
		}

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_m4);
		sb.AppendLine("FireMode: " + fireMode);
		sb.AppendLine(
			"Asset V=" + _m4.VerticalRecoil.ToString("F3", culture) +
			" H=" + _m4.HorizontalRecoil.ToString("F3", culture) +
			" Rec=" + _m4.RecoilRecoveryPerSecond.ToString("F2", culture) +
			" Auto×=" + _m4.AutoRecoilMultiplier.ToString("F2", culture) +
			" RPM=" + _m4.FireRateRpm.ToString("F0", culture));
		sb.AppendLine(
			"RecoilContract 5-shot |Offset| 0.313° → " +
			RecoilPlayBaselineProtocol.DegreesToCm(0.313f, 100f).ToString("F1", culture) +
			" cm @100m → " +
			RecoilPlayBaselineProtocol.DegreesToCm(0.313f, 50f).ToString("F1", culture) +
			" cm @50m (editor orientation, not a Play target).");
		sb.AppendLine();

		RecoilPlayBaselineMath.CaseMath a1 = RecoilPlayBaselineMath.EvaluateCase(
			_m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50);
		RecoilPlayBaselineMath.CaseMath a2 = RecoilPlayBaselineMath.EvaluateCase(
			_m4, RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50);
		RecoilPlayBaselineMath.CaseMath a3 = RecoilPlayBaselineMath.EvaluateCase(
			_m4, RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15);
		RecoilPlayBaselineMath.CaseMath a4 = RecoilPlayBaselineMath.EvaluateCase(
			_m4, RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50);

		sb.AppendLine("=== MATH (WeaponRecoilMath + pose/stance, RecoilControl 50 → ×1) ===");
		AppendCase(sb, a1, culture);
		AppendCase(sb, a2, culture);
		AppendCase(sb, a3, culture);
		AppendCase(sb, a4, culture);
		sb.AppendLine(
			"A5 MATH after 3 shots + 0.4s full recovery: |Offset|=" +
			a1.OffsetAfter3Pause04Deg.magnitude.ToString("F3", culture) +
			"° → " + a1.OffsetAfter3Pause04Cm.ToString("F1", culture) +
			" cm @50m. RecoilShotIndex is NOT reset (pause A). Remaining ≈ 0 means recovery drained Offset, not a StopFiring snap.");
		sb.AppendLine();

		sb.AppendLine("=== MATH form ===");
		AppendVerdict(sb, "A1 Shot1≈0, 3↑, 8↑↑", RecoilPlayBaselineMath.EvaluateA1Form(in a1));
		AppendVerdict(sb, "A2 Walk > Stand (5-shot |Offset| cm)", RecoilPlayBaselineMath.EvaluateA2Form(in a1, in a2));
		AppendVerdict(sb, "A3 HipFire deg > Aiming; Offset and spread separate", RecoilPlayBaselineMath.EvaluateA3Form(in a1, in a3));
		AppendVerdict(sb, "A4 Crouch < Stand", RecoilPlayBaselineMath.EvaluateA4Form(in a1, in a4));
		AppendVerdict(sb, "A5 pause 0.4s: Offset recovered not increased; index kept", RecoilPlayBaselineMath.EvaluateA5Form(in a1));
		sb.AppendLine();

		sb.AppendLine("=== MATH vs Play (5-shot group center @50m) ===");
		float mathMeanHit5 = a1.After5.MeanHitCm;
		float mathOffset5 = a1.After5.OffsetAfterCm;
		RecoilPlayBaselineProtocol.Verdict mathPlay =
			RecoilPlayBaselineMath.EvaluateMathVsPlay(mathMeanHit5, _playFiveShotGroupCm, out string mathPlayNote);
		sb.AppendLine(
			"MATH 5-shot |Offset| " + a1.After5.OffsetAfterShotsDeg.magnitude.ToString("F3", culture) +
			"° → " + mathOffset5.ToString("F1", culture) + " cm @50m (~27 cm orientation).");
		sb.AppendLine(
			"MATH MeanHitCm=" + mathMeanHit5.ToString("F1", culture) +
			" vs SIM_PLAY group median=" +
			(_playFiveShotGroupCm < 0f ? "PLAY_PENDING" : _playFiveShotGroupCm.ToString("F1", culture) + " cm") +
			".");
		sb.AppendLine("Status: " + FormatVerdict(mathPlay) + " — " + mathPlayNote);
		sb.AppendLine("SIM_PLAY group center is mean of hits 1..5 (cone around Offset). Compare to MeanHitCm, not |Offset| after 5.");
		sb.AppendLine();

		sb.AppendLine("=== PLAY (median of 3 repeats) ===");
		if (string.IsNullOrWhiteSpace(_playSection))
		{
			sb.AppendLine("PLAY_PENDING. Run Tools/Tests/Run Recoil Play Baseline (Auto).");
			sb.AppendLine("Auto does not hang recorder/session/probe on the unit. Human range is optional.");
		}
		else
			sb.AppendLine(_playSection.TrimEnd());
		sb.AppendLine();

		sb.AppendLine("=== N8 barrel gate (separate from cm table) ===");
		sb.AppendLine("Gate: muzzle forward vs ApplyOffsetToDirection(aim, RecoilOffset), weight 1.");
		sb.AppendLine("StopFiring does not clear RecoilOffset. Visual punch reset ≠ gameplay Offset.");
		sb.AppendLine(
			"M4 after 3+0.4s |Offset|=" +
			a1.OffsetAfter3Pause04Deg.magnitude.ToString("F3", culture) +
			"°. Aiming idle gate ≈ 3°. Remaining Offset << gate → M4 should fire.");
		AppendLmgLine(sb, _m249, "M249", culture);
		AppendLmgLine(sb, _pkm, "PKM", culture);
		if (string.IsNullOrWhiteSpace(_n8Section))
			sb.AppendLine("N8 PLAY_PENDING. Run Auto: remaining Offset vs idle gate 3°, StopFiring visual-only.");
		else
			sb.AppendLine(_n8Section.TrimEnd());
		sb.AppendLine();

		sb.AppendLine("=== Phase gate ===");
		sb.AppendLine("Phase B (Vertical → Recovery → Horizontal → Semi× → Auto× on 10 references): CLOSED.");
		sb.AppendLine("WeaponBalanceRunner classes: NOT created. Spec frozen in doc §20.2.");
		bool simFilled = !string.IsNullOrWhiteSpace(_playSection) && _playFiveShotGroupCm >= 0f;
		sb.AppendLine(
			simFilled
				? "RESULT: MATH form recorded. SIM_PLAY medians recorded (no unit hang). Phase B CLOSED."
				: "RESULT: MATH form recorded. Play cells PLAY_PENDING until Auto runner writes medians.");
		return sb.ToString();
	}

	public static string FormatVerdict(RecoilPlayBaselineProtocol.Verdict _verdict)
	{
		switch (_verdict)
		{
			case RecoilPlayBaselineProtocol.Verdict.Pass:
				return "PASS";
			case RecoilPlayBaselineProtocol.Verdict.Warn:
				return "WARN";
			case RecoilPlayBaselineProtocol.Verdict.Fail:
				return "FAIL";
			case RecoilPlayBaselineProtocol.Verdict.PlayPending:
				return "PLAY_PENDING";
			default:
				return "ОТЧЁТ";
		}
	}
	#endregion

	#region Private Methods
	private static void AppendCase(
		StringBuilder _sb,
		RecoilPlayBaselineMath.CaseMath _case,
		CultureInfo _culture)
	{
		_sb.AppendLine(
			RecoilPlayBaselineProtocol.CaseLabel(_case.Case) +
			" pose=" + _case.Pose +
			" stanceKick=" + _case.StanceKick.ToString("F2", _culture) +
			" dist=" + _case.DistanceMeters.ToString("F0", _culture) + "m");
		AppendShot(_sb, _case.After1, _culture);
		AppendShot(_sb, _case.After3, _culture);
		AppendShot(_sb, _case.After5, _culture);
		AppendShot(_sb, _case.After8, _culture);
	}

	private static void AppendShot(
		StringBuilder _sb,
		RecoilPlayBaselineMath.ShotSample _sample,
		CultureInfo _culture)
	{
		_sb.AppendLine(
			"  after " + _sample.ShotCount +
			" |Offset|=" + _sample.OffsetAfterShotsDeg.magnitude.ToString("F3", _culture) +
			"°  OffsetX=" + _sample.OffsetAfterShotsDeg.x.ToString("F3", _culture) +
			" OffsetY=" + _sample.OffsetAfterShotsDeg.y.ToString("F3", _culture) +
			"  |Offset|_cm=" + _sample.OffsetAfterCm.ToString("F1", _culture) +
			"  MeanHit_cm=" + _sample.MeanHitCm.ToString("F1", _culture) +
			"  SpreadDiam_cm(θ)=" + _sample.SpreadDiameterCm.ToString("F1", _culture));
	}

	private static void AppendVerdict(StringBuilder _sb, string _label, RecoilPlayBaselineProtocol.Verdict _verdict)
	{
		_sb.AppendLine("  " + _label + ": " + FormatVerdict(_verdict));
	}

	private static void AppendLmgLine(
		StringBuilder _sb,
		WeaponDefinition _weapon,
		string _fallbackName,
		CultureInfo _culture)
	{
		if (_weapon == null)
		{
			_sb.AppendLine(_fallbackName + ": asset not loaded.");
			return;
		}

		RecoilPlayBaselineMath.CaseMath sample = RecoilPlayBaselineMath.EvaluateCase(
			_weapon, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50);
		_sb.AppendLine(
			_weapon.name + " after 3+0.4s |Offset|=" +
			sample.OffsetAfter3Pause04Deg.magnitude.ToString("F3", _culture) +
			"°. Expect wait-until-aligned OR fire along aim+Offset. Fail = blocked because recoil state was cleared.");
	}
	#endregion
}
