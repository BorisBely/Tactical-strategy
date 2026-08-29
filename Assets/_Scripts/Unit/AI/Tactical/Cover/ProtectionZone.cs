using UnityEngine;

/// <summary>
/// Geometric protection record. Surface/Boundary/Obstacle — not a stance point.
/// ThreatDirection is not stored. #13.2C
/// </summary>
public sealed class ProtectionZone
{
	public int ZoneId;
	public ProtectionZoneType GeometryType;
	public Vector3 Center;
	public Vector3 Axis;
	public float Width;
	public float Depth;
	public float ProtectionHeight;
	public ProtectionHeightProfile Protection;
	public Vector3 SurfaceNormal;
	public ProtectionCapabilities Capabilities;
	public CoverRegionId RegionId;
	public int GeometryVersion;
	public bool NavMeshValid;
	public Vector3 OpeningCenter;
	public Vector3 OpeningAxis;
	public float OpeningWidth;
	public float LeftOffset;
	public float RightOffset;
	public Vector3 WindowCenter;
	public Vector3 WindowAxis;
	public float WindowWidth;
	public bool HasFrame;
	public bool HasTransparentPane;
	public Vector3 CornerFacing;
	public Vector3 CornerNormalA;
	public Vector3 CornerNormalB;
	public Vector3 CornerDirectionA;
	public Vector3 CornerDirectionB;
	public Vector3 CornerVertex;
	public float CornerMinRadius;
	public float CornerMaxRadius;
	public float CornerHalfAngleDegrees;
	public CoverCornerOrientation CornerOrientation;
	public Vector3 EdgeDirection;
	public ProtectionEdgeKind EdgeKind;
	public Vector3 ObstacleExtents;
}
