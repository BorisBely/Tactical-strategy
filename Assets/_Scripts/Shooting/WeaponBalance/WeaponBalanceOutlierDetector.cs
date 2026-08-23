using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class WeaponBalanceOutlierDetector
{
	#region Constants
	private const float c_NetDriftEpsilon = 0.005f;
	private const float c_AttachmentDeltaMinDegrees = 0.01f;
	#endregion

	#region Public Methods
	public static bool TryFlagOutlier(
		in WeaponBalanceRow _row,
		in WeaponBalanceRow _baseRow,
		out string _reason)
	{
		_reason = null;
		WeaponBalanceCase balanceCase = _row.Case;
		RecoilSampleResult recoil = _row.Recoil;

		WeaponBalanceExpectation.OffsetBand band = WeaponBalanceExpectation.ResolveOffsetBand(
			balanceCase.WeaponClass,
			balanceCase.Weapon != null ? balanceCase.Weapon.name : string.Empty);

		if (recoil.OffsetMagAfter5 < band.MinMagAfter5 * 0.7f)
		{
			_reason = "Vertical unexpectedly low |Off|@5";
			return true;
		}

		if (recoil.OffsetMagAfter5 > band.MaxMagAfter5 * 1.35f)
		{
			_reason = "Vertical unexpectedly high |Off|@5";
			return true;
		}

		if (band.RequireNetDrift && recoil.OffsetMagAfter5 > 0.1f &&
		    Mathf.Abs(recoil.NetDriftPerShot) < c_NetDriftEpsilon)
		{
			_reason = "NetDrift ~0 on automatic weapon";
			return true;
		}

		if (_baseRow.Case.Weapon != null &&
		    balanceCase.LoadoutLabel != "Base" &&
		    Mathf.Abs(recoil.OffsetMagAfter5 - _baseRow.Recoil.OffsetMagAfter5) < c_AttachmentDeltaMinDegrees)
		{
			_reason = "Attachment delta below perception threshold";
			return true;
		}

		if (_row.Score.Recovery == WeaponBalanceBandLevel.High &&
		    recoil.OffsetMagAfter5 > 0.05f && recoil.RecoveryAfterPause04 < 0.02f)
		{
			_reason = "Recovery outside class (pause wipes offset)";
			return true;
		}

		return false;
	}

	public static void AppendOutlierSummary(StringBuilder _sb, IReadOnlyList<WeaponBalanceRow> _outliers)
	{
		_sb.AppendLine("Outliers (" + _outliers.Count + "):");
		for (int i = 0; i < _outliers.Count; i++)
		{
			WeaponBalanceRow row = _outliers[i];
			string weapon = row.Case.Weapon != null ? row.Case.Weapon.name : "?";
			_sb.AppendLine(
				"  " + weapon + " " + row.Case.LoadoutLabel + " @" +
				row.Case.DistanceMeters.ToString("F0") + "m |Off|@5=" +
				row.Recoil.OffsetMagAfter5.ToString("F3") + "°");
		}
	}
	#endregion
}
