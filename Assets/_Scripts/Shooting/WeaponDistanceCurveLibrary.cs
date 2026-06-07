using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Дистанционные кривые оружейных платформ и множители автоматической очереди.
/// Источник: Assets/Docs/CombatBalance/CombatBalanceTables.md
/// </summary>
public static class WeaponDistanceCurveLibrary
{
	#region Nested Types
	public enum WeaponBalanceKind
	{
		Ak47,
		M4Mod2
	}

	public readonly struct WeaponBalanceCurves
	{
		public readonly float BaseShotDispersion;
		public readonly OpticDistanceCurveLibrary.DistanceKeyframe[] DispersionKeyframes;
		public readonly OpticDistanceCurveLibrary.DistanceKeyframe[] AimTimeKeyframes;
		public readonly OpticDistanceCurveLibrary.DistanceKeyframe[] AutoBurstSpreadKeyframes;

		public WeaponBalanceCurves(
			float _baseShotDispersion,
			OpticDistanceCurveLibrary.DistanceKeyframe[] _dispersionKeyframes,
			OpticDistanceCurveLibrary.DistanceKeyframe[] _aimTimeKeyframes,
			OpticDistanceCurveLibrary.DistanceKeyframe[] _autoBurstSpreadKeyframes)
		{
			BaseShotDispersion = _baseShotDispersion;
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
			return WeaponBalanceKind.M4Mod2;

		string name = _weapon.name ?? string.Empty;
		if (name.Contains("AK"))
			return WeaponBalanceKind.Ak47;

		return WeaponBalanceKind.M4Mod2;
	}

	public static WeaponBalanceCurves GetCurves(WeaponBalanceKind _kind) =>
		_kind switch
		{
			WeaponBalanceKind.Ak47 => s_Ak47,
			_ => s_M4Mod2
		};

	public static void ApplyToWeapon(WeaponDefinition _weapon)
	{
		if (_weapon == null)
			return;

		WeaponBalanceCurves curves = GetCurves(ResolveKind(_weapon));
		_weapon.SetCombatBalanceData(
			curves.BaseShotDispersion,
			OpticDistanceCurveLibrary.BuildCurve(curves.DispersionKeyframes),
			OpticDistanceCurveLibrary.BuildCurve(curves.AimTimeKeyframes),
			OpticDistanceCurveLibrary.BuildCurve(curves.AutoBurstSpreadKeyframes));
	}
	#endregion

	#region Curve Data
	private static OpticDistanceCurveLibrary.DistanceKeyframe K(float _distanceMeters, float _value) =>
		new OpticDistanceCurveLibrary.DistanceKeyframe(_distanceMeters, _value);

	private static readonly WeaponBalanceCurves s_Ak47 = new WeaponBalanceCurves(
		1.15f,
		new[]
		{
			K(0f, 0.75f), K(10f, 0.82f), K(20f, 0.90f), K(30f, 1.00f), K(40f, 1.12f),
			K(50f, 1.26f), K(60f, 1.42f), K(70f, 1.60f), K(80f, 1.80f), K(90f, 2.02f), K(100f, 2.25f)
		},
		new[]
		{
			K(0f, 1.09f), K(10f, 1.42f), K(20f, 1.75f), K(30f, 2.08f), K(40f, 2.41f),
			K(50f, 2.74f), K(60f, 3.06f), K(70f, 3.39f), K(80f, 3.72f), K(90f, 4.05f), K(100f, 4.38f)
		},
		new[]
		{
			K(1f, 1.00f), K(2f, 1.15f), K(3f, 1.35f), K(4f, 1.65f), K(5f, 2.00f),
			K(6f, 2.35f), K(7f, 2.70f), K(8f, 3.05f), K(9f, 3.40f), K(10f, 3.75f)
		});

	private static readonly WeaponBalanceCurves s_M4Mod2 = new WeaponBalanceCurves(
		0.90f,
		new[]
		{
			K(0f, 0.68f), K(10f, 0.74f), K(20f, 0.82f), K(30f, 0.92f), K(40f, 1.03f),
			K(50f, 1.15f), K(60f, 1.29f), K(70f, 1.44f), K(80f, 1.60f), K(90f, 1.78f), K(100f, 1.98f)
		},
		new[]
		{
			K(0f, 1.07f), K(10f, 1.39f), K(20f, 1.71f), K(30f, 2.04f), K(40f, 2.36f),
			K(50f, 2.68f), K(60f, 3.00f), K(70f, 3.32f), K(80f, 3.65f), K(90f, 3.97f), K(100f, 4.29f)
		},
		new[]
		{
			K(1f, 1.00f), K(2f, 1.10f), K(3f, 1.25f), K(4f, 1.45f), K(5f, 1.68f),
			K(6f, 1.92f), K(7f, 2.16f), K(8f, 2.40f), K(9f, 2.64f), K(10f, 2.88f)
		});
	#endregion

	#region Named Lookup
	private static readonly Dictionary<string, WeaponBalanceKind> s_NamedWeapons = new Dictionary<string, WeaponBalanceKind>
	{
		["Weapon_AK47"] = WeaponBalanceKind.Ak47,
		["Weapon_M4_ModA_1"] = WeaponBalanceKind.M4Mod2,
		["Weapon_M4_ModA_2"] = WeaponBalanceKind.M4Mod2
	};

	public static bool TryResolveKindByName(string _assetName, out WeaponBalanceKind _kind) =>
		s_NamedWeapons.TryGetValue(_assetName, out _kind);
	#endregion
}
