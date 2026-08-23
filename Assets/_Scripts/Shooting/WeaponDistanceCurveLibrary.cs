using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Дистанционные кривые оружейных платформ и множители автоматической очереди.
/// Роли по дистанции аналогичны OpticDistanceCurveLibrary: sweet spot, пересечение, деградация вне роли.
/// </summary>
public static class WeaponDistanceCurveLibrary
{
	#region Nested Types
	public enum WeaponBalanceKind
	{
		CqbShort,
		CqbControlled,
		ShotgunCqb,
		Carbine,
		CarbineModA1,
		CarbineModA2,
		BattleRifle762,
		BattleRifle762Default,
		BattleRifle762WoodHandguard,
		BattleRifle762Mod1,
		Intermediate545,
		MidRifle,
		Marksman,
		Dmr,
		Support762,
		Support545,
		HeavySupport,
		GrenadeSupport,

		// Legacy aliases
		Ak47 = BattleRifle762Default,
		Ak47WoodHandguard = BattleRifle762WoodHandguard,
		Ak47FoldingStock = CqbControlled,
		Ak74 = Intermediate545,
		Ak74Short = CqbShort,
		Rpk47 = Support762,
		Rpk74 = Support545,
		M4Carbine = CarbineModA1,
		Mk18Cqb = CqbShort,
		M16Rifle = MidRifle,
		M16A4Marksman = Marksman,
		Mk12Dmr = Dmr,
		M16Marksman = Marksman,
		M4Mod2 = CarbineModA2
	}

	public readonly struct WeaponBalanceCurves
	{
		public readonly OpticDistanceCurveLibrary.DistanceKeyframe[] DispersionKeyframes;
		public readonly OpticDistanceCurveLibrary.DistanceKeyframe[] AimTimeKeyframes;
		public readonly OpticDistanceCurveLibrary.DistanceKeyframe[] AutoBurstSpreadKeyframes;

		public WeaponBalanceCurves(
			OpticDistanceCurveLibrary.DistanceKeyframe[] _dispersionKeyframes,
			OpticDistanceCurveLibrary.DistanceKeyframe[] _aimTimeKeyframes,
			OpticDistanceCurveLibrary.DistanceKeyframe[] _autoBurstSpreadKeyframes)
		{
			DispersionKeyframes = _dispersionKeyframes;
			AimTimeKeyframes = _aimTimeKeyframes;
			AutoBurstSpreadKeyframes = _autoBurstSpreadKeyframes;
		}
	}
	#endregion

	#region Public Methods
	public static WeaponBalanceKind ResolveKind(WeaponDefinition _weapon)
	{
		if (_weapon == null)
			return WeaponBalanceKind.Carbine;

		string name = _weapon.name ?? string.Empty;
		if (TryResolveKindByName(name, out WeaponBalanceKind namedKind))
			return namedKind;

		if (name.Contains("RPK74"))
			return WeaponBalanceKind.Support545;
		if (name.Contains("RPK47"))
			return WeaponBalanceKind.Support762;
		if (name.Contains("AK74U"))
			return WeaponBalanceKind.CqbShort;
		if (name.Contains("AK74"))
			return WeaponBalanceKind.Intermediate545;
		if (name.Contains("AK47S"))
			return WeaponBalanceKind.CqbControlled;
		if (name.Contains("AK47") || name.Contains("AK"))
			return WeaponBalanceKind.BattleRifle762;
		if (name.Contains("MK12"))
			return WeaponBalanceKind.Dmr;
		if (name.Contains("MK18"))
			return WeaponBalanceKind.CqbShort;
		if (name.Contains("M16A4"))
			return WeaponBalanceKind.Marksman;
		if (name.Contains("M16A"))
			return WeaponBalanceKind.MidRifle;
		if (name.Contains("M4"))
			return WeaponBalanceKind.Carbine;

		return WeaponBalanceKind.Carbine;
	}

