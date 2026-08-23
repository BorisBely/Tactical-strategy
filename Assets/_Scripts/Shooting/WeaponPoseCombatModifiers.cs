/// <summary>
/// Единая тройка позы оружия: θ / kick / recovery.
/// Стойка лёжа сюда не входит — locomotion Prone отключён.
/// </summary>
public static class WeaponPoseCombatModifiers
{
	#region Constants
	public const float AimingSpreadMultiplier = 1f;
	public const float AimingKickMultiplier = 1f;
	public const float AimingRecoveryMultiplier = 1f;

	public const float PointAimSpreadMultiplier = 1.5f;
	public const float PointAimKickMultiplier = 1.1f;
	public const float PointAimRecoveryMultiplier = 0.9f;

	public const float PreAimSpreadMultiplier = 1.75f;
	public const float PreAimKickMultiplier = 1.15f;
	public const float PreAimRecoveryMultiplier = 0.85f;

	public const float HipFireSpreadMultiplier = 2.5f;
	public const float HipFireKickMultiplier = 1.35f;
	public const float HipFireRecoveryMultiplier = 0.7f;
	#endregion

	#region Public Methods
	public static float GetSpreadMultiplier(WeaponPoseState _pose)
	{
		if (_pose.IsHipFireHold())
			return HipFireSpreadMultiplier;
		if (_pose == WeaponPoseState.PointAim)
			return PointAimSpreadMultiplier;
		if (_pose == WeaponPoseState.PreAim)
			return PreAimSpreadMultiplier;
		return AimingSpreadMultiplier;
	}

	public static float GetKickMultiplier(WeaponPoseState _pose)
	{
		if (_pose.IsHipFireHold())
			return HipFireKickMultiplier;
		if (_pose == WeaponPoseState.PointAim)
			return PointAimKickMultiplier;
		if (_pose == WeaponPoseState.PreAim)
			return PreAimKickMultiplier;
		return AimingKickMultiplier;
	}

	public static float GetRecoveryMultiplier(WeaponPoseState _pose)
	{
		if (_pose.IsHipFireHold())
			return HipFireRecoveryMultiplier;
		if (_pose == WeaponPoseState.PointAim)
			return PointAimRecoveryMultiplier;
		if (_pose == WeaponPoseState.PreAim)
			return PreAimRecoveryMultiplier;
		return AimingRecoveryMultiplier;
	}
	#endregion
}
