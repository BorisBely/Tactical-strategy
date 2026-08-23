using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase E2 MATH planner distance sweep: M4 / AK-47 @ 20/50/80/100/150 m.
/// </summary>
public static class RecoilPlayE2PlannerDistanceRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ak47WeaponAssetName = "Weapon_AK47";

	private static readonly float[] s_DistancesMeters = { 20f, 50f, 80f, 100f, 150f };
	private const float c_OffsetCapMeters =
		WeaponAutoModeSelectionUtility.HumanTargetWidthMeters * 0.85f;
	#endregion

	#region Nested Types
	private struct PlanRow
	{
		public WeaponDefinition Weapon;
		public float DistanceMeters;
		public WeaponFireMode EffectiveFireMode;
		public int SeriesShots;
		public float RequiredAim;
		public float DisplacementMeters;
	}
	#endregion

	#region Public Methods
	public static string Run(WeaponDefinition _m4, WeaponDefinition _ak47)
	{
		var sb = new StringBuilder(4096);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayE2PlannerDistance MATH");
		sb.AppendLine("Phase E2: planner sweep 20/50/80/100/150 m. Auto discipline, deterministic.");
		sb.AppendLine("Offset cap 0.60 m × 0.85 = " + c_OffsetCapMeters.ToString("F2", culture) + " m.");
		sb.AppendLine();

		var m4Rows = SampleWeapon(_m4, culture, sb, "Weapon_M4_ModA_1");
		sb.AppendLine();
		var akRows = SampleWeapon(_ak47, culture, sb, "Weapon_AK47");

		sb.AppendLine();
		sb.AppendLine("Form checks:");
		AppendCapChecks(sb, _m4, m4Rows, "M4");
		AppendCapChecks(sb, _ak47, akRows, "AK-47");
		AppendCheck(sb, "M4 aim rises 20→150 m", m4Rows[0].RequiredAim < m4Rows[4].RequiredAim);
		AppendCheck(sb, "AK-47 aim rises 20→150 m", akRows[0].RequiredAim < akRows[4].RequiredAim);
		AppendCheck(sb, "M4 @150 m SemiAuto", m4Rows[4].EffectiveFireMode == WeaponFireMode.SemiAuto);
		AppendCheck(sb, "M4 @20 m Burst or FullAuto",
			m4Rows[0].EffectiveFireMode == WeaponFireMode.Burst ||
			m4Rows[0].EffectiveFireMode == WeaponFireMode.FullAuto);
		AppendCheck(sb, "M4 series @150 ≤ @20", m4Rows[4].SeriesShots <= m4Rows[0].SeriesShots);
		AppendCheck(sb, "AK-47 @150 m not FullAuto", akRows[4].EffectiveFireMode != WeaponFireMode.FullAuto);
		AppendCheck(sb, "AK-47 @20 m Burst or FullAuto",
			akRows[0].EffectiveFireMode == WeaponFireMode.Burst ||
			akRows[0].EffectiveFireMode == WeaponFireMode.FullAuto);
		sb.AppendLine("  Assets not changed. E1 anchors frozen. F specials = next phase.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static PlanRow[] SampleWeapon(
		WeaponDefinition _weapon,
		CultureInfo _culture,
		StringBuilder _sb,
		string _title)
	{
		var rows = new PlanRow[s_DistancesMeters.Length];
		_sb.AppendLine(_title + ":");
		for (int i = 0; i < s_DistancesMeters.Length; i++)
		{
			rows[i] = Sample(_weapon, s_DistancesMeters[i]);
			_sb.AppendLine(
				"  " + rows[i].DistanceMeters.ToString("F0", _culture) + " m" +
				"  mode=" + rows[i].EffectiveFireMode +
				"  series=" + rows[i].SeriesShots +
				"  aim=" + rows[i].RequiredAim.ToString("F2", _culture) +
				"  disp=" + rows[i].DisplacementMeters.ToString("F2", _culture) + " m");
		}

		return rows;
	}

	private static PlanRow Sample(WeaponDefinition _weapon, float _distanceMeters)
	{
		var row = new PlanRow
		{
			Weapon = _weapon,
			DistanceMeters = _distanceMeters
		};
		if (_weapon == null)
			return row;

		WeaponFireDisciplinePlan plan = WeaponFireDisciplinePlanner.CreatePlan(
			_weapon,
			WeaponFireMode.Auto,
			WeaponFireDisciplineMode.Auto,
			_distanceMeters,
			null,
			null,
			null,
			true);

		row.EffectiveFireMode = plan.EffectiveFireMode;
		row.SeriesShots = plan.SeriesShotCount;
		row.RequiredAim = plan.RequiredAimProgress01;

		WeaponRecoilContext context = WeaponRecoilContext.CreateBaseline(_weapon, plan.EffectiveFireMode);
		float offsetDeg = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, row.SeriesShots);
		row.DisplacementMeters = WeaponRecoilMath.OffsetToDisplacementMeters(offsetDeg, _distanceMeters);
		return row;
	}

	private static void AppendCapChecks(
		StringBuilder _sb,
		WeaponDefinition _weapon,
		PlanRow[] _rows,
		string _label)
	{
		for (int i = 0; i < _rows.Length; i++)
		{
			PlanRow row = _rows[i];
			bool withinCap = row.DisplacementMeters <= c_OffsetCapMeters + 1e-4f;
			bool minShotsFloor = !withinCap && IsMinShotsFloor(
				_weapon,
				row.EffectiveFireMode,
				row.DistanceMeters,
				row.SeriesShots);
			string suffix = minShotsFloor ? " (MinShots floor, NOTE)" : "";
			AppendCheck(
				_sb,
				_label + " @" + row.DistanceMeters.ToString("F0", CultureInfo.InvariantCulture) +
				" m disp ≤ cap" + suffix,
				withinCap || minShotsFloor);
		}
	}

	private static bool IsMinShotsFloor(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		float _distanceMeters,
		int _seriesShots)
	{
		if (_weapon == null || _seriesShots <= 1)
			return false;

		WeaponRecoilContext context = WeaponRecoilContext.CreateBaseline(_weapon, _fireMode);
		float dispAtSeries = WeaponRecoilMath.OffsetToDisplacementMeters(
			WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, _seriesShots),
			_distanceMeters);
		if (dispAtSeries <= c_OffsetCapMeters + 1e-4f)
			return false;

		float dispOneLess = WeaponRecoilMath.OffsetToDisplacementMeters(
			WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(in context, _seriesShots - 1),
			_distanceMeters);
		return dispOneLess <= c_OffsetCapMeters + 1e-4f;
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
