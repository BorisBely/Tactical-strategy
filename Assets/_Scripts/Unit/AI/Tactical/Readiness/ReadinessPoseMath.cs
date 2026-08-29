/// <summary>
/// #14B.2 ReadinessState → physical pose. Logical shortcuts stay; the pose layer may interpolate.
/// ArmFatigue does not change the mapping.
/// </summary>
public static class ReadinessPoseMath
{
	#region Public Methods
	public static WeaponPoseState ToPose(ReadinessState _state)
	{
		switch (_state)
		{
			case ReadinessState.NotReady:
				return WeaponPoseState.NotReady;
			case ReadinessState.Patrol:
				return WeaponPoseState.NotReadyPatrol;
			case ReadinessState.LowReady:
				return WeaponPoseState.LowReady;
			case ReadinessState.HighReady:
				return WeaponPoseState.HighReady;
			case ReadinessState.PreAim:
				return WeaponPoseState.PreAim;
			case ReadinessState.Aim:
				return WeaponPoseState.Aiming;
			default:
				return WeaponPoseState.NotReady;
		}
	}

	public static bool IsPeaceful(WeaponPoseState _pose) => _pose.IsPeacefulCarryPose();

	public static WeaponPoseMode ToMode(WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.HighReady:
				return WeaponPoseMode.HighReady;
			case WeaponPoseState.PreAim:
				return WeaponPoseMode.PreAim;
			case WeaponPoseState.Aiming:
				return WeaponPoseMode.Aiming;
			default:
				return WeaponPoseMode.LowReady;
		}
	}

	public static bool LogicalSkipsIntermediates(ReadinessState _from, ReadinessState _to)
	{
		return ReadinessMath.IsRaise(_from, _to) &&
		       ReadinessMath.Level(_to) - ReadinessMath.Level(_from) > 1;
	}

	public static bool PhysicalMayInterpolate(ReadinessState _from, ReadinessState _to) =>
		_from != _to;

	public static ReadinessPoseRequest FromState(ReadinessState _state)
	{
		WeaponPoseState pose = ToPose(_state);
		return new ReadinessPoseRequest
		{
			State = _state,
			Pose = pose,
			FromPose = pose,
			Mode = ToMode(pose),
			Duration = 0f,
			IsPeaceful = IsPeaceful(pose),
			FromLifeGate = false
		};
	}

	public static ReadinessPoseRequest FromController(ReadinessController _controller)
	{
		if (_controller == null)
			return FromState(ReadinessState.Patrol);

		ReadinessContext context = _controller.Context;
		bool pending = context.HasPendingTransition;
		ReadinessState state = pending ? context.TransitionTo : context.CurrentState;
		ReadinessState fromState = pending ? context.TransitionFrom : context.PreviousState;
		float duration = pending
			? context.TransitionDuration
			: _controller.LastRequest.Duration;

		WeaponPoseState pose = ToPose(state);
		return new ReadinessPoseRequest
		{
			State = state,
			Pose = pose,
			FromPose = ToPose(fromState),
			Mode = ToMode(pose),
			Duration = duration,
			IsPeaceful = IsPeaceful(pose),
			FromLifeGate = false
		};
	}

	public static ReadinessPoseRequest Incapacitated()
	{
		return new ReadinessPoseRequest
		{
			State = ReadinessState.NotReady,
			Pose = WeaponPoseState.NotReady,
			FromPose = WeaponPoseState.NotReady,
			Mode = WeaponPoseMode.LowReady,
			Duration = 0f,
			IsPeaceful = true,
			FromLifeGate = true
		};
	}

	public static bool FatigueAffectsPose() => false;
	#endregion
}
