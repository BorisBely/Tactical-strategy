using System;

/// <summary>Lookup key for a weapon local pose or right-hand IK target.</summary>
[Serializable]
public struct WeaponPoseKey : IEquatable<WeaponPoseKey>
{
	public WeaponStance Stance;
	public WeaponPoseState PoseState;

	public WeaponPoseKey(WeaponStance _stance, WeaponPoseState _poseState)
	{
		Stance = _stance;
		PoseState = _poseState;
	}

	public bool Equals(WeaponPoseKey _other) =>
		Stance == _other.Stance && PoseState == _other.PoseState;

	public override bool Equals(object _obj) => _obj is WeaponPoseKey other && Equals(other);

	public override int GetHashCode() => ((int)Stance * 397) ^ (int)PoseState;

	public override string ToString() => $"{Stance}/{PoseState}";
}
