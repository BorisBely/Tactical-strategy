/// <summary>
/// #14B.0 transition table. Raises may skip. Decay is one ladder step. Fatigue is ignored.
/// </summary>
public static class ReadinessMath
{
	#region Public Methods
	public static int Level(ReadinessState _state) => (int)_state;

	public static bool IsSame(ReadinessState _from, ReadinessState _to) => _from == _to;

	public static bool IsRaise(ReadinessState _from, ReadinessState _to) =>
		Level(_to) > Level(_from);

	public static bool IsAllowed(ReadinessState _from, ReadinessState _to)
	{
		if (IsSame(_from, _to) || IsRaise(_from, _to))
			return true;

		return IsSingleStepDecay(_from, _to);
	}

	public static bool IsSingleStepDecay(ReadinessState _from, ReadinessState _to)
	{
		switch (_from)
		{
			case ReadinessState.Aim:
				return _to == ReadinessState.PreAim;
			case ReadinessState.PreAim:
				return _to == ReadinessState.HighReady || _to == ReadinessState.LowReady;
			case ReadinessState.HighReady:
			case ReadinessState.LowReady:
				return _to == ReadinessState.Patrol || _to == ReadinessState.NotReady;
			case ReadinessState.Patrol:
				return _to == ReadinessState.NotReady;
			default:
				return false;
		}
	}

	public static ReadinessState InitialState(ReadinessRankKind _rank) =>
		ForRank(_rank).CalmState;

	public static ReadinessState HeardThreatState(ReadinessRankKind _rank) =>
		ForRank(_rank).HeardThreatState;

	public static ReadinessProfile ForRank(ReadinessRankKind _rank) =>
		ReadinessProfile.ForRank(_rank);

	public static ReadinessState NextDecayState(ReadinessState _current, in ReadinessProfile _profile)
	{
		if (_current == ReadinessState.Aim)
			return ReadinessState.PreAim;
		if (_current == ReadinessState.PreAim)
			return _profile.HeardThreatState;
		if (_current == _profile.CalmState)
			return _profile.CalmState;
		if (Level(_current) > Level(_profile.CalmState))
			return _profile.CalmState;

		return _current;
	}

	/// <summary>
	/// Time to finish a raise into Aim. ArmFatigue does not enter this formula.
	/// </summary>
	public static float AimTransitionDuration(ReadinessState _from, in ReadinessProfile _profile)
	{
		return ScaleDuration(AimProfileDuration(_from, in _profile), _profile.ToAimSpeed);
	}

	public static float AimProfileDuration(ReadinessState _from, in ReadinessProfile _profile)
	{
		switch (_from)
		{
			case ReadinessState.NotReady:
				return _profile.NotReadyAimDuration;
			case ReadinessState.Patrol:
				return _profile.PatrolAimDuration;
			case ReadinessState.LowReady:
				return _profile.LowReadyAimDuration;
			case ReadinessState.HighReady:
				return _profile.HighReadyAimDuration;
			case ReadinessState.PreAim:
				return _profile.PreAimAimDuration;
			default:
				return 0f;
		}
	}

	public static float ReadyTransitionDuration(in ReadinessProfile _profile)
	{
		return ScaleDuration(_profile.ReadyRaiseDuration, _profile.ToReadySpeed);
	}

	public static float TransitionDuration(
		ReadinessState _from,
		ReadinessState _to,
		in ReadinessProfile _profile)
	{
		if (_to == ReadinessState.Aim)
			return AimTransitionDuration(_from, in _profile);
		if (IsRaise(_from, _to))
			return ReadyTransitionDuration(in _profile);
		if (IsSingleStepDecay(_from, _to))
			return DecayTransitionDuration(_from, _to, in _profile);
		return 0f;
	}

	public static float DecayTransitionDuration(
		ReadinessState _from,
		ReadinessState _to,
		in ReadinessProfile _profile)
	{
		if (!IsSingleStepDecay(_from, _to))
			return 0f;

		float duration = DecayProfileDuration(_from, in _profile);
		if (duration < 0f)
			duration = 0f;
		return duration;
	}

	public static float DecayProfileDuration(ReadinessState _from, in ReadinessProfile _profile)
	{
		switch (_from)
		{
			case ReadinessState.Aim:
				return _profile.AimToPreAimDuration;
			case ReadinessState.PreAim:
				return _profile.PreAimToReadyDuration;
			default:
				return _profile.ReadyToCalmDuration;
		}
	}

	public static float HoldTime(ReadinessState _state, in ReadinessProfile _profile)
	{
		switch (_state)
		{
			case ReadinessState.Aim:
				return _profile.AimHoldTime;
			case ReadinessState.PreAim:
				return _profile.PreAimHoldTime;
			case ReadinessState.HighReady:
				return _profile.HighReadyHoldTime;
			case ReadinessState.LowReady:
				return _profile.LowReadyHoldTime;
			default:
				return _profile.CalmDownDelay;
		}
	}

	public static float EffectiveHoldTime(ReadinessState _state, in ReadinessProfile _profile)
	{
		float hold = HoldTime(_state, in _profile);
		if (hold < 0f)
			hold = 0f;
		return hold * ClampSpeed(_profile.RankCalmDownModifier);
	}

	public static float EffectiveCalmDownDelay(in ReadinessProfile _profile)
	{
		return EffectiveHoldTime(ReadinessState.Aim, in _profile);
	}

	public static float LadderCalmDownDuration(in ReadinessProfile _profile)
	{
		return _profile.AimHoldTime +
		       _profile.AimToPreAimDuration +
		       _profile.PreAimHoldTime +
		       _profile.PreAimToReadyDuration +
		       _profile.HighReadyHoldTime +
		       _profile.ReadyToCalmDuration;
	}

	public static bool IsPendingDecay(in ReadinessContext _context)
	{
		return _context.HasPendingTransition &&
		       !IsRaise(_context.TransitionFrom, _context.TransitionTo);
	}

	public static ReadinessDecayPhase DecayPhase(in ReadinessContext _context, in ReadinessProfile _profile)
	{
		if (_context.CurrentState == _profile.CalmState)
			return ReadinessDecayPhase.None;
		if (IsPendingDecay(in _context))
			return ReadinessDecayPhase.StepDown;
		if (_context.HasActiveCombatActivity)
			return ReadinessDecayPhase.None;
		return ReadinessDecayPhase.Hold;
	}

	public static float ScaleDuration(float _profileDuration, float _speed)
	{
		if (_profileDuration <= 0f)
			return 0f;
		return _profileDuration / ClampSpeed(_speed);
	}

	public static float ClampSpeed(float _speed)
	{
		return _speed < 0.01f ? 0.01f : _speed;
	}

	public static bool FatigueAffectsResult() => false;

	public static ReadinessRankKind RankFromAssetIndex(int _index)
	{
		if (_index < (int)ReadinessRankKind.Recruit || _index > (int)ReadinessRankKind.Elite)
			return ReadinessRankKind.Soldier;
		return (ReadinessRankKind)_index;
	}
	#endregion
}
