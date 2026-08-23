using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase G analytical balance runner. Second layer over RecoilContract — does not replace it.
/// </summary>
public static class WeaponBalanceRunner
{
	#region Public Methods
	public static WeaponBalanceReport Run(
		WeaponBalanceRunConfig _config,
		IReadOnlyList<WeaponDefinition> _weapons,
		IReadOnlyList<WeaponAttachmentDefinition> _attachmentCatalog,
		string _presetName = "Custom")
	{
		var report = new WeaponBalanceReport(_presetName);
		if (_config == null || _weapons == null)
			return report;

		List<WeaponBalanceCase> cases = WeaponBalanceCaseEnumerator.Enumerate(
			_config,
			_weapons,
			_attachmentCatalog);
		cases = FilterSmokeFireModes(cases, _presetName);

		var baseRowsByWeapon = new Dictionary<string, WeaponBalanceRow>();

		for (int i = 0; i < cases.Count; i++)
		{
			WeaponBalanceCase balanceCase = cases[i];
			RecoilSampleResult recoil = _config.EvaluateRecoil
				? WeaponBalanceRecoilPass.Evaluate(balanceCase)
				: default;
			AccuracySampleResult accuracy = _config.EvaluateAccuracy
				? WeaponBalanceAccuracyPass.Evaluate(balanceCase)
				: default;
			FireControlSampleResult fireControl = WeaponBalanceFireControlPass.Evaluate(balanceCase, _config);
			WeaponBalanceScore score = _config.EvaluateRecoil
				? WeaponBalanceScore.Evaluate(balanceCase, recoil)
				: default;

			WeaponBalanceVerdict verdict = ResolveVerdict(in score);
			var notes = new List<string>();

			string weaponKey = balanceCase.Weapon != null ? balanceCase.Weapon.name : "?";
			if (balanceCase.LoadoutLabel == "Base" && !baseRowsByWeapon.ContainsKey(weaponKey))
			{
				var baseRow = WeaponBalanceRow.Create(
					balanceCase,
					recoil,
					accuracy,
					fireControl,
					score,
					verdict,
					notes);
				baseRowsByWeapon[weaponKey] = baseRow;
			}

			baseRowsByWeapon.TryGetValue(weaponKey, out WeaponBalanceRow baseRowRef);
			bool isOutlier = WeaponBalanceOutlierDetector.TryFlagOutlier(
				WeaponBalanceRow.Create(balanceCase, recoil, accuracy, fireControl, score, verdict, notes),
				baseRowRef,
				out string outlierReason);
			if (isOutlier)
			{
				verdict = WeaponBalanceVerdict.Warn;
				notes.Add(outlierReason);
			}

			WeaponBalanceRow row = WeaponBalanceRow.Create(
				balanceCase,
				recoil,
				accuracy,
				fireControl,
				score,
				verdict,
				notes);
			report.AddRow(row, isOutlier);
		}

		if (_config.RunPlayGateOnOutliers)
			report.PlayGateSection = WeaponBalancePlayGate.RunAnalytical(report.Outliers, _config);

		return report;
	}
	#endregion

	#region Private Methods
	private static WeaponBalanceVerdict ResolveVerdict(in WeaponBalanceScore _score)
	{
		if (_score.Total == WeaponBalanceBandLevel.High)
			return WeaponBalanceVerdict.Warn;
		return WeaponBalanceVerdict.Pass;
	}

	private static List<WeaponBalanceCase> FilterSmokeFireModes(
		List<WeaponBalanceCase> _cases,
		string _presetName)
	{
		if (_presetName == null || !_presetName.Contains("Smoke"))
			return _cases;

		var filtered = new List<WeaponBalanceCase>(_cases.Count);
		for (int i = 0; i < _cases.Count; i++)
		{
			WeaponFireMode mode = _cases[i].FireMode;
			if (mode == WeaponFireMode.FullAuto || mode == WeaponFireMode.SemiAuto ||
			    mode == WeaponFireMode.Auto)
				filtered.Add(_cases[i]);
		}

		return filtered;
	}
	#endregion
}
