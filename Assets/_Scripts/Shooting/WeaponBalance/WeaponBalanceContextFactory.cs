using UnityEngine;

/// <summary>Builds WeaponRecoilContext and accuracy input from a balance case.</summary>
public static class WeaponBalanceContextFactory
{
	#region Public Methods
	public static WeaponRecoilContext CreateRecoilContext(in WeaponBalanceCase _case)
	{
		WeaponRecoilContext context = WeaponRecoilContext.CreateFromAttachments(
			_case.Weapon,
			_case.Attachments,
			_case.FireMode);
		context.InstanceHash = 0;
		context.AmmoDefinition = _case.Ammo;

		if (_case.IsTurret)
		{
			context.StanceKickMultiplier = 1f;
			context.StanceRecoveryMultiplier = 1f;
			context.PoseKickMultiplier = 1f;
			context.PoseRecoveryMultiplier = 1f;
			return context;
		}

		ResolveStanceMultipliers(_case.Stance, _case.Movement, out float stanceKick, out float stanceRecovery);
		context.StanceKickMultiplier = stanceKick;
		context.StanceRecoveryMultiplier = stanceRecovery;
		context.PoseKickMultiplier = WeaponPoseCombatModifiers.GetKickMultiplier(_case.Pose);
		context.PoseRecoveryMultiplier = WeaponPoseCombatModifiers.GetRecoveryMultiplier(_case.Pose);
		RecoilPlaySkillUtility.ApplyRecoilControlToContext(ref context, _case.RecoilControlSkill);
		return context;
	}

	public static WeaponShotAccuracyInput CreateAccuracyInput(in WeaponBalanceCase _case)
	{
		bool isMoving = _case.Movement != WeaponBalanceMovement.Idle;
		LocomotionStance locomotion = _case.Stance == WeaponBalanceStance.Crouch
			? LocomotionStance.Crouch
			: LocomotionStance.Standing;
		return RecoilPlayShotAccuracyUtility.BuildAccuracyInput(
			_case.Weapon,
			_case.Ammo,
			_case.DistanceMeters,
			_case.Pose,
			_case.FireMode,
			_case.FireMode,
			WeaponAimMode.FullAim,
			WeaponAimMode.FullAim,
			isMoving,
			locomotion);
	}

	public static RecoilAutoSelectorInputBuilder.Scenario CreateSelectorScenario(in WeaponBalanceCase _case)
	{
		ResolveStanceMultipliers(_case.Stance, _case.Movement, out float stanceKick, out float stanceRecovery);
		return new RecoilAutoSelectorInputBuilder.Scenario
		{
			Weapon = _case.Weapon,
			Ammo = _case.Ammo,
			DistanceMeters = _case.DistanceMeters,
			Pose = _case.Pose,
			StanceKickMultiplier = stanceKick,
			StanceRecoveryMultiplier = stanceRecovery,
			RecoilControlSkill = _case.RecoilControlSkill,
			IsMoving = _case.Movement == WeaponBalanceMovement.Walk ||
			           _case.Movement == WeaponBalanceMovement.Sprint,
			Stance = _case.Stance == WeaponBalanceStance.Crouch
				? LocomotionStance.Crouch
				: LocomotionStance.Standing
		};
	}
	#endregion

	#region Private Methods
	private static void ResolveStanceMultipliers(
		WeaponBalanceStance _stance,
		WeaponBalanceMovement _movement,
		out float _kick,
		out float _recovery)
	{
		switch (_movement)
		{
			case WeaponBalanceMovement.Walk:
				_kick = RecoilPlayBaselineProtocol.WalkKickMultiplier;
				_recovery = RecoilPlayBaselineProtocol.WalkRecoveryMultiplier;
				return;
			case WeaponBalanceMovement.Sprint:
				_kick = 1.6f;
				_recovery = 0.5f;
				return;
		}

		if (_stance == WeaponBalanceStance.Crouch)
		{
			_kick = RecoilPlayBaselineProtocol.CrouchKickMultiplier;
			_recovery = RecoilPlayBaselineProtocol.CrouchRecoveryMultiplier;
			return;
		}

		_kick = RecoilPlayBaselineProtocol.StandingKickMultiplier;
		_recovery = RecoilPlayBaselineProtocol.StandingRecoveryMultiplier;
	}
	#endregion
}
