using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Очередь посадки/высадки по дверям: параллельно разные двери, одна дверь — по одному.
/// У двери — прогрессбар и реалистичное время перед mount/dismount.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleBoardController : MonoBehaviour
{
	#region Nested
	private enum JobKind : byte
	{
		Board,
		BoardWoundedFromCarry,
		Disembark
	}

	private struct Job
	{
		public JobKind Kind;
		public RtsUnitMember Unit;
		public RtsUnitMember Carrier;
		public VehicleSeatId SeatHint;
		public bool HasSeatHint;
		public VehicleBoardSide Side;
		public bool DisembarkIncludeDriver;
		public UnitClickToMove.MoveTier BoardMoveTier;
	}
	#endregion

	#region Constants
	private const int c_DoorCount = 4;
	/// <summary>Насколько близко к Approach_* нужно подойти, чтобы начать фазу у двери.</summary>
	private const float c_ApproachArriveDistance = 0.7f;
	private const float c_ApproachAlignSnapRadius = 1.25f;
	private const float c_DoorQueueSpacingMeters = 0.85f;
	private const float c_ApproachTimeoutSeconds = 45f;
	private const bool c_BoardDebugLogs = false;
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleSeatLayout m_Seats;
	[SerializeField] private VehicleDoorController m_Doors;
	#endregion

	#region Private Fields
	private readonly Queue<Job>[] m_DoorQueues = new Queue<Job>[c_DoorCount];
	private readonly Coroutine[] m_DoorWorkers = new Coroutine[c_DoorCount];
	private readonly HashSet<RtsUnitMember> m_ActiveBoardUnits = new HashSet<RtsUnitMember>();
	private readonly Dictionary<RtsUnitMember, Vector3> m_ExpectedApproachTarget =
		new Dictionary<RtsUnitMember, Vector3>(8);
	private readonly Dictionary<RtsUnitMember, UnitClickToMove.MoveTier> m_BoardMoveTiers =
		new Dictionary<RtsUnitMember, UnitClickToMove.MoveTier>(8);
	private bool m_IsBusy;
	#endregion

	#region Active Unit Helpers
	private void TrackActiveBoardUnit(RtsUnitMember _unit)
	{
		if (_unit == null)
			return;
		m_ActiveBoardUnits.Add(_unit);
		m_Vehicle?.SetIgnoreUnitColliders(_unit, true);
	}

	private void UntrackActiveBoardUnit(RtsUnitMember _unit)
	{
		if (_unit == null)
			return;
		if (m_ActiveBoardUnits.Remove(_unit))
			m_Vehicle?.SetIgnoreUnitColliders(_unit, false);
	}

	private void ClearActiveBoardUnits()
	{
		foreach (RtsUnitMember unit in m_ActiveBoardUnits)
			m_Vehicle?.SetIgnoreUnitColliders(unit, false);
		m_ActiveBoardUnits.Clear();
	}
	#endregion

	#region Public Properties
	public bool IsBusy => m_IsBusy;

	/// <summary>Посадка/ожидание у двери — корпус держим (vel=0 + soft park), RB остаётся dynamic.</summary>
	public bool ShouldKeepChassisParked => m_IsBusy || m_ActiveBoardUnits.Count > 0;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		for (int i = 0; i < c_DoorCount; i++)
			m_DoorQueues[i] = new Queue<Job>(4);
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle, VehicleSeatLayout _seats, VehicleDoorController _doors)
	{
		m_Vehicle = _vehicle;
		m_Seats = _seats;
		m_Doors = _doors;
	}

	/// <summary>
	/// Called from unit move orders. Cancels boarding if the destination is not the
	/// board approach wait point issued by this controller.
	/// </summary>
	public static void NotifyUnitMoveOrderIssued(RtsUnitMember _unit, Vector3 _destination)
	{
		if (_unit == null)
			return;

		IReadOnlyList<VehicleController> vehicles = VehicleController.Instances;
		for (int i = 0; i < vehicles.Count; i++)
		{
			VehicleBoardController board = vehicles[i] != null ? vehicles[i].Board : null;
			board?.OnExternalOrPlayerMoveOrder(_unit, _destination);
		}
	}

	private void OnExternalOrPlayerMoveOrder(RtsUnitMember _unit, Vector3 _destination)
	{
		if (_unit == null || !m_ActiveBoardUnits.Contains(_unit))
			return;

		if (m_ExpectedApproachTarget.TryGetValue(_unit, out Vector3 expected) &&
		    HorizontalDistance(_destination, expected) <= 2.5f)
		{
			return;
		}

		CancelJobsForUnit(_unit, "unit-new-order");
	}

	public void CancelAllJobsAndCloseDoors(string _reason)
	{
		LogBoard($"CANCEL ALL — {_reason}");
		for (int i = 0; i < c_DoorCount; i++)
		{
			if (m_DoorWorkers[i] != null)
			{
				StopCoroutine(m_DoorWorkers[i]);
				m_DoorWorkers[i] = null;
			}

			m_DoorQueues[i].Clear();
		}

		ClearActiveBoardUnits();
		m_ExpectedApproachTarget.Clear();
		m_BoardMoveTiers.Clear();
		m_Seats?.ClearAllReservations();
		m_IsBusy = false;
		m_Doors?.CloseAllDoorsForcedImmediate();
	}

	public void CancelJobsForUnit(RtsUnitMember _unit, string _reason)
	{
		if (_unit == null || !m_ActiveBoardUnits.Contains(_unit))
			return;

		LogBoard($"CANCEL unit {UnitLabel(_unit)} — {_reason}");
		UntrackActiveBoardUnit(_unit);
		m_ExpectedApproachTarget.Remove(_unit);
		m_BoardMoveTiers.Remove(_unit);
		m_Seats?.UnreserveForBoarder(_unit);

		for (int i = 0; i < c_DoorCount; i++)
		{
			if (m_DoorQueues[i].Count == 0)
				continue;

			Queue<Job> filtered = new Queue<Job>(m_DoorQueues[i].Count);
			while (m_DoorQueues[i].Count > 0)
			{
				Job job = m_DoorQueues[i].Dequeue();
				RtsUnitMember involved = job.Kind == JobKind.BoardWoundedFromCarry ? job.Carrier : job.Unit;
				if (involved == _unit || job.Unit == _unit)
					continue;
				filtered.Enqueue(job);
			}

			m_DoorQueues[i] = filtered;
		}

		TryCloseDoorIfQueueIdleAll();
		RefreshBusyFlag();
	}

	public bool HasPendingJobsForDoor(VehicleDoorId _doorId)
	{
		int index = DoorIndex(_doorId);
		if (index < 0)
			return false;
		return m_DoorQueues[index].Count > 0;
	}

	public void EnqueueBoard(IReadOnlyList<RtsUnitMember> _units, VehicleBoardSide _side, bool _forceRun = true)
	{
		if (_units == null || m_Seats == null || m_Vehicle == null)
			return;

		LogBoard($"REQUEST board side={SideLabel(_side)} units={_units.Count} forceRun={_forceRun}");

		var pendingSeats = new HashSet<VehicleSeatId>();

		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null || UnitVehicleMountState.IsUnitMounted(unit))
				continue;

			UnitClickToMove.MoveTier tier = ResolveBoardMoveTier(unit, _forceRun);

			if (unit.TryGetComponent(out UnitFiremanCarryController carry) &&
			    carry.IsCarryingFallen &&
			    carry.CarriedVictim != null)
			{
				RtsUnitMember victim = carry.CarriedVictim;
				if (!m_Vehicle.CanAcceptBoarder(victim) || !m_Vehicle.CanAcceptBoarder(unit))
					continue;

				if (TryResolveEnqueueDoor(
					    unit,
					    _side,
					    _unconscious: true,
					    out VehicleDoorId woundedDoor,
					    out VehicleSeatId woundedSeat,
					    pendingSeats))
				{
					pendingSeats.Add(woundedSeat);
					m_Seats.ReserveForBoarder(victim, woundedSeat);
					EnqueueJob(woundedDoor, new Job
					{
						Kind = JobKind.BoardWoundedFromCarry,
						Unit = victim,
						Carrier = unit,
						Side = _side,
						SeatHint = woundedSeat,
						HasSeatHint = true,
						BoardMoveTier = tier
					});
				}

				if (TryResolveEnqueueDoor(
					    unit,
					    _side,
					    _unconscious: false,
					    out VehicleDoorId carrierDoor,
					    out VehicleSeatId carrierSeat,
					    pendingSeats))
				{
					pendingSeats.Add(carrierSeat);
					m_Seats.ReserveForBoarder(unit, carrierSeat);
					EnqueueJob(carrierDoor, new Job
					{
						Kind = JobKind.Board,
						Unit = unit,
						Side = _side,
						SeatHint = carrierSeat,
						HasSeatHint = true,
						BoardMoveTier = tier
					});
				}

				continue;
			}

			if (!m_Vehicle.CanAcceptBoarder(unit))
				continue;

			if (!TryResolveEnqueueDoor(
				    unit,
				    _side,
				    _unconscious: false,
				    out VehicleDoorId doorId,
				    out VehicleSeatId seatHint,
				    pendingSeats))
			{
				LogBoardSkip(unit, "board", "no seat/door for side " + SideLabel(_side));
				continue;
			}

			pendingSeats.Add(seatHint);
			m_Seats.ReserveForBoarder(unit, seatHint);

			LogBoard(
				$"PLAN board {UnitLabel(unit)} from {PosLabel(unit.transform.position)} " +
				$"→ door={DoorLabel(doorId)} seat={SeatLabel(seatHint)} side={SideLabel(_side)}");

			EnqueueJob(doorId, new Job
			{
				Kind = JobKind.Board,
				Unit = unit,
				Side = _side,
				SeatHint = seatHint,
				HasSeatHint = true,
				BoardMoveTier = tier
			});
		}
	}

	public void EnqueueBoardGunner(IReadOnlyList<RtsUnitMember> _units, VehicleBoardSide _side, bool _forceRun = true)
	{
		if (_units == null || m_Seats == null || m_Vehicle == null || !m_Seats.HasFreeGunnerSeat)
			return;
		if (m_Vehicle.Inventory != null && !m_Vehicle.Inventory.CanUseGunnerSeat)
			return;

		LogBoard($"REQUEST board-gunner side={SideLabel(_side)} units={_units.Count} forceRun={_forceRun}");

		for (int i = 0; i < _units.Count; i++)
		{
			RtsUnitMember unit = _units[i];
			if (unit == null || UnitVehicleMountState.IsUnitMounted(unit))
				continue;

			UnitClickToMove.MoveTier tier = ResolveBoardMoveTier(unit, _forceRun);
			if (!m_Vehicle.CanAcceptBoarder(unit))
				continue;
			if (unit.TryGetComponent(out UnitFiremanCarryController carry) && carry.IsCarryingFallen)
				continue;

			if (!TryResolveEnqueueDoor(
				    unit,
				    _side,
				    _unconscious: false,
				    out VehicleDoorId doorId,
				    out VehicleSeatId seatHint,
				    _pendingSeats: null,
				    VehicleSeatId.Gunner))
			{
				LogBoardSkip(unit, "board-gunner", "door not allowed");
				continue;
			}

			LogBoard(
				$"PLAN board-gunner {UnitLabel(unit)} from {PosLabel(unit.transform.position)} " +
				$"→ door={DoorLabel(doorId)} seat=Gunner side={SideLabel(_side)}");

			m_Seats.ReserveForBoarder(unit, VehicleSeatId.Gunner);

			EnqueueJob(doorId, new Job
			{
				Kind = JobKind.Board,
				Unit = unit,
				Side = _side,
				SeatHint = VehicleSeatId.Gunner,
				HasSeatHint = true,
				BoardMoveTier = tier
			});
			break;
		}
	}

	public void EnqueueLoadWoundedFromCarrier(RtsUnitMember _carrier, bool _forceRun = true)
	{
		if (_carrier == null ||
		    m_Vehicle == null ||
		    !_carrier.TryGetComponent(out UnitFiremanCarryController carry) ||
		    !carry.IsCarryingFallen ||
		    carry.CarriedVictim == null)
			return;

		if (!m_Vehicle.CanAcceptBoarder(carry.CarriedVictim))
			return;

		if (!TryResolveEnqueueDoor(
			    _carrier,
			    VehicleBoardSide.Any,
			    _unconscious: true,
			    out VehicleDoorId doorId,
			    out VehicleSeatId seatHint,
			    _pendingSeats: null))
			return;

		LogBoard(
			$"PLAN wounded-load carrier={UnitLabel(_carrier)} victim={UnitLabel(carry.CarriedVictim)} " +
			$"→ door={DoorLabel(doorId)}");

		m_Seats.ReserveForBoarder(carry.CarriedVictim, seatHint);

		UnitClickToMove.MoveTier tier = ResolveBoardMoveTier(_carrier, _forceRun);

		EnqueueJob(doorId, new Job
		{
			Kind = JobKind.BoardWoundedFromCarry,
			Unit = carry.CarriedVictim,
			Carrier = _carrier,
			Side = VehicleBoardSide.Any,
			SeatHint = seatHint,
			HasSeatHint = true,
			BoardMoveTier = tier
		});
	}

	public void EnqueueDisembarkAll(bool _includeDriver)
	{
		if (m_Seats == null)
			return;

		var ordered = new List<(VehicleSeatId Seat, RtsUnitMember Unit)>(8);
		m_Seats.CollectOccupantsOrdered(ordered);
		int planned = 0;
		for (int i = ordered.Count - 1; i >= 0; i--)
		{
			if (!_includeDriver && ordered[i].Seat == VehicleSeatId.Driver)
				continue;

			VehicleDoorId doorId = ResolveDisembarkDoor(ordered[i].Seat, ordered[i].Unit);
			LogBoard(
				$"PLAN disembark {UnitLabel(ordered[i].Unit)} seat={SeatLabel(ordered[i].Seat)} " +
				$"→ door={DoorLabel(doorId)} includeDriver={_includeDriver}");
			planned++;
			EnqueueJob(doorId, new Job
			{
				Kind = JobKind.Disembark,
				Unit = ordered[i].Unit,
				SeatHint = ordered[i].Seat,
				HasSeatHint = true,
				Side = VehicleBoardSide.Any,
				DisembarkIncludeDriver = _includeDriver
			});
		}

		LogBoard($"REQUEST disembark-all includeDriver={_includeDriver} planned={planned}");
	}

	public void EnqueueDisembarkUnit(RtsUnitMember _unit)
	{
		if (_unit == null || m_Seats == null || !m_Seats.TryGetSeatOf(_unit, out VehicleSeatId seat))
			return;

		VehicleDoorId doorId = ResolveDisembarkDoor(seat, _unit);
		LogBoard(
			$"PLAN disembark {UnitLabel(_unit)} seat={SeatLabel(seat)} → door={DoorLabel(doorId)}");
		EnqueueJob(doorId, new Job
		{
			Kind = JobKind.Disembark,
			Unit = _unit,
			SeatHint = seat,
			HasSeatHint = true
		});
	}
	#endregion

	#region Queue / Workers
	private void EnqueueJob(VehicleDoorId _doorId, Job _job)
	{
		int index = DoorIndex(_doorId);
		if (index < 0)
			return;

		RtsUnitMember tracked = _job.Kind == JobKind.BoardWoundedFromCarry ? _job.Carrier : _job.Unit;
		if (tracked != null)
			TrackActiveBoardUnit(tracked);
		if (_job.Unit != null)
			TrackActiveBoardUnit(_job.Unit);

		if (_job.Kind == JobKind.Board || _job.Kind == JobKind.BoardWoundedFromCarry)
			BeginApproachForBoardJob(_doorId, _job, index);

		// Chassis hold while Board.IsBusy (VehicleController.SyncChassisDriveHold — dynamic RB).
		// No HardStop here — it was a leftover from when infantry could shove the drive RB.

		int queuePos = m_DoorQueues[index].Count;
		m_DoorQueues[index].Enqueue(_job);
		LogBoardJobQueued(_doorId, _job, queuePos);
		m_IsBusy = true;
		EnsureDoorWorker(_doorId);
	}

	private void BeginApproachForBoardJob(VehicleDoorId _doorId, Job _job, int _doorIndex)
	{
		RtsUnitMember mover = _job.Kind == JobKind.BoardWoundedFromCarry ? _job.Carrier : _job.Unit;
		if (mover == null ||
		    UnitVehicleMountState.IsUnitMounted(mover) ||
		    m_Doors == null ||
		    !m_Doors.TryGetDoor(_doorId, out VehicleDoorController.DoorBinding door) ||
		    door.ApproachPoint == null)
			return;

		int waitIndex = m_DoorQueues[_doorIndex].Count + (m_DoorWorkers[_doorIndex] != null ? 1 : 0);
		Vector3 waitPoint = ComputeDoorQueueWaitPoint(door, waitIndex);
		waitPoint = SampleApproachOnNavMesh(waitPoint, door.ApproachPoint);
		m_ExpectedApproachTarget[mover] = waitPoint;
		m_BoardMoveTiers[mover] = _job.BoardMoveTier;

		LogBoard(
			$"APPROACH {UnitLabel(mover)} door={DoorLabel(_doorId)} queueSlot={waitIndex} " +
			$"from {PosLabel(mover.transform.position)} → wait {PosLabel(waitPoint)}");

		IssueBoardMoveOrder(mover, waitPoint, _job.BoardMoveTier);
	}

	private static void IssueBoardMoveOrder(RtsUnitMember _unit, Vector3 _destination, UnitClickToMove.MoveTier _tier)
	{
		_unit.IssueMoveOrder(_destination, _tier);
	}

	private static UnitClickToMove.MoveTier ResolveBoardMoveTier(RtsUnitMember _unit, bool _forceRun)
	{
		if (_forceRun)
			return UnitClickToMove.MoveTier.Run;
		if (_unit != null && _unit.TryGetComponent<UnitClickToMove>(out var ctm) && ctm.IsRunMoveMode)
			return UnitClickToMove.MoveTier.Run;
		return UnitClickToMove.MoveTier.Walk;
	}

	private UnitClickToMove.MoveTier GetBoardMoveTier(RtsUnitMember _unit)
	{
		if (_unit != null && m_BoardMoveTiers.TryGetValue(_unit, out UnitClickToMove.MoveTier tier))
			return tier;
		return ResolveBoardMoveTier(_unit, false);
	}

	private void EnsureDoorWorker(VehicleDoorId _doorId)
	{
		int index = DoorIndex(_doorId);
		if (index < 0 || m_DoorWorkers[index] != null)
			return;

		m_DoorWorkers[index] = StartCoroutine(ProcessDoorQueue(_doorId));
	}

	private IEnumerator ProcessDoorQueue(VehicleDoorId _doorId)
	{
		int index = DoorIndex(_doorId);
		if (index < 0)
			yield break;

		Queue<Job> queue = m_DoorQueues[index];
		while (queue.Count > 0)
		{
			Job job = queue.Dequeue();
			LogBoard(
				$"START {JobKindLabel(job.Kind)} door={DoorLabel(_doorId)} unit={UnitLabel(job.Unit)} " +
				$"seatHint={(job.HasSeatHint ? SeatLabel(job.SeatHint) : "-")} queueLeft={queue.Count}");
			switch (job.Kind)
			{
				case JobKind.Board:
					yield return BoardRoutine(
						job.Unit,
						job.Side,
						_unconscious: false,
						_doorId,
						job.HasSeatHint ? job.SeatHint : (VehicleSeatId?)null);
					break;
				case JobKind.BoardWoundedFromCarry:
					yield return BoardWoundedFromCarryRoutine(
						job.Carrier,
						job.Unit,
						job.Side,
						_doorId,
						job.HasSeatHint ? job.SeatHint : (VehicleSeatId?)null);
					break;
				case JobKind.Disembark:
					yield return DisembarkRoutine(job.Unit, _doorId);
					break;
			}

			if (queue.Count > 0)
				yield return new WaitForSeconds(VehicleBoardTimings.DoorTurnoverPauseSeconds);
		}

		if (!HasPendingJobsForDoor(_doorId))
			yield return CloseDoorAfterJob(_doorId);

		m_DoorWorkers[index] = null;
		RefreshBusyFlag();
	}

	private IEnumerator CloseDoorAfterJob(VehicleDoorId _doorId)
	{
		if (m_Doors == null)
			yield break;
		if (HasPendingJobsForDoor(_doorId))
		{
			m_Doors.KeepHold(_doorId);
			yield break;
		}

		m_Doors.ReleaseHold(_doorId);
		yield return m_Doors.CloseDoorIfFreeRoutine(_doorId);
	}

	private void TryCloseDoorIfQueueIdleAll()
	{
		if (m_Doors == null)
			return;
		for (int i = 0; i < c_DoorCount; i++)
		{
			if (m_DoorQueues[i].Count > 0 || m_DoorWorkers[i] != null)
				continue;
			VehicleDoorId doorId = (VehicleDoorId)i;
			m_Doors.ReleaseHold(doorId);
			StartCoroutine(m_Doors.CloseDoorIfFreeRoutine(doorId));
		}
	}

	private void RefreshBusyFlag()
	{
		for (int i = 0; i < c_DoorCount; i++)
		{
			if (m_DoorWorkers[i] != null || m_DoorQueues[i].Count > 0)
			{
				m_IsBusy = true;
				return;
			}
		}

		m_IsBusy = false;
	}

	private static int DoorIndex(VehicleDoorId _doorId)
	{
		int index = (int)_doorId;
		return index >= 0 && index < c_DoorCount ? index : -1;
	}

	private bool TryResolveEnqueueDoor(
		RtsUnitMember _unit,
		VehicleBoardSide _side,
		bool _unconscious,
		out VehicleDoorId _doorId,
		out VehicleSeatId _seatId,
		HashSet<VehicleSeatId> _pendingSeats,
		VehicleSeatId? _forcedSeat = null)
	{
		_doorId = default;
		_seatId = default;
		if (_unit == null || m_Seats == null)
			return false;

		if (_forcedSeat == VehicleSeatId.Gunner)
		{
			if (!m_Seats.HasFreeGunnerSeat)
				return false;
			if (m_Seats.IsSeatReservedForOther(VehicleSeatId.Gunner, _unit))
				return false;
			_seatId = VehicleSeatId.Gunner;
		}
		else if (!m_Seats.TryPeekSeatForBoarder(_unconscious, out _seatId, _pendingSeats))
		{
			return false;
		}

		_doorId = ResolveDoorForUnit(_seatId, _side, _unit.transform.position);
		return true;
	}

	private VehicleDoorId ResolveDisembarkDoor(VehicleSeatId _seatId, RtsUnitMember _unit)
	{
		Vector3 fromWorld = _unit != null ? _unit.transform.position : transform.position;
		if (m_Seats != null)
		{
			return m_Seats.ResolveDisembarkDoor(
				_doorId => GetApproachDistance(_doorId, fromWorld),
				_seatId);
		}

		return VehicleDoorController.DoorForSeat(_seatId, VehicleBoardSide.Left);
	}

	private bool MatchesBoardDoor(
		VehicleSeatId _seatId,
		VehicleDoorId _doorId,
		VehicleBoardSide _side,
		Vector3 _fromWorld)
	{
		if (m_Seats == null)
			return true;

		VehicleDoorId expected = m_Seats.ResolveBoardDoor(
			_seatId,
			_side,
			_doorId => GetApproachDistance(_doorId, _fromWorld));
		return expected == _doorId;
	}
	#endregion

	#region Board Routines
	private IEnumerator BoardWoundedFromCarryRoutine(
		RtsUnitMember _carrier,
		RtsUnitMember _victim,
		VehicleBoardSide _side,
		VehicleDoorId _doorId,
		VehicleSeatId? _seatHint = null)
	{
		if (_carrier == null || _victim == null || m_Seats == null || m_Doors == null || m_Vehicle == null)
			yield break;

		LogBoard(
			$"WOUNDED-LOAD carrier={UnitLabel(_carrier)} victim={UnitLabel(_victim)} " +
			$"door={DoorLabel(_doorId)} side={SideLabel(_side)}");

		if (!m_Seats.HasAnyFreeSeatForWounded())
		{
			LogBoardAbort("wounded-load", _carrier, _doorId, "no wounded seats");
			yield break;
		}

		if (!m_Vehicle.CanAcceptBoarder(_victim))
			yield break;

		VehicleSeatId seatId;
		if (_seatHint.HasValue)
			seatId = _seatHint.Value;
		else if (!m_Seats.TryPeekSeatForBoarder(true, out seatId))
			yield break;

		if (!m_Seats.TryGetSeat(seatId, out VehicleSeatLayout.SeatBinding seat) || seat.Anchor == null)
			yield break;

		if (!MatchesBoardDoor(seatId, _doorId, _side, _carrier.transform.position))
		{
			LogBoardAbort("wounded-load", _carrier, _doorId, "door/seat side mismatch");
			yield break;
		}

		if (!m_Doors.TryGetDoor(_doorId, out VehicleDoorController.DoorBinding door) ||
		    door.ApproachPoint == null)
			yield break;

		yield return ApproachDoorRoutine(_carrier, door.ApproachPoint, _doorId);
		if (_carrier == null || _victim == null)
			yield break;

		AlignUnitAtDoorApproach(_carrier, door);

		LogBoard($"DOOR open {DoorLabel(_doorId)} for wounded-load carrier={UnitLabel(_carrier)}");
		yield return m_Doors.OpenDoorRoutine(_doorId);

		LogBoard($"PROGRESS wounded-load carrier={UnitLabel(_carrier)} door={DoorLabel(_doorId)} " +
		         $"{VehicleBoardTimings.WoundedLoadSeconds:F1}s");
		yield return WaitAtDoorWithProgress(_carrier, door, VehicleBoardTimings.WoundedLoadSeconds);

		if (_carrier == null || _victim == null)
		{
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		if (_carrier.TryGetComponent(out UnitFiremanCarryController carry) && carry.IsCarryingFallen)
			carry.RequestRelease();

		float wait = 0f;
		while (wait < 1.5f &&
		       _carrier.TryGetComponent(out UnitFiremanCarryController c) &&
		       c.IsCarryingFallen)
		{
			wait += Time.deltaTime;
			yield return null;
		}

		_victim.transform.position = door.ApproachPoint.position;
		LogBoard($"WOUNDED-LOAD victim={UnitLabel(_victim)} placed at {PosLabel(door.ApproachPoint.position)}");

		LogBoard($"PROGRESS wounded-mount victim={UnitLabel(_victim)} door={DoorLabel(_doorId)} " +
		         $"{VehicleBoardTimings.VictimMountAfterLoadSeconds:F1}s");
		yield return WaitAtDoorWithProgress(_victim, door, VehicleBoardTimings.VictimMountAfterLoadSeconds);

		yield return CompleteBoardMount(_victim, _side, _doorId, _forcedUnconscious: true, _seatHint);
	}

	private IEnumerator BoardRoutine(
		RtsUnitMember _unit,
		VehicleBoardSide _side,
		bool _unconscious,
		VehicleDoorId _doorId,
		VehicleSeatId? _forcedSeat = null)
	{
		if (_unit == null || m_Seats == null || m_Doors == null || m_Vehicle == null)
			yield break;
		if (UnitVehicleMountState.IsUnitMounted(_unit))
		{
			LogBoardSkip(_unit, "board", "already mounted");
			yield break;
		}
		if (!m_Vehicle.CanAcceptBoarder(_unit))
		{
			LogBoardSkip(_unit, "board", "vehicle rejects boarder");
			yield break;
		}

		bool isUnconscious = _unconscious || IsUnconscious(_unit);
		bool forceGunner = _forcedSeat == VehicleSeatId.Gunner;

		if (forceGunner)
		{
			if (!m_Seats.HasFreeGunnerSeat)
			{
				LogBoardAbort("board", _unit, _doorId, "gunner seat taken");
				yield break;
			}
		}
		else if (isUnconscious)
		{
			if (!m_Seats.HasAnyFreeSeatForWounded())
			{
				LogBoardAbort("board", _unit, _doorId, "no wounded seats");
				yield break;
			}
		}
		else if (!m_Seats.HasAnyFreeSeatForLiving())
		{
			LogBoardAbort("board", _unit, _doorId, "no living seats");
			yield break;
		}

		VehicleSeatId seatId;
		if (forceGunner)
			seatId = VehicleSeatId.Gunner;
		else if (_forcedSeat.HasValue)
			seatId = _forcedSeat.Value;
		else if (!m_Seats.TryPeekSeatForBoarder(isUnconscious, out seatId))
		{
			LogBoardAbort("board", _unit, _doorId, "no seat peek");
			yield break;
		}

		LogBoard(
			$"BOARD {UnitLabel(_unit)} door={DoorLabel(_doorId)} seatPeek={SeatLabel(seatId)} " +
			$"side={SideLabel(_side)} forcedGunner={forceGunner} unconscious={isUnconscious} " +
			$"at {PosLabel(_unit.transform.position)}");

		if (!m_Seats.TryGetSeat(seatId, out VehicleSeatLayout.SeatBinding seat) || seat.Anchor == null)
		{
			LogBoardAbort("board", _unit, _doorId, "seat anchor missing");
			yield break;
		}

		if (!MatchesBoardDoor(seatId, _doorId, _side, _unit.transform.position))
		{
			VehicleDoorId correctDoor = ResolveDoorForUnit(seatId, _side, _unit.transform.position);
			if (correctDoor != _doorId)
			{
				LogBoard(
					$"REROUTE board {UnitLabel(_unit)} seat={SeatLabel(seatId)} " +
					$"{DoorLabel(_doorId)} → {DoorLabel(correctDoor)}");
				EnqueueJob(correctDoor, new Job
				{
					Kind = JobKind.Board,
					Unit = _unit,
					Side = _side,
					SeatHint = seatId,
					HasSeatHint = true
				});
				yield break;
			}

			LogBoardAbort("board", _unit, _doorId, "door/seat side mismatch");
			yield break;
		}

		if (!m_Doors.TryGetDoor(_doorId, out VehicleDoorController.DoorBinding door) ||
		    door.ApproachPoint == null)
		{
			LogBoardAbort("board", _unit, _doorId, "door binding missing");
			yield break;
		}

		if (!isUnconscious)
		{
			bool reached = false;
			yield return ApproachDoorRoutine(_unit, door.ApproachPoint, _doorId, _ok => reached = _ok);
			if (_unit == null)
				yield break;
			if (!m_ActiveBoardUnits.Contains(_unit))
			{
				LogBoardAbort("board", _unit, _doorId, "cancelled during approach");
				m_Vehicle?.SetIgnoreUnitColliders(_unit, false);
				yield return CloseDoorAfterJob(_doorId);
				yield break;
			}
			if (!reached)
			{
				LogBoardAbort("board", _unit, _doorId, "approach failed / still too far from door");
				yield break;
			}

			m_Vehicle?.SetIgnoreUnitColliders(_unit, true);
			AlignUnitAtDoorApproach(_unit, door);
		}
		else
		{
			Vector3 drop = SampleApproachOnNavMesh(door.ApproachPoint.position, door.ApproachPoint);
			_unit.transform.position = drop;
			LogBoard($"TELEPORT unconscious {UnitLabel(_unit)} → {PosLabel(drop)}");
		}

		if (!m_Vehicle.CanAcceptBoarder(_unit))
		{
			LogBoardAbort("board", _unit, _doorId, "vehicle rejects boarder at door");
			yield break;
		}

		if (!m_ActiveBoardUnits.Contains(_unit))
		{
			LogBoardAbort("board", _unit, _doorId, "cancelled before door open");
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		LogBoard($"DOOR open {DoorLabel(_doorId)} for board {UnitLabel(_unit)} seatPeek={SeatLabel(seatId)}");
		yield return m_Doors.OpenDoorRoutine(_doorId);

		if (!m_ActiveBoardUnits.Contains(_unit))
		{
			LogBoardAbort("board", _unit, _doorId, "cancelled while door open");
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		float boardSeconds = VehicleBoardTimings.GetBoardSeconds(seatId, isUnconscious);
		LogBoard($"PROGRESS board {UnitLabel(_unit)} door={DoorLabel(_doorId)} {boardSeconds:F1}s");
		yield return WaitAtDoorWithProgress(_unit, door, boardSeconds);

		yield return CompleteBoardMount(_unit, _side, _doorId, isUnconscious, _forcedSeat);
	}

	private IEnumerator CompleteBoardMount(
		RtsUnitMember _unit,
		VehicleBoardSide _side,
		VehicleDoorId _doorId,
		bool _forcedUnconscious,
		VehicleSeatId? _forcedSeat = null)
	{
		if (_unit == null || m_Seats == null || m_Doors == null || m_Vehicle == null)
			yield break;

		if (!m_Vehicle.CanAcceptBoarder(_unit))
		{
			LogBoardAbort("mount", _unit, _doorId, "vehicle rejects boarder");
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		bool isUnconscious = _forcedUnconscious || IsUnconscious(_unit);
		var displaceMoves = new List<(RtsUnitMember Unit, VehicleSeatId From, VehicleSeatId To)>(2);
		VehicleSeatId seatId;

		m_Seats.UnreserveForBoarder(_unit);

		if (_forcedSeat == VehicleSeatId.Gunner)
		{
			if (isUnconscious || !m_Seats.HasFreeGunnerSeat)
			{
				LogBoardAbort("mount", _unit, _doorId, "gunner seat unavailable");
				yield return CloseDoorAfterJob(_doorId);
				yield break;
			}

			seatId = VehicleSeatId.Gunner;
		}
		else if (_forcedSeat.HasValue)
		{
			if (!m_Seats.TryAssignPreferredSeatForBoarder(
				    _unit,
				    _forcedSeat.Value,
				    isUnconscious,
				    out seatId,
				    displaceMoves))
			{
				LogBoardAbort("mount", _unit, _doorId, $"preferred seat {_forcedSeat.Value} unavailable");
				yield return CloseDoorAfterJob(_doorId);
				yield break;
			}
		}
		else if (!m_Seats.TryAssignSeatForBoarder(_unit, isUnconscious, out seatId, displaceMoves))
		{
			LogBoardAbort("mount", _unit, _doorId, "seat assign failed");
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		for (int i = 0; i < displaceMoves.Count; i++)
		{
			LogBoard(
				$"DISPLACE {UnitLabel(displaceMoves[i].Unit)} " +
				$"{SeatLabel(displaceMoves[i].From)} → {SeatLabel(displaceMoves[i].To)}");
			RtsUnitMember moved = displaceMoves[i].Unit;
			if (moved == null || !m_Seats.TryGetSeat(displaceMoves[i].To, out VehicleSeatLayout.SeatBinding litter))
				continue;

			UnitVehicleMountState displacedMount = UnitVehicleMountState.GetOrAdd(moved);
			if (displacedMount.IsMounted)
				displacedMount.TransferToSeat(displaceMoves[i].To, litter.Anchor, _isLitter: true);
			else
				displacedMount.Mount(m_Vehicle, displaceMoves[i].To, litter.Anchor, _isLitter: true);
		}

		if (!m_Seats.TryGetSeat(seatId, out VehicleSeatLayout.SeatBinding seat) || seat.Anchor == null)
		{
			LogBoardAbort("mount", _unit, _doorId, "seat anchor missing");
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		UnitVehicleMountState state = UnitVehicleMountState.GetOrAdd(_unit);
		state.Mount(m_Vehicle, seatId, seat.Anchor, seat.IsLitter);
		m_Vehicle.SetIgnoreUnitColliders(_unit, true);
		m_Seats.Occupy(seatId, _unit);
		if (seatId == VehicleSeatId.Driver)
			m_Vehicle.SyncOwnershipFromDriverSeat();
		if (seatId == VehicleSeatId.Gunner)
			m_Vehicle.GunnerHatch?.SetGunnerRaised(true);
		m_Vehicle.NotifyOccupancyChanged();

		LogBoard(
			$"MOUNTED {UnitLabel(_unit)} seat={SeatLabel(seatId)} door={DoorLabel(_doorId)} " +
			$"litter={seat.IsLitter} vehicleTeam={m_Vehicle.Team}");

		UntrackActiveBoardUnit(_unit);
		m_ExpectedApproachTarget.Remove(_unit);
		m_BoardMoveTiers.Remove(_unit);
		yield return CloseDoorAfterJob(_doorId);
	}

	private IEnumerator DisembarkRoutine(RtsUnitMember _unit, VehicleDoorId _doorId)
	{
		if (_unit == null || m_Seats == null || m_Doors == null)
			yield break;
		if (!m_Seats.TryGetSeatOf(_unit, out VehicleSeatId seatId))
		{
			LogBoardSkip(_unit, "disembark", "not in vehicle");
			yield break;
		}
		if (!m_Seats.TryGetSeat(seatId, out VehicleSeatLayout.SeatBinding seat))
			yield break;

		LogBoard(
			$"DISEMBARK {UnitLabel(_unit)} seat={SeatLabel(seatId)} door={DoorLabel(_doorId)} " +
			$"inVehicle at {PosLabel(_unit.transform.position)}");

		if (seatId == VehicleSeatId.Driver)
		{
			LogBoard($"DISEMBARK stop vehicle for driver {UnitLabel(_unit)}");
			m_Vehicle?.HardStop();
		}

		if (!m_Doors.TryGetDoor(_doorId, out VehicleDoorController.DoorBinding door) || door.ExitPoint == null)
		{
			LogBoardAbort("disembark", _unit, _doorId, "door/exit missing");
			yield break;
		}

		LogBoard($"DOOR open {DoorLabel(_doorId)} for disembark {UnitLabel(_unit)} seat={SeatLabel(seatId)}");
		yield return m_Doors.OpenDoorRoutine(_doorId);

		bool isUnconscious = IsUnconscious(_unit);
		float disembarkSeconds = VehicleBoardTimings.GetDisembarkSeconds(seatId, isUnconscious);
		LogBoard(
			$"PROGRESS disembark {UnitLabel(_unit)} door={DoorLabel(_doorId)} " +
			$"{disembarkSeconds:F1}s exit→{PosLabel(door.ExitPoint.position)}");
		yield return WaitAtDoorWithProgress(_unit, door, disembarkSeconds, _snapToApproachPoint: false);

		if (_unit == null || !m_Seats.TryGetSeatOf(_unit, out seatId))
		{
			LogBoardAbort("disembark", _unit, _doorId, "unit/seat lost before exit");
			yield return CloseDoorAfterJob(_doorId);
			yield break;
		}

		Vector3 exitPos = door.ExitPoint.position;
		Quaternion exitRot = door.ExitPoint.rotation;
		UnitVehicleMountState state = UnitVehicleMountState.GetOrAdd(_unit);
		bool wasDriver = seatId == VehicleSeatId.Driver;
		m_Seats.Vacate(_unit);
		state.DismountWorldPosition(exitPos, exitRot);
		m_Vehicle?.SetIgnoreUnitColliders(_unit, false);
		if (wasDriver)
		{
			m_Vehicle?.HardStop();
			m_Vehicle?.SyncOwnershipFromDriverSeat();
		}

		m_Vehicle?.NotifyOccupancyChanged();

		LogBoard(
			$"EXIT {UnitLabel(_unit)} seat={SeatLabel(seatId)} door={DoorLabel(_doorId)} " +
			$"→ {PosLabel(exitPos)} driver={wasDriver} vehicleTeam={m_Vehicle?.Team}");

		m_Doors.WatchExit(_doorId, _unit);
		m_Doors.ReleaseHold(_doorId);
	}

	private Vector3 ComputeDoorQueueWaitPoint(VehicleDoorController.DoorBinding _door, int _queueIndex)
	{
		Vector3 approach = _door.ApproachPoint != null
			? _door.ApproachPoint.position
			: transform.position;

		if (_queueIndex <= 0)
			return approach;

		Vector3 awayFromDoor = Vector3.zero;
		if (m_Vehicle != null)
		{
			awayFromDoor = approach - m_Vehicle.transform.position;
			awayFromDoor.y = 0f;
		}

		if (awayFromDoor.sqrMagnitude < 0.04f && _door.ApproachPoint != null)
		{
			awayFromDoor = _door.ApproachPoint.forward;
			awayFromDoor.y = 0f;
		}

		if (awayFromDoor.sqrMagnitude < 0.04f)
			awayFromDoor = Vector3.back;

		awayFromDoor.Normalize();
		return approach + awayFromDoor * (_queueIndex * c_DoorQueueSpacingMeters);
	}
	#endregion

	#region Shared Helpers
	private IEnumerator WaitAtDoorWithProgress(
		RtsUnitMember _unit,
		VehicleDoorController.DoorBinding _door,
		float _durationSeconds,
		bool _snapToApproachPoint = true)
	{
		if (_unit == null || _durationSeconds <= 0f)
			yield break;

		Vector3 beforePos = _unit.transform.position;
		_unit.HardStop();
		m_Vehicle?.SetIgnoreUnitColliders(_unit, true);
		if (_snapToApproachPoint && !UnitVehicleMountState.IsUnitMounted(_unit))
			AlignUnitAtDoorApproach(_unit, _door);
		else
			FaceUnitTowardDoor(_unit, _door);

		if (_snapToApproachPoint && !UnitVehicleMountState.IsUnitMounted(_unit) &&
		    HorizontalDistance(beforePos, _unit.transform.position) > 0.05f)
		{
			LogBoard(
				$"ALIGN {UnitLabel(_unit)} {PosLabel(beforePos)} → {PosLabel(_unit.transform.position)} " +
				$"approach={PosLabel(_door.ApproachPoint != null ? _door.ApproachPoint.position : beforePos)}");
		}

		UnitWorldActionProgressBar bar = UnitWorldActionProgressBar.GetOrAdd(_unit.gameObject);
		bar?.Show();

		float elapsed = 0f;
		while (elapsed < _durationSeconds)
		{
			if (_unit == null)
				break;

			_unit.HardStop();
			FaceUnitTowardDoor(_unit, _door);
			elapsed += Time.deltaTime;
			bar?.SetProgress(elapsed / _durationSeconds);
			yield return null;
		}

		bar?.Hide();
		if (_unit != null)
			LogBoard($"PROGRESS done {UnitLabel(_unit)} at {PosLabel(_unit.transform.position)}");
	}

	private static void FaceUnitTowardDoor(RtsUnitMember _unit, VehicleDoorController.DoorBinding _door)
	{
		if (_unit == null)
			return;

		Vector3 focus = ResolveDoorFocusPoint(_door);
		Vector3 toDoor = focus - _unit.transform.position;
		toDoor.y = 0f;
		if (toDoor.sqrMagnitude < 0.0001f)
			return;

		_unit.transform.rotation = Quaternion.LookRotation(toDoor.normalized, Vector3.up);
	}

	private static Vector3 ResolveDoorFocusPoint(VehicleDoorController.DoorBinding _door)
	{
		if (_door.Hinge != null)
		{
			Renderer rend = _door.Hinge.GetComponentInChildren<Renderer>();
			if (rend != null)
				return rend.bounds.center;
			if (_door.Hinge.childCount > 0 && _door.Hinge.GetChild(0) != null)
				return _door.Hinge.GetChild(0).position;
			return _door.Hinge.position;
		}

		if (_door.ApproachPoint != null)
			return _door.ApproachPoint.position;
		return Vector3.zero;
	}

	private void AlignUnitAtDoorApproach(RtsUnitMember _unit, VehicleDoorController.DoorBinding _door)
	{
		if (_unit == null || _door.ApproachPoint == null)
			return;

		Vector3 target = SampleApproachOnNavMesh(_door.ApproachPoint.position, _door);
		_unit.HardStop();

		if (_unit.TryGetComponent(out NavMeshAgent agent) && agent.enabled)
		{
			if (agent.isOnNavMesh)
				agent.Warp(target);
			else
				_unit.transform.position = target;
		}
		else
		{
			_unit.transform.position = target;
		}

		FaceUnitTowardDoor(_unit, _door);
	}

	private Vector3 SampleApproachOnNavMesh(Vector3 _desired, VehicleDoorController.DoorBinding _door)
	{
		return SampleApproachOnNavMesh(_desired, _door.ApproachPoint);
	}

	private Vector3 SampleApproachOnNavMesh(Vector3 _desired, Transform _approachPoint)
	{
		Vector3 vehiclePos = m_Vehicle != null ? m_Vehicle.transform.position : _desired;
		Vector3 outward = _desired - vehiclePos;
		outward.y = 0f;
		if (outward.sqrMagnitude < 0.01f && _approachPoint != null)
		{
			outward = _approachPoint.forward;
			outward.y = 0f;
		}
		if (outward.sqrMagnitude < 0.01f)
			outward = Vector3.right;
		outward.Normalize();

		// Prefer the closest valid NavMesh point to the door marker (not the farthest).
		Vector3 best = _desired;
		float bestDist = float.MaxValue;
		bool found = false;
		for (int i = 0; i <= 3; i++)
		{
			Vector3 probe = _desired + outward * (i * 0.15f);
			if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 1.25f, NavMesh.AllAreas))
				continue;

			Vector3 flat = hit.position - vehiclePos;
			flat.y = 0f;
			// Reject samples that end up inside / under the chassis.
			if (flat.magnitude < 1.05f)
				continue;

			float dist = Vector3.Distance(hit.position, _desired);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = hit.position;
				found = true;
			}
		}

		if (found)
			return best;

		if (NavMesh.SamplePosition(_desired, out NavMeshHit fallback, 2f, NavMesh.AllAreas))
			return fallback.position;
		return _desired;
	}

	private IEnumerator ApproachDoorRoutine(
		RtsUnitMember _unit,
		Transform _approachPoint,
		VehicleDoorId _doorId,
		System.Action<bool> _onReached = null)
	{
		if (_unit == null || _approachPoint == null)
		{
			_onReached?.Invoke(false);
			yield break;
		}

		Vector3 navTarget = SampleApproachOnNavMesh(_approachPoint.position, _approachPoint);
		Vector3 startPos = _unit.transform.position;
		float already = HorizontalDistance(startPos, navTarget);
		if (already <= c_ApproachArriveDistance)
		{
			LogBoard(
				$"APPROACH done {UnitLabel(_unit)} door={DoorLabel(_doorId)} already near " +
				$"dist={already:F2}m at {PosLabel(_unit.transform.position)} " +
				$"target {PosLabel(navTarget)}");
			_onReached?.Invoke(true);
			yield break;
		}

		LogBoard(
			$"APPROACH run {UnitLabel(_unit)} door={DoorLabel(_doorId)} from {PosLabel(startPos)} " +
			$"→ door {PosLabel(navTarget)} dist={already:F2}m");

		m_ExpectedApproachTarget[_unit] = navTarget;
		IssueBoardMoveOrder(_unit, navTarget, GetBoardMoveTier(_unit));
		float elapsed = 0f;
		while (elapsed < c_ApproachTimeoutSeconds)
		{
			if (_unit == null || _approachPoint == null)
			{
				_onReached?.Invoke(false);
				yield break;
			}

			if (!m_ActiveBoardUnits.Contains(_unit))
			{
				LogBoard($"APPROACH abort {UnitLabel(_unit)} door={DoorLabel(_doorId)} — cancelled");
				_onReached?.Invoke(false);
				yield break;
			}

			float sqr = (_unit.transform.position - navTarget).sqrMagnitude;
			if (sqr <= c_ApproachArriveDistance * c_ApproachArriveDistance)
				break;

			elapsed += Time.deltaTime;
			yield return null;
		}

		_unit?.HardStop();
		if (_unit == null)
		{
			_onReached?.Invoke(false);
			yield break;
		}

		float finalDist = HorizontalDistance(_unit.transform.position, navTarget);
		// Slightly looser than arrive: UnitBlocker / agent radius often leave ~0.5–1.2 m gap.
		bool arrived = finalDist <= Mathf.Max(c_ApproachArriveDistance * 2.5f, 1.8f);
		LogBoard(
			$"APPROACH {(arrived ? "done" : "timeout")} {UnitLabel(_unit)} door={DoorLabel(_doorId)} " +
			$"at {PosLabel(_unit.transform.position)} dist={finalDist:F2}m " +
			$"target {PosLabel(navTarget)} elapsed={elapsed:F1}s");
		_onReached?.Invoke(arrived);
	}

	private VehicleDoorId ResolveDoorForUnit(
		VehicleSeatId _seatId,
		VehicleBoardSide _side,
		Vector3 _fromWorld)
	{
		if (m_Seats == null)
		{
			return VehicleSeatLayout.ResolveBoardDoorStatic(_seatId, _side);
		}

		return m_Seats.ResolveBoardDoor(_seatId, _side, _doorId => GetApproachDistance(_doorId, _fromWorld));
	}

	private float GetApproachDistance(VehicleDoorId _doorId, Vector3 _fromWorld)
	{
		if (m_Doors == null ||
		    !m_Doors.TryGetDoor(_doorId, out VehicleDoorController.DoorBinding door) ||
		    door.ApproachPoint == null)
			return float.MaxValue;

		return HorizontalDistance(_fromWorld, door.ApproachPoint.position);
	}

	private static bool IsUnconscious(RtsUnitMember _unit)
	{
		UnitConsciousness consciousness = _unit != null
			? _unit.GetComponentInChildren<UnitConsciousness>(true)
			: null;
		return consciousness != null && !consciousness.IsConscious;
	}

	private static float HorizontalDistance(Vector3 _a, Vector3 _b)
	{
		float dx = _a.x - _b.x;
		float dz = _a.z - _b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}
	#endregion

	#region Debug Logging
	private void LogBoard(string _message)
	{
	}

	private void LogBoardSkip(RtsUnitMember _unit, string _phase, string _reason)
	{
		LogBoard($"SKIP {_phase} {UnitLabel(_unit)} — {_reason}");
	}

	private void LogBoardAbort(string _phase, RtsUnitMember _unit, VehicleDoorId _doorId, string _reason)
	{
		LogBoard($"ABORT {_phase} {UnitLabel(_unit)} door={DoorLabel(_doorId)} — {_reason}");
		if (_unit != null && !UnitVehicleMountState.IsUnitMounted(_unit))
			m_Vehicle?.SetIgnoreUnitColliders(_unit, false);
	}

	private void LogBoardJobQueued(VehicleDoorId _doorId, Job _job, int _queueIndex)
	{
		string target = _job.Kind == JobKind.BoardWoundedFromCarry
			? $"carrier={UnitLabel(_job.Carrier)} victim={UnitLabel(_job.Unit)}"
			: UnitLabel(_job.Unit);
		string seatHint = _job.HasSeatHint ? SeatLabel(_job.SeatHint) : "-";
		LogBoard(
			$"QUEUE {_job.Kind} door={DoorLabel(_doorId)} {target} seatHint={seatHint} " +
			$"side={SideLabel(_job.Side)} queueIndex={_queueIndex}");
	}

	private static string UnitLabel(RtsUnitMember _unit) => _unit != null ? _unit.name : "null";

	private static string PosLabel(Vector3 _pos) => $"({_pos.x:F2},{_pos.y:F2},{_pos.z:F2})";

	private static string DoorLabel(VehicleDoorId _doorId) => _doorId.ToString();

	private static string SeatLabel(VehicleSeatId _seatId) => _seatId.ToString();

	private static string SideLabel(VehicleBoardSide _side) => _side.ToString();

	private static string JobKindLabel(JobKind _kind) => _kind.ToString();
	#endregion
}
