/// <summary>
/// Runtime hand-IK mode. Separate from <see cref="HandIkState"/> (cached targets + authored weights).
/// </summary>
public enum HandIkMode
{
	Disabled = 0,
	Frozen = 1,
	Reload = 2,
	BoltHold = 3,
	Transition = 4,
	SoftHold = 5,
	Hold = 6
}

/// <summary>Why IK is on or off this frame. Weight is how much; intent is why.</summary>
public enum HandIkIntent
{
	FullAnimation = 0,
	WeaponManipulation = 1,
	MovementRelaxation = 2,
	WeaponHold = 3
}

/// <summary>One-frame pose×stance snapshot so weapon pose and IK targets switch together.</summary>
public struct WeaponHoldContext
{
	public WeaponStance StanceFrom;
	public WeaponStance StanceTo;
	public float StanceBlend01;
	public WeaponPoseState PoseFrom;
	public WeaponPoseState PoseTo;
	public float PoseBlend01;
	public bool IsPoseBlending;
	public bool IsStanceBlending;

	public WeaponStance EffectiveStance =>
		StanceBlend01 >= 0.999f || StanceFrom == StanceTo ? StanceTo : StanceFrom;

	public bool IsBlending => IsPoseBlending || IsStanceBlending;
}

/// <summary>Debug-only grip solve check. Never rotates the weapon.</summary>
public struct GripValidity
{
	public bool IsReachable;
	public float DistanceError;
	public float AngleError;
	public bool IsStable;
	public bool LeftOutOfReach;
	public bool TargetJump;
	public bool WeightJump;
}
