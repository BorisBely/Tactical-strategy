using System.Collections.Generic;

/// <summary>Phase H row lookup helpers.</summary>
public static class WeaponBalanceRowMatcher
{
	#region Public Methods
	public static bool TryFindRow(
		IReadOnlyList<WeaponBalanceRow> _rows,
		string _weaponName,
		in WeaponBalanceComparableKey _key,
		out WeaponBalanceRow _row)
	{
		_row = default;
		if (_rows == null || string.IsNullOrEmpty(_weaponName))
			return false;

		for (int i = 0; i < _rows.Count; i++)
		{
			WeaponBalanceRow candidate = _rows[i];
			if (candidate.Case.Weapon == null || candidate.Case.Weapon.name != _weaponName)
				continue;
			if (_key.Matches(in candidate.Case))
			{
				_row = candidate;
				return true;
			}
		}

		return false;
	}

	public static WeaponBalanceRow FindBaselineRow(
		IReadOnlyList<WeaponBalanceRow> _rows,
		WeaponDefinition _weapon)
	{
		if (_weapon == null || _rows == null)
			return default;

		WeaponFireMode mode = WeaponBalanceComparableKey.ResolvePreferredFireMode(_weapon);
		var key = WeaponBalanceComparableKey.CreateCanonicalBaseline(mode);
		if (TryFindRow(_rows, _weapon.name, in key, out WeaponBalanceRow row))
			return row;

		var semiKey = WeaponBalanceComparableKey.CreateCanonicalBaseline(WeaponFireMode.SemiAuto);
		TryFindRow(_rows, _weapon.name, in semiKey, out row);
		return row;
	}
	#endregion
}
