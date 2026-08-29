using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// #13.6 runtime occupancy. Not geometry. Not score. Not Group / rank / formation.
/// One occupant per (region, CandidateId). TryReserve is a single operation.
/// </summary>
public sealed class CoverOccupancyBoard
{
	#region Nested
	private readonly struct SlotKey : System.IEquatable<SlotKey>
	{
		public readonly int X;
		public readonly int Z;
		public readonly int CandidateId;

		public SlotKey(CoverRegionId _region, int _candidateId)
		{
			X = _region.X;
			Z = _region.Z;
			CandidateId = _candidateId;
		}

		public CoverRegionId Region => new CoverRegionId(X, Z);

		public bool Equals(SlotKey _other)
		{
			return X == _other.X && Z == _other.Z && CandidateId == _other.CandidateId;
		}

		public override bool Equals(object _obj)
		{
			return _obj is SlotKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return (X * 397) ^ (Z * 17) ^ CandidateId;
			}
		}
	}

	private sealed class Slot
	{
		public CoverReservation Reservation;
	}
	#endregion

	#region Constants
	public const float DefaultReservationTtlSeconds = 8f;
	#endregion

	#region Private Fields
	private readonly Dictionary<SlotKey, Slot> m_Slots = new Dictionary<SlotKey, Slot>(64);
	private readonly Dictionary<int, SlotKey> m_ByUnit = new Dictionary<int, SlotKey>(64);
	private readonly List<SlotKey> m_ExpireScratch = new List<SlotKey>(16);
	private int m_OccupancyVersion;
	private int m_GeometryVersion = 1;
	private float m_ReservationTtlSeconds = DefaultReservationTtlSeconds;
	private float m_OccupancyRadiusMeters = CoverScoreMath.ArrivalSnapMeters;
	#endregion

	#region Public Properties
	public int OccupancyVersion => m_OccupancyVersion;
	public int GeometryVersion => m_GeometryVersion;
	public int SlotCount => m_Slots.Count;
	public float ReservationTtlSeconds
	{
		get => m_ReservationTtlSeconds;
		set => m_ReservationTtlSeconds = Mathf.Max(0.05f, value);
	}

	public float OccupancyRadiusMeters
	{
		get => m_OccupancyRadiusMeters;
		set => m_OccupancyRadiusMeters = Mathf.Max(0.05f, value);
	}
	#endregion

	#region Public Methods
	public void Tick(float _now)
	{
		ExpireDue(_now);
	}

	/// <summary>
	/// Refresh Reserved TTL while the unit is still approaching. Does not log. Occupied has no TTL.
	/// </summary>
	public CoverReserveOutcome Heartbeat(
		CoverCandidate _candidate,
		int _unitId,
		float _now)
	{
		if (_candidate == null)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);
		return Heartbeat(_candidate.RegionId, _candidate.CandidateId, _unitId, _now);
	}

	public CoverReserveOutcome Heartbeat(
		CoverRegionId _region,
		int _candidateId,
		int _unitId,
		float _now)
	{
		if (_unitId == 0 || _candidateId == 0)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);

		SlotKey key = new SlotKey(_region, _candidateId);
		if (!m_Slots.TryGetValue(key, out Slot slot) || slot == null)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);
		if (slot.Reservation.UnitId != _unitId)
			return Reject(
				slot.Reservation.State == CoverOccupancy.Occupied
					? CoverReservationReason.RejectedOccupied
					: CoverReservationReason.RejectedReserved,
				slot.Reservation.UnitId,
				slot.Reservation.State,
				slot.Reservation);

		CoverReservation reservation = slot.Reservation;
		if (reservation.State == CoverOccupancy.Reserved)
		{
			reservation.ExpiresAt = _now + m_ReservationTtlSeconds;
			slot.Reservation = reservation;
		}

		return Ok(CoverReservationResultKind.Reserved, CoverReservationReason.Idempotent, reservation);
	}

	public void NotifyGeometryVersion(int _geometryVersion, float _now)
	{
		if (_geometryVersion == m_GeometryVersion)
			return;
		m_GeometryVersion = _geometryVersion;
		ReleaseAll(CoverReservationReason.GeometryInvalid, _now, null);
	}

	public bool IsAvailable(CoverCandidate _candidate, float _now)
	{
		return GetState(_candidate, _now) == CoverOccupancy.Available;
	}

	public bool IsAvailable(CoverRegionId _region, int _candidateId, float _now)
	{
		return GetState(_region, _candidateId, _now) == CoverOccupancy.Available;
	}

	public bool IsUsable(CoverCandidate _candidate, int _unitId, float _now)
	{
		if (_candidate == null)
			return false;
		return IsUsable(_candidate.RegionId, _candidate.CandidateId, _unitId, _now);
	}

	public bool IsUsable(CoverRegionId _region, int _candidateId, int _unitId, float _now)
	{
		if (_unitId == 0)
			return true;
		CoverReservation reservation;
		if (!TryGetReservation(_region, _candidateId, _now, out reservation))
			return true;
		return reservation.UnitId == _unitId;
	}

	public CoverOccupancy GetState(CoverCandidate _candidate, float _now)
	{
		if (_candidate == null)
			return CoverOccupancy.Available;
		return GetState(_candidate.RegionId, _candidate.CandidateId, _now);
	}

	public CoverOccupancy GetState(CoverRegionId _region, int _candidateId, float _now)
	{
		CoverReservation reservation;
		if (!TryGetReservation(_region, _candidateId, _now, out reservation))
			return CoverOccupancy.Available;
		return reservation.State;
	}

	public bool TryGetReservation(
		CoverCandidate _candidate,
		float _now,
		out CoverReservation _reservation)
	{
		_reservation = default;
		if (_candidate == null)
			return false;
		return TryGetReservation(_candidate.RegionId, _candidate.CandidateId, _now, out _reservation);
	}

	public bool TryGetReservation(
		CoverRegionId _region,
		int _candidateId,
		float _now,
		out CoverReservation _reservation)
	{
		ExpireDue(_now);
		SlotKey key = new SlotKey(_region, _candidateId);
		if (!m_Slots.TryGetValue(key, out Slot slot) || slot == null)
		{
			_reservation = default;
			return false;
		}

		_reservation = slot.Reservation;
		return true;
	}

	public bool TryGetHeld(int _unitId, float _now, out CoverReservation _reservation)
	{
		ExpireDue(_now);
		_reservation = default;
		if (_unitId == 0 || !m_ByUnit.TryGetValue(_unitId, out SlotKey key))
			return false;
		if (!m_Slots.TryGetValue(key, out Slot slot) || slot == null)
			return false;
		_reservation = slot.Reservation;
		return _reservation.CandidateId != 0;
	}

	public bool IsWithinOccupancyRadius(Vector3 _a, Vector3 _b)
	{
		return CoverSpatialMath.PlanarDistanceSqr(_a, _b) <=
		       m_OccupancyRadiusMeters * m_OccupancyRadiusMeters;
	}

	public CoverReserveOutcome TryReserve(
		CoverCandidate _candidate,
		int _unitId,
		float _now,
		Component _logActor = null)
	{
		if (_candidate == null)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);
		CoverReserveOutcome outcome = TryReserve(
			_candidate.RegionId, _candidate.CandidateId, _unitId, _now, _logActor);
		if (outcome.Success && outcome.Reason == CoverReservationReason.Reserved)
			CoverDiagnosticLog.Ref(_logActor, _candidate.CandidateId, _candidate, "Reserve");
		return outcome;
	}

	public CoverReserveOutcome TryReserve(
		CoverRegionId _region,
		int _candidateId,
		int _unitId,
		float _now,
		Component _logActor = null)
	{
		ExpireDue(_now);
		if (_unitId == 0 || _candidateId == 0)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);

		SlotKey key = new SlotKey(_region, _candidateId);
		if (m_Slots.TryGetValue(key, out Slot existing) && existing != null)
		{
			if (existing.Reservation.UnitId == _unitId)
			{
				CoverReservation held = existing.Reservation;
				if (held.State == CoverOccupancy.Reserved)
				{
					held.ExpiresAt = _now + m_ReservationTtlSeconds;
					existing.Reservation = held;
				}

				CoverReserveOutcome same = Ok(
					CoverReservationResultKind.Reserved,
					CoverReservationReason.Idempotent,
					held);
				return same;
			}

			CoverReservationReason reason = existing.Reservation.State == CoverOccupancy.Occupied
				? CoverReservationReason.RejectedOccupied
				: CoverReservationReason.RejectedReserved;
			CoverReserveOutcome rejected = Reject(
				reason,
				existing.Reservation.UnitId,
				existing.Reservation.State,
				existing.Reservation);
			Log(_logActor, _unitId, _candidateId, rejected);
			return rejected;
		}

		if (m_ByUnit.TryGetValue(_unitId, out SlotKey previous) && !previous.Equals(key))
			ReleaseSlot(previous, CoverReservationReason.Released, _now, _logActor);

		var reservation = new CoverReservation
		{
			Region = _region,
			CandidateId = _candidateId,
			UnitId = _unitId,
			CreatedAt = _now,
			ExpiresAt = _now + m_ReservationTtlSeconds,
			Version = m_OccupancyVersion + 1,
			GeometryVersion = m_GeometryVersion,
			State = CoverOccupancy.Reserved
		};
		m_Slots[key] = new Slot { Reservation = reservation };
		m_ByUnit[_unitId] = key;
		BumpVersion();
		CoverReserveOutcome outcome = Ok(
			CoverReservationResultKind.Reserved,
			CoverReservationReason.Reserved,
			reservation);
		Log(_logActor, _unitId, _candidateId, outcome);
		CoverSlotLog.Write(
			_logActor,
			_unitId,
			_candidateId,
			CoverSlotPhase.Reserved,
			CoverReservationReason.Reserved);
		return outcome;
	}

	public CoverReserveOutcome ConfirmOccupied(
		CoverCandidate _candidate,
		int _unitId,
		float _now,
		Component _logActor = null)
	{
		if (_candidate == null)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);
		CoverReserveOutcome outcome = ConfirmOccupied(
			_candidate.RegionId, _candidate.CandidateId, _unitId, _now, _logActor);
		if (outcome.Success && outcome.Reservation.State == CoverOccupancy.Occupied)
			CoverDiagnosticLog.Ref(_logActor, _candidate.CandidateId, _candidate, "ConfirmOccupied");
		return outcome;
	}

	public CoverReserveOutcome ConfirmOccupied(
		CoverRegionId _region,
		int _candidateId,
		int _unitId,
		float _now,
		Component _logActor = null)
	{
		ExpireDue(_now);
		SlotKey key = new SlotKey(_region, _candidateId);
		if (!m_Slots.TryGetValue(key, out Slot slot) || slot == null || slot.Reservation.UnitId != _unitId)
		{
			CoverReserveOutcome rejected = Reject(
				CoverReservationReason.None,
				slot != null ? slot.Reservation.UnitId : 0,
				slot != null ? slot.Reservation.State : CoverOccupancy.Available,
				slot != null ? slot.Reservation : default);
			Log(_logActor, _unitId, _candidateId, rejected);
			return rejected;
		}

		CoverReservation reservation = slot.Reservation;
		bool becameOccupied = reservation.State != CoverOccupancy.Occupied;
		if (becameOccupied)
		{
			reservation.State = CoverOccupancy.Occupied;
			reservation.ExpiresAt = float.PositiveInfinity;
			reservation.Version = m_OccupancyVersion + 1;
			slot.Reservation = reservation;
			BumpVersion();
		}

		CoverReserveOutcome outcome = Ok(
			CoverReservationResultKind.Occupied,
			CoverReservationReason.Occupied,
			reservation);
		Log(_logActor, _unitId, _candidateId, outcome);
		if (becameOccupied)
		{
			CoverSlotLog.Write(
				_logActor,
				_unitId,
				_candidateId,
				CoverSlotPhase.Occupied,
				CoverReservationReason.Occupied);
		}

		return outcome;
	}

	public CoverReserveOutcome Release(
		CoverCandidate _candidate,
		int _unitId,
		float _now,
		CoverReservationReason _reason = CoverReservationReason.Released,
		Component _logActor = null)
	{
		if (_candidate == null)
			return Reject(CoverReservationReason.None, 0, CoverOccupancy.Available, default);
		return Release(_candidate.RegionId, _candidate.CandidateId, _unitId, _now, _reason, _logActor);
	}

	public CoverReserveOutcome Release(
		CoverRegionId _region,
		int _candidateId,
		int _unitId,
		float _now,
		CoverReservationReason _reason = CoverReservationReason.Released,
		Component _logActor = null)
	{
		ExpireDue(_now);
		SlotKey key = new SlotKey(_region, _candidateId);
		if (!m_Slots.TryGetValue(key, out Slot slot) || slot == null)
			return Ok(CoverReservationResultKind.Released, _reason, default);
		if (_unitId != 0 && slot.Reservation.UnitId != _unitId)
			return Reject(
				CoverReservationReason.RejectedOccupied,
				slot.Reservation.UnitId,
				slot.Reservation.State,
				slot.Reservation);
		return ReleaseSlot(key, _reason, _now, _logActor);
	}

	public CoverReserveOutcome ReleaseOccupied(
		CoverCandidate _candidate,
		int _unitId,
		float _now,
		Component _logActor = null)
	{
		return Release(_candidate, _unitId, _now, CoverReservationReason.Released, _logActor);
	}

	public void ReleaseUnit(
		int _unitId,
		float _now,
		CoverReservationReason _reason,
		Component _logActor = null)
	{
		ExpireDue(_now);
		if (_unitId == 0 || !m_ByUnit.TryGetValue(_unitId, out SlotKey key))
			return;
		ReleaseSlot(key, _reason, _now, _logActor);
	}

	public int CountHeld()
	{
		return m_Slots.Count;
	}
	#endregion

	#region Private Methods
	private void ExpireDue(float _now)
	{
		m_ExpireScratch.Clear();
		foreach (KeyValuePair<SlotKey, Slot> pair in m_Slots)
		{
			CoverReservation reservation = pair.Value.Reservation;
			if (reservation.State != CoverOccupancy.Reserved)
				continue;
			if (_now < reservation.ExpiresAt)
				continue;
			m_ExpireScratch.Add(pair.Key);
		}

		for (int i = 0; i < m_ExpireScratch.Count; i++)
			ReleaseSlot(m_ExpireScratch[i], CoverReservationReason.Expired, _now, null);
	}

	private void ReleaseAll(CoverReservationReason _reason, float _now, Component _logActor)
	{
		m_ExpireScratch.Clear();
		foreach (SlotKey key in m_Slots.Keys)
			m_ExpireScratch.Add(key);
		for (int i = 0; i < m_ExpireScratch.Count; i++)
			ReleaseSlot(m_ExpireScratch[i], _reason, _now, _logActor);
	}

	private CoverReserveOutcome ReleaseSlot(
		SlotKey _key,
		CoverReservationReason _reason,
		float _now,
		Component _logActor)
	{
		if (!m_Slots.TryGetValue(_key, out Slot slot) || slot == null)
			return Ok(CoverReservationResultKind.Released, _reason, default);

		CoverReservation reservation = slot.Reservation;
		m_Slots.Remove(_key);
		if (m_ByUnit.TryGetValue(reservation.UnitId, out SlotKey owned) && owned.Equals(_key))
			m_ByUnit.Remove(reservation.UnitId);
		BumpVersion();
		CoverReserveOutcome outcome = new CoverReserveOutcome
		{
			Success = true,
			Result = CoverReservationResultKind.Released,
			Reason = _reason,
			OwnerUnitId = 0,
			State = CoverOccupancy.Available,
			Reservation = reservation
		};
		Log(_logActor, reservation.UnitId, reservation.CandidateId, outcome);
		CoverSlotLog.Write(
			_logActor,
			reservation.UnitId,
			reservation.CandidateId,
			CoverSlotPhase.Released,
			_reason);
		CoverDiagnosticLog.HeartbeatRelease(_logActor, reservation.CandidateId, _reason);
		return outcome;
	}

	private void BumpVersion()
	{
		m_OccupancyVersion++;
	}

	private static CoverReserveOutcome Ok(
		CoverReservationResultKind _result,
		CoverReservationReason _reason,
		CoverReservation _reservation)
	{
		return new CoverReserveOutcome
		{
			Success = true,
			Result = _result,
			Reason = _reason,
			OwnerUnitId = _reservation.UnitId,
			State = _reservation.State,
			Reservation = _reservation
		};
	}

	private static CoverReserveOutcome Reject(
		CoverReservationReason _reason,
		int _ownerUnitId,
		CoverOccupancy _state,
		CoverReservation _reservation)
	{
		return new CoverReserveOutcome
		{
			Success = false,
			Result = CoverReservationResultKind.Rejected,
			Reason = _reason,
			OwnerUnitId = _ownerUnitId,
			State = _state,
			Reservation = _reservation
		};
	}

	private static void Log(Component _actor, int _unitId, int _candidateId, in CoverReserveOutcome _outcome)
	{
		if (!UnitActionLog.Enabled)
			return;
		string payload =
			"unit=" + _unitId +
			" candidate=C" + _candidateId +
			" result=" + _outcome.Result +
			" reason=" + _outcome.Reason +
			" owner=" + _outcome.OwnerUnitId +
			" state=" + _outcome.State;
		if (_actor != null)
			UnitActionLog.Write(_actor, UnitActionLog.PositionReservation, payload);
		UnitActionLog.Timeline(
			UnitActionLog.PositionReservation,
			(_actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty) + payload);
	}
	#endregion
}
