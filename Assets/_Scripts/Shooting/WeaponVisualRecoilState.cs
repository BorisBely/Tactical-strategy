using UnityEngine;

/// <summary>
/// Single visual-recoil runtime snapshot for one frame.
/// Punch and climb stay separate scalars; the applicator combines them only when building the Hand_R offset.
/// </summary>
public struct WeaponVisualRecoilState
{
	public float punchPitch;
	public float punchYaw;
	public float climbPitch;
	public float backOffset;
	public float upOffset;
	public bool isActive;
}
