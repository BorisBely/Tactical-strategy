using UnityEngine;

/// <summary>
/// Geometric occlusion of body segments. 0 = exposed, 1 = blocked. Not a damage modifier. #13.2 prototype.
/// </summary>
public struct CoverProtectionProfile
{
	public float Head;
	public float Torso;
	public float Pelvis;
	public float Legs;

	public float Average => (Head + Torso + Pelvis + Legs) * 0.25f;

	public bool AnyProtected(float _threshold)
	{
		return Head >= _threshold || Torso >= _threshold || Pelvis >= _threshold || Legs >= _threshold;
	}
}
