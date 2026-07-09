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

	private static OpticDistanceCurveLibrary.DistanceKeyframe[] DispRole(
		float _d0, float _d25, float _d50, float _d75, float _d100) =>
		new[] { K(0f, _d0), K(125f, _d25), K(250f, _d50), K(375f, _d75), K(500f, _d100) };

	private static OpticDistanceCurveLibrary.DistanceKeyframe[] AimRole(
		float _d0, float _d25, float _d50, float _d75, float _d100) =>
		new[] { K(0f, _d0), K(125f, _d25), K(250f, _d50), K(375f, _d75), K(500f, _d100) };

	private static OpticDistanceCurveLibrary.DistanceKeyframe[] BurstRole(
		float _b1, float _b3, float _b6, float _b10) =>
		new[] { K(1f, _b1), K(3f, _b3), K(6f, _b6), K(10f, _b10) };

	// CqbShort: close aim toned down — reddot helps but not instant ADS
	private static readonly WeaponBalanceCurves s_CqbShort = new WeaponBalanceCurves(
		DispRole(0.58f, 0.78f, 1.75f, 3.25f, 5.00f),
		AimRole(0.92f, 1.08f, 2.55f, 4.15f, 5.85f),
		BurstRole(1.00f, 1.50f, 3.10f, 6.00f));

	// CqbControlled: tactical short — slightly slower snap than raw CQB
	private static readonly WeaponBalanceCurves s_CqbControlled = new WeaponBalanceCurves(
		DispRole(0.62f, 0.82f, 1.55f, 2.80f, 4.30f),
		AimRole(0.84f, 1.10f, 2.36f, 3.79f, 5.33f),
		BurstRole(1.00f, 1.42f, 2.75f, 5.20f));

	// ShotgunCqb: real-meter niche 0-60 m — dominant close, collapses past rifle CQB
	private static readonly WeaponBalanceCurves s_ShotgunCqb = new WeaponBalanceCurves(
		new[]
		{
			K(0f, 0.72f),
			K(15f, 0.95f),
			K(25f, 1.45f),
			K(40f, 2.40f),
			K(60f, 3.90f),
			K(100f, 6.00f),
			K(250f, 9.00f),
			K(500f, 12.00f)
		},
		new[]
		{
			K(0f, 1.05f),
			K(15f, 1.18f),
			K(25f, 1.45f),
			K(40f, 1.95f),
			K(60f, 2.80f),
			K(100f, 4.20f),
			K(250f, 6.20f),
			K(500f, 8.50f)
		},
		BurstRole(1.00f, 1.65f, 3.40f, 6.50f));

	// Carbine: softened 375-500 m for realism after distance stretch
	private static readonly WeaponBalanceCurves s_Carbine = new WeaponBalanceCurves(
		DispRole(0.72f, 0.86f, 1.05f, 1.22f, 1.50f),
		AimRole(0.85f, 1.15f, 2.14f, 3.29f, 4.46f),
		BurstRole(1.00f, 1.25f, 1.90f, 3.20f));

	// CarbineModA1: light M4 carbine - cleaner 50m handling, still not a rifle at 100m
	private static readonly WeaponBalanceCurves s_CarbineModA1 = new WeaponBalanceCurves(
		DispRole(0.73f, 0.86f, 1.02f, 1.18f, 1.45f),
		AimRole(0.87f, 1.13f, 2.05f, 3.17f, 4.34f),
		BurstRole(1.00f, 1.24f, 1.84f, 3.08f));

	// CarbineModA2: railed M4 - steadier through medium distance, softer far-end penalty
	private static readonly WeaponBalanceCurves s_CarbineModA2 = new WeaponBalanceCurves(
		DispRole(0.75f, 0.84f, 1.00f, 1.10f, 1.40f),
		AimRole(0.90f, 1.12f, 1.98f, 3.05f, 4.20f),
		BurstRole(1.00f, 1.23f, 1.82f, 3.02f));

	// BattleRifle762: softened 375-500 m, still rougher than 5.56 carbines
	private static readonly WeaponBalanceCurves s_BattleRifle762 = new WeaponBalanceCurves(
		DispRole(0.78f, 0.95f, 1.20f, 1.48f, 1.95f),
		AimRole(0.95f, 1.35f, 2.45f, 3.69f, 4.93f),
		BurstRole(1.00f, 1.45f, 2.60f, 4.40f));

	// BattleRifle762Default: plain AK-47 - rougher past medium range than the platform average
	private static readonly WeaponBalanceCurves s_BattleRifle762Default = new WeaponBalanceCurves(
		DispRole(0.80f, 0.98f, 1.22f, 1.52f, 1.95f),
		AimRole(0.96f, 1.38f, 2.52f, 3.80f, 5.06f),
		BurstRole(1.00f, 1.49f, 2.72f, 4.62f));

	// BattleRifle762WoodHandguard: fuller AK-47 layout - modest medium-range improvement
	private static readonly WeaponBalanceCurves s_BattleRifle762WoodHandguard = new WeaponBalanceCurves(
		DispRole(0.79f, 0.94f, 1.12f, 1.38f, 1.72f),
		AimRole(0.98f, 1.34f, 2.38f, 3.58f, 4.78f),
		BurstRole(1.00f, 1.42f, 2.48f, 4.12f));

	// BattleRifle762Mod1: railed AK-47 - slower to bring up, better controlled once settled
	private static readonly WeaponBalanceCurves s_BattleRifle762Mod1 = new WeaponBalanceCurves(
		DispRole(0.82f, 0.93f, 1.10f, 1.30f, 1.65f),
		AimRole(1.02f, 1.36f, 2.34f, 3.48f, 4.62f),
		BurstRole(1.00f, 1.40f, 2.38f, 3.92f));

	// Intermediate545: AK-74 family - softer far-end than 7.62, still worse than M4 at 500 m
	private static readonly WeaponBalanceCurves s_Intermediate545 = new WeaponBalanceCurves(
		DispRole(0.74f, 0.88f, 1.02f, 1.12f, 1.40f),
		AimRole(0.90f, 1.25f, 2.16f, 3.22f, 4.29f),
		BurstRole(1.00f, 1.30f, 2.10f, 3.50f));

	// MidRifle: M16 - slight far-end soften, keeps rifle edge at 250-375 m
	private static readonly WeaponBalanceCurves s_MidRifle = new WeaponBalanceCurves(
		DispRole(0.90f, 0.75f, 0.65f, 0.88f, 1.45f),
		AimRole(1.25f, 1.12f, 1.62f, 2.24f, 2.99f),
		BurstRole(1.00f, 1.15f, 1.65f, 2.60f));

	// Marksman: Disp 1.00/0.82/0.58/0.70/1.20, Aim softened mid-long (keeps marksman edge), Burst 1.00/1.10/1.45/2.20
	private static readonly WeaponBalanceCurves s_Marksman = new WeaponBalanceCurves(
		DispRole(1.00f, 0.82f, 0.58f, 0.70f, 1.20f),
		AimRole(1.50f, 1.30f, 1.64f, 1.91f, 2.47f),
		BurstRole(1.00f, 1.10f, 1.45f, 2.20f));

	// Dmr: Disp 1.15/1.00/0.70/0.50/0.62, Aim softened mid-long (keeps DMR edge), Burst 1.00/1.08/1.32/1.90
	private static readonly WeaponBalanceCurves s_Dmr = new WeaponBalanceCurves(
		DispRole(1.15f, 1.00f, 0.70f, 0.50f, 0.62f),
		AimRole(1.80f, 1.60f, 1.74f, 1.65f, 1.84f),
		BurstRole(1.00f, 1.08f, 1.32f, 1.90f));

	// Support762: Disp 1.05/0.90/0.74/0.82/1.30, Aim softened mid-long, Burst 1.00/1.18/1.55/2.50
	private static readonly WeaponBalanceCurves s_Support762 = new WeaponBalanceCurves(
		DispRole(1.05f, 0.90f, 0.74f, 0.82f, 1.30f),
		AimRole(1.55f, 1.35f, 1.69f, 2.00f, 2.59f),
		BurstRole(1.00f, 1.18f, 1.55f, 2.50f));

	// Support545: Disp 1.00/0.85/0.66/0.70/1.05, Aim softened mid-long, Burst 1.00/1.12/1.42/2.20
	private static readonly WeaponBalanceCurves s_Support545 = new WeaponBalanceCurves(
		DispRole(1.00f, 0.85f, 0.66f, 0.70f, 1.05f),
		AimRole(1.50f, 1.28f, 1.61f, 1.86f, 2.37f),
		BurstRole(1.00f, 1.12f, 1.42f, 2.20f));
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
		["Weapon_Mosin"] = WeaponBalanceKind.Dmr,
		["Weapon_BenelliM4"] = WeaponBalanceKind.ShotgunCqb,
		["Weapon_M249"] = WeaponBalanceKind.Support545,
		["Weapon_Sniper762x51"] = WeaponBalanceKind.Dmr,
		["Weapon_PKM"] = WeaponBalanceKind.Support762,
		["Weapon_SVD"] = WeaponBalanceKind.Marksman
	};
	#endregion
}
