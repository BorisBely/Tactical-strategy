using UnityEngine;

/// <summary>
/// #14B.6 ArmFatigue 0..1. Load from Readiness / Firing, recover when unloaded.
/// Affects AimTime, RecoilControl, TurnToTargetTime only. Not ReadinessState.
/// </summary>
public static class ArmFatigueMath
{
	#region Constants
	public const float Min = 0f;
	public const float Max = 1f;
	public const float Band0 = 0f;
	public const float Band25 = 0.25f;
	public const float Band50 = 0.5f;
	public const float Band75 = 0.75f;
	#endregion

	#region Public Methods
	public static float Clamp01(float _fatigue)
	{
		if (_fatigue < Min)
			return Min;
		if (_fatigue > Max)
			return Max;
		return _fatigue;
	}

	public static float LoadRate(ReadinessState _state, in ArmFatigueProfile _profile)
	{
		switch (_state)
		{
			case ReadinessState.LowReady:
				return NonNegative(_profile.LoadRateLowReady);
			case ReadinessState.HighReady:
				return NonNegative(_profile.LoadRateHighReady);
			case ReadinessState.PreAim:
				return NonNegative(_profile.LoadRatePreAim);
			case ReadinessState.Aim:
				return NonNegative(_profile.LoadRateAim);
			case ReadinessState.Patrol:
				return NonNegative(_profile.LoadRatePatrol);
			default:
				return NonNegative(_profile.LoadRateNotReady);
		}
	}

	public static float EffectiveLoadRate(
		ReadinessState _state,
		bool _firing,
		in ArmFatigueProfile _profile)
	{
		float rate = LoadRate(_state, in _profile);
		if (_firing)
			rate = Mathf.Max(rate, NonNegative(_profile.LoadRateFiring));
		rate *= PositiveOrOne(_profile.ArmLoadMultiplier);
		rate *= PositiveOrOne(_profile.FatigueLoadModifier);
		return rate;
	}

	public static bool HasLoad(float _loadRate)
	{
		return _loadRate > 0.00001f;
	}

	public static float Step(
		float _fatigue,
		float _dt,
		ReadinessState _state,
		bool _firing,
		bool _allowed,
		in ArmFatigueProfile _profile,
		out bool _loaded)
	{
		float load = EffectiveLoadRate(_state, _firing, in _profile);
		_loaded = HasLoad(load);
		if (!_allowed || _dt <= 0f)
			return Clamp01(_fatigue);

		if (_loaded)
			return Clamp01(_fatigue + load * _dt);

		float recovery = NonNegative(_profile.RecoveryRate) * PositiveOrOne(_profile.FatigueRecoveryModifier);
		return Clamp01(_fatigue - recovery * _dt);
	}

	public static float AimTimeMultiplier(float _fatigue, in ArmFatigueProfile _profile)
	{
		return LerpAtFatigue(1f, _profile.FatigueAimMultiplier, _fatigue);
	}

	public static float RecoilControlModifier(float _fatigue, in ArmFatigueProfile _profile)
	{
		return LerpAtFatigue(1f, _profile.FatigueRecoilMultiplier, _fatigue);
	}

	public static float TurnTimeMultiplier(float _fatigue, in ArmFatigueProfile _profile)
	{
		return LerpAtFatigue(1f, _profile.FatigueTurnMultiplier, _fatigue);
	}

	public static float EffectiveRecoilControl(float _rankRecoilControl, float _fatigue, in ArmFatigueProfile _profile)
	{
		return Mathf.Max(0f, _rankRecoilControl * RecoilControlModifier(_fatigue, in _profile));
	}

	public static float FinalAimTime(float _baseAimTime, float _fatigue, in ArmFatigueProfile _profile)
	{
		float baseline = _baseAimTime < 0f ? 0f : _baseAimTime;
		return baseline * AimTimeMultiplier(_fatigue, in _profile);
	}

	public static float FinalTurnToTargetTime(float _fatigue, in ArmFatigueProfile _profile)
	{
		float baseline = _profile.BaseTurnToTargetTime < 0f ? 0f : _profile.BaseTurnToTargetTime;
		return baseline * TurnTimeMultiplier(_fatigue, in _profile);
	}

	public static ArmFatigueEffects Evaluate(float _fatigue, in ArmFatigueProfile _profile)
	{
		float fatigue = Clamp01(_fatigue);
		return new ArmFatigueEffects
		{
			Fatigue = fatigue,
			AimTimeMultiplier = AimTimeMultiplier(fatigue, in _profile),
			RecoilControlModifier = RecoilControlModifier(fatigue, in _profile),
			TurnTimeMultiplier = TurnTimeMultiplier(fatigue, in _profile),
			TurnToTargetTime = FinalTurnToTargetTime(fatigue, in _profile)
		};
	}

	public static int ThresholdBand(float _fatigue)
	{
		float fatigue = Clamp01(_fatigue);
		if (fatigue >= Max - 0.0001f)
			return 4;
		if (fatigue >= Band75)
			return 3;
		if (fatigue >= Band50)
			return 2;
		if (fatigue >= Band25)
			return 1;
		return 0;
	}

	public static bool AffectsReadinessState() => false;

	public static bool AffectsPerception() => false;

	public static bool AffectsG6() => false;

	public static bool AffectsCover() => false;

	public static bool AffectsMovement() => false;
	#endregion

	#region Private Methods
	private static float LerpAtFatigue(float _atZero, float _atOne, float _fatigue)
	{
		return Mathf.Lerp(_atZero, _atOne, Clamp01(_fatigue));
	}

	private static float NonNegative(float _value)
	{
		return _value < 0f ? 0f : _value;
	}

	private static float PositiveOrOne(float _value)
	{
		return _value < 0.01f ? 1f : _value;
	}
	#endregion
}
