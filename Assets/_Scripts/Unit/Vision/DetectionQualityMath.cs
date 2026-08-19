using UnityEngine;

/// <summary>
/// Pure detection math (G1/G1.1). Frozen AI handoff — do not retune for Search / tactics.
/// MovementFactor is always >= 1 (idle=1); movement only helps visibility.
/// Progress uses dual-threshold hysteresis (AcquireThreshold > LoseThreshold).
/// </summary>
public static class DetectionQualityMath
{
	public const float DefaultNearMeters = 20f;
	public const float DefaultFarMeters = 500f;
	public const float DefaultFarFactor = 0.08f;
	public const float DefaultFovHalfDegrees = 60f;
	public const float DefaultFovEdgeFactor = 0.15f;
	public const float DefaultWalkSpeed = 0.6f;
	public const float DefaultRunSpeed = 3.2f;
	public const float DefaultWalkMultiplier = 1.15f;
	public const float DefaultRunMultiplier = 1.35f;
	public const float DefaultMovementCap = 1.5f;
	public const float DefaultAcquireTime = 0.35f;
	public const float DefaultLossTime = 2.5f;
	public const float DefaultAcquireThreshold = 0.25f;
	public const float DefaultLoseThreshold = 0.20f;

	public static float DistanceFactor(
		float _distanceMeters,
		float _nearMeters = DefaultNearMeters,
		float _farMeters = DefaultFarMeters,
		float _farFactor = DefaultFarFactor)
	{
		float near = Mathf.Max(0.1f, _nearMeters);
		float far = Mathf.Max(near + 0.1f, _farMeters);
		if (_distanceMeters <= near)
			return 1f;
		if (_distanceMeters >= far)
			return _farFactor;

		float t = Mathf.InverseLerp(near, far, _distanceMeters);
		float shaped = SmoothStep01(t);
		return Mathf.Lerp(1f, _farFactor, shaped);
	}

	public static float FovFactor(
		float _fovOffsetDegrees,
		float _halfReferenceDegrees = DefaultFovHalfDegrees,
		float _edgeFactor = DefaultFovEdgeFactor)
	{
		float half = Mathf.Max(1f, _halfReferenceDegrees);
		float t = Mathf.Clamp01(Mathf.Abs(_fovOffsetDegrees) / half);
		float shaped = SmoothStep01(t);
		return Mathf.Lerp(1f, _edgeFactor, shaped);
	}

	/// <summary>Always >= 1. Idle = 1; walk/run are bonuses capped by _cap.</summary>
	public static float MovementFactor(
		float _speedMetersPerSecond,
		float _walkThreshold = DefaultWalkSpeed,
		float _runThreshold = DefaultRunSpeed,
		float _walkMultiplier = DefaultWalkMultiplier,
		float _runMultiplier = DefaultRunMultiplier,
		float _cap = DefaultMovementCap)
	{
		float walkMul = Mathf.Max(1f, _walkMultiplier);
		float runMul = Mathf.Max(1f, _runMultiplier);
		float cap = Mathf.Max(1f, _cap);

		float multiplier = 1f;
		if (_speedMetersPerSecond >= _runThreshold)
			multiplier = runMul;
		else if (_speedMetersPerSecond >= _walkThreshold)
			multiplier = walkMul;

		return Mathf.Min(cap, Mathf.Max(1f, multiplier));
	}

	public static float VisibilityQuality(
		float _distanceFactor,
		float _fovFactor,
		float _exposureFactor,
		float _movementFactor)
	{
		float movement = Mathf.Max(1f, _movementFactor);
		return Mathf.Clamp01(_distanceFactor * _fovFactor * _exposureFactor * movement);
	}

	/// <summary>
	/// Dual-threshold hysteresis:
	/// Q &gt; acquire → grow; lose &lt; Q ≤ acquire → hold; Q ≤ lose → decay.
	/// </summary>
	public static float IntegrateProgress(
		float _progress,
		float _quality,
		float _dt,
		float _acquireTimeSeconds = DefaultAcquireTime,
		float _lossTimeSeconds = DefaultLossTime,
		float _acquireThreshold = DefaultAcquireThreshold,
		float _loseThreshold = DefaultLoseThreshold)
	{
		if (_dt <= 0f)
			return Mathf.Clamp01(_progress);

		float acquire = Mathf.Clamp01(_acquireThreshold);
		float lose = Mathf.Clamp(_loseThreshold, 0f, acquire);

		float acquireRate = 1f / Mathf.Max(0.05f, _acquireTimeSeconds);
		float lossRate = 1f / Mathf.Max(0.1f, _lossTimeSeconds);

		if (_quality > acquire)
			return Mathf.Clamp01(_progress + _quality * acquireRate * _dt);

		if (_quality > lose)
			return Mathf.Clamp01(_progress);

		return Mathf.Clamp01(_progress - (1f - _quality) * lossRate * _dt);
	}

	public static DetectionState ResolveState(float _progress)
	{
		if (_progress <= 0f)
			return DetectionState.Undetected;
		if (_progress >= 1f)
			return DetectionState.Detected;
		return DetectionState.Detecting;
	}

	private static float SmoothStep01(float _t)
	{
		return _t * _t * (3f - 2f * _t);
	}
}
