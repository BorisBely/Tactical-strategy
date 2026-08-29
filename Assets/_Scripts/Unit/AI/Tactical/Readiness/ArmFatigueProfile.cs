using UnityEngine;

/// <summary>
/// #14B.6 arm-load parameters. Prototype relations, not freeze.
/// Does not add Readiness states. Rank modifiers default to 1 (same machine).
/// </summary>
public struct ArmFatigueProfile
{
	#region Public Fields
	public float LoadRateNotReady;
	public float LoadRatePatrol;
	public float LoadRateLowReady;
	public float LoadRateHighReady;
	public float LoadRatePreAim;
	public float LoadRateAim;
	public float LoadRateFiring;
	public float RecoveryRate;
	public float FatigueAimMultiplier;
	public float FatigueRecoilMultiplier;
	public float FatigueTurnMultiplier;
	public float ArmLoadMultiplier;
	public float FatigueLoadModifier;
	public float FatigueRecoveryModifier;
	public float BaseTurnToTargetTime;
	#endregion

	#region Public Methods
	/// <summary>Play-calibration prototype. Same structure for every rank.</summary>
	public static ArmFatigueProfile PlayPrototype()
	{
		return new ArmFatigueProfile
		{
			LoadRateNotReady = 0f,
			LoadRatePatrol = 0f,
			LoadRateLowReady = 0.03f,
			LoadRateHighReady = 0.06f,
			LoadRatePreAim = 0.11f,
			LoadRateAim = 0.14f,
			LoadRateFiring = 0.28f,
			RecoveryRate = 0.08f,
			FatigueAimMultiplier = 1.55f,
			FatigueRecoilMultiplier = 0.55f,
			FatigueTurnMultiplier = 1.5f,
			ArmLoadMultiplier = 1f,
			FatigueLoadModifier = 1f,
			FatigueRecoveryModifier = 1f,
			BaseTurnToTargetTime = 0.35f
		};
	}

	/// <summary>14B.0–14B.5 Instant: no accumulate, no recover. Effect curves still exist.</summary>
	public static ArmFatigueProfile Disabled()
	{
		ArmFatigueProfile profile = PlayPrototype();
		profile.LoadRateNotReady = 0f;
		profile.LoadRatePatrol = 0f;
		profile.LoadRateLowReady = 0f;
		profile.LoadRateHighReady = 0f;
		profile.LoadRatePreAim = 0f;
		profile.LoadRateAim = 0f;
		profile.LoadRateFiring = 0f;
		profile.RecoveryRate = 0f;
		return profile;
	}
	#endregion
}

/// <summary>Snapshot of the three independent 14B.6 effects.</summary>
public struct ArmFatigueEffects
{
	#region Public Fields
	public float Fatigue;
	public float AimTimeMultiplier;
	public float RecoilControlModifier;
	public float TurnTimeMultiplier;
	public float TurnToTargetTime;
	#endregion

	#region Public Methods
	public float EffectiveRecoilControl(float _rankRecoilControl)
	{
		return Mathf.Max(0f, _rankRecoilControl * RecoilControlModifier);
	}
	#endregion
}
