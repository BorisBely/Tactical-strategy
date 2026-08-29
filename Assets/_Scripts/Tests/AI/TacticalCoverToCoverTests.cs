using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.2 Cover-to-Cover. Direct remains baseline. Overlay does not Move. Not urban wall bias.
	/// </summary>
	public sealed class TacticalCoverToCoverTests
	{
		#region Nested
		private sealed class ListSource : ICoverCandidateSource
		{
			public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(16);

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

		#region A Direct baseline
		[Test]
		public void A1_DirectGood_StaysDirect()
		{
			TacticalRouteSituation situation = ExposedSit(5f, false);
			situation.CoverCandidates = new[] { Cover(4, new Vector3(2.5f, 0f, 3.5f)) };
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.IsTrue(decision.HasSelection);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Selected.Candidate.Kind);
			Assert.AreEqual(0, decision.Selected.Candidate.Intermediates.Count);
		}

		[Test]
		public void A2_DirectAcceptable_NoIntermediateCover()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation situation = ExposedSit(6f, false);
			situation.CoverCandidates = new[]
			{
				Cover(1, new Vector3(3f, 0f, 3.5f)),
				Cover(2, new Vector3(4f, 0f, -3.5f))
			};
			TacticalRouteDecision decision = evaluator.Evaluate(in situation, null);
			Assert.AreEqual(TacticalCoverPlanReason.DirectAcceptable, evaluator.CoverPlanner.LastReason);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Selected.Candidate.Kind);
			Assert.AreEqual(0, decision.Selected.Candidate.Intermediates.Count);
		}
		#endregion

		#region B Intermediate cover
		[Test]
		public void B1_DirectExposed_UsesIntermediateCover()
		{
			TacticalRouteDecision decision = EvaluateExposed();
			Assert.IsTrue(decision.HasSelection);
			Assert.AreEqual(TacticalRouteKind.Waypoint, decision.Selected.Candidate.Kind);
			Assert.Greater(decision.Selected.Candidate.Intermediates.Count, 0);
			Assert.AreNotEqual(0, decision.Selected.Candidate.Intermediates[0].CoverCandidateId);
		}

		[Test]
		public void B2_Intermediate_ReducesExposureVsDirect()
		{
			TacticalRouteDecision decision = EvaluateExposed();
			TacticalRouteEvaluation direct = default;
			TacticalRouteEvaluation cover = decision.Selected;
			for (int i = 0; i < decision.Evaluations.Count; i++)
			{
				if (decision.Evaluations[i].Candidate != null &&
				    decision.Evaluations[i].Candidate.Kind == TacticalRouteKind.Direct)
					direct = decision.Evaluations[i];
			}

			Assert.IsTrue(direct.Viable);
			Assert.Less(cover.Candidate.Exposure01, direct.Candidate.Exposure01);
		}
		#endregion

		#region C Progress
		[Test]
		public void C1_CoverTowardDestination_IsCandidate()
		{
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[] { Cover(1, new Vector3(12f, 0f, 3.5f)) };
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.AreEqual(TacticalRouteKind.Waypoint, decision.Selected.Candidate.Kind);
			Assert.AreEqual(1, decision.Selected.Candidate.Intermediates[0].CoverCandidateId);
		}

		[Test]
		public void C2_CoverFarBehind_Rejected()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[]
			{
				Cover(2, new Vector3(-8f, 0f, 0f)),
				Cover(1, new Vector3(12f, 0f, 3.5f))
			};
			TacticalRouteDecision decision = evaluator.Evaluate(in situation, null);
			Assert.AreEqual(1, decision.Selected.Candidate.Intermediates[0].CoverCandidateId);
			bool behind = false;
			IReadOnlyList<TacticalCoverFilterRejection> rejected = evaluator.CoverPlanner.LastRejections;
			for (int i = 0; i < rejected.Count; i++)
			{
				if (rejected[i].CandidateId == 2 &&
				    (rejected[i].Reason == TacticalCoverHopRejectReason.Behind ||
				     rejected[i].Reason == TacticalCoverHopRejectReason.NoProgress))
					behind = true;
			}

			Assert.IsTrue(behind);
		}
		#endregion

		#region D Hop limit
		[Test]
		public void D1_TwentyCovers_HopCountBounded()
		{
			var covers = new List<CoverCandidate>(20);
			for (int i = 0; i < 20; i++)
				covers.Add(Cover(i + 1, new Vector3(2f + i * 1.1f, 0f, 3.5f)));
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = covers;
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteDecision decision = evaluator.Evaluate(in situation, null);
			Assert.LessOrEqual(
				decision.Selected.Candidate.Intermediates.Count,
				evaluator.CoverPlanner.MaxIntermediateHops);
			Assert.LessOrEqual(evaluator.CoverPlanner.LastUsefulCount, 6);
			Assert.LessOrEqual(decision.CandidateCount, evaluator.CoverPlanner.MaxRouteEvaluations);
		}
		#endregion

		#region E Reservation skip
		[Test]
		public void E1_ReservedCover_UsesNextCandidate()
		{
			CoverCandidate c1 = Cover(1, new Vector3(8f, 0f, 3.5f));
			CoverCandidate c4 = Cover(4, new Vector3(16f, 0f, 3.5f));
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(c4, 99, 0f).Success);
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[] { c1, c4 };
			situation.Occupancy = board;
			situation.OccupancyUnitId = 7;
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.AreEqual(1, decision.Selected.Candidate.Intermediates[0].CoverCandidateId);
		}
		#endregion

		#region F Reservation release
		[Test]
		public void F1_LeavingIntermediate_ReleasesReservation()
		{
			CoverCandidate c4 = Cover(4, new Vector3(8f, 0f, 3.5f));
			CoverCandidate c9 = Cover(9, new Vector3(16f, 0f, 3.5f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[] { c4, c9 };
			situation.Occupancy = board;
			situation.OccupancyUnitId = 7;
			TacticalMovementDecision decision = overlay.Update(in situation);
			Assert.AreEqual(TacticalRouteKind.Waypoint, decision.Kind);
			int first = decision.Route.CurrentWaypoint.CoverCandidateId;
			Assert.AreNotEqual(0, first);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(Find(first, c4, c9), 0f));
			Assert.IsTrue(overlay.NotifyHopCompleted(1f));
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(Find(first, c4, c9), 1f));
		}
		#endregion

		#region G Final vs intermediate
		[Test]
		public void G1_IntermediateReleased_FinalPersistsUntilArrival()
		{
			CoverCandidate c4 = Cover(4, new Vector3(8f, 0f, 3.5f));
			CoverCandidate c13 = Cover(13, new Vector3(24f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[] { c4, c13 };
			situation.Occupancy = board;
			situation.OccupancyUnitId = 7;
			situation.FinalCoverCandidateId = 13;
			TacticalMovementDecision decision = overlay.Update(in situation);
			Assert.AreEqual(4, decision.Route.CurrentWaypoint.CoverCandidateId);
			Assert.IsTrue(overlay.NotifyHopCompleted(1f));
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c4, 1f));
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c13, 1f));
			Assert.IsTrue(overlay.NotifyHopCompleted(2f));
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c13, 2f));
		}
		#endregion

		#region H Executor
		[Test]
		public void H1_Executor_WalksSelectedHop()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation situation = ExposedSitWithCovers();
			TacticalMovementDecision decision = overlay.Update(in situation);
			var go = new GameObject("AI142_Exec");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				Assert.AreEqual(0, recorder.MoveCount);
				var nav = new TacticalNavigationExecutor();
				nav.Begin();
				nav.Tick(
					controller,
					true,
					decision.CurrentHop,
					TacticalNavigationMath.DefaultPointArrivalRadius,
					UnitNavigationReason.Attack);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(decision.CurrentHop, recorder.LastDestination);
				Assert.AreEqual(new Vector3(24f, 0f, 0f), overlay.Last.Destination);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void H2_Overlay_DoesNotMove()
		{
			var go = new GameObject("AI142_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				TacticalRouteSituation situation = ExposedSitWithCovers();
				controller.TacticalMovement.Update(in situation);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Extra
		[Test]
		public void I1_SameInput_SameRoute()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation situation = ExposedSitWithCovers();
			int id = evaluator.Evaluate(in situation, null).Selected.Candidate.CandidateId;
			for (int i = 0; i < 20; i++)
				Assert.AreEqual(id, evaluator.Evaluate(in situation, null).Selected.Candidate.CandidateId);
			Assert.AreEqual(1, evaluator.EvaluationCount);
		}

		[Test]
		public void J1_SharedCache_Reused()
		{
			var source = new ListSource();
			source.Candidates.Add(Cover(1, new Vector3(8f, 0f, 3.5f)));
			var cache = new SharedCoverSpatialCache(source);
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCache = cache;
			new TacticalRouteEvaluator().Evaluate(in situation, null);
			int first = cache.GenerationCount;
			new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.AreEqual(first, cache.GenerationCount);
			Assert.Greater(cache.CacheHitCount, 0);
		}

		[Test]
		public void K1_ThreeCovers_DoesNotUseAll()
		{
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[]
			{
				Cover(1, new Vector3(6f, 0f, 3.5f)),
				Cover(2, new Vector3(12f, 0f, 3.5f)),
				Cover(3, new Vector3(18f, 0f, 3.5f))
			};
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.Less(decision.Selected.Candidate.Intermediates.Count, 3);
		}
		#endregion

		#region Helpers
		private static TacticalRouteDecision EvaluateExposed()
		{
			return new TacticalRouteEvaluator().Evaluate(ExposedSitWithCovers(), null);
		}

		private static TacticalRouteSituation ExposedSitWithCovers()
		{
			TacticalRouteSituation situation = ExposedSit(24f, true);
			situation.CoverCandidates = new[]
			{
				Cover(1, new Vector3(8f, 0f, 3.5f)),
				Cover(2, new Vector3(16f, 0f, 3.5f))
			};
			return situation;
		}

		private static TacticalRouteSituation ExposedSit(float _distance, bool _threat)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(_distance, 0f, 0f),
				HasDestination = true,
				Mode = TacticalMovementMode.Tactical,
				HasKnownThreat = _threat,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
			};
		}

		private static CoverCandidate Cover(int _id, Vector3 _position)
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
					Head = 1f,
					Torso = 1f,
					Pelvis = 1f,
					Legs = 1f
				},
				CrouchProfile = new CoverProtectionProfile
				{
					Head = 1f,
					Torso = 1f,
					Pelvis = 1f,
					Legs = 1f
				},
				GeometryVersion = 1
			};
		}

		private static CoverCandidate Find(int _id, CoverCandidate _a, CoverCandidate _b)
		{
			return _a.CandidateId == _id ? _a : _b;
		}
		#endregion
	}
}
