using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>One authored weapon local pose under Hand_R (position + euler).</summary>
[Serializable]
public sealed class WeaponPoseEntry
{
	public WeaponStance Stance = WeaponStance.Standing;

	[FormerlySerializedAs("ReadyState")]
	public WeaponPoseState PoseState = WeaponPoseState.LowReady;

	public Vector3 Position;
	public Vector3 EulerAngles;

	public Quaternion Rotation => Quaternion.Euler(EulerAngles);

	public WeaponPoseKey Key => new WeaponPoseKey(Stance, PoseState);
}
