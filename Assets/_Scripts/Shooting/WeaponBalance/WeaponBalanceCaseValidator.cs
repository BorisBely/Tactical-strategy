using UnityEngine;

/// <summary>Filters impossible balance cases before simulation.</summary>
public static class WeaponBalanceCaseValidator
{
	#region Public Methods
	public static bool IsValid(in WeaponBalanceCase _case, WeaponBalanceRunConfig _config)
	{
		if (_case.Weapon == null)
			return false;
		if (!_case.Pose.CanShootFromPose())
			return false;
		if (_config != null && _config.SkipInvalidPoseMove && !IsMovementValid(in _case, _config))
			return false;
		if (_case.IsTurret && IsTurretForbidden(in _case))
			return false;
		return true;
	}

	public static bool IsTurretWeapon(WeaponDefinition _weapon)
	{
		if (_weapon == null)
			return false;
		return _weapon.WeaponClass == WeaponClassType.HeavyMachineGun ||
		       _weapon.WeaponClass == WeaponClassType.AutomaticGrenadeLauncher;
	}

	public static bool IsMovementValid(in WeaponBalanceCase _case, WeaponBalanceRunConfig _config)
	{
		if (_case.IsTurret && _case.Movement != WeaponBalanceMovement.Idle)
			return false;
		if (_case.IsTurret && _case.Pose.IsHipFireHold())
			return false;
		if (_case.Movement == WeaponBalanceMovement.Sprint &&
		    _case.Pose == WeaponPoseState.Aiming &&
		    (_config == null || !_config.AllowSprintWhileAiming))
			return false;
		if (_case.Pose.IsHipFireHold() && _case.Movement == WeaponBalanceMovement.Sprint)
			return false;
		return true;
	}

	private static bool IsTurretForbidden(in WeaponBalanceCase _case)
	{
		if (_case.Movement != WeaponBalanceMovement.Idle)
			return true;
		if (_case.Pose.IsHipFireHold())
			return true;
		if (_case.Stance != WeaponBalanceStance.Standing)
			return true;
		return false;
	}
	#endregion
}
