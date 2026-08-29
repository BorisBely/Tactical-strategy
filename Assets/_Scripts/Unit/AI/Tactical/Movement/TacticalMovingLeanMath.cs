using UnityEngine;

/// <summary>
/// #14.8 when to lean while traversing. Does not pick routes or fire.
/// Uses existing lean depths. Minimum sufficient depth. Not a freeze.
/// </summary>
public static class TacticalMovingLeanMath
{
	#region Constants
	public const float DefaultApproachMeters = 4f;
	public const float DefaultCorridorMeters = 1.5f;
	public const float MinLeanValue = 0.12f;
	#endregion

	#region Public Methods
	public static CoverMovementLeanRequest ToRequest(in TacticalMovingLeanDecision _decision)
	{
		if (!_decision.RequestsLean)
			return CoverMovementLeanRequest.Idle;
		return new CoverMovementLeanRequest
		{
			Mode = CoverMovementLeanMode.Leaning,
			Direction = _decision.Direction,
			Depth = _decision.Depth
		};
	}

	public static TacticalMovingLeanDecision Decide(in TacticalMovingLeanSituation _situation)
	{
		var decision = new TacticalMovingLeanDecision
		{
			TraversalCost01 = Mathf.Clamp01(_situation.TraversalCost01)
		};

		if (_situation.Arrived)
			return Interrupt(in _situation, in decision, TacticalMovingLeanReason.Arrival);
		if (_situation.ImmediateThreat)
			return Interrupt(in _situation, in decision, TacticalMovingLeanReason.ImmediateThreat);
		if (_situation.Replan || _situation.RouteChanged)
			return Interrupt(in _situation, in decision, TacticalMovingLeanReason.Replan);
		if (!_situation.Moving)
			return Interrupt(in _situation, in decision, TacticalMovingLeanReason.NotMoving);

		if (_situation.CurrentlyLeaning &&
		    (_situation.CornerPassed || !_situation.HasCorner || !_situation.InCorridor))
		{
			decision.Action = TacticalMovingLeanAction.Exit;
			decision.Reason = _situation.CornerPassed
				? TacticalMovingLeanReason.CornerPassed
				: TacticalMovingLeanReason.OpportunityEnded;
			decision.Request = CoverMovementLeanRequest.Idle;
			return decision;
		}

		if (!_situation.HasCorner)
		{
			decision.Reason = TacticalMovingLeanReason.NoOpportunity;
			return MaybeExitIdle(in _situation, in decision);
		}

		if (!_situation.InCorridor)
		{
			decision.Reason = TacticalMovingLeanReason.NotInCorridor;
			return MaybeExitIdle(in _situation, in decision);
		}

		if (_situation.DistanceToCornerMeters > DefaultApproachMeters)
		{
			decision.Reason = TacticalMovingLeanReason.FarFromCorner;
			return MaybeExitIdle(in _situation, in decision);
		}

		if (_situation.VisibleWithoutLean)
		{
			decision.Reason = TacticalMovingLeanReason.AlreadyVisible;
			return MaybeExitIdle(in _situation, in decision);
		}

		CoverPeekDirection direction;
		CoverLeanLevel depth;
		float gain;
		float exposure;
		if (!TrySelect(in _situation, out direction, out depth, out gain, out exposure))
		{
			decision.Reason = (!_situation.LeftAvailable && !_situation.RightAvailable)
				? TacticalMovingLeanReason.NoOpportunity
				: TacticalMovingLeanReason.NoBenefit;
			return MaybeExitIdle(in _situation, in decision);
		}

		float exposureChange = exposure - _situation.ExposureWithoutLean;
		float leanValue = gain - Mathf.Max(0f, exposureChange) - decision.TraversalCost01;
		if (leanValue < MinLeanValue)
		{
			decision.Reason = decision.TraversalCost01 >= gain
				? TacticalMovingLeanReason.TraversalCost
				: TacticalMovingLeanReason.NoBenefit;
			decision.VisibilityGain = gain;
			decision.ExposureChange = exposureChange;
			return MaybeExitIdle(in _situation, in decision);
		}

		decision.Action = TacticalMovingLeanAction.Lean;
		decision.Direction = direction;
		decision.Depth = depth;
		decision.Reason = TacticalMovingLeanReason.SectorGain;
		decision.Opportunity = true;
		decision.VisibilityGain = gain;
		decision.ExposureChange = exposureChange;
		decision.Request = new CoverMovementLeanRequest
		{
			Mode = CoverMovementLeanMode.Leaning,
			Direction = direction,
			Depth = depth
		};
		return decision;
	}
	#endregion

