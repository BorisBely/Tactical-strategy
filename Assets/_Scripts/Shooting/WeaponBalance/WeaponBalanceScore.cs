using UnityEngine;

public struct WeaponBalanceScore
{
	public WeaponBalanceBandLevel Vertical;
	public WeaponBalanceBandLevel Horizontal;
	public WeaponBalanceBandLevel Recovery;
	public WeaponBalanceBandLevel Burst;
	public WeaponBalanceBandLevel Total;

	public static WeaponBalanceScore Evaluate(
		in WeaponBalanceCase _case,
		in RecoilSampleResult _recoil)
	{
		WeaponBalanceExpectation.OffsetBand band = WeaponBalanceExpectation.ResolveOffsetBand(
			_case.WeaponClass,
			_case.Weapon != null ? _case.Weapon.name : string.Empty);

		var score = new WeaponBalanceScore
		{
			Vertical = WeaponBalanceExpectation.ClassifyOffset(_recoil.OffsetMagAfter5, in band),
			Horizontal = WeaponBalanceExpectation.ClassifyHorizontal(
				Mathf.Abs(_recoil.OffsetAfter5.y) > 1e-4f
					? Mathf.Abs(_recoil.OffsetAfter5.x) / Mathf.Abs(_recoil.OffsetAfter5.y)
					: 0f),
			Recovery = WeaponBalanceExpectation.ClassifyRecovery(
				_recoil.RecoveryAfterPause04,
				_recoil.OffsetMagAfter5),
			Burst = ClassifyBurst(_recoil, in band)
		};
		score.Total = ResolveTotal(in score);
		return score;
	}

	private static WeaponBalanceBandLevel ClassifyBurst(
		in RecoilSampleResult _recoil,
		in WeaponBalanceExpectation.OffsetBand _band)
	{
		if (_band.RequireNetDrift && Mathf.Abs(_recoil.NetDriftPerShot) < 0.005f &&
		    _recoil.OffsetMagAfter5 > 0.1f)
			return WeaponBalanceBandLevel.Low;
		if (_recoil.OffsetMagAfter10 > _band.MaxMagAfter5 * 2.5f)
			return WeaponBalanceBandLevel.High;
		return WeaponBalanceBandLevel.Medium;
	}

	private static WeaponBalanceBandLevel ResolveTotal(in WeaponBalanceScore _score)
	{
		int high = 0;
		if (_score.Vertical == WeaponBalanceBandLevel.High)
			high++;
		if (_score.Horizontal == WeaponBalanceBandLevel.High)
			high++;
		if (_score.Recovery == WeaponBalanceBandLevel.High)
			high++;
		if (_score.Burst == WeaponBalanceBandLevel.High)
			high++;
		if (high >= 2)
			return WeaponBalanceBandLevel.High;
		if (high == 1)
			return WeaponBalanceBandLevel.Medium;
		return WeaponBalanceBandLevel.Low;
	}
}
