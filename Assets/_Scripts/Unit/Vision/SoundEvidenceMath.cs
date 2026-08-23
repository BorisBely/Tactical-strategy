using UnityEngine;

/// <summary>
/// Stage 16: cheap audible falloff. No Physics, no occlusion, not VisionRange.
/// </summary>
public static class SoundEvidenceMath
{
	#region Constants
	public const float GunshotRangeMeters = 300f;
	public const float ExplosionRangeMeters = 500f;
	public const float FootstepRangeMeters = 25f;
	public const float ImpactRangeMeters = 40f;

	public const float GunshotStrength = 1f;
	public const float ExplosionStrength = 1f;
	public const float FootstepStrength = 0.35f;
	public const float ImpactStrength = 0.5f;
	#endregion

	#region Public Methods
	public static bool IsAudible(float _distanceSq, float _rangeSq)
	{
		if (_rangeSq <= 0f)
			return false;
		return _distanceSq <= _rangeSq;
	}

	public static float EvaluateConfidence(float _distance, float _strength, float _range)
	{
		if (_range <= 0f)
			return 0f;
		return Mathf.Clamp01(_strength * (1f - _distance / _range));
	}

	public static float DefaultRangeMeters(SoundEventType _type)
	{
		switch (_type)
		{
			case SoundEventType.Gunshot:
				return GunshotRangeMeters;
			case SoundEventType.Explosion:
				return ExplosionRangeMeters;
			case SoundEventType.Footstep:
				return FootstepRangeMeters;
			case SoundEventType.Impact:
				return ImpactRangeMeters;
			default:
				return GunshotRangeMeters;
		}
	}

	public static float DefaultStrength(SoundEventType _type)
	{
		switch (_type)
		{
			case SoundEventType.Gunshot:
				return GunshotStrength;
			case SoundEventType.Explosion:
				return ExplosionStrength;
			case SoundEventType.Footstep:
				return FootstepStrength;
			case SoundEventType.Impact:
				return ImpactStrength;
			default:
				return GunshotStrength;
		}
	}

	public static WorldSoundEvent Create(
		Transform _source,
		Vector3 _position,
		SoundEventType _type)
	{
		return new WorldSoundEvent
		{
			Source = _source,
			Position = _position,
			Type = _type,
			Strength = DefaultStrength(_type),
			AudibleRangeMeters = DefaultRangeMeters(_type),
			Time = Time.time
		};
	}
	#endregion
}
