using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.7 Arrival / Position Acquisition. NavMesh Reached ≠ cover acquired.
	/// Overlay does not Move. Does not replan. Does not change mission.
	/// </summary>
	public sealed class TacticalArrivalTests
	{
		#region Nested
		private sealed class ListSource : ICoverCandidateSource
		{
			public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(8);

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				for (int i = 0; i < Candidates.Count; i++)
					_destination.Add(Candidates[i]);
			}
		}
		#endregion

		#region A Envelope
		[Test]
		public void A1_NavArrival_InsideTolerance_Acquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(Arrive(c07, 0.42f, true));
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.None, decision.Reason);
			Assert.AreEqual(7, decision.Position.CandidateId);
			Assert.IsTrue(decision.Position.Valid);
			Assert.IsFalse(decision.Position.Occupied);
		}

		[Test]
		public void A2_NavArrival_OutsideTolerance_NotAcquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(Arrive(c07, 2f, true));
			Assert.AreNotEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(TacticalArrivalResult.OutOfTolerance, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.OutOfTolerance, decision.Reason);
			Assert.IsFalse(decision.Position.Valid);
		}

		[Test]
		public void A3_CoverHopArrivalRadius_FitsInsideAcquire()
		{
			Assert.LessOrEqual(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalArrivalMath.DefaultAcquireToleranceMeters);
			Assert.Less(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalNavigationMath.DefaultPointArrivalRadius);
			Assert.AreEqual(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalArrivalMath.ArrivalRadiusForHop(true));
			Assert.AreEqual(
				TacticalNavigationMath.DefaultPointArrivalRadius,
				TacticalArrivalMath.ArrivalRadiusForHop(false));
		}

		[Test]
		public void A4_OutOfTolerance_DoesNotRerouteOrOccupy()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay overlay = CommitFinal(board, 3, c07);
			Assert.IsTrue(overlay.CurrentHopRequiresCoverAcquire);
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(At(c07, 2f, 2f));
			Assert.AreEqual(TacticalArrivalResult.OutOfTolerance, decision.Result);
			Assert.IsTrue(TacticalArrivalMath.IsTransientAcquireMiss(decision.Reason));
			Assert.IsFalse(overlay.NeedsReroute);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 2f));
			Assert.AreNotEqual(CoverOccupancy.Occupied, board.GetState(c07, 2f));
		}

		[Test]
		public void A5_EvaluateAcquired_DoesNotOccupyBoard()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.IsFalse(decision.Position.Occupied);
			Assert.AreEqual(CoverOccupancy.Reserved, sit.Occupancy.GetState(c07, 1f));
		}

		[Test]
		public void A6_AttackWalkRadius_MatchesAcquireDisk()
		{
			Assert.AreEqual(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalArrivalMath.WalkArrivalRadius(UnitAIState.Attack, false));
			Assert.AreEqual(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalArrivalMath.WalkArrivalRadius(UnitAIState.Defense, false));
			Assert.AreEqual(
				TacticalNavigationMath.DefaultPointArrivalRadius,
				TacticalArrivalMath.WalkArrivalRadius(UnitAIState.Search, false));
			Assert.AreEqual(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalArrivalMath.WalkArrivalRadius(UnitAIState.Attack, true));
			Assert.IsFalse(
				TacticalNavigationMath.IsInsideArrival(
					new Vector3(0.85f, 0f, 0f),
					Vector3.zero,
					TacticalArrivalMath.WalkArrivalRadius(UnitAIState.Attack, false)));
		}

		[Test]
		public void A7_CandidateIdWithoutObject_CandidateMissing()
		{
			TacticalArrivalSituation sit = new TacticalArrivalSituation
			{
				NavigationReached = true,
				CurrentPosition = Vector3.zero,
				TargetPosition = new Vector3(10f, 0f, 0f),
				HasTargetPosition = true,
				CandidateId = 7,
				Candidate = null
			};
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Rejected, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.CandidateMissing, decision.Reason);
			Assert.AreEqual(7, decision.CandidateId);
		}

		[Test]
		public void A8_PathInvalid_Rejected()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.PathStatus = "PathInvalid";
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Rejected, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.PathInvalid, decision.Reason);
		}

		[Test]
		public void A9_PathPending_DoesNotReissueWalk()
		{
			Assert.IsFalse(
				TacticalNavigationMath.ShouldReissueStuckWalk(false, true, false, 12f));
			Assert.IsFalse(
				TacticalNavigationMath.ShouldReissueStuckWalk(false, true, true, 0f));
		}

		[Test]
		public void A10_StuckRemainingZero_OutsideArrival_Reissues()
		{
			Assert.IsTrue(
				TacticalNavigationMath.ShouldReissueStuckWalk(false, false, false, 0f));
			Assert.IsTrue(
				TacticalNavigationMath.ShouldReissueStuckWalk(
					false, false, true, TacticalNavigationMath.StuckRemainingMeters));
			Assert.IsFalse(
				TacticalNavigationMath.ShouldReissueStuckWalk(true, false, false, 0f));
			Assert.IsFalse(
				TacticalNavigationMath.ShouldReissueStuckWalk(false, false, true, 4f));
		}
		#endregion

		#region B Type
		[Test]
		public void B1_StandingCandidate_StandingValid_Acquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.RequiredCoverType = CoverType.Standing;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
		}

		[Test]
		public void B2_StandingNoLongerValid_Reject()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			c07.StandingValid = false;
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.RequiredCoverType = CoverType.Standing;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Invalid, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.InvalidPosition, decision.Reason);
		}
		#endregion

		#region C GeometryVersion
		[Test]
		public void C1_SameGeometryVersion_Acquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f), 12);
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.GeometryVersion = 12;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
		}

		[Test]
		public void C2_ChangedGeometryVersion_RevalidateReject()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f), 12);
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.GeometryVersion = 13;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Reevaluate, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.GeometryChanged, decision.Reason);
		}
		#endregion

		#region D Reservation
		[Test]
		public void D1_ReservedBySelf_Acquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(c07, 3, 1f).Success);
			TacticalMovementOverlay overlay = CommitFinal(board, 3, c07);
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(At(c07, 0.2f, 2f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 2f));
		}

		[Test]
		public void D2_ReservationLost_Fail()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			board.ReservationTtlSeconds = 1f;
			Assert.IsTrue(board.TryReserve(c07, 3, 1f).Success);
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.Occupancy = board;
			sit.UnitId = 3;
			sit.Now = 4f;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Reevaluate, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.ReservationLost, decision.Reason);
		}

		[Test]
		public void D3_OccupiedByOther_Fail()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(c07, 9, 1f).Success);
			Assert.IsTrue(board.ConfirmOccupied(c07, 9, 1f).Success);
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.Occupancy = board;
			sit.UnitId = 3;
			sit.Now = 2f;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Occupied, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.Occupied, decision.Reason);
		}

		[Test]
		public void D4_ReservedByOther_NotReservedByUnit()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(c07, 9, 1f).Success);
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.Occupancy = board;
			sit.UnitId = 3;
			sit.Now = 2f;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Rejected, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.NotReservedByUnit, decision.Reason);
		}
		#endregion

		#region E Occupancy transition
		[Test]
		public void E_ReservedArrival_Occupied_PreviousReleased()
		{
			CoverCandidate c03 = Cover(3, new Vector3(2f, 0f, 0f));
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay overlay = CommitFinal(board, 3, c03);
			overlay.NotifyTacticalArrival(At(c03, 0f, 1f));
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c03, 1f));
			TacticalRouteSituation sit = Sit(c07);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c03, c07 };
			sit.FinalCoverCandidateId = 7;
			sit.Now = 2f;
			overlay.Update(in sit, new[] { DirectTo(c07) });
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(At(c07, 0.1f, 3f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c03, 3f));
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 3f));
			Assert.AreEqual(7, overlay.CurrentTacticalPosition.CandidateId);
		}
		#endregion

		#region F Intermediate hop
		[Test]
		public void F_IntermediateHop_Released_FinalReservationRemains()
		{
			CoverCandidate c04 = Cover(4, new Vector3(8f, 0f, 0f));
			CoverCandidate c07 = Cover(7, new Vector3(16f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c07);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c04, c07 };
			sit.FinalCoverCandidateId = 7;
			sit.Now = 1f;
			overlay.Update(in sit, new[] { Via(c04, c07) });
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c04, 1f));
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(At(c04, 0.1f, 2f));
			Assert.AreEqual(TacticalArrivalResult.Traversed, decision.Result);
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c04, 2f));
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 2f));
			Assert.AreNotEqual(4, overlay.CurrentTacticalPosition.CandidateId);
			Assert.IsFalse(overlay.CurrentTacticalPosition.Valid);
		}
		#endregion

		#region G Final
		[Test]
		public void G_FinalDestination_Occupied_CurrentTacticalPosition()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay overlay = CommitFinal(board, 3, c07);
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(At(c07, 0.1f, 2f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 2f));
			Assert.AreEqual(7, overlay.CurrentTacticalPosition.CandidateId);
			Assert.IsTrue(overlay.CurrentTacticalPosition.Occupied);
			Assert.AreEqual(CoverType.Standing, overlay.CurrentTacticalPosition.CoverType);
		}

		[Test]
		public void G2_Heartbeat_KeepsReservationPastTtl()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard { ReservationTtlSeconds = 1f };
			TacticalMovementOverlay overlay = CommitFinal(board, 3, c07);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 1.2f));
			overlay.HeartbeatReservation(1.8f);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 2.2f));
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(At(c07, 0.1f, 2.3f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 2.3f));
		}

		[Test]
		public void G3_StoredCandidate_AcquiresWithoutArrivalObject()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay overlay = CommitFinal(board, 3, c07);
			Assert.AreSame(c07, overlay.ReservedCoverCandidate);
			TacticalArrivalSituation sit = At(c07, 0.1f, 2f);
			sit.Candidate = null;
			sit.CandidateId = 0;
			sit.HasTargetPosition = false;
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 2f));
			Assert.IsTrue(overlay.CurrentTacticalPosition.Occupied);
			Assert.AreEqual(7, overlay.CurrentTacticalPosition.CandidateId);
		}
		#endregion

		#region H Mission
		[Test]
		public void H_Attack_Acquire_AttackRemains()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var go = new GameObject("AI147_Attack");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				controller.EnsureStarted();
				Assert.IsTrue(controller.SetAttack(c07.Position));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				controller.TacticalMovement.Invalidate();
				var board = new CoverOccupancyBoard();
				controller.TacticalMovement.BindOccupancy(board, 3);
				TacticalRouteSituation sit = Sit(c07);
				sit.Occupancy = board;
				sit.OccupancyUnitId = 3;
				sit.CoverCandidates = new[] { c07 };
				sit.FinalCoverCandidateId = 7;
				sit.Now = 1f;
				controller.TacticalMovement.Update(in sit, new[] { DirectTo(c07) });
				TacticalArrivalDecision decision =
					controller.TacticalMovement.NotifyTacticalArrival(At(c07, 0.1f, 2f));
				Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(7, controller.TacticalMovement.CurrentTacticalPosition.CandidateId);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region I Stale route
		[Test]
		public void I1_StaleRoute_StillValid_Acquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalSituation sit = Arrive(c07, 0.2f, true);
			sit.RouteStale = true;
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
		}

		[Test]
		public void I2_StaleRoute_Invalid_Reevaluate()
		{
			var sit = new TacticalArrivalSituation
			{
				NavigationReached = true,
				CurrentPosition = Vector3.zero,
				TargetPosition = Vector3.zero,
				HasTargetPosition = true,
				RouteStale = true,
				DestinationInvalid = true
			};
			TacticalArrivalDecision decision = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(TacticalArrivalResult.Reevaluate, decision.Result);
			Assert.AreEqual(TacticalArrivalFailureReason.RouteStale, decision.Reason);
		}
		#endregion

		#region J Determinism
		[Test]
		public void J_SameInput_SameAcquisition()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			TacticalArrivalSituation sit = Arrive(c07, 0.38f, true);
			TacticalArrivalDecision a = TacticalArrivalMath.Evaluate(in sit);
			TacticalArrivalDecision b = TacticalArrivalMath.Evaluate(in sit);
			Assert.AreEqual(a.Result, b.Result);
			Assert.AreEqual(a.Reason, b.Reason);
			Assert.AreEqual(a.CandidateId, b.CandidateId);
			Assert.AreEqual(a.DistanceMeters, b.DistanceMeters);
		}
		#endregion

		#region Extra
		[Test]
		public void DestOnly_DoesNotStampCover()
		{
			var overlay = new TacticalMovementOverlay();
			overlay.Update(TacticalRouteMath.Goal(Vector3.zero, new Vector3(4f, 0f, 0f), TacticalMovementMode.Normal, 1f));
			var sit = new TacticalArrivalSituation
			{
				NavigationReached = true,
				CurrentPosition = new Vector3(4f, 0f, 0f),
				TargetPosition = new Vector3(4f, 0f, 0f),
				HasTargetPosition = true,
				Now = 1f
			};
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.IsFalse(overlay.CurrentTacticalPosition.Valid);
		}

		[Test]
		public void Arrival_DoesNotGenerateCover()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var source = new ListSource();
			source.Candidates.Add(c07);
			var cache = new SharedCoverSpatialCache(source);
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c07);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c07 };
			sit.CoverCache = cache;
			sit.FinalCoverCandidateId = 7;
			sit.Now = 1f;
			overlay.Update(in sit, new[] { DirectTo(c07) });
			int generated = cache.GenerationCount;
			overlay.NotifyTacticalArrival(At(c07, 0.1f, 2f));
			Assert.AreEqual(generated, cache.GenerationCount);
		}

		[Test]
		public void Overlay_DoesNotMove()
		{
			var go = new GameObject("AI147_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
				var board = new CoverOccupancyBoard();
				TacticalRouteSituation sit = Sit(c07);
				sit.Occupancy = board;
				sit.OccupancyUnitId = 3;
				sit.CoverCandidates = new[] { c07 };
				sit.FinalCoverCandidateId = 7;
				sit.Now = 1f;
				controller.TacticalMovement.Update(in sit, new[] { DirectTo(c07) });
				controller.TacticalMovement.NotifyTacticalArrival(At(c07, 0.1f, 2f));
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Helpers
		private static TacticalMovementOverlay CommitFinal(
			CoverOccupancyBoard _board,
			int _unitId,
			CoverCandidate _cover)
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(_cover);
			sit.Occupancy = _board;
			sit.OccupancyUnitId = _unitId;
			sit.CoverCandidates = new[] { _cover };
			sit.FinalCoverCandidateId = _cover.CandidateId;
			sit.Now = 1f;
			overlay.Update(in sit, new[] { DirectTo(_cover) });
			return overlay;
		}

		private static TacticalArrivalSituation Arrive(CoverCandidate _cover, float _offsetX, bool _reached)
		{
			var board = new CoverOccupancyBoard();
			board.TryReserve(_cover, 3, 1f);
			return new TacticalArrivalSituation
			{
				NavigationReached = _reached,
				CurrentPosition = _cover.Position + new Vector3(_offsetX, 0f, 0f),
				TargetPosition = _cover.Position,
				HasTargetPosition = true,
				Candidate = _cover,
				CandidateId = _cover.CandidateId,
				CandidateRegion = _cover.RegionId,
				RequiredCoverType = _cover.CoverType,
				Occupancy = board,
				UnitId = 3,
				GeometryVersion = _cover.GeometryVersion,
				Now = 1f
			};
		}

		private static TacticalArrivalSituation At(CoverCandidate _cover, float _offsetX, float _now)
		{
			return new TacticalArrivalSituation
			{
				NavigationReached = true,
				CurrentPosition = _cover.Position + new Vector3(_offsetX, 0f, 0f),
				TargetPosition = _cover.Position,
				HasTargetPosition = true,
				Candidate = _cover,
				CandidateId = _cover.CandidateId,
				CandidateRegion = _cover.RegionId,
				Now = _now,
				GeometryVersion = _cover.GeometryVersion
			};
		}

		private static TacticalRouteSituation Sit(CoverCandidate _dest)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = _dest.Position,
				HasDestination = true,
				Mode = TacticalMovementMode.Tactical,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
			};
		}

		private static TacticalRouteCandidate DirectTo(CoverCandidate _cover)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetDirect(1, Vector3.zero, _cover.Position);
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = 10f;
			candidate.TravelTimeSeconds = 6.7f;
			candidate.Exposure01 = 0.2f;
			candidate.Cover01 = 0.85f;
			candidate.Danger01 = 0.2f;
			candidate.MissionProgress01 = 0.8f;
			return candidate;
		}

		private static TacticalRouteCandidate Via(CoverCandidate _hop, CoverCandidate _dest)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetCoverHops(
				1,
				Vector3.zero,
				_dest.Position,
				new[]
				{
					TacticalRouteWaypoint.CoverHop(_hop.Position, _hop.CandidateId, _hop.RegionId)
				});
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = 16f;
			candidate.TravelTimeSeconds = 10.7f;
			candidate.Exposure01 = 0.2f;
			candidate.Cover01 = 0.85f;
			candidate.Danger01 = 0.2f;
			candidate.MissionProgress01 = 0.7f;
			return candidate;
		}

		private static CoverCandidate Cover(int _id, Vector3 _position, int _geometryVersion = 1)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = Vector3.forward,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = new CoverProtectionProfile
				{
					Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f
				},
				CrouchProfile = new CoverProtectionProfile
				{
					Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f
				},
				GeometryVersion = _geometryVersion
			};
		}
		#endregion
	}
}
