using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds Phase H 4-level report from frozen G input.</summary>
public static class WeaponBalanceHReportBuilder
{
	#region Public Methods
	public static WeaponBalanceHReport Build(
		in WeaponBalanceHInput _input,
		IReadOnlyList<WeaponDefinition> _referenceWeapons)
	{
		var report = new WeaponBalanceHReport(_input.GeneratedUtc);
		if (_input.ReferenceReport == null || _referenceWeapons == null)
			return report;

		report.ReferenceCaseCount = _input.ReferenceReport.CaseCount;
		report.AttachmentsCaseCount = _input.AttachmentsReport != null
			? _input.AttachmentsReport.CaseCount
			: 0;

		WeaponBalanceRow m4Row = WeaponBalanceRowMatcher.FindBaselineRow(
			_input.ReferenceReport.Rows,
			FindWeapon(_referenceWeapons, WeaponRecoilBalanceContract.ReferenceWeaponAssetName));
		report.M4BaselineRow = m4Row;

		var classOffsets = new Dictionary<WeaponClassType, List<float>>();
		var baselineRows = new List<(WeaponDefinition weapon, WeaponBalanceRow row)>(_referenceWeapons.Count);

		for (int i = 0; i < _referenceWeapons.Count; i++)
		{
			WeaponDefinition weapon = _referenceWeapons[i];
			if (weapon == null)
				continue;
			WeaponBalanceRow baselineRow = WeaponBalanceRowMatcher.FindBaselineRow(
				_input.ReferenceReport.Rows,
				weapon);
			if (baselineRow.Case.Weapon == null)
				continue;
			baselineRows.Add((weapon, baselineRow));
			AccumulateClassOffset(classOffsets, weapon.WeaponClass, baselineRow.Recoil.OffsetMagAfter5);
		}

		var summaries = new List<WeaponBalanceWeaponSummary>(baselineRows.Count);
		for (int i = 0; i < baselineRows.Count; i++)
		{
			WeaponDefinition weapon = baselineRows[i].weapon;
			WeaponBalanceRow baselineRow = baselineRows[i].row;
			float peerMedian = ResolvePeerMedian(classOffsets, weapon.WeaponClass);
			bool isOutlier = IsOutlierRow(_input.ReferenceReport.Outliers, in baselineRow);
			string outlierReason = GetOutlierReason(_input.ReferenceReport.Outliers, in baselineRow);
			WeaponBalanceWarnKind warnKind = WeaponBalanceWarnClassifier.Classify(
				in baselineRow.Case,
				in baselineRow.Recoil,
				outlierReason,
				peerMedian);
			WeaponBalanceScoreDetail scoreDetail = WeaponBalanceScoreDetail.Evaluate(
				in baselineRow.Case,
				in baselineRow.Recoil,
				in baselineRow.Score);
			WeaponBalanceVerdict verdict = WeaponBalanceVerdictResolver.Resolve(
				in baselineRow.Case,
				in baselineRow.Recoil,
				in baselineRow.Score,
				isOutlier,
				warnKind);

			var summary = new WeaponBalanceWeaponSummary
			{
				WeaponName = weapon.name,
				WeaponClass = weapon.WeaponClass,
				BaselineRow = baselineRow,
				ScoreDetail = scoreDetail,
				Verdict = verdict,
				WarnKind = warnKind,
				RelativeToM4 = BuildRelativeToM4(in m4Row, in baselineRow, weapon.name)
			};
			summaries.Add(summary);
			ClassifyWeaponConclusion(report, summary);
		}

		report.WeaponSummaries.AddRange(summaries);
		report.ClassGroups.AddRange(BuildClassGroups(classOffsets, summaries));
		report.LoadoutDeltas.AddRange(BuildLoadoutDeltas(_input.AttachmentsReport));
		report.OutlierRecords.AddRange(BuildOutlierRecords(_input.ReferenceReport));
		report.AutoDisciplineRows.AddRange(BuildAutoDisciplineRows(_input.ReferenceReport, _referenceWeapons));
		report.PlayCorrelations.AddRange(BuildPlayCorrelations(_input.ReferenceReport));

		return report;
	}
	#endregion

	#region Private Methods
	private static WeaponDefinition FindWeapon(
		IReadOnlyList<WeaponDefinition> _weapons,
		string _assetName)
	{
		for (int i = 0; i < _weapons.Count; i++)
		{
			if (_weapons[i] != null && _weapons[i].name == _assetName)
				return _weapons[i];
		}

		return null;
	}

	private static float ResolvePeerMedian(
		Dictionary<WeaponClassType, List<float>> _classOffsets,
		WeaponClassType _class)
	{
		if (!_classOffsets.TryGetValue(_class, out List<float> values) || values.Count == 0)
			return 0f;
		values.Sort();
		return values[values.Count / 2];
	}

