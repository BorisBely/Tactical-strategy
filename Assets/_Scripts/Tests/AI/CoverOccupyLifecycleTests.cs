using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// Integration occupy layer on top of frozen #13/#14.
	/// Reserved → Approaching → Acquired → Occupied. Does not retune CoverScore / PathScore / 0.60.
	/// </summary>
	public sealed class CoverOccupyLifecycleTests
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

		#region A Clean occupy
		[Test]
		public void A_Reserved_Approaching_Acquired_Occupied()
		{
			CoverCandidate c1 = StandingCover(1, new Vector3(4f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay overlay = Commit(board, 3, c1);
			Assert.AreEqual(1, overlay.ReservedCoverCandidateId);
			Assert.AreSame(c1, overlay.ReservedCoverCandidate);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c1, 1f));

			TacticalArrivalSituation sit = BareArrival(c1, 0.42f, 2f);
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c1, 2f));
			Assert.IsTrue(overlay.CurrentTacticalPosition.Occupied);
			Assert.AreEqual(1, overlay.CurrentTacticalPosition.CandidateId);
		}

		[Test]
		public void A2_Acquire_DoesNotRelookupCandidate()
		{
			CoverCandidate c2 = StandingCover(2, new Vector3(6f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay overlay = Commit(board, 5, c2);
			TacticalRouteSituation empty = Sit(c2);
			empty.Occupancy = board;
			empty.OccupancyUnitId = 5;
			empty.CoverCandidates = null;
			empty.FinalCoverCandidateId = 0;
			empty.Now = 1.5f;
			overlay.Update(in empty, new[] { DirectTo(c2) });
			Assert.AreSame(c2, overlay.ReservedCoverCandidate);

			TacticalArrivalSituation sit = BareArrival(c2, 0.1f, 2f);
			TacticalArrivalDecision decision = overlay.NotifyTacticalArrival(in sit);
			Assert.AreEqual(TacticalArrivalResult.Acquired, decision.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c2, 2f));
		}
		#endregion

		#region B Nav remaining vs acquire disk
		[Test]
		public void B_RemainingZero_OutsideAcquireDisk_IsNotInsideWalkArrival()
		{
			Vector3 dest = new Vector3(10f, 0f, 0f);
			Vector3 unit = dest + new Vector3(0.85f, 0f, 0f);
			float radius = TacticalArrivalMath.WalkArrivalRadius(UnitAIState.Attack, false);
			Assert.AreEqual(TacticalArrivalMath.DefaultAcquireToleranceMeters, radius);
			Assert.IsFalse(TacticalNavigationMath.IsInsideArrival(unit, dest, radius));
			Assert.Greater(0.85f, TacticalArrivalMath.DefaultAcquireToleranceMeters);
			Assert.Less(
				TacticalArrivalMath.CoverHopArrivalRadiusMeters,
				TacticalNavigationMath.DefaultPointArrivalRadius);
		}
		#endregion

		#region C Candidate swap only when invalid
		[Test]
		public void C1_ApproachingReserved_StaysCommitted_AgainstBetterCover()
		{
			CoverCandidate held = StandingCover(1, new Vector3(3f, 0f, 0f), 0.12f);
			CoverCandidate better = StandingCover(2, new Vector3(4f, 0f, 0f), 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			situation.UnitId = 3;
			CurrentTacticalPosition approaching = CurrentTacticalPosition.FromCandidate(held, false);
			Assert.IsTrue(approaching.Valid);
			Assert.IsFalse(approaching.Occupied);
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { held, better },
				in approaching);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, decision.Reason);
			Assert.AreEqual(1, decision.SelectedCandidateId);
			Assert.IsFalse(decision.HasDestination);
		}

		[Test]
		public void C2_InvalidHeld_SelectsNextAndOccupies()
		{
			CoverCandidate c1 = StandingCover(1, new Vector3(3f, 0f, 0f));
			CoverCandidate c2 = StandingCover(2, new Vector3(5f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			board.TryReserve(c1, 3, 1f);
			c1.NavMeshValid = false;
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			situation.UnitId = 3;
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { c1, c2 },
				CurrentTacticalPosition.Invalid,
				null,
				null,
				board);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.CurrentInvalid, decision.Reason);
			Assert.AreEqual(2, decision.SelectedCandidateId);

			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c2);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c1, c2 };
			sit.FinalCoverCandidateId = 2;
			sit.Now = 2f;
			overlay.Update(in sit, new[] { DirectTo(c2) });
			TacticalArrivalDecision arrival = overlay.NotifyTacticalArrival(BareArrival(c2, 0.1f, 3f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, arrival.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c2, 3f));
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c1, 3f));
		}

		[Test]
		public void C3_Overlay_DoesNotSwapUsableReservation()
		{
			CoverCandidate c1 = StandingCover(1, new Vector3(2f, 0f, 0f), 0.12f);
			CoverCandidate c2 = StandingCover(2, new Vector3(3f, 0f, 0f), 1f);
			var source = new ListSource();
			source.Candidates.Add(c1);
			source.Candidates.Add(c2);
			var cache = new SharedCoverSpatialCache(source);
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(cache);
			overlay.BindOccupancy(board);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			situation.UnitId = 7;
			TacticalCoverDecision first = overlay.Update(false, UnitAIState.Attack, in situation);
			Assert.IsTrue(first.HasDestination);
			int reservedId = first.SelectedCandidateId;
			Assert.AreNotEqual(0, reservedId);
			Assert.IsTrue(board.TryGetHeld(7, Time.time, out CoverReservation held));
			Assert.AreEqual(reservedId, held.CandidateId);

			TacticalCoverDecision second = overlay.Update(false, UnitAIState.Attack, in situation);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, second.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, second.Reason);
			Assert.AreEqual(reservedId, second.SelectedCandidateId);
			Assert.IsTrue(board.TryGetHeld(7, Time.time, out CoverReservation still));
			Assert.AreEqual(reservedId, still.CandidateId);
			Assert.AreEqual(CoverOccupancy.Reserved, still.State);
		}

		[Test]
		public void C4_Occupied_ValidLos_DoesNotSwapOnBetterScore()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, 0.12f);
			CoverCandidate better = StandingCover(2, new Vector3(6f, 0f, 0f), 1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(current, true);
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, better },
				in occupying);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.Committed, decision.Reason);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}
		#endregion

		#region D Unconscious
		[Test]
		public void D_Occupied_Unconscious_Released_NoTactical()
		{
			var go = new GameObject("Occupy_KO");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				ai.EnsureStarted();
				Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Attack(
					UnitAIStateContext.ForAttack(new Vector3(4f, 0f, 0f), Vector3.forward))));
				var board = new CoverOccupancyBoard();
				ai.BindCoverOccupancy(board);
				CoverCandidate cover = StandingCover(2, Vector3.zero);
				int unitId = ai.CoverOccupancyUnitId;
				Assert.IsTrue(board.TryReserve(cover, unitId, 0f).Success);
				Assert.IsTrue(board.ConfirmOccupied(cover, unitId, 0f).Success);
				ai.TacticalMovement.BindOccupancy(board, unitId);
				ai.NotifyLifeState(UnitLifeState.Unconscious);
				Assert.IsTrue(board.IsAvailable(cover, 0f));
				Assert.IsFalse(ai.TacticalMovement.CurrentTacticalPosition.Occupied);
				Assert.IsFalse(UnitLifeStateMath.AllowsTactical(UnitLifeState.Unconscious));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region E Two units
		[Test]
		public void E_TwoUnits_OccupyDifferentSlots()
		{
			CoverCandidate c1 = StandingCover(1, new Vector3(2f, 0f, 0f));
			CoverCandidate c2 = StandingCover(2, new Vector3(6f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			TacticalMovementOverlay a = Commit(board, 11, c1);
			TacticalMovementOverlay b = Commit(board, 12, c2);
			Assert.AreEqual(TacticalArrivalResult.Acquired, a.NotifyTacticalArrival(BareArrival(c1, 0.1f, 2f)).Result);
			Assert.AreEqual(TacticalArrivalResult.Acquired, b.NotifyTacticalArrival(BareArrival(c2, 0.1f, 2f)).Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c1, 2f));
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c2, 2f));
			Assert.AreEqual(1, a.CurrentTacticalPosition.CandidateId);
			Assert.AreEqual(2, b.CurrentTacticalPosition.CandidateId);
			Assert.IsTrue(a.CurrentTacticalPosition.Occupied);
			Assert.IsTrue(b.CurrentTacticalPosition.Occupied);
		}
		#endregion

		#region F Cover identity
		[Test]
		public void F_CandidateRef_NullIsMissing_SameObjectStable()
		{
			Assert.AreEqual("MISSING", CoverDiagnosticLog.CandidateRef(null));
			CoverCandidate c2 = StandingCover(2, new Vector3(6f, 0f, 0f));
			string first = CoverDiagnosticLog.CandidateRef(c2);
			string second = CoverDiagnosticLog.CandidateRef(c2);
			Assert.AreEqual(first, second);
			Assert.IsTrue(first.StartsWith("0x"));
			Assert.AreNotEqual("MISSING", first);
		}
		#endregion

		#region Helpers
		private static TacticalMovementOverlay Commit(
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

		private static TacticalArrivalSituation BareArrival(
			CoverCandidate _cover,
			float _offsetX,
			float _now)
		{
			return new TacticalArrivalSituation
			{
				NavigationReached = true,
				CurrentPosition = _cover.Position + new Vector3(_offsetX, 0f, 0f),
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

		private static CoverSituation RifleAt(Vector3 _unit, Vector3 _target)
		{
			return new CoverSituation
			{
				UnitPosition = _unit,
				Stance = CoverStance.Standing,
				Mission = CoverMissionIntent.Attack,
				Weapon = CoverWeaponClass.Rifle,
				Rank = CoverRankClass.Soldier,
				TargetPosition = _target,
				HasTarget = true,
				SectorForward = Vector3.forward,
				HostileDirection = Vector3.forward,
				GeometryVersion = 1
			};
		}

		private static CoverCandidate StandingCover(int _id, Vector3 _position, float _protection = 1f)
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
				StandingProfile = Profile(_protection),
				CrouchProfile = Profile(_protection),
				GeometryVersion = 1
			};
		}

		private static CoverProtectionProfile Profile(float _value)
		{
			return new CoverProtectionProfile
			{
				Head = _value,
				Torso = _value,
				Pelvis = _value,
				Legs = _value
			};
		}
		#endregion
	}
}
