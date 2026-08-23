using UnityEngine;

/// <summary>Phase H8 — range-based PASS/WARN/FAIL (not point equality to M4).</summary>
public static class WeaponBalanceVerdictResolver
{
	#region Public Methods
	public static WeaponBalanceVerdict Resolve(
		in WeaponBalanceCase _case,
		in RecoilSampleResult _recoil,
		in WeaponBalanceScore _score,
		bool _isGOutlier,
		WeaponBalanceWarnKind _warnKind)
	{
		WeaponBalanceExpectation.OffsetBand band = WeaponBalanceExpectation.ResolveOffsetBand(
			_case.WeaponClass,
			_case.Weapon != null ? _case.Weapon.name : string.Empty);

		if (_recoil.OffsetMagAfter5 > band.MaxMagAfter5 * 1.35f)
			return WeaponBalanceVerdict.Fail;

		if (_score.Total == WeaponBalanceBandLevel.High &&
		    _recoil.OffsetMagAfter5 > band.MaxMagAfter5 * 1.15f)
			return WeaponBalanceVerdict.Fail;

		if (_isGOutlier || _score.Total != WeaponBalanceBandLevel.Low ||
		    _warnKind != WeaponBalanceWarnKind.None)
			return WeaponBalanceVerdict.Warn;

		if (_recoil.OffsetMagAfter5 < band.MinMagAfter5 * 0.85f ||
		    _recoil.OffsetMagAfter5 > band.MaxMagAfter5 * 1.15f)
			return WeaponBalanceVerdict.Warn;

		return WeaponBalanceVerdict.Pass;
	}
	#endregion
}