	private static void AccumulateClassOffset(
		Dictionary<WeaponClassType, List<float>> _classOffsets,
		WeaponClassType _class,
		float _offsetMag5)
	{
		if (!_classOffsets.TryGetValue(_class, out List<float> list))
		{
			list = new List<float>(4);
			_classOffsets[_class] = list;
		}

		list.Add(_offsetMag5);
	}

	private static List<WeaponBalanceRelativeMetric> BuildRelativeToM4(
		in WeaponBalanceRow _m4Row,
		in WeaponBalanceRow _weaponRow,
		string _weaponName)
	{
		var metrics = new List<WeaponBalanceRelativeMetric>(4);
		if (_m4Row.Case.Weapon == null || _weaponName == WeaponRecoilBalanceContract.ReferenceWeaponAssetName)
			return metrics;

		AddRelativeMetric(metrics, _weaponName, "|Off|@5", _weaponRow.Recoil.OffsetMagAfter5,
			_m4Row.Recoil.OffsetMagAfter5);
		AddRelativeMetric(metrics, _weaponName, "Recovery@0.4", _weaponRow.Recoil.RecoveryAfterPause04,
			_m4Row.Recoil.RecoveryAfterPause04);
		AddRelativeMetric(metrics, _weaponName, "θ", _weaponRow.Accuracy.ThetaHalfAngleDegrees,
			_m4Row.Accuracy.ThetaHalfAngleDegrees);
		return metrics;
	}

	private static void AddRelativeMetric(
		List<WeaponBalanceRelativeMetric> _metrics,
		string _weaponName,
		string _metricName,
		float _value,
		float _m4Value)
	{
		float ratio = _m4Value > 1e-4f ? _value / _m4Value : 0f;
		_metrics.Add(new WeaponBalanceRelativeMetric
		{
			WeaponName = _weaponName,
			MetricName = _metricName,
			Value = _value,
			M4Value = _m4Value,
			Ratio = ratio
		});
	}

	private static List<WeaponBalanceClassGroup> BuildClassGroups(
		Dictionary<WeaponClassType, List<float>> _classOffsets,
		List<WeaponBalanceWeaponSummary> _summaries)
	{
		var groups = new List<WeaponBalanceClassGroup>();
		foreach (KeyValuePair<WeaponClassType, List<float>> pair in _classOffsets)
		{
			List<float> values = pair.Value;
			if (values.Count == 0)
				continue;
			values.Sort();
			var weaponNames = new List<string>();
			for (int i = 0; i < _summaries.Count; i++)
			{
				if (_summaries[i].WeaponClass == pair.Key)
					weaponNames.Add(_summaries[i].WeaponName);
			}

			groups.Add(new WeaponBalanceClassGroup
			{
				ClassType = pair.Key,
				WeaponNames = weaponNames,
				MinOffsetMag5 = values[0],
				MaxOffsetMag5 = values[values.Count - 1],
				MedianOffsetMag5 = values[values.Count / 2]
			});
		}

		return groups;
	}

	private static List<WeaponBalanceLoadoutDelta> BuildLoadoutDeltas(WeaponBalanceReport _attachmentsReport)
	{
		var deltas = new List<WeaponBalanceLoadoutDelta>(128);
		if (_attachmentsReport == null)
			return deltas;

		var baseByWeapon = new Dictionary<string, float>();
		for (int i = 0; i < _attachmentsReport.Rows.Count; i++)
		{
			WeaponBalanceRow row = _attachmentsReport.Rows[i];
			if (row.Case.Weapon == null)
				continue;
			if (row.Case.LoadoutLabel == WeaponBalanceComparableKey.CanonicalLoadoutLabel &&
			    row.Case.Pose == WeaponPoseState.Aiming &&
			    row.Case.Stance == WeaponBalanceStance.Standing &&
			    row.Case.Movement == WeaponBalanceMovement.Idle &&
			    Mathf.Approximately(row.Case.DistanceMeters, WeaponBalanceComparableKey.CanonicalDistanceMeters))
			{
				baseByWeapon[row.Case.Weapon.name] = row.Recoil.OffsetMagAfter5;
			}
		}

		var seenKeys = new HashSet<LoadoutDeltaKey>();
		for (int i = 0; i < _attachmentsReport.Rows.Count; i++)
		{
			WeaponBalanceRow row = _attachmentsReport.Rows[i];
			if (row.Case.Weapon == null ||
			    row.Case.LoadoutLabel == WeaponBalanceComparableKey.CanonicalLoadoutLabel)
				continue;
			if (!baseByWeapon.TryGetValue(row.Case.Weapon.name, out float baseMag))
				continue;

			var key = new LoadoutDeltaKey(
				row.Case.Weapon.name,
				row.Case.LoadoutLabel,
				row.Case.Pose,
				row.Case.Stance,
				row.Case.Movement,
				row.Case.DistanceMeters,
				row.Case.FireMode);
			if (!seenKeys.Add(key))
				continue;

			float delta = row.Recoil.OffsetMagAfter5 - baseMag;
			deltas.Add(new WeaponBalanceLoadoutDelta
			{
				WeaponName = row.Case.Weapon.name,
				LoadoutLabel = row.Case.LoadoutLabel,
				Pose = row.Case.Pose,
				Stance = row.Case.Stance,
				Movement = row.Case.Movement,
				DistanceMeters = row.Case.DistanceMeters,
				FireMode = row.Case.FireMode,
				BaseOffsetMag5 = baseMag,
				LoadoutOffsetMag5 = row.Recoil.OffsetMagAfter5,
				DeltaDegrees = delta,
				WarnKind = Mathf.Abs(delta) < 0.01f
					? WeaponBalanceWarnKind.Diagnostic
					: WeaponBalanceWarnKind.Balance
			});
		}

		return deltas;
	}

