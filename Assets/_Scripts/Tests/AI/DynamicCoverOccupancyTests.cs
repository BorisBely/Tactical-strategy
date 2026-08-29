using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.6 Occupancy / Reservation. Not score. Not geometry. Not Group AI. Not Move.
	/// </summary>
	public sealed class DynamicCoverOccupancyTests
	{
		#region Nested
		private sealed class RecordingSource : ICoverCandidateSource
		{
			public int GenerateCount;

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				GenerateCount++;
				_destination.Add(StandingCover(1, _bounds.center, Vector3.forward, new CoverRegionId(_region.X, _region.Z)));
			}
		}
		#endregion

		#region A Basic state
		[Test]
		public void A1_NewCandidate_Available()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(candidate, 0f));
			Assert.IsTrue(board.IsAvailable(candidate, 0f));
		}

		[Test]
		public void A2_Reserve_Reserved()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			CoverReserveOutcome outcome = board.TryReserve(candidate, 3, 0f);
			Assert.IsTrue(outcome.Success);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(candidate, 0f));
			Assert.AreEqual(CoverReservationResultKind.Reserved, outcome.Result);
		}

		[Test]
		public void A3_Confirm_Occupied()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			board.TryReserve(candidate, 3, 0f);
			CoverReserveOutcome occupied = board.ConfirmOccupied(candidate, 3, 0f);
			Assert.IsTrue(occupied.Success);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(candidate, 0f));
		}

		[Test]
		public void A4_Release_Available()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			board.TryReserve(candidate, 3, 0f);
			board.ConfirmOccupied(candidate, 3, 0f);
			board.Release(candidate, 3, 0f);
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(candidate, 0f));
		}
		#endregion

		#region B Double reservation
		[Test]
		public void B_SecondUnit_Fails()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			CoverReserveOutcome a = board.TryReserve(candidate, 1, 0f);
			CoverReserveOutcome b = board.TryReserve(candidate, 2, 0f);
			Assert.IsTrue(a.Success);
			Assert.IsFalse(b.Success);
			Assert.AreEqual(CoverReservationResultKind.Rejected, b.Result);
			Assert.AreEqual(1, b.OwnerUnitId);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(candidate, 0f));
		}
		#endregion

		#region C Idempotent
		[Test]
		public void C_SameUnitRepeat_NoDuplicate()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			CoverReserveOutcome first = board.TryReserve(candidate, 1, 0f);
			int version = board.OccupancyVersion;
			CoverReserveOutcome second = board.TryReserve(candidate, 1, 0f);
			Assert.IsTrue(first.Success);
			Assert.IsTrue(second.Success);
			Assert.AreEqual(1, board.SlotCount);
			Assert.AreEqual(version, board.OccupancyVersion);
			Assert.AreEqual(CoverReservationReason.Idempotent, second.Reason);
		}
		#endregion

		#region D Release then other unit
		[Test]
		public void D_Release_ThenOtherReserves()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			board.TryReserve(candidate, 1, 0f);
			board.Release(candidate, 1, 0f);
			CoverReserveOutcome b = board.TryReserve(candidate, 2, 0f);
			Assert.IsTrue(b.Success);
			Assert.AreEqual(2, b.Reservation.UnitId);
		}
		#endregion

		#region E TTL
		[Test]
		public void E_TtlExpires_Available()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard { ReservationTtlSeconds = 1f };
			board.TryReserve(candidate, 1, 0f);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(candidate, 0.5f));
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(candidate, 1.05f));
		}

		[Test]
		public void E2_Heartbeat_DoesNotExpire()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard { ReservationTtlSeconds = 1f };
			Assert.IsTrue(board.TryReserve(candidate, 1, 0f).Success);
			board.Heartbeat(candidate, 1, 0.8f);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(candidate, 1.5f));
		}
		#endregion

		#region F Death
		[Test]
		public void F_Death_Releases()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			board.TryReserve(candidate, 7, 0f);
			board.ReleaseUnit(7, 0f, CoverReservationReason.Death);
			Assert.IsTrue(board.IsAvailable(candidate, 0f));
		}

		[Test]
		public void F2_Unconscious_Releases()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			board.TryReserve(candidate, 7, 0f);
			board.ConfirmOccupied(candidate, 7, 0f);
			board.ReleaseUnit(7, 0f, CoverReservationReason.Unconscious);
			Assert.IsTrue(board.IsAvailable(candidate, 0f));
		}
		#endregion

		#region G Command
		[Test]
		public void G_NewCommand_Releases()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			UnitAIController controller = new GameObject("AI136_Cmd").AddComponent<UnitAIController>();
			try
			{
				controller.BindCoverOccupancy(board);
				int unitId = controller.CoverOccupancyUnitId;
				Assert.IsTrue(board.TryReserve(candidate, unitId, Time.time).Success);
				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Attack(new Vector3(8f, 0f, 0f)));
				Assert.IsTrue(result.Accepted);
				Assert.IsTrue(board.IsAvailable(candidate, Time.time));
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}
		#endregion

		#region H Geometry
		[Test]
		public void H_GeometryVersion_ReleasesWithoutGeometryQuery()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			cache.GetCandidates(Vector3.zero);
			int gen = cache.GenerationCount;
			int geometry = cache.GeometryVersion;
			board.TryReserve(candidate, 1, 0f);
			int occupancy = board.OccupancyVersion;
			board.NotifyGeometryVersion(geometry + 1, 0f);
			Assert.IsTrue(board.IsAvailable(candidate, 0f));
			Assert.AreEqual(gen, cache.GenerationCount);
			Assert.AreEqual(geometry, cache.GeometryVersion);
			Assert.Greater(board.OccupancyVersion, occupancy);
			Assert.AreNotEqual(board.OccupancyVersion, cache.GeometryVersion);
		}
		#endregion

		#region I Different candidates
		[Test]
		public void I_DifferentCandidates_BothSucceed()
		{
			CoverCandidate c1 = StandingCover(1, Vector3.zero, Vector3.forward);
			CoverCandidate c2 = StandingCover(2, new Vector3(4f, 0f, 0f), Vector3.forward);
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(c1, 1, 0f).Success);
			Assert.IsTrue(board.TryReserve(c2, 2, 0f).Success);
			Assert.AreEqual(2, board.SlotCount);
		}

		[Test]
		public void I2_TryGetHeld_ReturnsUnitReservation()
		{
			CoverCandidate c1 = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			board.TryReserve(c1, 4, 0f);
			Assert.IsTrue(board.TryGetHeld(4, 0f, out CoverReservation held));
			Assert.AreEqual(1, held.CandidateId);
			Assert.AreEqual(4, held.UnitId);
			Assert.AreEqual(CoverOccupancy.Reserved, held.State);
			Assert.IsFalse(board.TryGetHeld(9, 0f, out _));
		}
		#endregion

		#region J Different regions
		[Test]
		public void J_SameCandidateId_DifferentRegions_BothSucceed()
		{
			CoverCandidate r1 = StandingCover(1, Vector3.zero, Vector3.forward, new CoverRegionId(1, 0));
			CoverCandidate r2 = StandingCover(1, new Vector3(40f, 0f, 0f), Vector3.forward, new CoverRegionId(2, 0));
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(r1, 1, 0f).Success);
			Assert.IsTrue(board.TryReserve(r2, 2, 0f).Success);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(r1, 0f));
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(r2, 0f));
		}
		#endregion

		#region K Stress
		[Test]
		public void K1_HundredUnits_SixteenCandidates_AtMostSixteenHeld()
		{
			var board = new CoverOccupancyBoard();
			CoverCandidate[] candidates = new CoverCandidate[16];
			for (int i = 0; i < 16; i++)
				candidates[i] = StandingCover(i + 1, new Vector3(i * 2f, 0f, 0f), Vector3.forward);
			int heldUnits = 0;
			for (int unit = 1; unit <= 100; unit++)
			{
				for (int c = 0; c < 16; c++)
				{
					if (!board.TryReserve(candidates[c], unit, 0f).Success)
						continue;
					heldUnits++;
					break;
				}
			}

			Assert.AreEqual(16, heldUnits);
			Assert.AreEqual(16, board.CountHeld());
		}

		[Test]
		public void K2_HundredSimultaneous_TryReserve_OneWinner()
		{
			CoverCandidate candidate = StandingCover(1, Vector3.zero, Vector3.forward);
			var board = new CoverOccupancyBoard();
			int success = 0;
			int fail = 0;
			for (int unit = 1; unit <= 100; unit++)
			{
				if (board.TryReserve(candidate, unit, 0f).Success)
					success++;
				else
					fail++;
			}

			Assert.AreEqual(1, success);
			Assert.AreEqual(99, fail);
			Assert.AreEqual(1, board.SlotCount);
		}
		#endregion

		#region L Score exclude not zero
		[Test]
		public void L_Unavailable_Excluded_ScoreUnchanged()
		{
			CoverCandidate c1 = StandingCover(1, new Vector3(3f, 0f, 0f), Vector3.forward);
			CoverCandidate c2 = StandingCover(2, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			float score = CoverScoreMath.PositionScore(c1, in situation, null);
			var board = new CoverOccupancyBoard();
			board.TryReserve(c1, 1, Time.time);
			situation.UnitId = 2;
			situation.OccupancyVersion = board.OccupancyVersion;
			CoverEvaluationResult result = new CoverPositionEvaluator().Evaluate(
				new[] { c1, c2 }, in situation, null, board);
			Assert.Greater(score, 0f);
			Assert.AreEqual(score, CoverScoreMath.PositionScore(c1, in situation, null));
			Assert.IsTrue(result.HasBest);
			Assert.AreEqual(2, result.Best.Candidate.CandidateId);
		}
		#endregion

		#region M Emergency skip
		[Test]
		public void M_Emergency_SkipsReserved_PicksAlternative()
		{
			CoverCandidate reserved = StandingCover(1, new Vector3(3f, 0f, 0f), Vector3.forward);
			CoverCandidate free = StandingCover(2, new Vector3(5f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			situation.UnitId = 2;
			var board = new CoverOccupancyBoard();
			board.TryReserve(reserved, 1, Time.time);
			situation.OccupancyVersion = board.OccupancyVersion;
			EmergencyCoverDecision decision = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in situation, new[] { reserved, free }, null, null, board);
			Assert.AreEqual(2, decision.SelectedCandidateId);
			Assert.AreNotEqual(EmergencyCoverResult.None, decision.Result);
		}
		#endregion

		#region N Geometry not regenerated
		[Test]
		public void N_Occupancy_DoesNotRegenerateGeometry()
		{
			RecordingSource source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalCoverOverlay();
			overlay.BindCache(cache);
			overlay.BindOccupancy(board);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			situation.UnitId = 4;
			overlay.Update(false, UnitAIState.Idle, in situation);
			int gen = cache.GenerationCount;
			board.TryReserve(StandingCover(9, Vector3.right, Vector3.forward), 8, Time.time);
			situation.OccupancyVersion = board.OccupancyVersion;
			overlay.Update(false, UnitAIState.Idle, in situation);
			Assert.AreEqual(1, gen);
			Assert.AreEqual(gen, cache.GenerationCount);
			Assert.AreEqual(1, source.GenerateCount);
		}
		#endregion

		#region Helpers
		private static CoverSituation RifleAt(Vector3 _unit, Vector3 _target)
		{
			return new CoverSituation
			{
				UnitPosition = _unit,
				Stance = CoverStance.Standing,
				Mission = CoverMissionIntent.Hold,
				Weapon = CoverWeaponClass.Rifle,
				Rank = CoverRankClass.Soldier,
				TargetPosition = _target,
				HasTarget = true,
				SectorForward = Vector3.forward,
				HostileDirection = Vector3.forward,
				GeometryVersion = 1
			};
		}

		private static CoverCandidate StandingCover(
			int _id,
			Vector3 _position,
			Vector3 _normal,
			CoverRegionId _region = default)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = _normal,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = Profile(1f),
				CrouchProfile = Profile(1f),
				GeometryVersion = 1,
				RegionId = _region,
				Occupancy = CoverOccupancy.Available
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
