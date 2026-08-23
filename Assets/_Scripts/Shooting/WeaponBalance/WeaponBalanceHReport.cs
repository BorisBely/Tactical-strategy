using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>Phase H canonical 4-level balance report.</summary>
public sealed class WeaponBalanceHReport
{
	#region Constants
	public const string LogFileName = "WeaponBalanceH_LAST.txt";
	public const string ReferenceSnapshotFileName = "WeaponBalance_Reference_LAST.txt";
	public const string AttachmentsSnapshotFileName = "WeaponBalance_Attachments_LAST.txt";
	#endregion

	#region Public Properties
	public DateTime GeneratedUtc { get; }
	public WeaponBalanceRow M4BaselineRow { get; set; }
	public List<WeaponBalanceWeaponSummary> WeaponSummaries { get; } = new List<WeaponBalanceWeaponSummary>(16);
	public List<WeaponBalanceLoadoutDelta> LoadoutDeltas { get; } = new List<WeaponBalanceLoadoutDelta>(128);
	public List<WeaponBalanceClassGroup> ClassGroups { get; } = new List<WeaponBalanceClassGroup>(8);
	public List<WeaponBalanceOutlierRecord> OutlierRecords { get; } = new List<WeaponBalanceOutlierRecord>(256);
	public List<WeaponBalanceAutoDisciplineRow> AutoDisciplineRows { get; } =
		new List<WeaponBalanceAutoDisciplineRow>(64);
	public List<WeaponBalancePlayCorrelation> PlayCorrelations { get; } =
		new List<WeaponBalancePlayCorrelation>(256);
	public List<string> BalancedWeapons { get; } = new List<string>(16);
	public List<string> ReviewWeapons { get; } = new List<string>(16);
	public List<string> ActionCandidates { get; } = new List<string>(16);
	public int ReferenceCaseCount { get; set; }
	public int AttachmentsCaseCount { get; set; }
	#endregion

	#region Public Methods
	public WeaponBalanceHReport(DateTime _generatedUtc)
	{
		GeneratedUtc = _generatedUtc;
	}

