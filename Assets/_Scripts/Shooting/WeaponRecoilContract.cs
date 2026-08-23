using System.Text;
using UnityEngine;

/// <summary>
/// A10 recoil finish contract: Offset after 3/5/8/10, pauses 0.2/0.4/0.8,
/// HipFire kick &gt; Aiming, crouch kick &lt; stand. Prone matrix is not evaluated.
/// Pause A: prediction uses full recovery while not firing.
/// </summary>
public static class WeaponRecoilContract
{
	#region Constants
	public const float CrouchKickMultiplierForContract = 0.95f;
	public const float StandingKickMultiplierForContract = 1f;
	public const int PoseComparisonShotCount = 5;
	#endregion

	#region Public Methods
	public static string EvaluateWeapons(WeaponDefinition[] _weapons, out bool _passed)
	{
		var report = new StringBuilder(4096);
		report.AppendLine("RecoilContract");
		report.AppendLine("Pause A: StopFiring does not clear RecoilOffset; pause recovery is full rate.");
		report.AppendLine("Prone: DISABLED (crouch numbers, locomotion off). No prone matrix.");
		report.AppendLine("Cap 12° is a safety ceiling, not a balance target.");
		report.AppendLine();

		_passed = true;
		if (_weapons == null || _weapons.Length == 0)
		{
			report.AppendLine("FAIL: no WeaponDefinition assets.");
			_passed = false;
			return report.ToString();
		}

		for (int i = 0; i < _weapons.Length; i++)
		{
			WeaponDefinition weapon = _weapons[i];
			if (weapon == null)
				continue;

			if (!EvaluateWeapon(weapon, report))
				_passed = false;
		}

		report.AppendLine(_passed ? "RESULT: PASS" : "RESULT: FAIL");
		return report.ToString();
	}
	#endregion

	#region Private Methods
	private static bool EvaluateWeapon(WeaponDefinition _weapon, StringBuilder _report)
	{
		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		WeaponRecoilBalanceContract.Metrics metrics =
			WeaponRecoilBalanceContract.EvaluateBaseline(_weapon, fireMode);

		_report.AppendLine($"{_weapon.name} ({fireMode})");
		_report.AppendLine(
			$"  A kick: V={metrics.VerticalRecoilDegrees:F3}° H={metrics.HorizontalRecoilDegrees:F3}° " +
			$"recovery={metrics.RecoilRecoveryPerSecond:F2}°/s");
		_report.AppendLine(
			$"  B |Offset|: 3={metrics.OffsetMagnitudeAfter3Shots:F3}° 5={metrics.OffsetMagnitudeAfter5Shots:F3}° " +
			$"8={metrics.OffsetMagnitudeAfter8Shots:F3}° 10={metrics.OffsetMagnitudeAfter10Shots:F3}°");
		WeaponRecoilContext axesContext = WeaponRecoilContext.CreateBaseline(_weapon, fireMode);
		Vector2 after3 = WeaponRecoilMath.PredictOffsetAfterShots(in axesContext, 3);
		Vector2 after5 = WeaponRecoilMath.PredictOffsetAfterShots(in axesContext, 5);
		Vector2 after8 = WeaponRecoilMath.PredictOffsetAfterShots(in axesContext, 8);
		Vector2 after10 = WeaponRecoilMath.PredictOffsetAfterShots(in axesContext, 10);
		float netDriftPerShot = (after10.magnitude - after5.magnitude) / 5f;
		float maxAbsX = 0f;
		for (int n = 1; n <= 10; n++)
			maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(WeaponRecoilMath.PredictOffsetAfterShots(in axesContext, n).x));
		float absXOverAbsY5 = Mathf.Abs(after5.y) > 1e-4f ? Mathf.Abs(after5.x) / Mathf.Abs(after5.y) : 0f;
		_report.AppendLine(
			$"  B axes after 3: X={after3.x:F3}° Y={after3.y:F3}° |Offset|={after3.magnitude:F3}°");
		_report.AppendLine(
			$"  B axes after 5: X={after5.x:F3}° Y={after5.y:F3}° |Offset|={after5.magnitude:F3}°");
		_report.AppendLine(
			$"  B axes after 8: X={after8.x:F3}° Y={after8.y:F3}° |Offset|={after8.magnitude:F3}°");
		_report.AppendLine(
			$"  B axes after 10: X={after10.x:F3}° Y={after10.y:F3}° |Offset|={after10.magnitude:F3}°");
		_report.AppendLine(
			$"  |X|/|Y| after 5: {absXOverAbsY5:F2}  max |X| 1-10: {maxAbsX:F3}°");
		_report.AppendLine(
			$"  NetDriftPerShot 5→10: {netDriftPerShot:F4}°/shot");
		_report.AppendLine(
			$"  B @100m 5 shots → {metrics.DisplacementMetersAfter5ShotsAt100m:F2} m");
		_report.AppendLine(
			$"  C after 5 + pause: 0.2s={metrics.OffsetMagnitudeAfterPause020:F3}° " +
			$"0.4s={metrics.OffsetMagnitudeAfterPause040:F3}° 0.8s={metrics.OffsetMagnitudeAfterPause080:F3}°");
		if (metrics.OffsetMagnitudeAfter5Shots > 0.05f && metrics.OffsetMagnitudeAfterPause040 < 0.02f)
		{
			_report.AppendLine(
				"  NOTE: pause 0.4s wipes |Offset| after 5 — possible over-recovery (Phase B, not a contract FAIL).");
		}

