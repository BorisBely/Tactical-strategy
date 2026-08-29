using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.10 Final Acceptance. Compose 14.0–14.9. No new scoring. Overlay does not Move.
	/// </summary>
	public sealed class TacticalMovementFinalAcceptanceTests
	{
		#region Nested
		private sealed class RecordingLeanExecutor : ICoverLeanExecutor
		{
			public CoverLeanLevel LastLevel;
			public CoverPeekDirection LastDirection;

			public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
			{
				LastLevel = _level;
				LastDirection = _direction;
			}
		}
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			TacticalUpdateScheduler.ResetShared();
		}

		[TearDown]
		public void TearDown()
		{
			TacticalUpdateScheduler.ResetShared();
		}
		#endregion

		#region A Golden end-to-end
		[Test]
		public void A_EndToEnd_Golden()
		{
			CoverCandidate c01 = Cover(1, new Vector3(4f, 0f, 3f));
			CoverCandidate c05 = Cover(5, new Vector3(12f, 0f, 3f));
			CoverCandidate c07 = Cover(7, new Vector3(20f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			var lean = new RecordingLeanExecutor();
			var trace = new List<string>(16);
			TacticalRouteSituation sit = Sit(c07.Position, 1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c01, c05, c07 };
			sit.FinalCoverCandidateId = 7;
			sit.HasKnownThreat = true;
			trace.Add("CMD");
			trace.Add("COVER_QUERY");
			trace.Add("POSITION_DECISION");
			TacticalMovementDecision route = overlay.Update(in sit, new[] { Via(c05, c07) });
			trace.Add("ROUTE_SELECT");
			Assert.IsTrue(route.HasRoute);
			Assert.AreEqual(c07.Position, route.Destination);
			Assert.AreEqual(c05.Position, route.CurrentHop);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c05, 1f));
			trace.Add("POSITION_RESERVATION");
			overlay.NotifyLod(Combat());
			trace.Add("LOD");
			TacticalMovingLeanDecision leaned = overlay.NotifyMovingLean(Benefit(), lean);
			Assert.AreEqual(TacticalMovingLeanAction.Lean, leaned.Action);
			trace.Add("MOVING_LEAN");
			TacticalMovingLeanSituation passed = Benefit();
			passed.CornerPassed = true;
			overlay.NotifyMovingLean(in passed, lean);
			Assert.IsFalse(overlay.MovingLeanActive);
			TacticalArrivalDecision hop = overlay.NotifyTacticalArrival(At(c05, 0.1f, 2f));
			Assert.AreEqual(TacticalArrivalResult.Traversed, hop.Result);
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c05, 2f));
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 2f));
			sit.Now = 3f;
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
			overlay.Update(in sit, new[] { Via(c05, c07) });
			trace.Add("REPLAN_CHECK");
			Assert.AreEqual(0, overlay.ReplacementCount);
			TacticalArrivalDecision acquired = overlay.NotifyTacticalArrival(At(c07, 0.1f, 4f));
			trace.Add("ARRIVAL");
			Assert.AreEqual(TacticalArrivalResult.Acquired, acquired.Result);
			trace.Add("POSITION_ACQUIRE");
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 4f));
			Assert.AreEqual(7, overlay.CurrentTacticalPosition.CandidateId);
			Assert.AreEqual("CMD", trace[0]);
			Assert.Contains("COVER_QUERY", trace);
			Assert.Contains("POSITION_DECISION", trace);
			Assert.Contains("ROUTE_SELECT", trace);
			Assert.Contains("POSITION_RESERVATION", trace);
			Assert.Contains("MOVING_LEAN", trace);
			Assert.Contains("REPLAN_CHECK", trace);
			Assert.Contains("ARRIVAL", trace);
			Assert.Contains("POSITION_ACQUIRE", trace);
			Assert.Contains("LOD", trace);
		}
		#endregion

		#region B Destination
		[Test]
		public void B_Destination_RemainsGoal()
		{
			Vector3 dest = new Vector3(20f, 0f, 0f);
			var overlay = new TacticalMovementOverlay();
			TacticalMovementDecision decision = overlay.Update(
				TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Tactical, 1f));
			Assert.IsTrue(TacticalRouteMath.DestinationUnchanged(in decision, dest));
			Assert.AreNotEqual(TacticalRouteKind.None, decision.Kind);
		}
		#endregion

		#region C Navigation
		[Test]
		public void C_SelectedRoute_ExecutorWalks()
		{
			var go = new GameObject("AI1410_Nav");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				Vector3 dest = new Vector3(12f, 0f, 0f);
				TacticalMovementDecision decision = controller.TacticalMovement.Update(
					TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Normal, 1f));
				var nav = new TacticalNavigationExecutor();
				nav.Begin();
				nav.Tick(
					controller,
					true,
					decision.CurrentHop,
					TacticalNavigationMath.DefaultPointArrivalRadius,
					UnitNavigationReason.Attack);
				Assert.Greater(recorder.MoveCount, 0);
				Assert.AreEqual(dest, recorder.LastDestination);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region D Cover destination
		[Test]
		public void D_CoverC07_RouteDoesNotRewriteCoverDecision()
		{
			CoverCandidate c07 = Cover(7, new Vector3(16f, 0f, 0f));
			var cover = new TacticalCoverOverlay();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c07.Position, 1f);
			sit.CoverCandidates = new[] { c07 };
			sit.FinalCoverCandidateId = 7;
			TacticalMovementDecision decision = overlay.Update(in sit, new[] { DirectTo(c07) });
			Assert.AreEqual(c07.Position, decision.Destination);
			Assert.AreEqual(TacticalCoverDecisionKind.None, cover.Last.Decision);
		}
		#endregion

		#region E Occupancy
		[Test]
		public void E_Reserve_Arrive_Occupied()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c07.Position, 1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c07 };
			sit.FinalCoverCandidateId = 7;
			overlay.Update(in sit, new[] { DirectTo(c07) });
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 1f));
			TacticalArrivalDecision acquired = overlay.NotifyTacticalArrival(At(c07, 0.1f, 2f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, acquired.Result);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 2f));
		}
		#endregion

		#region F Replan
		[Test]
		public void F_MinorKeeps_MajorReplaces()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(new Vector3(20f, 0f, 0f), 1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			Assert.AreEqual(0, overlay.ReplacementCount);
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.47f));
			sit.Now = 3f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			Assert.AreEqual(TacticalReplanAction.Replace, next.ReplanAction);
			Assert.AreEqual(2, next.SelectedCandidateId);
		}
		#endregion

		#region G Under fire
		[Test]
		public void G_NearbyCover_Continues()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(new Vector3(10f, 0f, 0f), 1f);
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			sit.UnderFire = NearbyCoverFire(2f);
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { CoverAheadHop(2) });
			Assert.AreEqual(TacticalUnderFireAction.Continue, next.UnderFireAction);
			Assert.AreEqual(0, overlay.ReevaluationCount);
		}

		[Test]
		public void G2_OpenThreat_EmergencyCover()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(new Vector3(20f, 0f, 0f), 1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			sit.UnderFire = NearbyEmergency();
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { ExposedDirect(1) });
			Assert.AreEqual(TacticalUnderFireAction.EmergencyCover, next.UnderFireAction);
			Assert.IsTrue(next.NeedsEmergencyCover);
		}
		#endregion

		#region H Arrival
		[Test]
		public void H_NavReached_ThenAcquired()
		{
			CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c07.Position, 1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c07 };
			sit.FinalCoverCandidateId = 7;
			overlay.Update(in sit, new[] { DirectTo(c07) });
			TacticalArrivalDecision missed = overlay.NotifyTacticalArrival(At(c07, 2f, 1.5f));
			Assert.AreNotEqual(TacticalArrivalResult.Acquired, missed.Result);
			TacticalArrivalDecision acquired = overlay.NotifyTacticalArrival(At(c07, 0.1f, 2f));
			Assert.AreEqual(TacticalArrivalResult.Acquired, acquired.Result);
		}
		#endregion

		#region I Moving lean
		[Test]
		public void I_Normal_Lean_Normal_ThenThreatExit()
		{
			var overlay = new TacticalMovementOverlay();
			var executor = new RecordingLeanExecutor();
			overlay.NotifyMovingLean(Far(), executor);
			overlay.NotifyMovingLean(Benefit(), executor);
			Assert.IsTrue(overlay.MovingLeanActive);
			TacticalMovingLeanSituation passed = Benefit();
			passed.CornerPassed = true;
			overlay.NotifyMovingLean(in passed, executor);
			Assert.IsFalse(overlay.MovingLeanActive);
			overlay.NotifyMovingLean(Benefit(), executor);
			TacticalMovingLeanSituation fire = Benefit();
			fire.ImmediateThreat = true;
			TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(in fire, executor);
			Assert.AreEqual(TacticalMovingLeanAction.Exit, exit.Action);
			Assert.AreEqual(TacticalMovingLeanReason.ImmediateThreat, exit.Reason);
		}
		#endregion

		#region J LOD invariance
		[Test]
		public void J_FullVsReduced_SameRoute()
		{
			TacticalRouteSituation sit = Sit(new Vector3(20f, 0f, 0f), 1f);
			sit.HasKnownThreat = true;
			sit.Mode = TacticalMovementMode.Tactical;
			TacticalRouteCandidate[] pair = DirectVsWall();
			TacticalRouteDecision full = new TacticalRouteEvaluator().Evaluate(in sit, pair);
			TacticalRouteDecision reduced = new TacticalRouteEvaluator().Evaluate(in sit, pair);
			Assert.AreEqual(full.Selected.Candidate.CandidateId, reduced.Selected.Candidate.CandidateId);
			Assert.AreEqual(full.Selected.Score, reduced.Selected.Score);

			var schedulerFull = new TacticalUpdateScheduler();
			schedulerFull.BeginTick(0, 1f);
			var overlayFull = new TacticalMovementOverlay();
			overlayFull.BindScheduler(schedulerFull, 1);
			overlayFull.NotifyLod(Combat());
			TacticalMovementDecision left = overlayFull.Update(in sit, pair);
			var schedulerReduced = new TacticalUpdateScheduler();
			schedulerReduced.BeginTick(0, 1f);
			var overlayReduced = new TacticalMovementOverlay();
			overlayReduced.BindScheduler(schedulerReduced, 2);
			overlayReduced.NotifyLod(Moving());
			TacticalMovementDecision right = overlayReduced.Update(in sit, pair);
			Assert.AreEqual(left.SelectedCandidateId, right.SelectedCandidateId);
			Assert.AreEqual(left.SelectedScore, right.SelectedScore);
			Assert.AreEqual(left.Kind, right.Kind);
		}
		#endregion

		#region K Urban
		[Test]
		public void K_SafeDirect_TacticalWall_DetourDirect()
		{
			TacticalRouteDecision safe = new TacticalRouteEvaluator().Evaluate(
				SitMode(TacticalMovementMode.Normal, false), null);
			Assert.AreEqual(TacticalRouteKind.Direct, safe.Selected.Candidate.Kind);
			TacticalRouteSituation tactical = SitMode(TacticalMovementMode.Tactical, true);
			tactical.WallAnchors = NorthWall();
			TacticalRouteDecision wall = new TacticalRouteEvaluator().Evaluate(
				in tactical, OpenVsWall(0.9f, 0.22f, 0f, 0.55f, 10f, 16f));
			Assert.AreEqual(2, wall.Selected.Candidate.CandidateId);
			TacticalRouteCandidate open = AuthoredDirect(1, 10f, 6.7f, 0.42f, 0.2f, 0.4f, 0.5f, 0.4f);
			TacticalRouteCandidate farWall = AuthoredWaypoint(
				2, new Vector3(5f, 0f, 6f), 10.5f, 7f, 0.42f, 0.2f, 0.4f, 0.5f, 0.4f);
			TacticalRouteDecision detour = new TacticalRouteEvaluator().Evaluate(
				in tactical, new[] { open, farWall });
			Assert.AreEqual(1, detour.Selected.Candidate.CandidateId);
		}
		#endregion

		#region L Cover-to-cover
		[Test]
		public void L_C01_C04_C07_ReservationLifecycle()
		{
			CoverCandidate c04 = Cover(4, new Vector3(8f, 0f, 0f));
			CoverCandidate c07 = Cover(7, new Vector3(16f, 0f, 0f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(c07.Position, 1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 3;
			sit.CoverCandidates = new[] { c04, c07 };
			sit.FinalCoverCandidateId = 7;
			overlay.Update(in sit, new[] { Via(c04, c07) });
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c04, 1f));
			overlay.NotifyTacticalArrival(At(c04, 0.1f, 2f));
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c04, 2f));
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 2f));
			overlay.NotifyTacticalArrival(At(c07, 0.1f, 3f));
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 3f));
		}
		#endregion

		#region M No thrashing
		[Test]
		public void M_NearEqualScores_DoNotOscillate()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(new Vector3(20f, 0f, 0f), 1f);
			TacticalRouteCandidate a = AuthoredDirect(1, 10f, 6.7f, 0.31f, 0.7f, 0.3f, 0.8f, 0.2f);
			TacticalRouteCandidate b = AuthoredDirect(2, 10.2f, 6.8f, 0.30f, 0.7f, 0.3f, 0.8f, 0.2f);
			overlay.Update(in sit, new[] { a, b });
			int first = overlay.Last.SelectedCandidateId;
			for (int i = 0; i < 6; i++)
			{
				overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
				sit.Now = 2f + i;
				overlay.Update(in sit, new[] { a, b });
				Assert.AreEqual(first, overlay.Last.SelectedCandidateId);
			}

			Assert.AreEqual(0, overlay.ReplacementCount);
		}
		#endregion

		#region N No per-frame recompute
		[Test]
		public void N_QuietTicks_NoRouteRebuild()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(new Vector3(20f, 0f, 0f), 1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			int evals = overlay.Evaluator.EvaluationCount;
			int fills = overlay.Evaluator.ExposureFillCount;
			for (int i = 0; i < 50; i++)
			{
				sit.Now = 1f + i * 0.05f;
				overlay.Update(in sit, new[] { SafeDirect(1) });
			}

			Assert.AreEqual(evals, overlay.Evaluator.EvaluationCount);
			Assert.AreEqual(fills, overlay.Evaluator.ExposureFillCount);
			Assert.AreEqual(0, overlay.ReevaluationCount);
		}
		#endregion

		#region O Architecture
		[Test]
		public void O_Movement_DoesNotFire_OrChangeRoE_OrPickCover()
		{
			var go = new GameObject("AI1410_Bounds");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				UseOfForceLevel roe = controller.CurrentUseOfForceLevel;
				var cover = new TacticalCoverOverlay();
				controller.TacticalMovement.Update(
					TacticalRouteMath.Goal(Vector3.zero, new Vector3(8f, 0f, 0f), TacticalMovementMode.Tactical, 1f));
				controller.TacticalMovement.NotifyEvent(
					TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.47f));
				Assert.AreEqual(roe, controller.CurrentUseOfForceLevel);
				Assert.AreEqual(TacticalCoverDecisionKind.None, cover.Last.Decision);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region P LOD budget
		[Test]
		public void P_HundredUnits_BoundedEvaluations()
		{
			var scheduler = new TacticalUpdateScheduler
			{
				MaxRouteEvaluationsPerTick = 20,
				StaggerSlots = 1
			};
			scheduler.BeginTick(0, 0f);
			for (int i = 0; i < 10; i++)
				scheduler.ReportTier(i + 1, TacticalLodTier.Full);
			for (int i = 0; i < 20; i++)
				scheduler.ReportTier(100 + i, TacticalLodTier.Reduced);
			for (int i = 0; i < 70; i++)
				scheduler.ReportTier(200 + i, TacticalLodTier.Background);
			for (int i = 0; i < 100; i++)
				scheduler.TryAdmit(i + 1, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium);
			Assert.AreEqual(20, scheduler.AdmittedCount);
			Assert.AreEqual(10, scheduler.FullCount);
			Assert.AreEqual(20, scheduler.ReducedCount);
			Assert.AreEqual(70, scheduler.BackgroundCount);
			scheduler.Enqueue(999, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Emergency);
			scheduler.BeginTick(1, 1f);
			scheduler.Enqueue(1, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Low);
			scheduler.Enqueue(999, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Emergency);
			scheduler.Dispatch();
			Assert.AreEqual(TacticalCriticality.Emergency, scheduler.Admitted[0].Criticality);
		}
		#endregion

		#region Q Overlay
		[Test]
		public void Q_Overlay_DoesNotMove()
		{
			var go = new GameObject("AI1410_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				CoverCandidate c07 = Cover(7, new Vector3(10f, 0f, 0f));
				TacticalRouteSituation sit = Sit(c07.Position, 1f);
				sit.CoverCandidates = new[] { c07 };
				controller.TacticalMovement.Update(in sit, new[] { DirectTo(c07) });
				controller.TacticalMovement.NotifyMovingLean(Benefit());
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
		private static TacticalLodSituation Combat()
		{
			return new TacticalLodSituation { InCombat = true };
		}

		private static TacticalLodSituation Moving()
		{
			return new TacticalLodSituation { HasActiveTacticalMovement = true };
		}

		private static TacticalRouteSituation Sit(Vector3 _destination, float _now)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = _destination,
				HasDestination = true,
				Mode = TacticalMovementMode.Tactical,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed,
				Now = _now
			};
		}

		private static TacticalRouteSituation SitMode(TacticalMovementMode _mode, bool _threat)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(20f, 0f, 0f),
				HasDestination = true,
				Mode = _mode,
				HasKnownThreat = _threat,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
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

		private static TacticalMovingLeanSituation Benefit()
		{
			return new TacticalMovingLeanSituation
			{
				Present = true,
				Moving = true,
				HasCorner = true,
				InCorridor = true,
				DistanceToCornerMeters = 1.2f,
				Approach = true,
				LeftAvailable = true,
				LeftVisibilityGain = 0.41f,
				LeftExposure01 = 0.18f,
				LeftSmallSufficient = true,
				ExposureWithoutLean = 0.10f
			};
		}

		private static TacticalMovingLeanSituation Far()
		{
			TacticalMovingLeanSituation sit = Benefit();
			sit.DistanceToCornerMeters = 12f;
			sit.LeftSmallSufficient = false;
			sit.LeftVisibilityGain = 0f;
			return sit;
		}

		private static TacticalUnderFireSituation NearbyCoverFire(float _meters)
		{
			return new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = _meters,
				CoverAheadMeters = _meters,
				CoverAheadProtected = true,
				CurrentExposure01 = 0.35f
			};
		}

		private static TacticalUnderFireSituation NearbyEmergency()
		{
			return new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = 20f,
				CoverAheadProtected = false,
				CurrentExposure01 = 0.8f,
				HasNearbyEmergencyCover = true,
				HasCoverCandidates = true
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

		private static TacticalRouteCandidate SafeDirect(int _id)
		{
			return AuthoredDirect(_id, 10f, 6.7f, 0.15f, 0.8f, 0.1f, 0.8f, 0.2f);
		}

		private static TacticalRouteCandidate ExposedDirect(int _id)
		{
			return AuthoredDirect(_id, 10f, 6.7f, 0.9f, 0.1f, 0.8f, 0.5f, 0.1f);
		}

		private static TacticalRouteCandidate CoveredHop(int _id)
		{
			return AuthoredWaypoint(
				_id, new Vector3(5f, 0f, 6f), 16f, 10.7f, 0.15f, 0.85f, 0.2f, 0.7f, 0.88f);
		}

		private static TacticalRouteCandidate CoverAheadHop(int _id)
		{
			CoverCandidate cover = Cover(7, new Vector3(2f, 0f, 0f));
			var candidate = new TacticalRouteCandidate();
			candidate.SetCoverHops(
				_id,
				Vector3.zero,
				new Vector3(10f, 0f, 0f),
				new[]
				{
					TacticalRouteWaypoint.CoverHop(cover.Position, cover.CandidateId, cover.RegionId)
				});
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = 16f;
			candidate.TravelTimeSeconds = 10.7f;
			candidate.Exposure01 = 0.2f;
			candidate.Cover01 = 0.85f;
			candidate.Danger01 = 0.2f;
			candidate.MissionProgress01 = 0.6f;
			return candidate;
		}

		private static TacticalRouteCandidate[] DirectVsWall()
		{
			return new[]
			{
				AuthoredDirect(1, 20f, 13.3f, 0.82f, 0.1f, 0.7f, 0.5f, 0.08f),
				AuthoredWaypoint(2, new Vector3(10f, 0f, 4f), 24f, 16f, 0.18f, 0.85f, 0.2f, 0.5f, 0.88f)
			};
		}

		private static TacticalRouteCandidate[] OpenVsWall(
			float _openExposure,
			float _wallExposure,
			float _openCover,
			float _wallCover,
			float _openMeters,
			float _wallMeters)
		{
			return new[]
			{
				AuthoredDirect(
					1, _openMeters, _openMeters / 1.5f, _openExposure, _openCover,
					_openExposure * 0.85f, 0.5f, 0.08f),
				AuthoredWaypoint(
					2, new Vector3(_wallMeters * 0.5f, 0f, 6f), _wallMeters, _wallMeters / 1.5f,
					_wallExposure, _wallCover, _wallExposure * 0.85f, 0.5f, 0.88f)
			};
		}

		private static TacticalRouteCandidate AuthoredDirect(
			int _id,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission,
			float _wallProximity)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetDirect(_id, Vector3.zero, new Vector3(20f, 0f, 0f));
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = _distance;
			candidate.TravelTimeSeconds = _time;
			candidate.Exposure01 = _exposure;
			candidate.Cover01 = _cover;
			candidate.Danger01 = _danger;
			candidate.MissionProgress01 = _mission;
			candidate.WallProximity01 = _wallProximity;
			candidate.OpenExposure01 = 1f - _wallProximity;
			return candidate;
		}

		private static TacticalRouteCandidate AuthoredWaypoint(
			int _id,
			Vector3 _hop,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission,
			float _wallProximity)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetWaypoint(_id, Vector3.zero, new Vector3(20f, 0f, 0f), _hop);
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = _distance;
			candidate.TravelTimeSeconds = _time;
			candidate.Exposure01 = _exposure;
			candidate.Cover01 = _cover;
			candidate.Danger01 = _danger;
			candidate.MissionProgress01 = _mission;
			candidate.WallProximity01 = _wallProximity;
			candidate.OpenExposure01 = 1f - _wallProximity;
			return candidate;
		}

		private static TacticalWallAnchor[] NorthWall()
		{
			return new[]
			{
				new TacticalWallAnchor
				{
					Position = new Vector3(10f, 0f, 3.2f),
					Normal = Vector3.back,
					Length = 20f
				}
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
		#endregion
	}
}
