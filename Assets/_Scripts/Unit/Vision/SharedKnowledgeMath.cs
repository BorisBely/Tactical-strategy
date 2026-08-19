/// <summary>
/// G7 shared-report decay / TTL. Independent from vision memory and sound.
/// </summary>
public static class SharedKnowledgeMath
{
	public const float DefaultHorizonSeconds = 8f;
	public const float DefaultShapeExponent = 1.5f;

	public static float Evaluate(
		float _elapsedSinceReport,
		float _initialConfidence = 1f,
		float _horizonSeconds = DefaultHorizonSeconds,
		float _shapeExponent = DefaultShapeExponent)
	{
		return MemoryDecayMath.Evaluate(
			_elapsedSinceReport,
			_initialConfidence,
			_horizonSeconds,
			_shapeExponent);
	}

	public static bool HasEvidence(float _confidence)
	{
		return _confidence > 0f;
	}
}
