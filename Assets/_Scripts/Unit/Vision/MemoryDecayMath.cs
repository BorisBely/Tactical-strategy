using UnityEngine;

/// <summary>
/// Pure G4 memory decay. Frozen AI handoff (5 / 30 / 1.5 / 0.25).
/// Trust in LastKnown after LOS loss. Not Search. Not orders.
/// Does not reference Unit / Transform / Vision / Combat / UnitTeam.
/// Independent from DetectionProgress and IdentityConfidence.
/// </summary>
public static class MemoryDecayMath
{
	/// <summary>Block B locked: 5 s fresh-loss window.</summary>
	public const float DefaultRecentlyLostSeconds = 5f;
	/// <summary>Block B locked: 30 s useful-memory horizon.</summary>
	public const float DefaultHorizonSeconds = 30f;
	public const float DefaultShapeExponent = 1.5f;
	public const float DefaultStaleThreshold = 0.25f;

	/// <summary>
	/// Parametric decay: remaining = (1 - elapsed/horizon)^shape, then scaled by initial.
	/// elapsed ≤ 0 → initial; elapsed ≥ horizon → 0.
	/// </summary>
	public static float Evaluate(
		float _elapsedSinceLastSeen,
		float _initialConfidence = 1f,
		float _horizonSeconds = DefaultHorizonSeconds,
		float _shapeExponent = DefaultShapeExponent)
	{
		float initial = Mathf.Clamp01(_initialConfidence);
		if (_elapsedSinceLastSeen <= 0f)
			return initial;

		float horizon = Mathf.Max(0.01f, _horizonSeconds);
		if (_elapsedSinceLastSeen >= horizon)
			return 0f;

		float t = Mathf.Clamp01(_elapsedSinceLastSeen / horizon);
		float remaining = 1f - t;
		float shape = Mathf.Max(0.01f, _shapeExponent);
		return Mathf.Clamp01(initial * Mathf.Pow(remaining, shape));
	}

	public static bool HasMemory(float _confidence)
	{
		return _confidence > 0f;
	}

	public static bool IsForgotten(float _confidence)
	{
		return _confidence <= 0f;
	}

	public static bool IsStale(float _confidence, float _staleThreshold = DefaultStaleThreshold)
	{
		float threshold = Mathf.Clamp01(_staleThreshold);
		return _confidence > 0f && _confidence <= threshold;
	}

	/// <summary>
	/// Inverse of <see cref="Evaluate"/> for initial=1: elapsed where remaining confidence equals <paramref name="_confidence"/>.
	/// Used by calibration / G4 waits. Forgotten (0) maps to horizon.
	/// </summary>
	public static float ElapsedSecondsForConfidence(
		float _confidence,
		float _horizonSeconds = DefaultHorizonSeconds,
		float _shapeExponent = DefaultShapeExponent)
	{
		float horizon = Mathf.Max(0.01f, _horizonSeconds);
		float conf = Mathf.Clamp01(_confidence);
		if (conf <= 0f)
			return horizon;
		if (conf >= 1f)
			return 0f;

		float shape = Mathf.Max(0.01f, _shapeExponent);
		return horizon * (1f - Mathf.Pow(conf, 1f / shape));
	}
}
