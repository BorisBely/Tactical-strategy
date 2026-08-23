using UnityEngine;

/// <summary>
/// Единый вход для kick / recovery / prediction. Pause A: StopFiring не сбрасывает Offset.
/// </summary>
public struct WeaponRecoilContext
{
	public WeaponDefinition WeaponDefinition;
	public WeaponFireMode FireMode;
	public AmmoDefinition AmmoDefinition;
	public float AttachmentKickProduct;
	public float AttachmentVerticalProduct;
	public float AttachmentHorizontalProduct;
	public float AttachmentRecoveryProduct;
	public float SkillKickMultiplier;
	public float TraitsKickMultiplier;
	public float ConditionKickMultiplier;
	public float StanceKickMultiplier;
	public float PoseKickMultiplier;
	public float SkillRecoveryMultiplier;
	public float TraitsRecoveryMultiplier;
	public float ConditionRecoveryMultiplier;
	public float StanceRecoveryMultiplier;
	public float PoseRecoveryMultiplier;
	public float RecoveryWhileFiringMultiplier;
	public float RecoveryWhenNotReadyMultiplier;
	public float MaxOffsetDegrees;
	public int InstanceHash;

	public static WeaponRecoilContext CreateBaseline(
		WeaponDefinition _weaponDefinition,
		WeaponFireMode _fireMode)
	{
		return new WeaponRecoilContext
		{
			WeaponDefinition = _weaponDefinition,
			FireMode = _fireMode,
			AmmoDefinition = null,
			AttachmentKickProduct = 1f,
			AttachmentVerticalProduct = 1f,
			AttachmentHorizontalProduct = 1f,
			AttachmentRecoveryProduct = 1f,
			SkillKickMultiplier = 1f,
			TraitsKickMultiplier = 1f,
			ConditionKickMultiplier = 1f,
			StanceKickMultiplier = 1f,
			PoseKickMultiplier = 1f,
			SkillRecoveryMultiplier = 1f,
			TraitsRecoveryMultiplier = 1f,
			ConditionRecoveryMultiplier = 1f,
			StanceRecoveryMultiplier = 1f,
			PoseRecoveryMultiplier = 1f,
			RecoveryWhileFiringMultiplier = WeaponRecoilMath.RecoveryWhileFiringForPrediction,
			RecoveryWhenNotReadyMultiplier = 1.2f,
			MaxOffsetDegrees = WeaponRecoilMath.DefaultMaxOffsetDegrees,
			InstanceHash = 0
		};
	}

	public static WeaponRecoilContext CreateFromAttachments(
		WeaponDefinition _weaponDefinition,
		WeaponAttachmentDefinition[] _attachments,
		WeaponFireMode _fireMode)
	{
		WeaponRecoilContext context = CreateBaseline(_weaponDefinition, _fireMode);
		context.AttachmentKickProduct =
			WeaponDistanceAimEvaluator.GetAttachmentRecoilProduct(_attachments, _fireMode);
		context.AttachmentVerticalProduct =
			WeaponDistanceAimEvaluator.GetAttachmentRecoilVerticalProduct(_attachments);
		context.AttachmentHorizontalProduct =
			WeaponDistanceAimEvaluator.GetAttachmentRecoilHorizontalProduct(_attachments);
		context.AttachmentRecoveryProduct =
			WeaponDistanceAimEvaluator.GetAttachmentRecoilRecoveryProduct(_attachments);
		return context;
	}
}
