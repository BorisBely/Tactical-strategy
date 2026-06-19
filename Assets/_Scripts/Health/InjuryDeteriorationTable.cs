using System;
using UnityEngine;

/// <summary>
/// Скорость накопления летального давления по типу травмы (ед/сек до порога 100).
/// После стабилизации: худшая травма ≈10 мин, лёгкая ≈45 мин (с учётом циклов лечения).
/// </summary>
public static class InjuryDeteriorationTable
{
	#region Serializable Types
	[Serializable]
	public struct InjuryPressureEntry
	{
		public string InjuryStatusLocalizationKey;
		[Min(0f)] public float PressurePerSecond;
		[Min(0f)] public float StabilizedPressurePerSecond;
	}
	#endregion

	#region Constants
	public const float LethalPressureThreshold = 100f;
	public const float DefaultUnconsciousPressureMultiplier = 1.35f;
	public const float DefaultSelfStabilizationRetreatMaxSeconds = 0f;

	public const float WorstStabilizedSurvivalSeconds = 600f;
	public const float LightStabilizedSurvivalSeconds = 2700f;
	public const float WorstBleedingPressurePerSecond = 0.75f;
	public const float LightBleedingPressurePerSecond = 0.10f;
	#endregion

	#region Default Injury Rates
	private static readonly InjuryPressureEntry[] s_DefaultInjuryRates =
	{
		Entry("health.injury.neck_bleeding", 0.75f, 0.145f),
		Entry("health.injury.internal_bleeding", 0.40f, 0.054f),
		Entry("health.injury.lung_damage", 0.55f, 0.073f),
		Entry("health.injury.chest_bleeding", 0.30f, 0.047f),
		Entry("health.injury.head_wound", 0.22f, 0.042f),
		Entry("health.injury.left_leg_bleeding", 0.14f, 0.038f),
		Entry("health.injury.right_leg_bleeding", 0.14f, 0.038f),
		Entry("health.injury.arm_bleeding", 0.12f, 0.037f),
		Entry("health.injury.left_arm_bleeding", 0.12f, 0.037f),
		Entry("health.injury.generic_wound", 0.10f, 0.037f),
		Entry("health.injury.leg_fracture", 0f, 0f),
		Entry("health.injury.right_leg_fracture", 0f, 0f),
		Entry("health.injury.concussion", 0f, 0f)
	};
	#endregion

	#region Public Methods
	public static float GetPressurePerSecond(in InjuryUiEntry _injury)
	{
		if (TryGetInjuryEntry(_injury.StatusLocalizationKey, out InjuryPressureEntry entry))
			return Mathf.Max(0f, entry.PressurePerSecond);

		return GetFallbackPressurePerSecond(_injury.SortPriority);
	}

	public static float GetStabilizedPressurePerSecond(in InjuryUiEntry _injury)
	{
		if (TryGetInjuryEntry(_injury.StatusLocalizationKey, out InjuryPressureEntry entry))
			return Mathf.Max(0f, entry.StabilizedPressurePerSecond);

		float baseRate = GetFallbackPressurePerSecond(_injury.SortPriority);
		return ResolveStabilizedPressurePerSecond(baseRate, _injury.SortPriority);
	}

	public static float GetFallbackPressurePerSecond(int _sortPriority)
	{
		if (_sortPriority <= 10)
			return 0.70f;
		if (_sortPriority <= 20)
			return 0.45f;
		if (_sortPriority <= 30)
			return 0.20f;
		if (_sortPriority <= 40)
			return 0.12f;

		return 0.08f;
	}

	public static float GetSelfStabilizationDurationSeconds(int _sortPriority)
	{
		int cycles = SelfHealPresentationTiming.ResolveHealCycles(_sortPriority);
		return DefaultSelfStabilizationRetreatMaxSeconds +
		       SelfHealPresentationTiming.GetTotalPresentationDurationSeconds(cycles);
	}

	public static float EstimateSecondsToLethalPressure(
		float _pressurePerSecond,
		float _startingPressure = 0f,
		bool _isUnconscious = false,
		float _unconsciousMultiplier = DefaultUnconsciousPressureMultiplier,
		float _tickSeconds = 1f)
	{
		float rate = Mathf.Max(0f, _pressurePerSecond);
		if (rate <= 0f)
			return float.PositiveInfinity;

		if (_isUnconscious)
			rate *= Mathf.Max(1f, _unconsciousMultiplier);

		float remaining = LethalPressureThreshold - Mathf.Max(0f, _startingPressure);
		if (remaining <= 0f)
			return 0f;

		float tickRate = rate * Mathf.Max(0.1f, _tickSeconds);
		int ticks = Mathf.CeilToInt(remaining / tickRate);
		return ticks * Mathf.Max(0.1f, _tickSeconds);
	}

