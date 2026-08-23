using UnityEngine;

/// <summary>
/// Shared hitscan damage-range math. Keep in lockstep with
/// <c>Tools/weapon_damage_range_model.py</c>. Does not retune BaseDamage.
/// </summary>
public static class WeaponDamageRangeMath
{
	#region Constants
	public const float DefaultFalloffZeroRangeMultiplier = 2f;
	public const float AmmoCapEpsilon = 0.1f;
	public const float MaxHitscanEnvelopeMeters = 300f;
	public const float ProposedOpticEffectiveRangeModifier = 1f;
	#endregion

	#region Public Methods
	public static float ResolveEffectiveRangeMeters(
		float _weaponRangeMeters,
		float _attachmentProduct,
		float _ammoRangeMeters)
	{
		float effective = Mathf.Max(0f, _weaponRangeMeters) * Mathf.Max(0f, _attachmentProduct);
		if (_ammoRangeMeters > AmmoCapEpsilon)
			effective = Mathf.Min(effective, _ammoRangeMeters);

		return effective;
	}

	public static float ComputeFalloffMultiplier(
		float _distanceMeters,
		float _effectiveRangeMeters,
		float _falloffZeroRangeMultiplier = DefaultFalloffZeroRangeMultiplier)
	{
		if (_effectiveRangeMeters <= AmmoCapEpsilon)
			return 1f;

		if (_distanceMeters <= _effectiveRangeMeters)
			return 1f;

		float zeroAt = ComputeZeroDamageDistance(_effectiveRangeMeters, _falloffZeroRangeMultiplier);
		if (_distanceMeters >= zeroAt)
			return 0f;

		return 1f - (_distanceMeters - _effectiveRangeMeters) / (zeroAt - _effectiveRangeMeters);
	}

	public static float ComputeZeroDamageDistance(
		float _effectiveRangeMeters,
		float _falloffZeroRangeMultiplier = DefaultFalloffZeroRangeMultiplier)
	{
		return Mathf.Max(0f, _effectiveRangeMeters) * Mathf.Max(1.01f, _falloffZeroRangeMultiplier);
	}
	#endregion
}
