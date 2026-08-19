/// <summary>
/// G7 sound-channel decay. Independent from LastSeenConfidence / DetectionProgress.
/// Reuses the G4 parametric curve with a shorter default horizon.
/// </summary>
public static class SoundKnowledgeMath
{
	public const float DefaultHorizonSeconds = 3f;
	public const float DefaultShapeExponent = 1.5f;

	public static float Evaluate(
		float _elapsedSinceEvent,
		float _initialConfidence = 1f,
		float _horizonSeconds = DefaultHorizonSeconds,
		float _shapeExponent = DefaultShapeExponent)
	{
		return MemoryDecayMath.Evaluate(
			_elapsedSinceEvent,
			_initialConfidence,
			_horizonSeconds,
			_shapeExponent);
	}

	public static bool HasEvidence(float _confidence)
	{
		return _confidence > 0f;
	}
}
