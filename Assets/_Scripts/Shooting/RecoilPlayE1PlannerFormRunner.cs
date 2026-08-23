using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase E1 MATH fire-discipline planner spot-check: M4 / AK-47 @ 20 m vs 150 m.
/// Does not retune assets or FireDisciplineContractTests.
/// </summary>
public static class RecoilPlayE1PlannerFormRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ak47WeaponAssetName = "Weapon_AK47";

	private const float c_CloseDistanceMeters = 20f;
	private const float c_FarDistanceMeters = 150f;
	private const float c_OffsetCapMeters =
		WeaponAutoModeSelectionUtility.HumanTargetWidthMeters * 0.85f;
	#endregion

	#region Nested Types
	private struct PlanRow
	{
		public string WeaponName;
		public float DistanceMeters;
		public WeaponFireMode EffectiveFireMode;
		public int SeriesShots;
		public float RequiredAim;
		public float OffsetDegAtSeries;
		public float DisplacementMeters;
	}
	#endregion

	#region Public Methods
	public static string Run(WeaponDefinition _m4, WeaponDefinition _ak47)
	{
		var sb = new StringBuilder(2560);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayE1PlannerForm MATH");
		sb.AppendLine("Phase E1: WeaponFireDisciplinePlanner Auto discipline, deterministic.");
		sb.AppendLine("Goal A10: M4/AK aggressive @20 m (Burst/FullAuto); Semi @150 m. Offset cap 0.60 m × 0.85.");
		sb.AppendLine();

		PlanRow m4Close = Sample(_m4, c_CloseDistanceMeters);
		PlanRow m4Far = Sample(_m4, c_FarDistanceMeters);
		PlanRow akClose = Sample(_ak47, c_CloseDistanceMeters);
		PlanRow akFar = Sample(_ak47, c_FarDistanceMeters);

		AppendRow(sb, in m4Close, culture);
		AppendRow(sb, in m4Far, culture);
		AppendRow(sb, in akClose, culture);
		AppendRow(sb, in akFar, culture);

		sb.AppendLine("Form checks:");
		AppendCheck(sb, "M4 @20 m Burst or FullAuto",
			m4Close.EffectiveFireMode == WeaponFireMode.Burst ||
			m4Close.EffectiveFireMode == WeaponFireMode.FullAuto);
		AppendCheck(sb, "M4 @20 m series ≥ 2", m4Close.SeriesShots >= 2);
		AppendCheck(sb, "M4 @150 m SemiAuto", m4Far.EffectiveFireMode == WeaponFireMode.SemiAuto);
		AppendCheck(sb, "M4 @150 m series ≤ 2", m4Far.SeriesShots <= 2);
		AppendCheck(sb, "AK-47 @20 m Burst or FullAuto",
			akClose.EffectiveFireMode == WeaponFireMode.Burst ||
			akClose.EffectiveFireMode == WeaponFireMode.FullAuto);
		AppendCheck(sb, "AK-47 @150 m not FullAuto", akFar.EffectiveFireMode != WeaponFireMode.FullAuto);
		AppendCheck(sb, "AK-47 @150 m series ≤ 4", akFar.SeriesShots <= 4);
		AppendCheck(sb, "M4 @20 m aim < @150 m", m4Close.RequiredAim < m4Far.RequiredAim);
		AppendOffsetCap(sb, in m4Close, "M4 @20 m");
		AppendOffsetCap(sb, in m4Far, "M4 @150 m");
		AppendOffsetCap(sb, in akClose, "AK-47 @20 m");
		AppendOffsetCap(sb, in akFar, "AK-47 @150 m");
		sb.AppendLine("  Assets not changed. FireDisciplineContractTests frozen. E2 distances = next.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static PlanRow Sample(WeaponDefinition _weapon, float _distanceMeters)
	{
		var row = new PlanRow
		{
			WeaponName = _weapon != null ? _weapon.name : "?",
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
		row.OffsetDegAtSeries = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(
			in context,
			row.SeriesShots);
		row.DisplacementMeters = WeaponRecoilMath.OffsetToDisplacementMeters(
			row.OffsetDegAtSeries,
			_distanceMeters);
		return row;
	}

	private static void AppendRow(StringBuilder _sb, in PlanRow _row, CultureInfo _culture)
	{
		_sb.AppendLine(_row.WeaponName + " @ " + _row.DistanceMeters.ToString("F0", _culture) + " m");
		_sb.AppendLine(
			"  mode=" + _row.EffectiveFireMode +
			"  series=" + _row.SeriesShots +
			"  aim=" + _row.RequiredAim.ToString("F2", _culture));
		_sb.AppendLine(
			"  |Off|@series=" + _row.OffsetDegAtSeries.ToString("F3", _culture) +
			"°  disp=" + _row.DisplacementMeters.ToString("F2", _culture) + " m");
		_sb.AppendLine();
	}

	private static void AppendOffsetCap(StringBuilder _sb, in PlanRow _row, string _label)
	{
		AppendCheck(
			_sb,
			_label + " disp ≤ " + c_OffsetCapMeters.ToString("F2", CultureInfo.InvariantCulture) + " m",
			_row.DisplacementMeters <= c_OffsetCapMeters + 1e-4f);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
