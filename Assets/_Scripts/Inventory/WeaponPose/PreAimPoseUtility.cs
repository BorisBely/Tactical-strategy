using UnityEngine;

/// <summary>
/// PreAim (former HighReady) is a calculated pose: lerp LowReady → Aiming.
/// Not authored, not tuner-editable. Combat multipliers stay as they were.
/// </summary>
public static class PreAimPoseUtility
{
	public const float WeaponBlend = 0.75f;
	public const float RightHandBlend = WeaponBlend;

	public const float AimTimeMult = 0.70f;
	public const float SpreadMult = 1.75f;

	public const float PreAimFireThreshold01 = 0.45f;
	public const float HipFireFireThreshold01 = 0.35f;
	public const float PointAimFireThreshold01 = 0.65f;
	public const float AimingFireThreshold01 = 1f;

	public static void BlendLocal(
		Vector3 _lowPos,
		Quaternion _lowRot,
		Vector3 _aimPos,
		Quaternion _aimRot,
		float _blend,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		float t = Mathf.Clamp01(_blend);
		_position = Vector3.Lerp(_lowPos, _aimPos, t);
		_rotation = Quaternion.Slerp(_lowRot, _aimRot, t);
	}

	public static float GetPoseFireThreshold01(WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
				return HipFireFireThreshold01;
			case WeaponPoseState.PreAim:
				return 2f;
			case WeaponPoseState.PointAim:
				return PointAimFireThreshold01;
			case WeaponPoseState.Aiming:
				return AimingFireThreshold01;
			default:
				return 1f;
		}
	}
}
