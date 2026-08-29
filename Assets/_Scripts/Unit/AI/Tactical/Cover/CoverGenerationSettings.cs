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
	/// <summary>#13.2B candidate bake only. Zone Boundary ignores this 3 m cap.</summary>
	public float MinEdgeSurfaceLengthMeters = 3f;
	public float EdgeEndProximityMeters = 1.15f;
	public float EdgeInsetMeters = 0.45f;
	public float MinOpeningWidthMeters = 0.7f;
	public float MaxOpeningWidthMeters = 3.5f;
	public float MaxOpeningPlaneOffsetMeters = 0.5f;
	public float MaxOpeningWallThicknessMeters = 1.25f;
	public float OpeningNormalAlignDot = 0.85f;
	public float MinOpeningSurfaceLengthMeters = 2f;
	public float MergePlaneOffsetMeters = 0.5f;
	public float MergeNormalAlignDot = 0.85f;
	public float MergeSeamGapMeters = 0.45f;
	public float MergeBlockedSeamMeters = 1.55f;
	public float MinCornerSurfaceLengthMeters = 1f;
	public float MaxCornerVertexSeparationMeters = 0.6f;
	public float CornerNormalMaxAlignDot = 0.75f;
	public float MinProtectedCornerArmLengthMeters = 1.2f;
	public float MinProtectedCornerHeightMeters = 0.8f;
	public float MinProtectedCornerFacingDot = 0.2f;
	public float ProtectedCornerProbeDistanceMeters = 0.95f;
	public float CornerPocketMinRadiusMeters = 0.3f;
	public float CornerPocketMaxRadiusMeters = 0.9f;
	public int CornerFanRays = 5;
	public float CornerFanHalfAngleDegrees = 40f;
	public float CornerFrontClearMeters = 3.5f;
	public int MinCornerOpenFanRays = 3;
	public float MinZoneWidthMeters = 0.8f;
	public float ZoneDepthMeters = 0.65f;
	public float ZoneHeightSplitMeters = 0.55f;
	public float ZoneDedupRadiusMeters = 1.1f;
	public float MaxWallEndThicknessMeters = 1.5f;
	public float MaxSmallObstacleMeters = 2.5f;
	public float MinSmallObstacleMeters = 0.8f;
	public float MinSmallObstacleHeightMeters = 0.7f;
	public float MaxSmallObstacleHeightMeters = 2f;
	public int ZoneWalkSamples = 5;
	#endregion
}
