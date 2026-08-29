using System;
using UnityEngine;

/// <summary>
/// #13.6 reservation record. Not score. Not geometry. Not squad.
/// </summary>
public struct CoverReservation
{
	public CoverRegionId Region;
	public int CandidateId;
	public int UnitId;
	public float CreatedAt;
	public float ExpiresAt;
	public int Version;
	public int GeometryVersion;
	public CoverOccupancy State;
}

/// <summary>
/// Why a reservation changed. Rank / squad are not reasons.
/// </summary>
public enum CoverReservationReason
{
	None = 0,
	Reserved = 1,
	Occupied = 2,
	Released = 3,
	RejectedOccupied = 4,
	RejectedReserved = 5,
	Expired = 6,
	Death = 7,
	CommandChanged = 8,
	GeometryInvalid = 9,
	Idempotent = 10,
	Unconscious = 11
}

/// <summary>
/// Log / API result kind for <see cref="UnitActionLog.PositionReservation"/>.
/// </summary>
public enum CoverReservationResultKind
{
	None = 0,
	Reserved = 1,
	Rejected = 2,
	Released = 3,
	Occupied = 4
}

/// <summary>
/// Outcome of TryReserve / Release / Confirm. Success is atomic for TryReserve.
/// </summary>
public struct CoverReserveOutcome
{
	public bool Success;
	public CoverReservationResultKind Result;
	public CoverReservationReason Reason;
	public int OwnerUnitId;
	public CoverOccupancy State;
	public CoverReservation Reservation;
}
