using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// One-click A1–A5 + N8 for M4 / M249 / PKM. No unit, no recorder hang.
/// Not a WeaponBalanceRunner (phase G). Does not retune recoil fields.
/// </summary>
public static class RecoilPlayBaselineAutoRunner
{
	#region Constants
	private static readonly int[] s_InstanceHashes = { 11, 29, 47 };
	private static readonly int[] s_RandomSeeds = { 101, 202, 303 };
	#endregion

	#region Nested Types
	public struct CaseMedian
	{
		public RecoilPlayBaselineProtocol.CaseId Case;
		public int ShotCount;
		public float CenterAbsCm;
		public float SpreadDiameterCm;
		public float RecoilOffsetDeg;
		public int RecoilShotIndexAtLastShot;
	}

	public struct GateSample
	{
		public string WeaponName;
		public float RemainingDeg;
		public float GateDeg;
		public bool WouldFireMuzzleOnTarget;
		public bool WouldFireMuzzleFollowsOffset;
		public int RecoilShotIndexAfterBurst;
	}

	public struct RunResult
	{
		public string PlaySection;
		public string N8Section;
		public float A1FiveShotGroupCm;
		public CaseMedian A1Shot1;
		public CaseMedian A1Shot3;
		public CaseMedian A1Shot5;
		public CaseMedian A1Shot8;
		public CaseMedian A2Shot5;
		public CaseMedian A3Shot5;
		public CaseMedian A4Shot5;
		public CaseMedian A5Shot4;
		public GateSample M4Gate;
		public GateSample M249Gate;
		public GateSample PkmGate;
	}
	#endregion

