using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class WeaponBalancePlayGate
{
	#region Public Methods
	public static string RunAnalytical(
		IReadOnlyList<WeaponBalanceRow> _outliers,
		WeaponBalanceRunConfig _config)
	{
		if (_outliers == null || _outliers.Count == 0)
			return "No outliers — Play Gate skipped.";

		var sb = new StringBuilder(2048);
		sb.AppendLine("Analytical gameplay-path re-check (" + _outliers.Count + " outliers):");
		int pass = 0;
		for (int i = 0; i < _outliers.Count; i++)
		{
			WeaponBalanceRow row = _outliers[i];
			RecoilSampleResult replay = WeaponBalanceRecoilPass.Evaluate(row.Case);
			bool magMatch = Mathf.Abs(replay.OffsetMagAfter5 - row.Recoil.OffsetMagAfter5) < 0.0001f;
			if (magMatch)
				pass++;
			string weapon = row.Case.Weapon != null ? row.Case.Weapon.name : "?";
			sb.AppendLine(
				"  " + weapon + " " + row.Case.LoadoutLabel +
				" |Off|@5 replay=" + replay.OffsetMagAfter5.ToString("F4") +
				" orig=" + row.Recoil.OffsetMagAfter5.ToString("F4") +
				" | " + (magMatch ? "PASS" : "FAIL"));
		}

		sb.AppendLine("Play Gate analytical: " + pass + "/" + _outliers.Count + " PASS");
		sb.AppendLine("In-scene Play Mode harness: not run (outliers only, optional Phase G11 tier-2).");
		return sb.ToString();
	}

	public static List<WeaponBalanceRow> SelectPlaySubset(
		IReadOnlyList<WeaponBalanceRow> _allRows,
		IReadOnlyList<WeaponBalanceRow> _outliers)
	{
		var subset = new List<WeaponBalanceRow>();
		if (_outliers != null)
			subset.AddRange(_outliers);

		if (_allRows == null)
			return subset;

		for (int i = 0; i < _allRows.Count; i++)
		{
			WeaponBalanceRow row = _allRows[i];
			if (row.Case.Weapon != null &&
			    row.Case.Weapon.name == WeaponRecoilBalanceContract.ReferenceWeaponAssetName &&
			    row.Case.LoadoutLabel == "Base")
			{
				if (!ContainsRow(subset, row))
					subset.Add(row);
				break;
			}
		}

		return subset;
	}

	private static bool ContainsRow(List<WeaponBalanceRow> _rows, WeaponBalanceRow _row)
	{
		for (int i = 0; i < _rows.Count; i++)
		{
			if (_rows[i].Case.CaseId == _row.Case.CaseId)
				return true;
		}

		return false;
	}
	#endregion
}