	public static float EstimateTotalSecondsToLethalAfterSelfStabilization(in InjuryUiEntry _injury)
	{
		float baseRate = GetPressurePerSecond(_injury);
		if (baseRate <= 0f)
			return float.PositiveInfinity;

		float healSeconds = GetSelfStabilizationDurationSeconds(_injury.SortPriority);
		float pressureDuringHeal = baseRate * healSeconds;
		float stabilizedRate = GetStabilizedPressurePerSecond(_injury);
		float afterHealSeconds = EstimateSecondsToLethalPressure(stabilizedRate, pressureDuringHeal);
		if (float.IsPositiveInfinity(afterHealSeconds))
			return float.PositiveInfinity;

		return healSeconds + afterHealSeconds;
	}

	public static float EstimateUnitSecondsToLethal(
		UnitHealth _health,
		bool _isUnconscious,
		float _unconsciousMultiplier = DefaultUnconsciousPressureMultiplier,
		float _tickSeconds = 1f)
	{
		if (_health == null || _health.IsDead || !_health.HasInjuries)
			return float.PositiveInfinity;

		float currentPressure = _health.GetTotalLethalPressure();
		if (currentPressure >= LethalPressureThreshold)
			return 0f;

		float ratePerSecond = 0f;
		for (int i = 0; i < _health.InjuryCount; i++)
		{
			if (!_health.TryGetInjury(i, out InjuryUiEntry injury))
				continue;

			ratePerSecond += injury.IsStabilized
				? GetStabilizedPressurePerSecond(injury)
				: GetPressurePerSecond(injury);
		}

		return EstimateSecondsToLethalPressure(
			ratePerSecond,
			currentPressure,
			_isUnconscious,
			_unconsciousMultiplier,
			_tickSeconds);
	}

	public static string FormatRoundedSurvivalEstimate(float _secondsToLethal)
	{
		if (float.IsPositiveInfinity(_secondsToLethal))
			return LocalizationManager.Get("health.survival.stable", "стабилен");

		if (_secondsToLethal <= 0f)
			return LocalizationManager.Get("health.survival.imminent", "смерть неизбежна");

		float minutes = _secondsToLethal / 60f;
		if (minutes <= 1f)
			return LocalizationManager.Get("health.survival.less_than_1_min", "меньше 1 минуты");
		if (minutes <= 5f)
			return LocalizationManager.Get("health.survival.less_than_5_min", "меньше 5 минут");
		if (minutes <= 10f)
			return LocalizationManager.Get("health.survival.less_than_10_min", "меньше 10 минут");
		if (minutes <= 15f)
			return LocalizationManager.Get("health.survival.less_than_15_min", "меньше 15 минут");
		if (minutes <= 30f)
			return LocalizationManager.Get("health.survival.less_than_30_min", "меньше 30 минут");
		if (minutes <= 60f)
			return LocalizationManager.Get("health.survival.less_than_60_min", "меньше 1 часа");

		return LocalizationManager.Get("health.survival.more_than_60_min", "более 1 часа");
	}
	#endregion

	#region Private Methods
	private static bool TryGetInjuryEntry(string _statusKey, out InjuryPressureEntry _entry)
	{
		_entry = default;
		if (string.IsNullOrWhiteSpace(_statusKey))
			return false;

		for (int i = 0; i < s_DefaultInjuryRates.Length; i++)
		{
			if (string.Equals(
				    s_DefaultInjuryRates[i].InjuryStatusLocalizationKey,
				    _statusKey,
				    StringComparison.Ordinal))
			{
				_entry = s_DefaultInjuryRates[i];
				return true;
			}
		}

		return false;
	}

	private static float ResolveStabilizedPressurePerSecond(float _baseRate, int _sortPriority)
	{
		if (_baseRate <= 0f)
			return 0f;

		float targetSurvivalSeconds = ResolveTargetStabilizedSurvivalSeconds(_baseRate);
		float healSeconds = GetSelfStabilizationDurationSeconds(_sortPriority);
		float pressureDuringHeal = _baseRate * healSeconds;
		float remainingPressure = LethalPressureThreshold - pressureDuringHeal;
		float afterHealSeconds = targetSurvivalSeconds - healSeconds;
		if (remainingPressure <= 0f || afterHealSeconds <= 0f)
			return 0f;

		return remainingPressure / afterHealSeconds;
	}

	private static float ResolveTargetStabilizedSurvivalSeconds(float _baseRate)
	{
		float clampedRate = Mathf.Clamp(
			_baseRate,
			LightBleedingPressurePerSecond,
			WorstBleedingPressurePerSecond);
		float severity01 = (WorstBleedingPressurePerSecond - clampedRate) /
		                   (WorstBleedingPressurePerSecond - LightBleedingPressurePerSecond);

		return Mathf.Lerp(
			WorstStabilizedSurvivalSeconds,
			LightStabilizedSurvivalSeconds,
			severity01);
	}

	private static InjuryPressureEntry Entry(
		string _statusKey,
		float _pressurePerSecond,
		float _stabilizedPressurePerSecond)
	{
		return new InjuryPressureEntry
		{
			InjuryStatusLocalizationKey = _statusKey,
			PressurePerSecond = _pressurePerSecond,
			StabilizedPressurePerSecond = _stabilizedPressurePerSecond
		};
	}
	#endregion
}
