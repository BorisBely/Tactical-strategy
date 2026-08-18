/// <summary>Visual / IK weapon hold pose (source of truth for weapon transform + right-hand IK).</summary>
public enum WeaponPoseState
{
	/// <summary>Combat barrel-down. Was NotReady in the first pose split.</summary>
	LowReady = 0,
	/// <summary>Was Ready — raised without optic ADS. Value 1 keeps existing pose assets valid.</summary>
	PointAim = 1,
	/// <summary>From the hip.</summary>
	HipFire = 2,
	/// <summary>Full aiming; optics apply.</summary>
	Aiming = 3,
	/// <summary>«Не готов» — safe carry; not combat LowReady. Saved on character; excluded from AI auto.</summary>
	NotReady = 4,
	/// <summary>Former HighReady — nearly on target, derived LowReady→Aiming. Fire forbidden.</summary>
	PreAim = 5,
	/// <summary>Muzzle up toward a threat sector. Authored pose; fire and AimProgress forbidden.</summary>
	HighReady = 6,
	/// <summary>Peaceful patrol carry. Same rules as NotReady; own authored coordinates.</summary>
	NotReadyPatrol = 7,
	/// <summary>HipFire while walking standing. Own authored local + IK; player mode stays HipFire.</summary>
	HipFireWalk = 8,
	/// <summary>HipFire while walking crouched. Own authored local + IK; player mode stays HipFire.</summary>
	HipFireCrouchWalk = 9,
}

/// <summary>Pose classification used by fire, aim, animator, and Auto AI.</summary>
public static class WeaponPoseStateExtensions
{
	public static bool IsAiAutoPose(this WeaponPoseState _pose) =>
		!_pose.IsPeacefulCarryPose();

	public static bool IsPeacefulCarryPose(this WeaponPoseState _pose) =>
		_pose == WeaponPoseState.NotReady || _pose == WeaponPoseState.NotReadyPatrol;

	public static bool IsCombatPose(this WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.HighReady:
			case WeaponPoseState.PreAim:
			case WeaponPoseState.PointAim:
			case WeaponPoseState.Aiming:
				return true;
			default:
				return false;
		}
	}

	/// <summary>
	/// Poses that raise toward a target (barrel correction / ready). PreAim is included.
	/// Actual trigger pull uses <see cref="CanShootFromPose"/>.
	/// </summary>
	public static bool CanFireFromPose(this WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.PreAim:
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
			case WeaponPoseState.PointAim:
			case WeaponPoseState.Aiming:
				return true;
			default:
				return false;
		}
	}

	/// <summary>Огонь только из HipFire / HipFireWalk / HipFireCrouchWalk / PointAim / Aiming. PreAim — нет.</summary>
	public static bool CanShootFromPose(this WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
			case WeaponPoseState.PointAim:
			case WeaponPoseState.Aiming:
				return true;
			default:
				return false;
		}
	}

	/// <summary>Idle HipFire or walk HipFire (stand / crouch) — same fire, aim, and facing rules.</summary>
	public static bool IsHipFireHold(this WeaponPoseState _pose) =>
		_pose == WeaponPoseState.HipFire
		|| _pose == WeaponPoseState.HipFireWalk
		|| _pose == WeaponPoseState.HipFireCrouchWalk;

	public static bool CanAccumulateAimFromPose(this WeaponPoseState _pose) =>
		_pose.CanFireFromPose();

	/// <summary>
	/// Standing / crouch relaxed idle: Stand_Relaxed_Idle, RifleCrouch_Idle
	/// (NotReady, NotReadyPatrol, HipFire). HipFireWalk / HipFireCrouchWalk use aim-walk clips, not this idle.
	/// Combat poses use the aim idle. Vehicle HipFire is an exception — see UsesVehicleSeatAimClip.
	/// </summary>
	public static bool UsesRelaxedStandIdle(this WeaponPoseState _pose) =>
		_pose == WeaponPoseState.NotReady
		|| _pose == WeaponPoseState.NotReadyPatrol
		|| _pose == WeaponPoseState.HipFire;

	/// <summary>
	/// Vehicle seat clip: Seat_Aim for HipFire / walk HipFire and combat poses, Seat_relax for NotReady / Patrol.
	/// </summary>
	public static bool UsesVehicleSeatAimClip(this WeaponPoseState _pose) =>
		_pose.IsHipFireHold() || !_pose.UsesRelaxedStandIdle();

	/// <summary>
	/// Raised toward a threat sector (HighReady / PreAim / HipFire / walk HipFire / PointAim / Aiming).
	/// Used by body/spine facing. Not the fire gate — see <see cref="CanShootFromPose"/>.
	/// </summary>
	public static bool IsWeaponRaised(this WeaponPoseState _pose) =>
		_pose != WeaponPoseState.NotReady
		&& _pose != WeaponPoseState.NotReadyPatrol
		&& _pose != WeaponPoseState.LowReady;
}

/// <summary>Player / AI selection mode. Auto resolves to a <see cref="WeaponPoseState"/> from baked caps.</summary>
public enum WeaponPoseMode
{
	LowReady = 0,
	PointAim = 1,
	HipFire = 2,
	Aiming = 3,
	Auto = 4,
	/// <summary>Former HighReady combat raise (fire forbidden). Value 5 keeps serialized debug/UI.</summary>
	PreAim = 5,
	/// <summary>Muzzle-up readiness. Fire forbidden.</summary>
	HighReady = 6,
}

public static class WeaponPoseModeExtensions
{
	/// <summary>Manual E cycle: LowReady / HighReady / PreAim / Aiming / PointAim. HipFire and Auto are not in this set.</summary>
	public static bool IsManualCombatMode(this WeaponPoseMode _mode)
	{
		switch (_mode)
		{
			case WeaponPoseMode.LowReady:
			case WeaponPoseMode.HighReady:
			case WeaponPoseMode.PreAim:
			case WeaponPoseMode.Aiming:
			case WeaponPoseMode.PointAim:
				return true;
			default:
				return false;
		}
	}
}