	public static WeaponBalanceCurves GetCurves(WeaponBalanceKind _kind) =>
		_kind switch
		{
			WeaponBalanceKind.CqbShort => s_CqbShort,
			WeaponBalanceKind.CqbControlled => s_CqbControlled,
			WeaponBalanceKind.ShotgunCqb => s_ShotgunCqb,
			WeaponBalanceKind.Carbine => s_Carbine,
			WeaponBalanceKind.CarbineModA1 => s_CarbineModA1,
			WeaponBalanceKind.CarbineModA2 => s_CarbineModA2,
			WeaponBalanceKind.BattleRifle762 => s_BattleRifle762,
			WeaponBalanceKind.BattleRifle762Default => s_BattleRifle762Default,
			WeaponBalanceKind.BattleRifle762WoodHandguard => s_BattleRifle762WoodHandguard,
			WeaponBalanceKind.BattleRifle762Mod1 => s_BattleRifle762Mod1,
			WeaponBalanceKind.Intermediate545 => s_Intermediate545,
			WeaponBalanceKind.MidRifle => s_MidRifle,
			WeaponBalanceKind.Marksman => s_Marksman,
			WeaponBalanceKind.Dmr => s_Dmr,
			WeaponBalanceKind.Support762 => s_Support762,
			WeaponBalanceKind.Support545 => s_Support545,
			WeaponBalanceKind.HeavySupport => s_HeavySupport,
			WeaponBalanceKind.GrenadeSupport => s_GrenadeSupport,
			_ => s_Carbine
		};