		bool passed = true;
		WeaponRecoilContext aiming = WeaponRecoilContext.CreateBaseline(_weapon, fireMode);
		aiming.PoseKickMultiplier = WeaponPoseCombatModifiers.AimingKickMultiplier;
		WeaponRecoilContext hipFire = aiming;
		hipFire.PoseKickMultiplier = WeaponPoseCombatModifiers.HipFireKickMultiplier;
		float aimingOffset = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in aiming, PoseComparisonShotCount);
		float hipFireOffset = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in hipFire, PoseComparisonShotCount);
		bool hipFireKickGreater = hipFireOffset > aimingOffset + 1e-4f || aimingOffset <= 0.001f;
		_report.AppendLine(
			$"  HipFire vs Aiming after {PoseComparisonShotCount}: " +
			$"{hipFireOffset:F3}° vs {aimingOffset:F3}° " +
			$"(kick ×{WeaponPoseCombatModifiers.HipFireKickMultiplier:F2} vs " +
			$"×{WeaponPoseCombatModifiers.AimingKickMultiplier:F2}) " +
			$"{(hipFireKickGreater ? "PASS" : "FAIL")}");
		if (!hipFireKickGreater)
			passed = false;

		WeaponRecoilContext standing = WeaponRecoilContext.CreateBaseline(_weapon, fireMode);
		standing.StanceKickMultiplier = StandingKickMultiplierForContract;
		WeaponRecoilContext crouch = standing;
		crouch.StanceKickMultiplier = CrouchKickMultiplierForContract;
		float standingOffset = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in standing, PoseComparisonShotCount);
		float crouchOffset = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in crouch, PoseComparisonShotCount);
		bool crouchKickLess = crouchOffset < standingOffset - 1e-4f || standingOffset <= 0.001f;
		_report.AppendLine(
			$"  Crouch vs Stand after {PoseComparisonShotCount}: " +
			$"{crouchOffset:F3}° vs {standingOffset:F3}° " +
			$"(kick ×{CrouchKickMultiplierForContract:F2} vs ×{StandingKickMultiplierForContract:F2}) " +
			$"{(crouchKickLess ? "PASS" : "FAIL")}");
		if (!crouchKickLess)
			passed = false;

		bool pauseRecovers =
			metrics.OffsetMagnitudeAfterPause080 <= metrics.OffsetMagnitudeAfterPause040 + 1e-4f &&
			metrics.OffsetMagnitudeAfterPause040 <= metrics.OffsetMagnitudeAfterPause020 + 1e-4f &&
			metrics.OffsetMagnitudeAfterPause020 <= metrics.OffsetMagnitudeAfter5Shots + 1e-4f;
		_report.AppendLine(
			$"  Pause A recovery 0.2/0.4/0.8: {(pauseRecovers ? "PASS" : "FAIL")}");
		if (!pauseRecovers)
			passed = false;

		_report.AppendLine("  Prone: not evaluated.");
		_report.AppendLine();
		return passed;
	}
	#endregion
}
