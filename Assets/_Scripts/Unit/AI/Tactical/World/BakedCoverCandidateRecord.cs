using System;
using UnityEngine;

/// <summary>
/// Editor-baked #13 geometry. Occupancy is not stored here.
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
