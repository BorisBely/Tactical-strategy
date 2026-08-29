using System;
using UnityEngine;

/// <summary>
/// Editor-baked #13 geometry. Occupancy is not stored here.
/// #13.2B adds Edge / Opening / Window fields. Peek/exposure/facing are not baked.
/// </summary>
[Serializable]
public struct BakedCoverCandidateRecord
{
	public int CandidateId;
	public Vector3 Position;
	public Vector3 Normal;
	public CoverType CoverType;
	public bool StandingValid;
	public bool CrouchValid;
	public bool PartialValid;
	public bool CornerValid;
	public bool EdgeValid;
	public bool OpeningValid;
	public bool WindowValid;
	public Vector3 EdgeDirection;
	public Vector3 OpeningAxis;
	public Vector3 OpeningCenter;
	public Vector3 WindowCenter;
	public Vector3 WindowAxis;
	public float LeftOffset;
	public float RightOffset;
	public float OpeningWidth;
	public float WindowWidth;
	public bool HasFrame;
	public bool HasTransparentPane;
	public Vector3 CornerFacing;
	public Vector3 CornerNormalA;
	public Vector3 CornerNormalB;
	public Vector3 CornerVertex;
	public CoverCornerOrientation CornerOrientation;
	public CoverCapabilities Capabilities;
	public float StandingHead;
	public float StandingTorso;
	public float StandingPelvis;
	public float StandingLegs;
	public float CrouchHead;
	public float CrouchTorso;
	public float CrouchPelvis;
	public float CrouchLegs;
	public bool NavMeshValid;
	public int RegionX;
	public int RegionZ;
	public int GeometryVersion;

	public static BakedCoverCandidateRecord FromCandidate(CoverCandidate _candidate)
	{
		if (_candidate == null)
			return default;
		return new BakedCoverCandidateRecord
		{
			CandidateId = _candidate.CandidateId,
			Position = _candidate.Position,
			Normal = _candidate.Normal,
			CoverType = _candidate.CoverType,
			StandingValid = _candidate.StandingValid,
			CrouchValid = _candidate.CrouchValid,
			PartialValid = _candidate.PartialValid,
			CornerValid = _candidate.CornerValid,
			EdgeValid = _candidate.EdgeValid,
			OpeningValid = _candidate.OpeningValid,
			WindowValid = _candidate.WindowValid,
			EdgeDirection = _candidate.EdgeDirection,
			OpeningAxis = _candidate.OpeningAxis,
			OpeningCenter = _candidate.OpeningCenter,
			WindowCenter = _candidate.WindowCenter,
			WindowAxis = _candidate.WindowAxis,
			LeftOffset = _candidate.LeftOffset,
			RightOffset = _candidate.RightOffset,
			OpeningWidth = _candidate.OpeningWidth,
			WindowWidth = _candidate.WindowWidth,
			HasFrame = _candidate.HasFrame,
			HasTransparentPane = _candidate.HasTransparentPane,
			CornerFacing = _candidate.CornerFacing,
			CornerNormalA = _candidate.CornerNormalA,
			CornerNormalB = _candidate.CornerNormalB,
			CornerVertex = _candidate.CornerVertex,
			CornerOrientation = _candidate.CornerOrientation,
			Capabilities = _candidate.Capabilities,
			StandingHead = _candidate.StandingProfile.Head,
			StandingTorso = _candidate.StandingProfile.Torso,
			StandingPelvis = _candidate.StandingProfile.Pelvis,
			StandingLegs = _candidate.StandingProfile.Legs,
			CrouchHead = _candidate.CrouchProfile.Head,
			CrouchTorso = _candidate.CrouchProfile.Torso,
			CrouchPelvis = _candidate.CrouchProfile.Pelvis,
			CrouchLegs = _candidate.CrouchProfile.Legs,
			NavMeshValid = _candidate.NavMeshValid,
			RegionX = _candidate.RegionId.X,
			RegionZ = _candidate.RegionId.Z,
			GeometryVersion = _candidate.GeometryVersion
		};
	}

	public CoverCandidate ToCandidate()
	{
		return new CoverCandidate
		{
			CandidateId = CandidateId,
			Position = Position,
			Normal = Normal,
			CoverType = CoverType,
			StandingValid = StandingValid,
			CrouchValid = CrouchValid,
			PartialValid = PartialValid,
			CornerValid = CornerValid,
			EdgeValid = EdgeValid,
			OpeningValid = OpeningValid,
			WindowValid = WindowValid,
			EdgeDirection = EdgeDirection,
			OpeningAxis = OpeningAxis,
			OpeningCenter = OpeningCenter,
			WindowCenter = WindowCenter,
			WindowAxis = WindowAxis,
			LeftOffset = LeftOffset,
			RightOffset = RightOffset,
			OpeningWidth = OpeningWidth,
			WindowWidth = WindowWidth,
			HasFrame = HasFrame,
			HasTransparentPane = HasTransparentPane,
			CornerFacing = CornerFacing,
			CornerNormalA = CornerNormalA,
			CornerNormalB = CornerNormalB,
			CornerVertex = CornerVertex,
			CornerOrientation = CornerOrientation,
			Capabilities = Capabilities,
			StandingProfile = new CoverProtectionProfile
			{
				Head = StandingHead,
				Torso = StandingTorso,
				Pelvis = StandingPelvis,
				Legs = StandingLegs
			},
			CrouchProfile = new CoverProtectionProfile
			{
				Head = CrouchHead,
				Torso = CrouchTorso,
				Pelvis = CrouchPelvis,
				Legs = CrouchLegs
			},
			NavMeshValid = NavMeshValid,
			RegionId = new CoverRegionId(RegionX, RegionZ),
			GeometryVersion = GeometryVersion,
			Occupancy = CoverOccupancy.Available
		};
	}
}
