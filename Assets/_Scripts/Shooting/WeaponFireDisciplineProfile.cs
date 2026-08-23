using UnityEngine;

/// <summary>
/// Класс поведения огневой дисциплины. Не VisionRange и не MaxFireRange.
/// </summary>
public enum WeaponFireDisciplineProfileKind
{
	Cqb = 0,
	Shotgun = 1,
	Assault = 2,
	Lmg = 3,
	Marksman = 4,
	Sniper = 5,
	Heavy = 6,
	Grenade = 7
}

/// <summary>
/// Нормализованный пояс дисциплины: 0 = вплотную, 1 = дальний край рабочего диапазона класса.
/// </summary>
public enum WeaponFireDisciplineDistanceBand
{
	Close = 0,
	Near = 1,
	Mid = 2,
	Far = 3,
	VeryFar = 4
}

/// <summary>
/// Рабочий диапазон дисциплины по классу ствола. Stage 9 engagement edge, не ScopeVisionRange.
/// </summary>
public static class WeaponFireDisciplineProfile
{
	#region Constants
	public const float CqbWorkingRangeMeters = 150f;
	public const float ShotgunWorkingRangeMeters = 50f;
	public const float AssaultWorkingRangeMeters = 200f;
	public const float LmgWorkingRangeMeters = 220f;
	public const float MarksmanWorkingRangeMeters = 250f;
	public const float SniperWorkingRangeMeters = 300f;
	public const float HeavyWorkingRangeMeters = 300f;
	public const float GrenadeWorkingRangeMeters = 300f;

	public const float CloseEnter = 0.20f;
	public const float NearEnter = 0.45f;
	public const float MidEnter = 0.70f;
	public const float FarEnter = 0.90f;
	public const float Hysteresis = 0.08f;
	#endregion

	#region Public Methods
	public static WeaponFireDisciplineProfileKind ResolveKind(WeaponDefinition _weapon)
	{
		if (_weapon == null)
			return WeaponFireDisciplineProfileKind.Assault;

		if (_weapon.WeaponClass == WeaponClassType.Shotgun)
			return WeaponFireDisciplineProfileKind.Shotgun;
		if (_weapon.WeaponClass == WeaponClassType.Pistol ||
		    _weapon.WeaponClass == WeaponClassType.SubmachineGun)
			return WeaponFireDisciplineProfileKind.Cqb;
		if (_weapon.WeaponClass == WeaponClassType.LightMachineGun)
			return WeaponFireDisciplineProfileKind.Lmg;
		if (_weapon.WeaponClass == WeaponClassType.HeavyMachineGun)
			return WeaponFireDisciplineProfileKind.Heavy;
		if (_weapon.WeaponClass == WeaponClassType.AutomaticGrenadeLauncher)
			return WeaponFireDisciplineProfileKind.Grenade;

		WeaponDistanceCurveLibrary.WeaponBalanceKind kind =
			WeaponDistanceCurveLibrary.ResolveKind(_weapon);
		return kind switch
		{
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbShort => WeaponFireDisciplineProfileKind.Cqb,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.CqbControlled => WeaponFireDisciplineProfileKind.Cqb,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.ShotgunCqb => WeaponFireDisciplineProfileKind.Shotgun,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Marksman => WeaponFireDisciplineProfileKind.Marksman,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Dmr => WeaponFireDisciplineProfileKind.Sniper,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support762 => WeaponFireDisciplineProfileKind.Lmg,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.Support545 => WeaponFireDisciplineProfileKind.Lmg,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.HeavySupport => WeaponFireDisciplineProfileKind.Heavy,
			WeaponDistanceCurveLibrary.WeaponBalanceKind.GrenadeSupport => WeaponFireDisciplineProfileKind.Grenade,
			_ => WeaponFireDisciplineProfileKind.Assault
		};
	}

	public static float GetWorkingRangeMeters(WeaponFireDisciplineProfileKind _kind)
	{
		return _kind switch
		{
			WeaponFireDisciplineProfileKind.Cqb => CqbWorkingRangeMeters,
			WeaponFireDisciplineProfileKind.Shotgun => ShotgunWorkingRangeMeters,
			WeaponFireDisciplineProfileKind.Lmg => LmgWorkingRangeMeters,
			WeaponFireDisciplineProfileKind.Marksman => MarksmanWorkingRangeMeters,
			WeaponFireDisciplineProfileKind.Sniper => SniperWorkingRangeMeters,
			WeaponFireDisciplineProfileKind.Heavy => HeavyWorkingRangeMeters,
			WeaponFireDisciplineProfileKind.Grenade => GrenadeWorkingRangeMeters,
			_ => AssaultWorkingRangeMeters
		};
	}

	public static float GetWorkingRangeMeters(WeaponDefinition _weapon)
	{
		return GetWorkingRangeMeters(ResolveKind(_weapon));
	}

	public static float NormalizeDistance(float _distanceMeters, float _workingRangeMeters)
	{
		float range = Mathf.Max(1f, _workingRangeMeters);
		return Mathf.Clamp01(Mathf.Max(0f, _distanceMeters) / range);
	}

	public static WeaponFireDisciplineDistanceBand ResolveBand(
		float _normalizedDistance01,
		WeaponFireDisciplineDistanceBand? _previousBand)
	{
		float n = Mathf.Clamp01(_normalizedDistance01);
		if (!_previousBand.HasValue)
			return ResolveBandForward(n);

		WeaponFireDisciplineDistanceBand current = _previousBand.Value;
		int currentIndex = (int)current;
		int forward = (int)ResolveBandForward(n);
		if (forward > currentIndex)
			return (WeaponFireDisciplineDistanceBand)forward;

		int backward = ResolveBandBackward(n);
		if (backward < currentIndex)
			return (WeaponFireDisciplineDistanceBand)backward;

		return current;
	}

	public static bool DoesNotForbidFire(int _seriesShotCount) => _seriesShotCount >= 1;
	#endregion

	#region Private Methods
	private static WeaponFireDisciplineDistanceBand ResolveBandForward(float _n)
	{
		if (_n < CloseEnter)
			return WeaponFireDisciplineDistanceBand.Close;
		if (_n < NearEnter)
			return WeaponFireDisciplineDistanceBand.Near;
		if (_n < MidEnter)
			return WeaponFireDisciplineDistanceBand.Mid;
		if (_n < FarEnter)
			return WeaponFireDisciplineDistanceBand.Far;
		return WeaponFireDisciplineDistanceBand.VeryFar;
	}

	private static int ResolveBandBackward(float _n)
	{
		if (_n < CloseEnter - Hysteresis)
			return (int)WeaponFireDisciplineDistanceBand.Close;
		if (_n < NearEnter - Hysteresis)
			return (int)WeaponFireDisciplineDistanceBand.Near;
		if (_n < MidEnter - Hysteresis)
			return (int)WeaponFireDisciplineDistanceBand.Mid;
		if (_n < FarEnter - Hysteresis)
			return (int)WeaponFireDisciplineDistanceBand.Far;
		return (int)WeaponFireDisciplineDistanceBand.VeryFar;
	}
	#endregion
}
