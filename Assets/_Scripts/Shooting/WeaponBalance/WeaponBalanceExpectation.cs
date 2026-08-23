using UnityEngine;

/// <summary>Class-based expected bands for balance scoring (Phase G8).</summary>
public static class WeaponBalanceExpectation
{
	#region Nested Types
	public struct OffsetBand
	{
		public float MinMagAfter5;
		public float MaxMagAfter5;
		public bool RequireNetDrift;
	}
	#endregion

	#region Public Methods
	public static OffsetBand ResolveOffsetBand(WeaponClassType _class, string _weaponName)
	{
		if (IsM4Like(_weaponName))
			return new OffsetBand { MinMagAfter5 = 0.25f, MaxMagAfter5 = 0.40f, RequireNetDrift = true };
		if (IsAkLike(_weaponName))
			return new OffsetBand { MinMagAfter5 = 0.55f, MaxMagAfter5 = 0.90f, RequireNetDrift = true };

		switch (_class)
		{
			case WeaponClassType.LightMachineGun:
			case WeaponClassType.HeavyMachineGun:
				return new OffsetBand { MinMagAfter5 = 0.20f, MaxMagAfter5 = 0.50f, RequireNetDrift = true };
			case WeaponClassType.SniperRifle:
				return new OffsetBand { MinMagAfter5 = 0f, MaxMagAfter5 = 0.20f, RequireNetDrift = false };
			case WeaponClassType.Shotgun:
				return new OffsetBand { MinMagAfter5 = 0.20f, MaxMagAfter5 = 0.70f, RequireNetDrift = true };
			case WeaponClassType.AutomaticGrenadeLauncher:
				return new OffsetBand { MinMagAfter5 = 0.80f, MaxMagAfter5 = 2.50f, RequireNetDrift = true };
			default:
				return new OffsetBand { MinMagAfter5 = 0.25f, MaxMagAfter5 = 0.90f, RequireNetDrift = true };
		}
	}

	public static WeaponBalanceBandLevel ClassifyOffset(float _magAfter5, in OffsetBand _band)
	{
		if (_magAfter5 < _band.MinMagAfter5 * 0.85f)
			return WeaponBalanceBandLevel.Low;
		if (_magAfter5 > _band.MaxMagAfter5 * 1.15f)
			return WeaponBalanceBandLevel.High;
		return WeaponBalanceBandLevel.Medium;
	}

	public static WeaponBalanceBandLevel ClassifyHorizontal(float _absXOverY)
	{
		if (_absXOverY < 0.08f)
			return WeaponBalanceBandLevel.Low;
		if (_absXOverY > 0.35f)
			return WeaponBalanceBandLevel.High;
		return WeaponBalanceBandLevel.Medium;
	}

	public static WeaponBalanceBandLevel ClassifyRecovery(float _pause04, float _magAfter5)
	{
		if (_magAfter5 > 0.05f && _pause04 < 0.02f)
			return WeaponBalanceBandLevel.High;
		if (_pause04 > _magAfter5 * 0.5f)
			return WeaponBalanceBandLevel.Low;
		return WeaponBalanceBandLevel.Medium;
	}
	#endregion

	#region Private Methods
	private static bool IsM4Like(string _weaponName)
	{
		return _weaponName != null &&
		       (_weaponName.Contains("M4") || _weaponName.Contains("M16") || _weaponName.Contains("MK18"));
	}

	private static bool IsAkLike(string _weaponName)
	{
		return _weaponName != null &&
		       (_weaponName.Contains("AK") || _weaponName.Contains("RPK"));
	}
	#endregion
}
