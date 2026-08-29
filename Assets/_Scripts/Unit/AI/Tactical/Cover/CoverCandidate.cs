using System;
using UnityEngine;

/// <summary>
/// Grid cell for shared cover queries. Not a designer CoverPoint.
/// </summary>
public readonly struct CoverRegionId : IEquatable<CoverRegionId>
{
	public readonly int X;
	public readonly int Z;

	public CoverRegionId(int _x, int _z)
	{
		X = _x;
		Z = _z;
	}

	public string LogLabel => "R" + X + "_" + Z;

	public bool Equals(CoverRegionId _other)
	{
		return X == _other.X && Z == _other.Z;
	}

	public override bool Equals(object _obj)
	{
		return _obj is CoverRegionId other && Equals(other);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			return (X * 397) ^ Z;
		}
	}

	public static bool operator ==(CoverRegionId _a, CoverRegionId _b)
	{
		return _a.Equals(_b);
	}

	public static bool operator !=(CoverRegionId _a, CoverRegionId _b)
	{
		return !_a.Equals(_b);
	}
}

/// <summary>
/// Shared geometry of one tactical position. No “best cover”. Individual AI scores this separately.
/// </summary>
public sealed class CoverCandidate
{
	public int CandidateId;
	public Vector3 Position;
	public Vector3 Normal;
	public CoverType CoverType;
	public bool StandingValid;
	public bool CrouchValid;
	public bool PartialValid;
	public bool CornerValid;
	public CoverProtectionProfile StandingProfile;
	public CoverProtectionProfile CrouchProfile;
	public bool NavMeshValid;
	public CoverRegionId RegionId;
	public int GeometryVersion;
	public CoverOccupancy Occupancy;
}
