using UnityEngine;

/// <summary>
/// Editor accuracy input builder mirroring hitscan pose spread + distance curve (N9/N14).
/// </summary>
public static class RecoilPlayShotAccuracyUtility
{
	#region Public Methods
	public static float ResolvePoseSpreadMultiplier(
		WeaponPoseState _pose,
		float _distanceMeters,
		WeaponAttachmentDefinition[] _attachments = null)
	{
		float poseSpread = ResolveBasePoseSpreadMultiplier(_pose);
		poseSpread *= WeaponPoseDistanceCurves.GetAccuracyMultiplier(_pose, _distanceMeters);
		if (_pose == WeaponPoseState.PointAim)
			poseSpread *= WeaponLaserModifiers.GetPointAimSpreadProduct(_attachments, _distanceMeters);
		return poseSpread;
	}

	public static WeaponShotAccuracyInput BuildAccuracyInput(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		float _distanceMeters,
		WeaponPoseState _pose,
		WeaponFireMode _selectedFireMode,
		WeaponFireMode _effectiveFireMode,
		WeaponAimMode _selectedAimMode,
		WeaponAimMode _effectiveAimMode,
		bool _isMoving = false,
		LocomotionStance _stance = LocomotionStance.Standing,
		int _burstShotIndex = 1)
	{
		return new WeaponShotAccuracyInput
		{
			WeaponDefinition = _weapon,
			WeaponState = null,
			AmmoDefinition = _ammo,
			CombatStats = null,
			IndividualTraits = null,
			CombatCondition = null,
			TargetDistanceMeters = _distanceMeters,
			BaseSpreadToDegrees = RecoilPlayBaselineProtocol.HitscanBaseSpreadToDegrees,
			MinHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMinHalfAngleDegrees,
			MaxHalfAngleDegrees = RecoilPlayBaselineProtocol.HitscanMaxHalfAngleDegrees,
			Stance = _stance,
			IsMoving = _isMoving,
			IsSprinting = false,
			StandingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanStandingSpreadMultiplier,
			CrouchSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanCrouchSpreadMultiplier,
			MovingSpreadMultiplier = RecoilPlayBaselineProtocol.HitscanMovingSpreadMultiplier,
			AimProgress01 = 1f,
			SelectedAimMode = _selectedAimMode,
			AimMode = _effectiveAimMode,
			SelectedFireMode = _selectedFireMode,
			FireMode = _effectiveFireMode,
			BurstShotIndex = _burstShotIndex,
			WeaponPose = _pose,
			PoseSpreadMultiplier = ResolvePoseSpreadMultiplier(_pose, _distanceMeters),
			ExcludeOpticAttachments = _pose.IsHipFireHold()
			                          || _pose == WeaponPoseState.PointAim
			                          || _pose == WeaponPoseState.PreAim
		};
	}

	public static float EvaluateHalfAngleDegrees(WeaponShotAccuracyInput _input)
	{
		return WeaponShotAccuracyEvaluator.Evaluate(_input).HalfAngleDegrees;
	}
	#endregion

	#region Private Methods
	private static float ResolveBasePoseSpreadMultiplier(WeaponPoseState _pose)
	{
		if (_pose.IsHipFireHold())
			return WeaponPoseAutoCapabilityBaker.DefaultHipFireSpreadMult;
		if (_pose == WeaponPoseState.PointAim)
			return WeaponPoseAutoCapabilityBaker.DefaultPointAimSpreadMult;
		if (_pose == WeaponPoseState.PreAim)
			return PreAimPoseUtility.SpreadMult;
		return WeaponPoseCombatModifiers.GetSpreadMultiplier(_pose);
	}
	#endregion
}
