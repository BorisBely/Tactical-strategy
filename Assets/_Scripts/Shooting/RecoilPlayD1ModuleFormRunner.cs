using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Phase D1 MATH single-module delta vs Base on M4 / AK-47. M249/PKM base regression only.
/// Does not retune assets. Cosmetic-only modules out of scope.
/// </summary>
public static class RecoilPlayD1ModuleFormRunner
{
	#region Constants
	public const string Ak47WeaponAssetName = "Weapon_AK47";
	public const string M4MuzzleBrakeAssetName = "Attachment_M4_MuzzleBrakeM4";
	public const string AkMuzzleBrakeAssetName = "Attachment_AK_MuzzleBrakeAK";

	private const float c_B0RegressionEpsilonDegrees = 0.015f;
	private const float c_B8RegressionEpsilonDegrees = 0.015f;
	private const float c_MinModuleDeltaDegrees = 0.01f;
	#endregion

	#region Nested Types
	private struct ModuleRow
	{
		public string WeaponName;
		public string ModuleName;
		public float Base5;
		public float Loaded5;
		public float ModuleKickProduct;
		public bool HasModule;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4,
		WeaponAttachmentDefinition _m4Module,
		WeaponDefinition _ak47,
		WeaponAttachmentDefinition _ak47Module,
		WeaponDefinition _m249,
		WeaponDefinition _pkm)
	{
		var sb = new StringBuilder(2048);
		CultureInfo culture = CultureInfo.InvariantCulture;
		sb.AppendLine("RecoilPlayD1ModuleForm MATH");
		sb.AppendLine("Phase D1: Base vs single recoil module. Aiming, stand, FullAuto, InstanceHash=0.");
		sb.AppendLine("M249/PKM: base regression only (no D1 module probe in content).");
		sb.AppendLine();

		ModuleRow m4 = Sample(_m4, _m4Module);
		ModuleRow ak47 = Sample(_ak47, _ak47Module);
		ModuleRow m249 = Sample(_m249, null);
		ModuleRow pkm = Sample(_pkm, null);

		AppendRow(sb, in m4, culture);
		AppendRow(sb, in ak47, culture);
		AppendRow(sb, in m249, culture);
		AppendRow(sb, in pkm, culture);

		sb.AppendLine("Form checks:");
		AppendModuleDirection(sb, in m4, "M4 muzzle brake");
		AppendModuleDirection(sb, in ak47, "AK-47 muzzle brake");
		AppendCheck(sb, "M4 module SupportsWeapon", _m4Module != null && _m4Module.SupportsWeapon(_m4));
		AppendCheck(sb, "AK-47 module SupportsWeapon", _ak47Module != null && _ak47Module.SupportsWeapon(_ak47));
		AppendCheck(sb, "M4 Base @5 ≈ 0.313° (B0)", Mathf.Abs(m4.Base5 - 0.313f) <= c_B0RegressionEpsilonDegrees);
		AppendCheck(sb, "M249 Base @5 ≈ 0.254° (B8)", Mathf.Abs(m249.Base5 - 0.254f) <= c_B8RegressionEpsilonDegrees);
		AppendCheck(sb, "PKM Base @5 ≈ 0.296° (B8)", Mathf.Abs(pkm.Base5 - 0.296f) <= c_B8RegressionEpsilonDegrees);
		sb.AppendLine("  Assets not changed. Pose/stance frozen (C). Multi-loadout pairs = D2.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static ModuleRow Sample(WeaponDefinition _weapon, WeaponAttachmentDefinition _module)
	{
		var row = new ModuleRow
		{
			WeaponName = _weapon != null ? _weapon.name : "?",
			ModuleName = _module != null ? _module.name : "(base only)",
			HasModule = _module != null
		};
		if (_weapon == null)
			return row;

		WeaponFireMode fireMode = WeaponRecoilBalanceContract.ResolveBaselineFireMode(_weapon);
		row.Base5 = Off5(_weapon, fireMode, null);
		if (_module != null)
		{
			row.ModuleKickProduct = _module.GetRecoilModifier(fireMode);
			row.Loaded5 = Off5(_weapon, fireMode, new[] { _module });
		}
		else
		{
			row.ModuleKickProduct = 1f;
			row.Loaded5 = row.Base5;
		}

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

	private static void AppendRow(StringBuilder _sb, in ModuleRow _row, CultureInfo _culture)
	{
		_sb.AppendLine(_row.WeaponName + " + " + _row.ModuleName);
		_sb.AppendLine(
			"  Base@5=" + _row.Base5.ToString("F3", _culture) +
			"°  Loaded@5=" + _row.Loaded5.ToString("F3", _culture) +
			"°  kick×=" + _row.ModuleKickProduct.ToString("F2", _culture));
		if (_row.HasModule)
		{
			float delta = _row.Loaded5 - _row.Base5;
			_sb.AppendLine("  Δ=" + delta.ToString("F3", _culture) + "°");
		}

		_sb.AppendLine();
	}

	private static void AppendModuleDirection(StringBuilder _sb, in ModuleRow _row, string _label)
	{
		if (!_row.HasModule)
		{
			_sb.AppendLine("  WARN  " + _label + " missing");
			return;
		}

		bool expectIncrease = _row.ModuleKickProduct > 1f + 1e-4f;
		bool expectDecrease = _row.ModuleKickProduct < 1f - 1e-4f;
		bool ok = expectIncrease
			? _row.Loaded5 > _row.Base5 + c_MinModuleDeltaDegrees
			: expectDecrease
				? _row.Loaded5 < _row.Base5 - c_MinModuleDeltaDegrees
				: Mathf.Abs(_row.Loaded5 - _row.Base5) <= c_MinModuleDeltaDegrees;
		AppendCheck(_sb, _label + " Δ matches kick product", ok);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _ok)
	{
		_sb.AppendLine("  " + (_ok ? "OK  " : "WARN") + "  " + _label);
	}
	#endregion
}
