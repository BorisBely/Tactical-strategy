using UnityEngine;

/// <summary>Comparable balance case identity for H absolute/M4-relative rows.</summary>
public readonly struct WeaponBalanceComparableKey
{
	public const float CanonicalDistanceMeters = 50f;
	public const string CanonicalLoadoutLabel = "Base";

	public readonly WeaponPoseState Pose;
	public readonly WeaponBalanceStance Stance;
	public readonly WeaponBalanceMovement Movement;
	public readonly string LoadoutLabel;
	public readonly float DistanceMeters;
	public readonly WeaponFireMode FireMode;

	public WeaponBalanceComparableKey(
		WeaponPoseState _pose,
		WeaponBalanceStance _stance,
		WeaponBalanceMovement _movement,
		string _loadoutLabel,
		float _distanceMeters,
		WeaponFireMode _fireMode)
	{
		Pose = _pose;
		Stance = _stance;
		Movement = _movement;
		LoadoutLabel = _loadoutLabel ?? CanonicalLoadoutLabel;
		DistanceMeters = _distanceMeters;
		FireMode = _fireMode;
	}

	public static WeaponBalanceComparableKey CreateCanonicalBaseline(WeaponFireMode _fireMode)
	{
		return new WeaponBalanceComparableKey(
			WeaponPoseState.Aiming,
			WeaponBalanceStance.Standing,
			WeaponBalanceMovement.Idle,
			CanonicalLoadoutLabel,
			CanonicalDistanceMeters,
			_fireMode);
	}

	public static WeaponFireMode ResolvePreferredFireMode(WeaponDefinition _weapon)
	{
		if (_weapon == null)
			return WeaponFireMode.FullAuto;
		WeaponFireMode[] modes = _weapon.AvailableFireModes;
		if (modes == null || modes.Length == 0)
			return WeaponFireMode.FullAuto;
		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] == WeaponFireMode.FullAuto)
				return WeaponFireMode.FullAuto;
		}

		for (int i = 0; i < modes.Length; i++)
		{
			if (modes[i] == WeaponFireMode.Auto)
				return WeaponFireMode.Auto;
		}

		return modes[0];
	}

	public bool Matches(in WeaponBalanceCase _case)
	{
		return _case.Pose == Pose &&
		       _case.Stance == Stance &&
		       _case.Movement == Movement &&
		       _case.LoadoutLabel == LoadoutLabel &&
		       Mathf.Approximately(_case.DistanceMeters, DistanceMeters) &&
		       _case.FireMode == FireMode;
	}
}
