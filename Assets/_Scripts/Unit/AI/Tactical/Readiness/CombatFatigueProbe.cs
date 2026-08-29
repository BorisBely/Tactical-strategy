using UnityEngine;

/// <summary>
/// #14B.7 samples the closed AimTime / Turn / Recoil path. No new combat math.
/// </summary>
public static class CombatFatigueProbe
{
	#region Constants
	public const float BaselineAimTimeSeconds = 0.25f;
	public const float DefaultTurnDeltaDegrees = 45f;
	public const float TurnArriveDegrees = 0.5f;
	public const float RecoilProbeOffsetDegrees = 4f;
	public const float RecoilProbeSeconds = 0.5f;
	#endregion

	#region Public Methods
	public static float SampleAimTimeSeconds(Component _host)
	{
		if (_host != null && _host.TryGetComponent(out UnitWeaponAimProgressController aim))
			return aim.SampleAimTimeSeconds();

		ArmFatigueEffects effects = ArmFatigueBinding.EffectsOrNeutral(_host);
		return BaselineAimTimeSeconds * effects.AimTimeMultiplier;
	}

	public static float SampleAimYawSmoothTime(Component _host)
	{
		if (_host != null && _host.TryGetComponent(out UnitWeaponAiming aiming))
			return aiming.SampleAimYawSmoothTime();

		ArmFatigueEffects effects = ArmFatigueBinding.EffectsOrNeutral(_host);
		return 0.04f * effects.TurnTimeMultiplier;
	}

	public static float EstimateTurnSeconds(float _smoothTime, float _deltaDegrees)
	{
		float delta = Mathf.Abs(_deltaDegrees);
		if (delta <= TurnArriveDegrees)
			return 0f;

		float tau = Mathf.Max(0.0001f, _smoothTime);
		return -tau * Mathf.Log(TurnArriveDegrees / delta);
	}

	public static float SampleTurnSeconds(Component _host, float _deltaDegrees)
	{
		return EstimateTurnSeconds(SampleAimYawSmoothTime(_host), _deltaDegrees);
	}

	public static float SampleEffectiveRecoilControl(Component _host)
	{
		if (_host != null && _host.TryGetComponent(out UnitWeaponRecoilController recoil))
			return recoil.SampleEffectiveRecoilControl();

		float rankControl = 50f;
		if (_host != null && _host.TryGetComponent(out UnitCombatStats stats))
			rankControl = stats.RecoilControl;
		return ArmFatigueBinding.EffectsOrNeutral(_host).EffectiveRecoilControl(rankControl);
	}

	public static float SampleSkillRecoveryMultiplier(Component _host)
	{
		float control = SampleEffectiveRecoilControl(_host);
		if (_host != null && _host.TryGetComponent(out UnitCombatStats stats))
			return stats.GetRecoilRecoveryMultiplier(control);

		if (_host != null && _host.TryGetComponent(out UnitWeaponRecoilController recoil))
			return recoil.SampleSkillRecoveryMultiplier();

		return 1f;
	}

	public static float RemainingRecoilAfter(float _recoveryPerSecond, float _seconds)
	{
		Vector2 next = WeaponRecoilMath.Recover(
			new Vector2(RecoilProbeOffsetDegrees, 0f),
			_recoveryPerSecond,
			_seconds);
		return next.magnitude;
	}
	#endregion
}
