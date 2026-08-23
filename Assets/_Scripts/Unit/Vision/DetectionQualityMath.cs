using UnityEngine;

/// <summary>
/// Pure detection math (G1/G1.1). Frozen AI handoff — do not retune for Search / tactics.
/// MovementFactor is always >= 1 (idle=1); movement only helps visibility.
/// Progress uses dual-threshold hysteresis (AcquireThreshold > LoseThreshold).
/// Distance uses one normalized curve: t = distance / resolvedVisionRange.
/// </summary>
public static class DetectionQualityMath
{
	#region Constants
	public const float DefaultNearMeters = 20f;
	public const float DefaultFarMeters = 150f;
	public const float DefaultFarFactor = 0.30f;
	public const float DefaultAcquisitionExponent = 3.8f;
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

	private static readonly float[] s_DistanceCurveT =
	{
		0.00f, 0.10f, 0.25f, 0.40f, 0.55f, 0.70f, 0.82f, 0.90f, 0.96f, 1.00f
	};

	private static readonly float[] s_DistanceCurveFactor =
	{
		1.00f, 1.00f, 0.98f, 0.92f, 0.82f, 0.68f, 0.50f, 0.38f, 0.32f, 0.30f
	};
	#endregion

	#region Distance
	/// <summary>Normalized distance curve. t &gt; 1 clamps to <see cref="DefaultFarFactor"/>.</summary>
	public static float EvaluateDistanceCurve(float _normalizedDistance)
	{
		if (_normalizedDistance <= 0f)
			return 1f;
		if (_normalizedDistance >= 1f)
			return DefaultFarFactor;

		for (int i = 0; i < s_DistanceCurveT.Length - 1; i++)
		{
			float t0 = s_DistanceCurveT[i];
			float t1 = s_DistanceCurveT[i + 1];
			if (_normalizedDistance <= t1 + 1e-6f)
			{
				float u = Mathf.InverseLerp(t0, t1, _normalizedDistance);
				return Mathf.Lerp(s_DistanceCurveFactor[i], s_DistanceCurveFactor[i + 1], u);
			}
		}

		return DefaultFarFactor;
	}

	/// <summary>Distance factor from meters and current resolved vision range (eye 150 or optic up to 300).</summary>
	public static float DistanceFactor(
		float _distanceMeters,
		float _resolvedVisionRange = DefaultFarMeters)
	{
		float range = Mathf.Max(0.5f, _resolvedVisionRange);
		float t = Mathf.Max(0f, _distanceMeters) / range;
		return EvaluateDistanceCurve(t);
	}
	#endregion

	#region FOV / Movement / Q
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

	/// <summary>Q = D × F × E × M. Attention is not a factor.</summary>
	public static float VisibilityQuality(
		float _distanceFactor,
		float _fovFactor,
		float _exposureFactor,
		float _movementFactor)
	{
		float movement = Mathf.Max(1f, _movementFactor);
		return Mathf.Clamp01(_distanceFactor * _fovFactor * _exposureFactor * movement);
	}
	#endregion

	#region Progress
	/// <summary>
	/// Monotonic acquire speed from Q. Exponent 1 = legacy <c>Q</c>.
	/// Production 3.8: <c>Q / (1 + (exponent-1)*(1-Q))</c> — fast near, slow at the live edge.
	/// </summary>
	public static float AcquisitionFactor(
		float _quality,
		float _exponent = DefaultAcquisitionExponent)
	{
		float q = Mathf.Clamp01(_quality);
		float exponent = Mathf.Max(1f, _exponent);
		if (exponent <= 1.0001f)
			return q;
		float k = exponent - 1f;
		return q / (1f + k * (1f - q));
	}

	/// <summary>
	/// Continuous-time Detected estimate. Negative if Q cannot grow (Q ≤ acquire).
	/// Attention is rate-only; default 1 matches frozen Q-time anchors.
	/// </summary>
	public static float EstimateDetectTimeSeconds(
		float _quality,
		float _acquireTimeSeconds = DefaultAcquireTime,
		float _acquireThreshold = DefaultAcquireThreshold,
		float _exponent = DefaultAcquisitionExponent,
		float _attentionMultiplier = 1f)
	{
		if (_quality <= _acquireThreshold)
			return -1f;
		float factor = AcquisitionFactor(_quality, _exponent);
		float att = AttentionMath.ClampMultiplier(_attentionMultiplier);
		if (factor <= 1e-6f)
			return -1f;
		return Mathf.Max(0.05f, _acquireTimeSeconds) / (factor * att);
	}

	/// <summary>
	/// Dual-threshold hysteresis:
	/// Q &gt; acquire → grow; lose &lt; Q ≤ acquire → hold; Q ≤ lose → decay.
	/// Hold / loss branches are unchanged. Grow uses <see cref="AcquisitionFactor"/> × Attention.
	/// Attention is not a Q factor and cannot grow when Q ≤ acquire.
	/// </summary>
	public static float IntegrateProgress(
		float _progress,
		float _quality,
		float _dt,
		float _acquireTimeSeconds = DefaultAcquireTime,
		float _lossTimeSeconds = DefaultLossTime,
		float _acquireThreshold = DefaultAcquireThreshold,
		float _loseThreshold = DefaultLoseThreshold,
		float _exponent = DefaultAcquisitionExponent,
		float _attentionMultiplier = 1f)
	{
		if (_dt <= 0f)
			return Mathf.Clamp01(_progress);

		float acquire = Mathf.Clamp01(_acquireThreshold);
		float lose = Mathf.Clamp(_loseThreshold, 0f, acquire);

		float acquireRate = 1f / Mathf.Max(0.05f, _acquireTimeSeconds);
		float lossRate = 1f / Mathf.Max(0.1f, _lossTimeSeconds);

		if (_quality > acquire)
		{
			float factor = AcquisitionFactor(_quality, _exponent);
			float att = AttentionMath.ClampMultiplier(_attentionMultiplier);
			return Mathf.Clamp01(_progress + factor * att * acquireRate * _dt);
		}

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
	#endregion

	#region Private Methods
	private static float SmoothStep01(float _t)
	{
		return _t * _t * (3f - 2f * _t);
	}
	#endregion
}
