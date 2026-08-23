using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class WeaponBalanceReport
{
	#region Constants
	public const string LogFileName = "WeaponBalance_LAST.txt";
	#endregion

	#region Public Properties
	public string PresetName { get; }
	public int CaseCount => Rows.Count;
	public int OutlierCount => Outliers.Count;
	public int WarnCount { get; private set; }
	public int FailCount { get; private set; }
	public List<WeaponBalanceRow> Rows { get; } = new List<WeaponBalanceRow>(512);
	public List<WeaponBalanceRow> Outliers { get; } = new List<WeaponBalanceRow>(64);
	public string PlayGateSection { get; set; }
	#endregion

	#region Public Methods
	public WeaponBalanceReport(string _presetName)
	{
		PresetName = _presetName ?? "Custom";
	}

	public void AddRow(in WeaponBalanceRow _row, bool _isOutlier)
	{
		Rows.Add(_row);
		if (_row.Verdict == WeaponBalanceVerdict.Warn)
			WarnCount++;
		if (_row.Verdict == WeaponBalanceVerdict.Fail)
			FailCount++;
		if (_isOutlier)
			Outliers.Add(_row);
	}

	public string BuildTextReport()
	{
		var sb = new StringBuilder(16384);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("WeaponBalanceRunner " + PresetName);
		sb.AppendLine("Phase G: analytical matrix. RecoilContract remains independent regression.");
		sb.AppendLine("Selector threshold = 0.775 m. Planner cap = 0.51 m. Assets not written.");
		sb.AppendLine();
		sb.AppendLine("Summary: cases=" + CaseCount + " outliers=" + OutlierCount +
		              " WARN=" + WarnCount + " FAIL=" + FailCount);
		sb.AppendLine();

		int preview = System.Math.Min(Rows.Count, 40);
		sb.AppendLine("Rows (preview " + preview + "/" + CaseCount + "):");
		sb.AppendLine("Weapon | Loadout | Mode | Pose | Dist | |Off|@5 | θ | Verdict");
		for (int i = 0; i < preview; i++)
		{
			WeaponBalanceRow row = Rows[i];
			string weapon = row.Case.Weapon != null ? row.Case.Weapon.name : "?";
			sb.AppendLine(
				weapon + " | " + row.Case.LoadoutLabel +
				" | " + row.Case.FireMode +
				" | " + row.Case.Pose +
				" | " + row.Case.DistanceMeters.ToString("F0", culture) + "m" +
				" | " + row.Recoil.OffsetMagAfter5.ToString("F3", culture) +
				" | " + row.Accuracy.ThetaHalfAngleDegrees.ToString("F3", culture) +
				" | " + row.Verdict);
		}

		if (OutlierCount > 0)
		{
			sb.AppendLine();
			WeaponBalanceOutlierDetector.AppendOutlierSummary(sb, Outliers);
		}

		if (!string.IsNullOrEmpty(PlayGateSection))
		{
			sb.AppendLine();
			sb.AppendLine("Play Gate:");
			sb.AppendLine(PlayGateSection);
		}

		sb.AppendLine();
		sb.AppendLine("FAIL triage: expectation band → outlier → Play Gate. Do not retune V/H/Rec assets here.");
		return sb.ToString();
	}
	#endregion
}
