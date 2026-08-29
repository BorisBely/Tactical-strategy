using UnityEngine;

/// <summary>
/// Prototype sampling knobs for #13.1. Not a freeze. Not a tactical score.
/// </summary>
public sealed class CoverGenerationSettings
{
	#region Public Fields
	public float SampleSpacingMeters = 2f;
	public float StandOffMeters = 0.45f;
	public float DedupRadiusMeters = 0.75f;
	public float GeometryMarginMeters = 1.5f;
	public int MaxCoverCandidates = CoverSpatialMath.DefaultMaxCoverCandidates;
	public int MinSamplesPerSurface = 1;
	public bool ConfirmSurfaceWithPhysics;
	public LayerMask PhysicsMask = ~0;
	#endregion
}
