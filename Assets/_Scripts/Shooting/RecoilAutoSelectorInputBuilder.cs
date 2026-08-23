using UnityEngine;

/// <summary>
/// Builds <see cref="WeaponAutoModeSelectionInput"/> for editor N9 tests without a unit.
/// </summary>
public static class RecoilAutoSelectorInputBuilder
{
	#region Nested Types
	public struct Scenario
	{
		public WeaponDefinition Weapon;
		public AmmoDefinition Ammo;
		public float DistanceMeters;
		public WeaponPoseState Pose;
		public float StanceKickMultiplier;
		public float StanceRecoveryMultiplier;
		public float RecoilControlSkill;
		public bool IsMoving;
		public LocomotionStance Stance;
	}
	#endregion

	#region Public Methods
	public static WeaponAutoModeSelectionInput BuildContract(in Scenario _scenario)
	{
		WeaponDefinition weapon = _scenario.Weapon;
		float poseKick = WeaponPoseCombatModifiers.GetKickMultiplier(_scenario.Pose);
		float poseRecovery = WeaponPoseCombatModifiers.GetRecoveryMultiplier(_scenario.Pose);
		float skillKick = RecoilPlaySkillUtility.GetRecoilControlKickMultiplier(_scenario.RecoilControlSkill);
		float skillRecovery = RecoilPlaySkillUtility.GetRecoilControlRecoveryMultiplier(_scenario.RecoilControlSkill);
		float stanceKick = _scenario.IsMoving
			? RecoilPlayBaselineProtocol.WalkKickMultiplier
			: _scenario.StanceKickMultiplier;
		float stanceRecovery = _scenario.IsMoving
			? RecoilPlayBaselineProtocol.WalkRecoveryMultiplier
			: _scenario.StanceRecoveryMultiplier;
		var accuracyInput = RecoilPlayShotAccuracyUtility.BuildAccuracyInput(
			weapon,
			_scenario.Ammo,
			_scenario.DistanceMeters,
			_scenario.Pose,
			WeaponFireMode.Auto,
			WeaponFireMode.FullAuto,
			WeaponAimMode.Auto,
			WeaponAimMode.FullAim,
			_scenario.IsMoving,
			_scenario.Stance);

		return new WeaponAutoModeSelectionInput
		{
			AccuracyInput = accuracyInput,
			SelectedFireMode = WeaponFireMode.Auto,
			SelectedAimMode = WeaponAimMode.Auto,
			AvailableFireModes = weapon != null ? weapon.AvailableFireModes : null,
			TargetDistanceMeters = _scenario.DistanceMeters,
			StanceKickMultiplier = stanceKick,
			StanceRecoveryMultiplier = stanceRecovery,
			PoseKickMultiplier = poseKick,
			PoseRecoveryMultiplier = poseRecovery,
			SkillKickMultiplier = skillKick,
			SkillRecoveryMultiplier = skillRecovery
		};
	}

	/// <summary>Runtime-mirror path: structured like hitscan BuildAccuracyInput + stance resolution.</summary>
	public static WeaponAutoModeSelectionInput BuildRuntimeMirror(in Scenario _scenario)
	{
		WeaponDefinition weapon = _scenario.Weapon;
		float stanceKick = _scenario.IsMoving
			? RecoilPlayBaselineProtocol.WalkKickMultiplier
			: _scenario.StanceKickMultiplier;
		float stanceRecovery = _scenario.IsMoving
			? RecoilPlayBaselineProtocol.WalkRecoveryMultiplier
			: _scenario.StanceRecoveryMultiplier;

		float poseSpread = RecoilPlayShotAccuracyUtility.ResolvePoseSpreadMultiplier(
			_scenario.Pose,
			_scenario.DistanceMeters);
		float poseKick = WeaponPoseCombatModifiers.GetKickMultiplier(_scenario.Pose);
		float poseRecovery = WeaponPoseCombatModifiers.GetRecoveryMultiplier(_scenario.Pose);
		float skillKick = RecoilPlaySkillUtility.GetRecoilControlKickMultiplier(_scenario.RecoilControlSkill);
		float skillRecovery = RecoilPlaySkillUtility.GetRecoilControlRecoveryMultiplier(_scenario.RecoilControlSkill);

		var accuracyInput = new WeaponShotAccuracyInput
		{
			WeaponDefinition = weapon,
			AmmoDefinition = _scenario.Ammo,
			TargetDistanceMeters = _scenario.DistanceMeters,
			BaseSpreadToDegrees = RecoilPlayBaselineProtocol.HitscanBaseSpreadToDegrees,
			MinHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMinHalfAngleDegrees,
			MaxHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMaxHalfAngleDegrees,
			Stance = _scenario.Stance,
			IsMoving = _scenario.IsMoving,
			StandingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanStandingSpreadMultiplier,
			CrouchSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanCrouchSpreadMultiplier,
			MovingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanMovingSpreadMultiplier,
			AimProgress01 = 1f,
			SelectedAimMode = WeaponAimMode.Auto,
			AimMode = WeaponAimMode.FullAim,
			SelectedFireMode = WeaponFireMode.Auto,
			FireMode = WeaponFireMode.FullAuto,
			BurstShotIndex = 1,
			WeaponPose = _scenario.Pose,
			PoseSpreadMultiplier = poseSpread,
			ExcludeOpticAttachments = _scenario.Pose.IsHipFireHold()
			                          || _scenario.Pose == WeaponPoseState.PointAim
			                          || _scenario.Pose == WeaponPoseState.PreAim
		};

		return new WeaponAutoModeSelectionInput
		{
			AccuracyInput = accuracyInput,
			SelectedFireMode = WeaponFireMode.Auto,
			SelectedAimMode = WeaponAimMode.Auto,
			AvailableFireModes = weapon != null ? weapon.AvailableFireModes : null,
			TargetDistanceMeters = _scenario.DistanceMeters,
			StanceKickMultiplier = stanceKick,
			StanceRecoveryMultiplier = stanceRecovery,
			PoseKickMultiplier = poseKick,
			PoseRecoveryMultiplier = poseRecovery,
			SkillKickMultiplier = skillKick,
			SkillRecoveryMultiplier = skillRecovery
		};
	}

	public static WeaponRecoilContext BuildRecoilContext(
		in Scenario _scenario,
		WeaponFireMode _fireMode)
	{
		WeaponRecoilContext context = RecoilPlayBaselineProtocol.CreateContext(
			_scenario.Weapon,
			_fireMode,
			_scenario.Pose,
			_scenario.IsMoving
				? RecoilPlayBaselineProtocol.WalkKickMultiplier
				: _scenario.StanceKickMultiplier,
			_scenario.IsMoving
				? RecoilPlayBaselineProtocol.WalkRecoveryMultiplier
				: _scenario.StanceRecoveryMultiplier);
		RecoilPlaySkillUtility.ApplyRecoilControlToContext(ref context, _scenario.RecoilControlSkill);
		return context;
	}

	public static Scenario CreateBaselineScenario(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		float _distanceMeters,
		float _recoilControlSkill = 50f)
	{
		return new Scenario
		{
			Weapon = _weapon,
			Ammo = _ammo,
			DistanceMeters = _distanceMeters,
			Pose = WeaponPoseState.Aiming,
			StanceKickMultiplier = RecoilPlayBaselineProtocol.StandingKickMultiplier,
			StanceRecoveryMultiplier = RecoilPlayBaselineProtocol.StandingRecoveryMultiplier,
			RecoilControlSkill = _recoilControlSkill,
			IsMoving = false,
			Stance = LocomotionStance.Standing
		};
	}
	#endregion
}
