using System;
using UnityEngine;

/// <summary>
/// Editor-baked #13.2C zone. Occupancy and DesiredPosition are not stored.
/// </summary>
[Serializable]
public struct BakedProtectionZoneRecord
{
	public int ZoneId;
	public ProtectionZoneType GeometryType;
	public Vector3 Center;
	public Vector3 Axis;
	public float Width;
	public float Depth;
	public float ProtectionHeight;
	public float StandingHead;
	public float StandingTorso;
	public float StandingPelvis;
	public float StandingLegs;
	public float CrouchHead;
	public float CrouchTorso;
	public float CrouchPelvis;
	public float CrouchLegs;
	public float RearProtection;
	public float SideProtection;
	public Vector3 SurfaceNormal;
	public ProtectionCapabilities Capabilities;
	public bool NavMeshValid;
	public int RegionX;
	public int RegionZ;
	public int GeometryVersion;
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

	public static BakedProtectionZoneRecord FromZone(ProtectionZone _zone)
	{
		if (_zone == null)
			return default;
		return new BakedProtectionZoneRecord
		{
			ZoneId = _zone.ZoneId,
			GeometryType = _zone.GeometryType,
			Center = _zone.Center,
			Axis = _zone.Axis,
			Width = _zone.Width,
			Depth = _zone.Depth,
			ProtectionHeight = _zone.ProtectionHeight,
			StandingHead = _zone.Protection.Standing.Head,
			StandingTorso = _zone.Protection.Standing.Torso,
			StandingPelvis = _zone.Protection.Standing.Pelvis,
			StandingLegs = _zone.Protection.Standing.Legs,
			CrouchHead = _zone.Protection.Crouch.Head,
			CrouchTorso = _zone.Protection.Crouch.Torso,
			CrouchPelvis = _zone.Protection.Crouch.Pelvis,
			CrouchLegs = _zone.Protection.Crouch.Legs,
			RearProtection = _zone.Protection.RearProtection,
			SideProtection = _zone.Protection.SideProtection,
			SurfaceNormal = _zone.SurfaceNormal,
			Capabilities = _zone.Capabilities,
			NavMeshValid = _zone.NavMeshValid,
			RegionX = _zone.RegionId.X,
			RegionZ = _zone.RegionId.Z,
			GeometryVersion = _zone.GeometryVersion,
			OpeningCenter = _zone.OpeningCenter,
			OpeningAxis = _zone.OpeningAxis,
			OpeningWidth = _zone.OpeningWidth,
			LeftOffset = _zone.LeftOffset,
			RightOffset = _zone.RightOffset,
			WindowCenter = _zone.WindowCenter,
			WindowAxis = _zone.WindowAxis,
			WindowWidth = _zone.WindowWidth,
			HasFrame = _zone.HasFrame,
			HasTransparentPane = _zone.HasTransparentPane,
			CornerFacing = _zone.CornerFacing,
			CornerNormalA = _zone.CornerNormalA,
			CornerNormalB = _zone.CornerNormalB,
			CornerDirectionA = _zone.CornerDirectionA,
			CornerDirectionB = _zone.CornerDirectionB,
			CornerVertex = _zone.CornerVertex,
			CornerMinRadius = _zone.CornerMinRadius,
			CornerMaxRadius = _zone.CornerMaxRadius,
			CornerHalfAngleDegrees = _zone.CornerHalfAngleDegrees,
			CornerOrientation = _zone.CornerOrientation,
			EdgeDirection = _zone.EdgeDirection,
			EdgeKind = _zone.EdgeKind,
			ObstacleExtents = _zone.ObstacleExtents
		};
	}

	public ProtectionZone ToZone()
	{
		return new ProtectionZone
		{
			ZoneId = ZoneId,
			GeometryType = GeometryType,
			Center = Center,
			Axis = Axis,
			Width = Width,
			Depth = Depth,
			ProtectionHeight = ProtectionHeight,
			Protection = new ProtectionHeightProfile
			{
				HeightMeters = ProtectionHeight,
				Standing = new CoverProtectionProfile
				{
					Head = StandingHead,
					Torso = StandingTorso,
					Pelvis = StandingPelvis,
					Legs = StandingLegs
				},
				Crouch = new CoverProtectionProfile
				{
					Head = CrouchHead,
					Torso = CrouchTorso,
					Pelvis = CrouchPelvis,
					Legs = CrouchLegs
				},
				RearProtection = RearProtection,
				SideProtection = SideProtection
			},
			SurfaceNormal = SurfaceNormal,
			Capabilities = Capabilities,
			NavMeshValid = NavMeshValid,
			RegionId = new CoverRegionId(RegionX, RegionZ),
			GeometryVersion = GeometryVersion,
			OpeningCenter = OpeningCenter,
			OpeningAxis = OpeningAxis,
			OpeningWidth = OpeningWidth,
			LeftOffset = LeftOffset,
			RightOffset = RightOffset,
			WindowCenter = WindowCenter,
			WindowAxis = WindowAxis,
			WindowWidth = WindowWidth,
			HasFrame = HasFrame,
			HasTransparentPane = HasTransparentPane,
			CornerFacing = CornerFacing,
			CornerNormalA = CornerNormalA,
			CornerNormalB = CornerNormalB,
			CornerDirectionA = CornerDirectionA,
			CornerDirectionB = CornerDirectionB,
			CornerVertex = CornerVertex,
			CornerMinRadius = CornerMinRadius,
			CornerMaxRadius = CornerMaxRadius,
			CornerHalfAngleDegrees = CornerHalfAngleDegrees,
			CornerOrientation = CornerOrientation,
			EdgeDirection = EdgeDirection,
			EdgeKind = EdgeKind,
			ObstacleExtents = ObstacleExtents
		};
	}
}
