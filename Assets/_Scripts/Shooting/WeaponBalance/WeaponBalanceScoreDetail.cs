using System.Collections.Generic;
using UnityEngine;

/// <summary>Phase H7 — numeric score with human-readable reasons. Does not replace G WeaponBalanceScore.</summary>
public struct WeaponBalanceScoreDetail
{
	public float VerticalNumeric;
	public float HorizontalNumeric;
	public float RecoveryNumeric;
	public float BurstNumeric;
	public float TotalNumeric;
	public List<string> Reasons;

	public static WeaponBalanceScoreDetail Evaluate(
		in WeaponBalanceCase _case,
		in RecoilSampleResult _recoil,
		in WeaponBalanceScore _score)
	{
		WeaponBalanceExpectation.OffsetBand band = WeaponBalanceExpectation.ResolveOffsetBand(
			_case.WeaponClass,
			_case.Weapon != null ? _case.Weapon.name : string.Empty);

		var detail = new WeaponBalanceScoreDetail
		{
			VerticalNumeric = BandToNumeric(_score.Vertical),
			HorizontalNumeric = BandToNumeric(_score.Horizontal),
			RecoveryNumeric = BandToNumeric(_score.Recovery),
			BurstNumeric = BandToNumeric(_score.Burst),
			Reasons = new List<string>(4)
		};
		detail.TotalNumeric = (detail.VerticalNumeric + detail.HorizontalNumeric +
		                       detail.RecoveryNumeric + detail.BurstNumeric) / 4f;

		detail.Reasons.Add(
			"Vertical " + detail.VerticalNumeric.ToString("F1") + "/10: |Off|@5=" +
			_recoil.OffsetMagAfter5.ToString("F3") + "° band " +
			band.MinMagAfter5.ToString("F2") + "–" + band.MaxMagAfter5.ToString("F2"));

		float absXOverY = Mathf.Abs(_recoil.OffsetAfter5.y) > 1e-4f
			? Mathf.Abs(_recoil.OffsetAfter5.x) / Mathf.Abs(_recoil.OffsetAfter5.y)
			: 0f;
		detail.Reasons.Add(
			"Horizontal " + detail.HorizontalNumeric.ToString("F1") + "/10: |X|/|Y|@5=" +
			absXOverY.ToString("F2"));

		detail.Reasons.Add(
			"Recovery " + detail.RecoveryNumeric.ToString("F1") + "/10: pause0.4=" +
			_recoil.RecoveryAfterPause04.ToString("F3") + "°");

		detail.Reasons.Add(
			"Burst " + detail.BurstNumeric.ToString("F1") + "/10: NetDrift=" +
			_recoil.NetDriftPerShot.ToString("F4") + "°/shot");

		return detail;
	}

	private static float BandToNumeric(WeaponBalanceBandLevel _level)
	{
		switch (_level)
		{
			case WeaponBalanceBandLevel.Low:
				return 4f;
			case WeaponBalanceBandLevel.High:
				return 9f;
			default:
				return 7f;
		}
	}
}
