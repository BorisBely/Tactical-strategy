using UnityEngine;

/// <summary>
/// Explicit stance for #13.3 protection. Auto stance / lean is later.
/// </summary>
public enum CoverStance
{
	Standing = 0,
	Crouch = 1
}

/// <summary>
/// Minimal mission bridge for cover score. Not a new AI state.
/// </summary>
public enum CoverMissionIntent
{
	Hold = 0,
	Defense = 1,
	Attack = 2
}

/// <summary>
/// Weapon class interface for #13.3. Not #15 doctrine.
/// </summary>
public enum CoverWeaponClass
{
	Rifle = 0,
	Sniper = 1,
	Lmg = 2
}

/// <summary>
/// Rank nudge for #13.3. Not #15B behaviour.
/// </summary>
public enum CoverRankClass
{
	Recruit = 0,
	Soldier = 1,
	Veteran = 2
}

/// <summary>
/// Individual facts for scoring shared cover. Not stored on <see cref="CoverCandidate"/>.
/// </summary>
public struct CoverSituation
{
	public Vector3 UnitPosition;
	public CoverStance Stance;
	public CoverMissionIntent Mission;
	public CoverWeaponClass Weapon;
	public CoverRankClass Rank;
	public Vector3 TargetPosition;
	public bool HasTarget;
	public Vector3 SectorForward;
	public Vector3 HostileDirection;
	public Vector3 ThreatDirection;
	public bool HasThreatDirection;
	public ThreatDirectionSource ThreatSource;
	public ThreatDirectionState ThreatState;
	public float ThreatConfidence;
	public float ThreatUncertaintyDegrees;
	public bool ThreatRepositionAllowed;
	public int GeometryVersion;
	public CoverRegionId RegionId;
	public int UnitId;
	public int OccupancyVersion;
}
