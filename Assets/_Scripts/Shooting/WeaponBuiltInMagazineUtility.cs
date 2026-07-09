using UnityEngine;

/// <summary>
/// Встроенный несъёмный магазин (дробовик, internal box).
/// </summary>
public static class WeaponBuiltInMagazineUtility
{
	public static void TryEnsureBuiltInMagazine(
		WeaponRuntimeState _weaponState,
		AmmoDefinition _ammo = null,
		int _rounds = -1,
		bool _chamberRound = true,
		bool _fillIfEmpty = true)
	{
		if (_weaponState == null)
			return;

		WeaponDefinition weapon = _weaponState.WeaponDefinition;
		if (weapon == null || !weapon.UsesShellByShellReload)
			return;

		MagazineDefinition magazineDefinition = weapon.BuiltInMagazineDefinition;
		if (magazineDefinition == null)
			return;

		AmmoDefinition ammo = _ammo ?? weapon.BuiltInMagazineDefaultAmmo;
		int roundCount = ResolveRounds(magazineDefinition, _rounds);

		if (!_weaponState.HasMagazine)
		{
			AmmoDefinition insertAmmo = _fillIfEmpty ? ammo : null;
			int insertRounds = _fillIfEmpty ? roundCount : 0;
			_weaponState.TryInsertBuiltInMagazine(magazineDefinition, insertAmmo, insertRounds);
		}
		else if (_fillIfEmpty && ammo != null)
		{
			MagazineRuntimeState magazineState = _weaponState.CurrentMagazine;
			if (magazineState != null && !magazineState.HasAmmo)
				magazineState.Configure(magazineDefinition, ammo, roundCount);
		}

		if (_chamberRound && _weaponState.HasAmmoInMagazine && !_weaponState.HasRoundInChamber)
			_weaponState.TryChamberRoundFromMagazine();
	}

	public static bool IsMagazineSlotLocked(WeaponDefinition _weapon)
	{
		return _weapon != null && _weapon.UsesShellByShellReload;
	}

	private static int ResolveRounds(MagazineDefinition _magazineDefinition, int _rounds)
	{
		if (_magazineDefinition == null)
			return 0;

		if (_rounds < 0)
			return _magazineDefinition.Capacity;

		return Mathf.Clamp(_rounds, 0, _magazineDefinition.Capacity);
	}
}