	public static void ApplyToWeapon(WeaponDefinition _weapon)
	{
		if (_weapon == null)
			return;

		WeaponBalanceCurves curves = GetCurves(ResolveKind(_weapon));
		_weapon.SetCombatBalanceData(
			_weapon.BaseShotDispersion,
			OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes),
			OpticDistanceCurveLibrary.BuildCurve(curves.AimTimeKeyframes),
			OpticDistanceCurveLibrary.BuildCurve(curves.AutoBurstSpreadKeyframes));
	}

	public static bool TryResolveKindByName(string _assetName, out WeaponBalanceKind _kind) =>
		s_NamedWeapons.TryGetValue(_assetName, out _kind);
	#endregion

	#region Curve Data
	private static OpticDistanceCurveLibrary.DistanceKeyframe K(float _distanceMeters, float _value) =>
		new OpticDistanceCurveLibrary.DistanceKeyframe(_distanceMeters, _value);

	private static OpticDistanceCurveLibrary.DistanceKeyframe[] BurstRole(
		float _b1, float _b3, float _b6, float _b10) =>
		new[] { K(1f, _b1), K(3f, _b3), K(6f, _b6), K(10f, _b10) };

	private static readonly WeaponBalanceCurves s_CqbShort = new WeaponBalanceCurves(
		new[] { K(0f, 0.58f), K(50f, 0.66f), K(100f, 0.74f), K(150f, 0.97f), K(220f, 1.52f), K(300f, 2.35f) },
		new[] { K(0f, 0.92f), K(50f, 0.98f), K(100f, 1.05f), K(150f, 1.37f), K(220f, 2.20f), K(300f, 3.19f) },
		BurstRole(1.00f, 1.50f, 3.10f, 6.00f));

	private static readonly WeaponBalanceCurves s_CqbControlled = new WeaponBalanceCurves(
		new[] { K(0f, 0.62f), K(50f, 0.70f), K(100f, 0.78f), K(150f, 0.97f), K(220f, 1.38f), K(300f, 2.05f) },
		new[] { K(0f, 0.84f), K(50f, 0.94f), K(100f, 1.05f), K(150f, 1.35f), K(220f, 2.06f), K(300f, 2.93f) },
		BurstRole(1.00f, 1.42f, 2.75f, 5.20f));

	private static readonly WeaponBalanceCurves s_ShotgunCqb = new WeaponBalanceCurves(
		new[]
		{
			K(0f, 0.72f), K(15f, 0.95f), K(25f, 1.45f), K(40f, 2.40f),
			K(60f, 3.90f), K(100f, 6.00f), K(150f, 7.00f)
		},
		new[]
		{
			K(0f, 1.05f), K(15f, 1.18f), K(25f, 1.45f), K(40f, 1.95f),
			K(60f, 2.80f), K(100f, 4.20f), K(150f, 4.87f)
		},
		BurstRole(1.00f, 1.65f, 3.40f, 6.50f));

	private static readonly WeaponBalanceCurves s_Carbine = new WeaponBalanceCurves(
		new[] { K(0f, 0.72f), K(75f, 0.80f), K(150f, 0.90f), K(220f, 1.01f), K(300f, 1.12f) },
		new[] { K(0f, 0.85f), K(75f, 1.03f), K(150f, 1.35f), K(220f, 1.90f), K(300f, 2.60f) },
		BurstRole(1.00f, 1.25f, 1.90f, 3.20f));

	private static readonly WeaponBalanceCurves s_CarbineModA1 = new WeaponBalanceCurves(
		new[] { K(0f, 0.73f), K(75f, 0.81f), K(150f, 0.89f), K(220f, 0.98f), K(300f, 1.08f) },
		new[] { K(0f, 0.87f), K(75f, 1.03f), K(150f, 1.31f), K(220f, 1.84f), K(300f, 2.50f) },
		BurstRole(1.00f, 1.24f, 1.84f, 3.08f));

	private static readonly WeaponBalanceCurves s_CarbineModA2 = new WeaponBalanceCurves(
		new[] { K(0f, 0.75f), K(75f, 0.80f), K(150f, 0.87f), K(220f, 0.96f), K(300f, 1.04f) },
		new[] { K(0f, 0.90f), K(75f, 1.03f), K(150f, 1.29f), K(220f, 1.78f), K(300f, 2.41f) },
		BurstRole(1.00f, 1.23f, 1.82f, 3.02f));

	private static readonly WeaponBalanceCurves s_BattleRifle762 = new WeaponBalanceCurves(
		new[] { K(0f, 0.78f), K(75f, 0.88f), K(150f, 1.00f), K(220f, 1.14f), K(300f, 1.30f) },
		new[] { K(0f, 0.95f), K(75f, 1.19f), K(150f, 1.57f), K(220f, 2.19f), K(300f, 2.95f) },
		BurstRole(1.00f, 1.45f, 2.60f, 4.40f));

	private static readonly WeaponBalanceCurves s_BattleRifle762Default = new WeaponBalanceCurves(
		new[] { K(0f, 0.80f), K(75f, 0.91f), K(150f, 1.03f), K(220f, 1.16f), K(300f, 1.34f) },
		new[] { K(0f, 0.96f), K(75f, 1.21f), K(150f, 1.61f), K(220f, 2.25f), K(300f, 3.04f) },
		BurstRole(1.00f, 1.49f, 2.72f, 4.62f));

	private static readonly WeaponBalanceCurves s_BattleRifle762WoodHandguard = new WeaponBalanceCurves(
		new[] { K(0f, 0.79f), K(75f, 0.88f), K(150f, 0.98f), K(220f, 1.08f), K(300f, 1.22f) },
		new[] { K(0f, 0.98f), K(75f, 1.20f), K(150f, 1.55f), K(220f, 2.12f), K(300f, 2.86f) },
		BurstRole(1.00f, 1.42f, 2.48f, 4.12f));

	private static readonly WeaponBalanceCurves s_BattleRifle762Mod1 = new WeaponBalanceCurves(
		new[] { K(0f, 0.82f), K(75f, 0.89f), K(150f, 0.96f), K(220f, 1.06f), K(300f, 1.18f) },
		new[] { K(0f, 1.02f), K(75f, 1.22f), K(150f, 1.56f), K(220f, 2.10f), K(300f, 2.80f) },
		BurstRole(1.00f, 1.40f, 2.38f, 3.92f));

	private static readonly WeaponBalanceCurves s_Intermediate545 = new WeaponBalanceCurves(
		new[] { K(0f, 0.74f), K(75f, 0.82f), K(150f, 0.91f), K(220f, 1.00f), K(300f, 1.06f) },
		new[] { K(0f, 0.90f), K(75f, 1.11f), K(150f, 1.43f), K(220f, 1.98f), K(300f, 2.58f) },
		BurstRole(1.00f, 1.30f, 2.10f, 3.50f));

	private static readonly WeaponBalanceCurves s_MidRifle = new WeaponBalanceCurves(
		new[] { K(0f, 0.90f), K(75f, 0.80f), K(150f, 0.65f), K(200f, 0.70f), K(250f, 0.82f), K(300f, 1.00f) },
		new[] { K(0f, 1.25f), K(75f, 1.15f), K(150f, 1.12f), K(200f, 1.30f), K(250f, 1.55f), K(300f, 1.90f) },
		BurstRole(1.00f, 1.15f, 1.65f, 2.60f));

	private static readonly WeaponBalanceCurves s_Marksman = new WeaponBalanceCurves(
		new[] { K(0f, 1.00f), K(75f, 0.88f), K(150f, 0.62f), K(200f, 0.58f), K(250f, 0.60f), K(300f, 0.78f) },
		new[] { K(0f, 1.50f), K(80f, 1.32f), K(150f, 1.28f), K(200f, 1.30f), K(250f, 1.45f), K(300f, 1.70f) },
		BurstRole(1.00f, 1.10f, 1.45f, 2.20f));

	private static readonly WeaponBalanceCurves s_Dmr = new WeaponBalanceCurves(
		new[] { K(0f, 1.15f), K(80f, 1.05f), K(150f, 0.80f), K(220f, 0.58f), K(260f, 0.50f), K(300f, 0.55f) },
		new[] { K(0f, 1.80f), K(80f, 1.65f), K(150f, 1.60f), K(220f, 1.55f), K(260f, 1.58f), K(300f, 1.70f) },
		BurstRole(1.00f, 1.08f, 1.32f, 1.90f));

	private static readonly WeaponBalanceCurves s_Support762 = new WeaponBalanceCurves(
		new[] { K(0f, 1.05f), K(80f, 0.92f), K(150f, 0.74f), K(200f, 0.76f), K(250f, 0.86f), K(300f, 1.05f) },
		new[] { K(0f, 1.55f), K(80f, 1.38f), K(150f, 1.35f), K(200f, 1.45f), K(250f, 1.70f), K(300f, 2.10f) },
		BurstRole(1.00f, 1.18f, 1.55f, 2.50f));

	private static readonly WeaponBalanceCurves s_Support545 = new WeaponBalanceCurves(
		new[] { K(0f, 1.00f), K(80f, 0.88f), K(150f, 0.66f), K(200f, 0.68f), K(250f, 0.78f), K(300f, 0.95f) },
		new[] { K(0f, 1.50f), K(80f, 1.32f), K(150f, 1.28f), K(200f, 1.38f), K(250f, 1.60f), K(300f, 1.95f) },
		BurstRole(1.00f, 1.12f, 1.42f, 2.20f));

	private static readonly WeaponBalanceCurves s_HeavySupport = new WeaponBalanceCurves(
		new[] { K(0f, 1.10f), K(80f, 0.90f), K(150f, 0.75f), K(200f, 0.70f), K(250f, 0.78f), K(300f, 0.95f) },
		new[] { K(0f, 0.85f), K(80f, 0.92f), K(150f, 1.05f), K(200f, 1.15f), K(250f, 1.30f), K(300f, 1.50f) },
		BurstRole(1.00f, 1.20f, 1.70f, 2.80f));

	private static readonly WeaponBalanceCurves s_GrenadeSupport = new WeaponBalanceCurves(
		new[] { K(0f, 1.10f), K(80f, 0.90f), K(150f, 0.75f), K(200f, 0.70f), K(250f, 0.78f), K(300f, 0.95f) },
		new[] { K(0f, 0.85f), K(80f, 0.92f), K(150f, 1.05f), K(200f, 1.15f), K(250f, 1.30f), K(300f, 1.50f) },
		BurstRole(1.00f, 1.20f, 1.70f, 2.80f));
	#endregion

	#region Named Lookup
	private static readonly Dictionary<string, WeaponBalanceKind> s_NamedWeapons = new Dictionary<string, WeaponBalanceKind>
	{
		["Weapon_AK47"] = WeaponBalanceKind.BattleRifle762Default,
		["Weapon_AK47_1"] = WeaponBalanceKind.BattleRifle762WoodHandguard,
		["Weapon_AK47MOD1"] = WeaponBalanceKind.BattleRifle762Mod1,
		["Weapon_AK47S"] = WeaponBalanceKind.CqbControlled,
		["Weapon_AK74"] = WeaponBalanceKind.Intermediate545,
		["Weapon_AK74MOD1"] = WeaponBalanceKind.Intermediate545,
		["Weapon_AK74U"] = WeaponBalanceKind.CqbShort,
		["Weapon_AK74UMOD1"] = WeaponBalanceKind.CqbControlled,
		["Weapon_RPK47"] = WeaponBalanceKind.Support762,
		["Weapon_RPK47MOD1"] = WeaponBalanceKind.Support762,
		["Weapon_RPK74"] = WeaponBalanceKind.Support545,
		["Weapon_RPK74MOD1"] = WeaponBalanceKind.Support545,
		["Weapon_M4_ModA_1"] = WeaponBalanceKind.CarbineModA1,
		["Weapon_M4_ModA_2"] = WeaponBalanceKind.CarbineModA2,
		["Weapon_M16A_ModA_1"] = WeaponBalanceKind.MidRifle,
		["Weapon_M16A4_ModA_2"] = WeaponBalanceKind.Marksman,
		["Weapon_MK12"] = WeaponBalanceKind.Dmr,
		["Weapon_MK18"] = WeaponBalanceKind.CqbShort,
		["Weapon_Mosin"] = WeaponBalanceKind.Marksman,
		["Weapon_BenelliM4"] = WeaponBalanceKind.ShotgunCqb,
		["Weapon_M249"] = WeaponBalanceKind.Support545,
		["Weapon_Sniper762x51"] = WeaponBalanceKind.Dmr,
		["Weapon_PKM"] = WeaponBalanceKind.Support762,
		["Weapon_SVD"] = WeaponBalanceKind.Marksman,
		["Weapon_M2Browning_127"] = WeaponBalanceKind.HeavySupport,
		["Weapon_MK19"] = WeaponBalanceKind.GrenadeSupport
	};
	#endregion
}
