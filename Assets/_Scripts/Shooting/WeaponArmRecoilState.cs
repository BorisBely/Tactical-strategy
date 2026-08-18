using UnityEngine;

public enum ArmRecoilQuality
{
	Off = 0,
	Light = 1,
	Full = 2
}

/// <summary>
/// One-frame snapshot of the arm-recoil overlay. Does not store bone rotations.
/// </summary>
public struct WeaponArmRecoilState
{
	public float impulse;
	public Vector3 recoilDirectionWorld;
	public float shoulderAmount;
	public float upperArmAmount;
	public float elbowAmount;
	public bool isActive;
}