	#region Private Methods
	private static TacticalMovingLeanDecision Interrupt(
		in TacticalMovingLeanSituation _situation,
		in TacticalMovingLeanDecision _seed,
		TacticalMovingLeanReason _reason)
	{
		TacticalMovingLeanDecision decision = _seed;
		decision.Reason = _reason;
		if (_situation.CurrentlyLeaning)
		{
			decision.Action = TacticalMovingLeanAction.Exit;
			decision.Request = CoverMovementLeanRequest.Idle;
			return decision;
		}

		decision.Action = TacticalMovingLeanAction.None;
		decision.Request = CoverMovementLeanRequest.Idle;
		return decision;
	}

	private static TacticalMovingLeanDecision MaybeExitIdle(
		in TacticalMovingLeanSituation _situation,
		in TacticalMovingLeanDecision _seed)
	{
		TacticalMovingLeanDecision decision = _seed;
		if (_situation.CurrentlyLeaning)
		{
			decision.Action = TacticalMovingLeanAction.Exit;
			if (decision.Reason == TacticalMovingLeanReason.None ||
			    decision.Reason == TacticalMovingLeanReason.NoBenefit ||
			    decision.Reason == TacticalMovingLeanReason.NoOpportunity ||
			    decision.Reason == TacticalMovingLeanReason.AlreadyVisible ||
			    decision.Reason == TacticalMovingLeanReason.FarFromCorner ||
			    decision.Reason == TacticalMovingLeanReason.NotInCorridor)
			{
				decision.Reason = TacticalMovingLeanReason.OpportunityEnded;
			}

			decision.Request = CoverMovementLeanRequest.Idle;
			return decision;
		}

		decision.Action = TacticalMovingLeanAction.None;
		decision.Request = CoverMovementLeanRequest.Idle;
		return decision;
	}

	private static bool TrySelect(
		in TacticalMovingLeanSituation _situation,
		out CoverPeekDirection _direction,
		out CoverLeanLevel _depth,
		out float _gain,
		out float _exposure)
	{
		_direction = CoverPeekDirection.None;
		_depth = CoverLeanLevel.None;
		_gain = 0f;
		_exposure = 0f;
		bool left = _situation.LeftAvailable &&
		            HasDepth(_situation.LeftSmallSufficient, _situation.LeftMediumSufficient,
			            _situation.LeftDeepSufficient);
		bool right = _situation.RightAvailable &&
		             HasDepth(_situation.RightSmallSufficient, _situation.RightMediumSufficient,
			             _situation.RightDeepSufficient);
		if (!left && !right)
			return false;

		float leftScore = left
			? _situation.LeftVisibilityGain - _situation.LeftExposure01
			: float.MinValue;
		float rightScore = right
			? _situation.RightVisibilityGain - _situation.RightExposure01
			: float.MinValue;
		if (left && (!right || leftScore > rightScore + 0.001f ||
		             (Mathf.Abs(leftScore - rightScore) <= 0.001f &&
		              _situation.LeftExposure01 <= _situation.RightExposure01)))
		{
			_direction = CoverPeekDirection.Left;
			_depth = MinDepth(
				_situation.LeftSmallSufficient,
				_situation.LeftMediumSufficient,
				_situation.LeftDeepSufficient);
			_gain = _situation.LeftVisibilityGain;
			_exposure = _situation.LeftExposure01;
			return _depth != CoverLeanLevel.None;
		}

		if (right)
		{
			_direction = CoverPeekDirection.Right;
			_depth = MinDepth(
				_situation.RightSmallSufficient,
				_situation.RightMediumSufficient,
				_situation.RightDeepSufficient);
			_gain = _situation.RightVisibilityGain;
			_exposure = _situation.RightExposure01;
			return _depth != CoverLeanLevel.None;
		}

		return false;
	}

	private static bool HasDepth(bool _small, bool _medium, bool _deep)
	{
		return _small || _medium || _deep;
	}

	private static CoverLeanLevel MinDepth(bool _small, bool _medium, bool _deep)
	{
		if (_small)
			return CoverLeanLevel.Small;
		if (_medium)
			return CoverLeanLevel.Medium;
		if (_deep)
			return CoverLeanLevel.Deep;
		return CoverLeanLevel.None;
	}
	#endregion
}
