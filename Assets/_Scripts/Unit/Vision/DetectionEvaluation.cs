/// <summary>
/// Snapshot of the current detection quality evaluation (not long-term AI knowledge).
/// Lives on <see cref="PerceivedContact.CurrentEvaluation"/> as frame evidence, not memory.
/// </summary>
public struct DetectionEvaluation
{
	public float VisibilityQuality;
	public float DistanceFactor;
	public float FovFactor;
	public float ExposureFactor;
	public float MovementFactor;

	public static DetectionEvaluation ClearedIdleMovement()
	{
		return new DetectionEvaluation
		{
			VisibilityQuality = 0f,
			DistanceFactor = 0f,
			FovFactor = 0f,
			ExposureFactor = 0f,
			MovementFactor = 1f
		};
	}
}