	#region Public Methods
	public static RunResult Run(WeaponDefinition _m4, WeaponDefinition _m249, WeaponDefinition _pkm)
	{
		var result = new RunResult();
		if (_m4 != null)
		{
			result.A1Shot1 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 1);
			result.A1Shot3 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 3);
			result.A1Shot5 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 5);
			result.A1Shot8 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A1AimingStand50, 8);
			result.A2Shot5 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A2AimingWalk50, 5);
			result.A3Shot5 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A3HipFireStand15, 5);
			result.A4Shot5 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A4AimingCrouch50, 5);
			result.A5Shot4 = MedianCase(_m4, RecoilPlayBaselineProtocol.CaseId.A5Pause04Stand50, 4);
			result.A1FiveShotGroupCm = result.A1Shot5.CenterAbsCm;
			result.M4Gate = SampleGate(_m4);
		}

		if (_m249 != null)
			result.M249Gate = SampleGate(_m249);
		if (_pkm != null)
			result.PkmGate = SampleGate(_pkm);

		result.PlaySection = BuildPlaySection(in result);
		result.N8Section = BuildN8Section(in result, _m4, _m249, _pkm);
		return result;
	}
	#endregion

	#region Private Methods
	private static CaseMedian MedianCase(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		int _shotCount)
	{
		float c0 = SimulateOne(_weapon, _case, _shotCount, 0, out RecoilPlayBaselineSimulator.BurstResult r0);
		float c1 = SimulateOne(_weapon, _case, _shotCount, 1, out RecoilPlayBaselineSimulator.BurstResult r1);
		float c2 = SimulateOne(_weapon, _case, _shotCount, 2, out RecoilPlayBaselineSimulator.BurstResult r2);
		float medianCenter = RecoilPlayBaselineProtocol.Median3(c0, c1, c2);
		RecoilPlayBaselineSimulator.BurstResult mid = PickMiddle(c0, c1, c2, r0, r1, r2);
		return new CaseMedian
		{
			Case = _case,
			ShotCount = _shotCount,
			CenterAbsCm = medianCenter,
			SpreadDiameterCm = RecoilPlayBaselineProtocol.Median3(
				r0.SpreadDiameterCm, r1.SpreadDiameterCm, r2.SpreadDiameterCm),
			RecoilOffsetDeg = mid.RecoilOffsetAtLastShotDeg.magnitude,
			RecoilShotIndexAtLastShot = mid.RecoilShotIndexAtLastShot
		};
	}

	private static float SimulateOne(
		WeaponDefinition _weapon,
		RecoilPlayBaselineProtocol.CaseId _case,
		int _shotCount,
		int _repeatIndex,
		out RecoilPlayBaselineSimulator.BurstResult _burst)
	{
		_burst = RecoilPlayBaselineSimulator.SimulateBurst(
			_weapon,
			_case,
			_shotCount,
			s_InstanceHashes[_repeatIndex],
			s_RandomSeeds[_repeatIndex]);
		return _burst.CenterAbsCm;
	}

	private static RecoilPlayBaselineSimulator.BurstResult PickMiddle(
		float _c0,
		float _c1,
		float _c2,
		in RecoilPlayBaselineSimulator.BurstResult _r0,
		in RecoilPlayBaselineSimulator.BurstResult _r1,
		in RecoilPlayBaselineSimulator.BurstResult _r2)
	{
		float median = RecoilPlayBaselineProtocol.Median3(_c0, _c1, _c2);
		if (Mathf.Approximately(median, _c1))
			return _r1;
		if (Mathf.Approximately(median, _c0))
			return _r0;
		return _r2;
	}

	private static GateSample SampleGate(WeaponDefinition _weapon)
	{
		Vector2 remaining = RecoilPlayBaselineSimulator.PredictRemainingAfterPause(
			_weapon,
			RecoilPlayBaselineProtocol.CaseId.A1AimingStand50,
			RecoilPlayBaselineProtocol.A5BurstShots,
			RecoilPlayBaselineProtocol.PauseA5Seconds);
		float mag = remaining.magnitude;
		float gate = RecoilPlayBaselineProtocol.BarrelGateIdleDegrees;
		return new GateSample
		{
			WeaponName = _weapon != null ? _weapon.name : "?",
			RemainingDeg = mag,
			GateDeg = gate,
			WouldFireMuzzleOnTarget = mag <= gate,
			WouldFireMuzzleFollowsOffset = true,
			RecoilShotIndexAfterBurst = RecoilPlayBaselineProtocol.A5BurstShots
		};
	}

	private static string BuildPlaySection(in RunResult _result)
	{
		var sb = new StringBuilder(2048);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("Mode: " + RecoilPlayBaselineProtocol.SimPlayLabel +
		              " (WeaponRecoilMath + hitscan cone, 3× median). No unit hang.");
		sb.AppendLine("RecoilControl 50, no attachments, prone off, FullAuto.");
		AppendMedian(sb, "A1 shot1 |group|_cm", _result.A1Shot1, culture);
		AppendMedian(sb, "A1 shot3 |group|_cm", _result.A1Shot3, culture);
		AppendMedian(sb, "A1 shot5 |group|_cm", _result.A1Shot5, culture);
		AppendMedian(sb, "A1 shot8 |group|_cm", _result.A1Shot8, culture);
		AppendMedian(sb, "A2 shot5 |group|_cm", _result.A2Shot5, culture);
		AppendMedian(sb, "A3 shot5 |group|_cm @15m", _result.A3Shot5, culture);
		AppendMedian(sb, "A4 shot5 |group|_cm", _result.A4Shot5, culture);
		AppendMedian(sb, "A5 shot4 |group|_cm", _result.A5Shot4, culture);
		sb.AppendLine(
			"A5 RecoilShotIndex at shot4 (hitscan)=" + _result.A5Shot4.RecoilShotIndexAtLastShot +
			" RecoilOffset=" + _result.A5Shot4.RecoilOffsetDeg.ToString("F3", culture) +
			"° (must not snap to 0/1). Pause A: StopFiring resets visual punch only.");
		return sb.ToString();
	}

	private static void AppendMedian(
		StringBuilder _sb,
		string _label,
		in CaseMedian _median,
		CultureInfo _culture)
	{
		_sb.AppendLine(
			_label + ": median " + _median.CenterAbsCm.ToString("F1", _culture) +
			" cm  spread " + _median.SpreadDiameterCm.ToString("F1", _culture) +
			" cm  RecoilOffset " + _median.RecoilOffsetDeg.ToString("F3", _culture) +
			"° (n=3)");
	}

	private static string BuildN8Section(
		in RunResult _result,
		WeaponDefinition _m4,
		WeaponDefinition _m249,
		WeaponDefinition _pkm)
	{
		var sb = new StringBuilder(1024);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("Mode: " + RecoilPlayBaselineProtocol.SimPlayLabel + " N8. StopFiring does not clear RecoilOffset.");
		AppendGate(sb, in _result.M4Gate, _m4, culture);
		AppendGate(sb, in _result.M249Gate, _m249, culture);
		AppendGate(sb, in _result.PkmGate, _pkm, culture);
		return sb.ToString();
	}

	private static void AppendGate(
		StringBuilder _sb,
		in GateSample _gate,
		WeaponDefinition _weapon,
		CultureInfo _culture)
	{
		if (_weapon == null)
		{
			_sb.AppendLine(_gate.WeaponName + ": asset not loaded.");
			return;
		}

		_sb.AppendLine(
			_gate.WeaponName +
			" remaining after 3+0.4s=" + _gate.RemainingDeg.ToString("F3", _culture) +
			"° gate=" + _gate.GateDeg.ToString("F1", _culture) +
			"° muzzleOnTargetWouldFire=" + _gate.WouldFireMuzzleOnTarget +
			" muzzleFollowsOffsetWouldFire=" + _gate.WouldFireMuzzleFollowsOffset +
			" RecoilShotIndex=" + _gate.RecoilShotIndexAfterBurst +
			" ResetRecoilOnStopFiring=visual only");
	}
	#endregion
}