	private readonly struct LoadoutDeltaKey : IEquatable<LoadoutDeltaKey>
	{
		private readonly string m_WeaponName;
		private readonly string m_LoadoutLabel;
		private readonly WeaponPoseState m_Pose;
		private readonly WeaponBalanceStance m_Stance;
		private readonly WeaponBalanceMovement m_Movement;
		private readonly float m_DistanceMeters;
		private readonly WeaponFireMode m_FireMode;

		public LoadoutDeltaKey(
			string _weaponName,
			string _loadoutLabel,
			WeaponPoseState _pose,
			WeaponBalanceStance _stance,
			WeaponBalanceMovement _movement,
			float _distanceMeters,
			WeaponFireMode _fireMode)
		{
			m_WeaponName = _weaponName;
			m_LoadoutLabel = _loadoutLabel;
			m_Pose = _pose;
			m_Stance = _stance;
			m_Movement = _movement;
			m_DistanceMeters = _distanceMeters;
			m_FireMode = _fireMode;
		}

		public bool Equals(LoadoutDeltaKey _other)
		{
			return m_WeaponName == _other.m_WeaponName &&
			       m_LoadoutLabel == _other.m_LoadoutLabel &&
			       m_Pose == _other.m_Pose &&
			       m_Stance == _other.m_Stance &&
			       m_Movement == _other.m_Movement &&
			       Mathf.Approximately(m_DistanceMeters, _other.m_DistanceMeters) &&
			       m_FireMode == _other.m_FireMode;
		}

		public override bool Equals(object _obj) => _obj is LoadoutDeltaKey other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = m_WeaponName != null ? m_WeaponName.GetHashCode() : 0;
				hash = hash * 31 + (m_LoadoutLabel != null ? m_LoadoutLabel.GetHashCode() : 0);
				hash = hash * 31 + (int)m_Pose;
				hash = hash * 31 + (int)m_Stance;
				hash = hash * 31 + (int)m_Movement;
				hash = hash * 31 + m_DistanceMeters.GetHashCode();
				hash = hash * 31 + (int)m_FireMode;
				return hash;
			}
		}
	}

	private static List<WeaponBalanceOutlierRecord> BuildOutlierRecords(WeaponBalanceReport _referenceReport)
	{
		var records = new List<WeaponBalanceOutlierRecord>(_referenceReport.OutlierCount);
		for (int i = 0; i < _referenceReport.Outliers.Count; i++)
		{
			WeaponBalanceRow row = _referenceReport.Outliers[i];
			WeaponBalanceExpectation.OffsetBand band = WeaponBalanceExpectation.ResolveOffsetBand(
				row.Case.WeaponClass,
				row.Case.Weapon != null ? row.Case.Weapon.name : string.Empty);
			string reason = row.Notes != null && row.Notes.Count > 0 ? row.Notes[0] : "G outlier";
			WeaponBalanceWarnKind kind = WeaponBalanceWarnClassifier.Classify(
				in row.Case,
				in row.Recoil,
				reason,
				band.MinMagAfter5);

			records.Add(new WeaponBalanceOutlierRecord
			{
				WeaponName = row.Case.Weapon != null ? row.Case.Weapon.name : "?",
				CaseLabel = BuildCaseLabel(in row.Case),
				MetricName = "|Off|@5",
				Actual = row.Recoil.OffsetMagAfter5,
				Expected = band.MinMagAfter5.ToString("F2") + "–" + band.MaxMagAfter5.ToString("F2") + "°",
				WarnKind = kind,
				Severity = row.Verdict,
				PlayNeeded = true,
				Reason = reason
			});
		}

		return records;
	}

	private static List<WeaponBalanceAutoDisciplineRow> BuildAutoDisciplineRows(
		WeaponBalanceReport _referenceReport,
		IReadOnlyList<WeaponDefinition> _referenceWeapons)
	{
		var rows = new List<WeaponBalanceAutoDisciplineRow>(64);
		if (_referenceReport == null)
			return rows;

		var seen = new HashSet<string>();
		for (int i = 0; i < _referenceReport.Rows.Count; i++)
		{
			WeaponBalanceRow row = _referenceReport.Rows[i];
			if (row.Case.Weapon == null)
				continue;
			if (row.Case.Pose != WeaponPoseState.Aiming ||
			    row.Case.Stance != WeaponBalanceStance.Standing ||
			    row.Case.Movement != WeaponBalanceMovement.Idle ||
			    row.Case.LoadoutLabel != WeaponBalanceComparableKey.CanonicalLoadoutLabel)
				continue;

			string key = row.Case.Weapon.name + "@" + row.Case.DistanceMeters.ToString("F0");
			if (!seen.Add(key))
				continue;

			rows.Add(new WeaponBalanceAutoDisciplineRow
			{
				WeaponName = row.Case.Weapon.name,
				DistanceMeters = row.Case.DistanceMeters,
				SelectedMode = row.FireControl.SelectedAutoFireMode,
				GroupDiameterMeters = row.FireControl.PredictedGroupDiameterMeters,
				AutoAcceptable = row.FireControl.AutoIsAcceptable,
				PlannerSeriesLength = row.FireControl.PlannerSeriesLength,
				PlannerDisplacementMeters = row.FireControl.PlannerDisplacementMeters
			});
		}

		return rows;
	}

	private static List<WeaponBalancePlayCorrelation> BuildPlayCorrelations(WeaponBalanceReport _referenceReport)
	{
		var correlations = new List<WeaponBalancePlayCorrelation>(_referenceReport.OutlierCount);
		for (int i = 0; i < _referenceReport.Outliers.Count; i++)
		{
			WeaponBalanceRow row = _referenceReport.Outliers[i];
			RecoilSampleResult replay = WeaponBalanceRecoilPass.Evaluate(row.Case);
			float delta = Mathf.Abs(replay.OffsetMagAfter5 - row.Recoil.OffsetMagAfter5);
			correlations.Add(new WeaponBalancePlayCorrelation
			{
				WeaponName = row.Case.Weapon != null ? row.Case.Weapon.name : "?",
				LoadoutLabel = row.Case.LoadoutLabel,
				AnalyticalOffsetMag5 = row.Recoil.OffsetMagAfter5,
				ReplayOffsetMag5 = replay.OffsetMagAfter5,
				Delta = delta,
				Pass = delta < 0.0001f
			});
		}

		return correlations;
	}

	private static void ClassifyWeaponConclusion(
		WeaponBalanceHReport _report,
		in WeaponBalanceWeaponSummary _summary)
	{
		if (_summary.Verdict == WeaponBalanceVerdict.Pass &&
		    _summary.WarnKind == WeaponBalanceWarnKind.None)
		{
			_report.BalancedWeapons.Add(_summary.WeaponName);
			return;
		}

		if (_summary.WarnKind == WeaponBalanceWarnKind.Balance)
			_report.ActionCandidates.Add(_summary.WeaponName);
		else
			_report.ReviewWeapons.Add(_summary.WeaponName);
	}

	private static bool IsOutlierRow(
		IReadOnlyList<WeaponBalanceRow> _outliers,
		in WeaponBalanceRow _row)
	{
		for (int i = 0; i < _outliers.Count; i++)
		{
			if (_outliers[i].Case.CaseId == _row.Case.CaseId)
				return true;
		}

		return false;
	}

	private static string GetOutlierReason(
		IReadOnlyList<WeaponBalanceRow> _outliers,
		in WeaponBalanceRow _row)
	{
		for (int i = 0; i < _outliers.Count; i++)
		{
			if (_outliers[i].Case.CaseId == _row.Case.CaseId &&
			    _outliers[i].Notes != null &&
			    _outliers[i].Notes.Count > 0)
				return _outliers[i].Notes[0];
		}

		return null;
	}

	private static string BuildCaseLabel(in WeaponBalanceCase _case)
	{
		return _case.FireMode + " " + _case.Pose + " " + _case.Stance + " " + _case.Movement +
		       " @" + _case.DistanceMeters.ToString("F0") + "m " + _case.LoadoutLabel;
	}
	#endregion
}
