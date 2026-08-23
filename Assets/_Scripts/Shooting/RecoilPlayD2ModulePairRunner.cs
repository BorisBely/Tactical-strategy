using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase D2 MATH two-module loadout on M4 ModA_2 (multi-slot). Muzzle brake + recoil helper pair.
/// Does not retune assets.
/// </summary>
public static class RecoilPlayD2ModulePairRunner
{
	#region Constants
	public const string M4ModA2WeaponAssetName = "Weapon_M4_ModA_2";
	public const string M4MuzzleBrakeAssetName = "Attachment_M4_MuzzleBrakeM4";
	public const string M4ForeGripAssetName = "Attachment_M4_ForeGrip1";
	public const string M4StockAssetName = "Attachment_M4_Stock1";

	private const float c_M4ModA2BaseOff5Degrees = 0.270f;
	private const float c_BaseRegressionEpsilonDegrees = 0.015f;
	private const float c_MinModuleDeltaDegrees = 0.01f;
	#endregion

	#region Nested Types
	private struct LoadoutRow
	{
		public string Label;
		public float Off5;
		public float KickProduct;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4ModA2,
		WeaponAttachmentDefinition _muzzle,
		WeaponAttachmentDefinition _foreGrip,
		WeaponAttachmentDefinition _stock)
	{
		var sb = new StringBuilder(2048);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayD2ModulePair MATH");
		sb.AppendLine("Phase D2: M4 ModA_2 multi-slot loadouts. Aiming, stand, FullAuto, InstanceHash=0.");
		sb.AppendLine("Pair rule: muzzle brake (kick↑) + foregrip/stock (kick↓) — net between Base and muzzle-only.");
		sb.AppendLine();

		if (_m4ModA2 == null)
		{
			sb.AppendLine("FAIL: Weapon_M4_ModA_2 missing.");
			return sb.ToString();
		}

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_m4ModA2);
		LoadoutRow baseRow = Sample(_m4ModA2, fireMode, null, "Base");
		LoadoutRow muzzleRow = Sample(_m4ModA2, fireMode, new[] { _muzzle }, "Muzzle brake");
		LoadoutRow forePairRow = Sample(_m4ModA2, fireMode, new[] { _muzzle, _foreGrip }, "Muzzle + ForeGrip1");
		LoadoutRow stockPairRow = Sample(_m4ModA2, fireMode, new[] { _muzzle, _stock }, "Muzzle + Stock1");

		AppendRow(sb, in baseRow, culture);
		AppendRow(sb, in muzzleRow, culture);
		AppendRow(sb, in forePairRow, culture);
		AppendRow(sb, in stockPairRow, culture);

		sb.AppendLine("Form checks:");
		AppendCheck(sb, "M4 ModA_2 Base @5 ≈ 0.270° (N7)",
			Mathf.Abs(baseRow.Off5 - c_M4ModA2BaseOff5Degrees) <= c_BaseRegressionEpsilonDegrees);
		AppendCheck(sb, "Muzzle-only > Base", muzzleRow.Off5 > baseRow.Off5 + c_MinModuleDeltaDegrees);
		AppendCheck(sb, "ForeGrip pair < Muzzle-only", forePairRow.Off5 < muzzleRow.Off5 - c_MinModuleDeltaDegrees);
		AppendCheck(sb, "ForeGrip pair > Base", forePairRow.Off5 > baseRow.Off5 + c_MinModuleDeltaDegrees);
		AppendCheck(sb, "Stock pair < Muzzle-only", stockPairRow.Off5 < muzzleRow.Off5 - c_MinModuleDeltaDegrees);
		AppendCheck(sb, "ForeGrip pair < Stock pair (stronger recoil cut)",
			forePairRow.Off5 < stockPairRow.Off5 - c_MinModuleDeltaDegrees);
		AppendCheck(sb, "Muzzle SupportsWeapon", _muzzle != null && _muzzle.SupportsWeapon(_m4ModA2));
		AppendCheck(sb, "ForeGrip SupportsWeapon", _foreGrip != null && _foreGrip.SupportsWeapon(_m4ModA2));
		AppendCheck(sb, "Stock SupportsWeapon", _stock != null && _stock.SupportsWeapon(_m4ModA2));
		AppendProductCheck(sb, "ForeGrip pair kick product",
			forePairRow.KickProduct,
			_muzzle != null && _foreGrip != null
				? _muzzle.GetRecoilModifier(fireMode) * _foreGrip.GetRecoilModifier(fireMode)
				: 0f);
		sb.AppendLine("  NOTE: AK-47 has no second recoil module in content — D2 scope is M4 ModA_2 only.");
		sb.AppendLine("  Assets not changed. D1 single-module frozen. Full loadout generator = phase G.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static LoadoutRow Sample(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		WeaponAttachmentDefinition[] _attachments,
		string _label)
	{
		var row = new LoadoutRow { Label = _label };
		row.KickProduct = _attachments == null || _attachments.Length == 0
			? 1f
			: WeaponDistanceAimEvaluator.GetAttachmentRecoilProduct(_attachments, _fireMode);
		row.Off5 = Off5(_weapon, _fireMode, _attachments);
		return row;
	}

	private static float Off5(
		WeaponDefinition _weapon,
		WeaponFireMode _fireMode,
		WeaponAttachmentDefinition[] _attachments)
	{
		WeaponRecoilContext context = _attachments == null || _attachments.Length == 0
			? WeaponRecoilContext.CreateBaseline(_weapon, _fireMode)
			: WeaponRecoilContext.CreateFromAttachments(_weapon, _attachments, _fireMode);
		context.PoseKickMultiplier = WeaponPoseCombatModifiers.AimingKickMultiplier;
		context.PoseRecoveryMultiplier = WeaponPoseCombatModifiers.AimingRecoveryMultiplier;
		context.StanceKickMultiplier = RecoilPlayBaselineProtocol.StandingKickMultiplier;
		context.StanceRecoveryMultiplier = RecoilPlayBaselineProtocol.StandingRecoveryMultiplier;
		return WeaponRecoilMath.PredictOffsetAfterShots(in context, 5).magnitude;
	}

	private static void AppendRow(StringBuilder _sb, in LoadoutRow _row, CultureInfo _culture)
	{
		_sb.AppendLine(_row.Label);
		_sb.AppendLine(
			"  |Off|@5=" + _row.Off5.ToString("F3", _culture) +
			"°  kick×=" + _row.KickProduct.ToString("F3", _culture));
		_sb.AppendLine();
	}

	private static void AppendProductCheck(StringBuilder _sb, string _label, float _actual, float _expected)
	{
		AppendCheck(_sb, _label + " = " + _expected.ToString("F3", CultureInfo.InvariantCulture),
			Mathf.Abs(_actual - _expected) < 0.001f);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
