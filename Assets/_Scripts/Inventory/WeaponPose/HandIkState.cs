using UnityEngine;

/// <summary>Authored right-hand IK empties for one stance.</summary>
[System.Serializable]
public struct HandIkPoseSet
{
	public Transform HoldNotReady;
	public Transform HoldNotReadyPatrol;
	public Transform LowReady;
	public Transform HipFire;
	public Transform HipFireWalk;
	public Transform HipFireCrouchWalk;
	public Transform PointAim;
	public Transform Aiming;
	public Transform HighReady;

	public Transform Pick(WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.NotReady:
				return HoldNotReady != null ? HoldNotReady : LowReady;
			case WeaponPoseState.NotReadyPatrol:
				return HoldNotReadyPatrol != null
					? HoldNotReadyPatrol
					: (HoldNotReady != null ? HoldNotReady : LowReady);
			case WeaponPoseState.HipFire:
				return HipFire != null ? HipFire : (PointAim != null ? PointAim : LowReady);
			case WeaponPoseState.HipFireWalk:
				return HipFireWalk != null
					? HipFireWalk
					: (HipFire != null ? HipFire : (PointAim != null ? PointAim : LowReady));
			case WeaponPoseState.HipFireCrouchWalk:
				return HipFireCrouchWalk != null
					? HipFireCrouchWalk
					: (HipFire != null ? HipFire : (HipFireWalk != null ? HipFireWalk : (PointAim != null ? PointAim : LowReady)));
			case WeaponPoseState.Aiming:
				return Aiming != null ? Aiming : PointAim;
			case WeaponPoseState.PointAim:
				return PointAim != null ? PointAim : Aiming;
			case WeaponPoseState.PreAim:
				return null;
			case WeaponPoseState.HighReady:
				return HighReady != null ? HighReady : (Aiming != null ? Aiming : PointAim);
			default:
				return LowReady;
		}
	}
}

/// <summary>Equip-time cache — direct Transform refs, no Find in hot path.</summary>
public struct CachedRightHandTargets
{
	public HandIkPoseSet Standing;
	public HandIkPoseSet Crouch;
	public HandIkPoseSet Vehicle;

	public Transform Pick(WeaponStance _stance, WeaponPoseState _pose)
	{
		HandIkPoseSet set = PickSet(_stance);
		Transform t = set.Pick(_pose);
		if (t != null)
			return t;
		if (_stance != WeaponStance.Standing)
			return Standing.Pick(_pose);
		return null;
	}

	/// <summary>Legacy bool API: ready → PointAim, not ready → LowReady.</summary>
	public Transform Pick(WeaponStance _stance, bool _ready)
	{
		return Pick(_stance, _ready ? WeaponPoseState.PointAim : WeaponPoseState.LowReady);
	}

	public HandIkPoseSet PickSet(WeaponStance _stance)
	{
		switch (_stance)
		{
			case WeaponStance.Crouching:
				return Crouch;
			case WeaponStance.Vehicle:
				return Vehicle;
			default:
				return Standing;
		}
	}

	public bool HasAny =>
		Standing.LowReady != null || Standing.PointAim != null || Standing.HipFire != null || Standing.Aiming != null
		|| Standing.HoldNotReady != null || Standing.HoldNotReadyPatrol != null || Standing.HighReady != null;
}

/// <summary>Resolved IK targets + weights for one frame (cached between state changes).</summary>
public struct HandIkState
{
	public Transform RightTarget;
	public Transform LeftTarget;
	public float RightWeight;
	public float LeftWeight;

	public bool HasRight => RightTarget != null && RightTarget.gameObject.activeInHierarchy;
	public bool HasLeft => LeftTarget != null && LeftTarget.gameObject.activeInHierarchy;
}