	public string BuildTextReport()
	{
		var sb = new StringBuilder(65536);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("WeaponBalanceHReport");
		sb.AppendLine("Phase H: Compare + Report over frozen G. Assets not written.");
		sb.AppendLine("M4 reference: Aiming / Standing / idle / Base / FullAuto @50 m");
		sb.AppendLine("Selector threshold = 0.775 m. Planner cap = 0.51 m (separate).");
		sb.AppendLine("Generated UTC: " + GeneratedUtc.ToString("u", culture));
		sb.AppendLine();

		AppendSummary(sb, culture);
		AppendPerWeapon(sb, culture);
		AppendPerLoadout(sb, culture);
		AppendClassComparison(sb, culture);
		AppendAutoDiscipline(sb, culture);
		AppendOutliers(sb, culture);
		AppendPlayCorrelation(sb, culture);
		AppendConclusions(sb);

		sb.AppendLine();
		sb.AppendLine("Pipeline: G Measure → H Compare → H Report → Manual review → [optional] Tuner → Re-run G");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private void AppendSummary(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== 1. SUMMARY ===");
		_sb.AppendLine("Reference cases: " + ReferenceCaseCount + " | Attachments cases: " + AttachmentsCaseCount);
		_sb.AppendLine("Weapons summarized: " + WeaponSummaries.Count);
		_sb.AppendLine("Loadout deltas: " + LoadoutDeltas.Count);
		_sb.AppendLine("Outliers: " + OutlierRecords.Count);
		_sb.AppendLine("Balanced: " + BalancedWeapons.Count + " | Review: " + ReviewWeapons.Count +
		               " | Action candidates: " + ActionCandidates.Count);
		if (M4BaselineRow.Case.Weapon != null)
		{
			_sb.AppendLine("M4 baseline |Off|@5: " +
			               M4BaselineRow.Recoil.OffsetMagAfter5.ToString("F3", _culture) + "°");
		}

		_sb.AppendLine();
	}

	private void AppendPerWeapon(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== 2. PER WEAPON ===");
		for (int i = 0; i < WeaponSummaries.Count; i++)
		{
			WeaponBalanceWeaponSummary summary = WeaponSummaries[i];
			WeaponBalanceRow row = summary.BaselineRow;
			RecoilSampleResult r = row.Recoil;
			_sb.AppendLine("--- " + summary.WeaponName + " (" + summary.WeaponClass + ") " + summary.Verdict +
			               " [" + summary.WarnKind + "] ---");
			_sb.AppendLine("Kick V=" + r.VerticalKickShot1.ToString("F3", _culture) + "° H=" +
			               r.HorizontalKickShot1.ToString("F3", _culture) + "° Rec/s=" +
			               r.RecoveryPerSecond.ToString("F2", _culture));
			_sb.AppendLine("|Off|@3=" + r.OffsetMagAfter3.ToString("F3", _culture) + " @5=" +
			               r.OffsetMagAfter5.ToString("F3", _culture) + " @8=" +
			               r.OffsetMagAfter8.ToString("F3", _culture) + " @10=" +
			               r.OffsetMagAfter10.ToString("F3", _culture));
			_sb.AppendLine("Offset@5 X=" + r.OffsetAfter5.x.ToString("F3", _culture) + " Y=" +
			               r.OffsetAfter5.y.ToString("F3", _culture));
			_sb.AppendLine("NetDrift=" + r.NetDriftPerShot.ToString("F4", _culture) + " pause0.2=" +
			               r.RecoveryAfterPause02.ToString("F3", _culture) + " 0.4=" +
			               r.RecoveryAfterPause04.ToString("F3", _culture) + " 0.8=" +
			               r.RecoveryAfterPause08.ToString("F3", _culture));
			_sb.AppendLine("θ=" + row.Accuracy.ThetaHalfAngleDegrees.ToString("F3", _culture) +
			               " spread=" + row.Accuracy.SpreadDiameterMeters.ToString("F3", _culture) + " m");
			_sb.AppendLine("Score total=" + summary.ScoreDetail.TotalNumeric.ToString("F1", _culture) + "/10 " +
			               "(V=" + summary.ScoreDetail.VerticalNumeric.ToString("F1", _culture) +
			               " H=" + summary.ScoreDetail.HorizontalNumeric.ToString("F1", _culture) +
			               " Rec=" + summary.ScoreDetail.RecoveryNumeric.ToString("F1", _culture) +
			               " Burst=" + summary.ScoreDetail.BurstNumeric.ToString("F1", _culture) + ")");
			for (int rIdx = 0; rIdx < summary.ScoreDetail.Reasons.Count; rIdx++)
				_sb.AppendLine("  " + summary.ScoreDetail.Reasons[rIdx]);

			if (summary.RelativeToM4 != null && summary.RelativeToM4.Count > 0)
			{
				_sb.AppendLine("Relative to M4:");
				for (int m = 0; m < summary.RelativeToM4.Count; m++)
				{
					WeaponBalanceRelativeMetric metric = summary.RelativeToM4[m];
					_sb.AppendLine("  " + metric.MetricName + " " + metric.Value.ToString("F3", _culture) +
					               " / M4 " + metric.M4Value.ToString("F3", _culture) + " = " +
					               metric.Ratio.ToString("F2", _culture) + "×");
				}
			}

			_sb.AppendLine();
		}
	}

	private void AppendPerLoadout(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== 3. PER LOADOUT (Base → Delta) ===");
		int preview = System.Math.Min(LoadoutDeltas.Count, 80);
		for (int i = 0; i < preview; i++)
		{
			WeaponBalanceLoadoutDelta delta = LoadoutDeltas[i];
			if (delta.LoadoutLabel == WeaponBalanceComparableKey.CanonicalLoadoutLabel)
				continue;
			_sb.AppendLine(delta.WeaponName + " " + delta.FormatCaseContext() +
			               " Base " + delta.BaseOffsetMag5.ToString("F3", _culture) +
			               "° → " + delta.LoadoutLabel + " " + delta.LoadoutOffsetMag5.ToString("F3", _culture) +
			               "° Δ=" + delta.DeltaDegrees.ToString("F3", _culture) + "° [" + delta.WarnKind + "]");
		}

		if (LoadoutDeltas.Count > preview)
			_sb.AppendLine("... (" + (LoadoutDeltas.Count - preview) + " more loadout rows)");
		_sb.AppendLine();
	}

	private void AppendClassComparison(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== CLASS COMPARISON (within WeaponClassType only) ===");
		for (int i = 0; i < ClassGroups.Count; i++)
		{
			WeaponBalanceClassGroup group = ClassGroups[i];
			_sb.AppendLine(group.ClassType + ": " + string.Join(", ", group.WeaponNames) +
			               " |Off|@5 min=" + group.MinOffsetMag5.ToString("F3", _culture) +
			               " med=" + group.MedianOffsetMag5.ToString("F3", _culture) +
			               " max=" + group.MaxOffsetMag5.ToString("F3", _culture));
		}

		_sb.AppendLine();
	}

	private void AppendAutoDiscipline(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== AUTO / DISCIPLINE (selector 0.775 m ≠ planner 0.51 m) ===");
		int preview = System.Math.Min(AutoDisciplineRows.Count, 40);
		for (int i = 0; i < preview; i++)
		{
			WeaponBalanceAutoDisciplineRow row = AutoDisciplineRows[i];
			_sb.AppendLine(row.WeaponName + " @" + row.DistanceMeters.ToString("F0", _culture) + "m mode=" +
			               row.SelectedMode + " group=" + row.GroupDiameterMeters.ToString("F3", _culture) +
			               "m ok=" + row.AutoAcceptable + " plannerSeries=" + row.PlannerSeriesLength +
			               " disp=" + row.PlannerDisplacementMeters.ToString("F3", _culture) + "m");
		}

		if (AutoDisciplineRows.Count > preview)
			_sb.AppendLine("... (" + (AutoDisciplineRows.Count - preview) + " more auto rows)");
		_sb.AppendLine();
	}

	private void AppendOutliers(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== 4. OUTLIERS ===");
		int preview = System.Math.Min(OutlierRecords.Count, 60);
		_sb.AppendLine("Weapon | Case | Metric | Actual | Expected | Kind | Severity | Play");
		for (int i = 0; i < preview; i++)
		{
			WeaponBalanceOutlierRecord record = OutlierRecords[i];
			_sb.AppendLine(record.WeaponName + " | " + record.CaseLabel + " | " + record.MetricName + " | " +
			               record.Actual.ToString("F3", _culture) + " | " + record.Expected + " | " +
			               record.WarnKind + " | " + record.Severity + " | " +
			               (record.PlayNeeded ? "yes" : "no"));
		}

		if (OutlierRecords.Count > preview)
			_sb.AppendLine("... (" + (OutlierRecords.Count - preview) + " more outliers)");
		_sb.AppendLine();
	}

	private void AppendPlayCorrelation(StringBuilder _sb, CultureInfo _culture)
	{
		_sb.AppendLine("=== PLAY CORRELATION (analytical replay) ===");
		int preview = System.Math.Min(PlayCorrelations.Count, 40);
		_sb.AppendLine("Weapon | Loadout | Analytical | Replay | Delta | Status");
		for (int i = 0; i < preview; i++)
		{
			WeaponBalancePlayCorrelation row = PlayCorrelations[i];
			_sb.AppendLine(row.WeaponName + " | " + row.LoadoutLabel + " | " +
			               row.AnalyticalOffsetMag5.ToString("F4", _culture) + " | " +
			               row.ReplayOffsetMag5.ToString("F4", _culture) + " | " +
			               row.Delta.ToString("F4", _culture) + " | " + (row.Pass ? "PASS" : "FAIL"));
		}

		if (PlayCorrelations.Count > preview)
			_sb.AppendLine("... (" + (PlayCorrelations.Count - preview) + " more play rows)");
		_sb.AppendLine();
	}

	private void AppendConclusions(StringBuilder _sb)
	{
		_sb.AppendLine("=== CONCLUSIONS ===");
		_sb.AppendLine("Balanced (" + BalancedWeapons.Count + "): " + string.Join(", ", BalancedWeapons));
		_sb.AppendLine("Review diagnostic (" + ReviewWeapons.Count + "): " + string.Join(", ", ReviewWeapons));
		_sb.AppendLine("Action candidates (" + ActionCandidates.Count + "): " +
		               string.Join(", ", ActionCandidates));
	}
	#endregion
}
