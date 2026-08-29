using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.9 Tactical LOD / scheduler. When, not what. Overlay does not Move.
	/// </summary>
	public sealed class TacticalLodTests
	{
		#region Nested
		private sealed class RecordingLeanExecutor : ICoverLeanExecutor
		{
			public int SetLeanCount;

			public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
			{
				SetLeanCount++;
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

		#region A Tier selection
		[Test]
		public void A1_IdleFar_Background()
		{
			TacticalLodDecision decision = TacticalLodMath.Select(IdleFar());
			Assert.AreEqual(TacticalLodTier.Background, decision.Tier);
			Assert.AreEqual(TacticalLodReason.IdleFar, decision.Reason);
			Assert.AreEqual(TacticalCriticality.Low, decision.Criticality);
		}

		[Test]
		public void A2_ActiveMovement_Reduced()
		{
			TacticalLodDecision decision = TacticalLodMath.Select(Moving());
			Assert.AreEqual(TacticalLodTier.Reduced, decision.Tier);
			Assert.AreEqual(TacticalLodReason.ActiveMovement, decision.Reason);
		}

		[Test]
		public void A3_Combat_Full()
		{
			TacticalLodDecision decision = TacticalLodMath.Select(Combat());
			Assert.AreEqual(TacticalLodTier.Full, decision.Tier);
			Assert.AreEqual(TacticalLodReason.Combat, decision.Reason);
			Assert.AreEqual(TacticalCriticality.High, decision.Criticality);
		}

		[Test]
		public void A4_FarUnderFire_Full()
		{
			TacticalLodSituation sit = IdleFar();
			sit.UnderFire = true;
			TacticalLodDecision decision = TacticalLodMath.Select(in sit);
			Assert.AreEqual(TacticalLodTier.Full, decision.Tier);
			Assert.AreEqual(TacticalLodReason.Combat, decision.Reason);
		}

		[Test]
		public void A5_NearIdle_Reduced()
		{
			var sit = new TacticalLodSituation
			{
				Idle = true,
				HasPlayerDistance = true,
				DistanceToPlayerMeters = 5f
			};
			TacticalLodDecision decision = TacticalLodMath.Select(in sit);
			Assert.AreEqual(TacticalLodTier.Reduced, decision.Tier);
			Assert.AreEqual(TacticalLodReason.NearIdle, decision.Reason);
		}
		#endregion

		#region B Event wake-up
		[Test]
		public void B1_Background_ImmediateThreat_Full()
		{
			TacticalLodSituation sit = IdleFar();
			sit.PreviousTier = TacticalLodTier.Background;
			sit.HasImmediateThreat = true;
			TacticalLodDecision decision = TacticalLodMath.Select(in sit);
			Assert.AreEqual(TacticalLodTier.Full, decision.Tier);
			Assert.AreEqual(TacticalLodReason.ImmediateThreat, decision.Reason);
			Assert.AreEqual(TacticalCriticality.Emergency, decision.Criticality);
		}

		[Test]
		public void B2_Background_NewHostile_Full()
		{
			TacticalLodSituation sit = IdleFar();
			sit.PreviousTier = TacticalLodTier.Background;
			sit.SeesHostile = true;
			sit.HasPendingSignificantEvent = true;
			TacticalLodDecision decision = TacticalLodMath.Select(in sit);
			Assert.AreEqual(TacticalLodTier.Full, decision.Tier);
			Assert.AreEqual(TacticalLodReason.NewHostile, decision.Reason);
		}
		#endregion

		#region C Return to background
		[Test]
		public void C_Full_Quiet_Reduced_Background()
		{
			TacticalLodDecision full = TacticalLodMath.Select(Combat());
			Assert.AreEqual(TacticalLodTier.Full, full.Tier);
			var quiet = new TacticalLodSituation
			{
				Idle = true,
				PreviousTier = TacticalLodTier.Full,
				SecondsSinceSignificantEvent = TacticalLodMath.QuietToReducedSeconds + 0.5f
			};
			TacticalLodDecision reduced = TacticalLodMath.Select(in quiet);
			Assert.AreEqual(TacticalLodTier.Reduced, reduced.Tier);
			Assert.AreEqual(TacticalLodReason.Quiet, reduced.Reason);
			quiet.PreviousTier = TacticalLodTier.Reduced;
			quiet.SecondsSinceSignificantEvent = TacticalLodMath.QuietToBackgroundSeconds + 0.5f;
			TacticalLodDecision background = TacticalLodMath.Select(in quiet);
			Assert.AreEqual(TacticalLodTier.Background, background.Tier);
		}
		#endregion

		#region D Decision invariance
		[Test]
		public void D_FullVsReduced_SameRoute()
		{
			TacticalRouteSituation sit = Sit();
			TacticalRouteCandidate[] pair = DirectVsWall();
			TacticalRouteDecision full = new TacticalRouteEvaluator().Evaluate(in sit, pair);
			TacticalRouteDecision reduced = new TacticalRouteEvaluator().Evaluate(in sit, pair);
			Assert.IsTrue(full.HasSelection && reduced.HasSelection);
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

		#region E Budget
		[Test]
		public void E_HundredUnits_RespectBudget()
		{
			var scheduler = new TacticalUpdateScheduler
			{
				MaxRouteEvaluationsPerTick = 20,
				StaggerSlots = 1
			};
			scheduler.BeginTick(0, 0f);
			for (int i = 0; i < 100; i++)
				scheduler.TryAdmit(i + 1, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium);
			Assert.AreEqual(20, scheduler.AdmittedCount);
			Assert.AreEqual(0, scheduler.RouteBudgetRemaining);
		}
		#endregion

		#region F Staggering
		[Test]
		public void F_Eligible_DistributedAcrossTicks()
		{
			var scheduler = new TacticalUpdateScheduler
			{
				MaxRouteEvaluationsPerTick = 100,
				StaggerSlots = 5
			};
			int[] perTick = new int[5];
			for (int tick = 0; tick < 5; tick++)
			{
				scheduler.BeginTick(tick, tick);
				for (int i = 0; i < 100; i++)
					scheduler.TryAdmit(i, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium);
				perTick[tick] = scheduler.AdmittedCount;
				Assert.Less(scheduler.AdmittedCount, 100);
			}

			Assert.AreEqual(20, perTick[0]);
			Assert.AreEqual(20, perTick[1]);
			int total = 0;
			for (int i = 0; i < perTick.Length; i++)
				total += perTick[i];
			Assert.AreEqual(100, total);
		}
		#endregion

		#region G Priority
		[Test]
		public void G_Emergency_BeforeBackground()
		{
			var scheduler = new TacticalUpdateScheduler
			{
				MaxRouteEvaluationsPerTick = 20,
				StaggerSlots = 1
			};
			scheduler.BeginTick(0, 0f);
			for (int i = 1; i <= 99; i++)
				scheduler.Enqueue(i, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Low);
			scheduler.Enqueue(1000, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Emergency);
			int granted = scheduler.Dispatch();
			Assert.AreEqual(20, granted);
			Assert.AreEqual(TacticalCriticality.Emergency, scheduler.Admitted[0].Criticality);
			Assert.AreEqual(1000, scheduler.Admitted[0].UnitId);
		}
		#endregion

		#region H Route cache
		[Test]
		public void H_NoRelevantChange_NoRouteRecompute()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation sit = Sit();
			sit.GeometryVersion = 4;
			sit.KnowledgeVersion = 2;
			TacticalRouteCandidate[] pair = DirectVsWall();
			evaluator.Evaluate(in sit, pair);
			Assert.AreEqual(1, evaluator.EvaluationCount);
			evaluator.Evaluate(in sit, pair);
			Assert.AreEqual(1, evaluator.EvaluationCount);
			Assert.Greater(evaluator.CacheHitCount, 0);
			Assert.IsTrue(TacticalLodMath.RouteCacheValid(
				TacticalLodMath.Stamp(1, 4, 2, 1f, 2), 1, 4, 2));
			sit.KnowledgeVersion = 3;
			evaluator.Evaluate(in sit, pair);
			Assert.AreEqual(2, evaluator.EvaluationCount);
		}
		#endregion

		#region I Exposure cache
		[Test]
		public void I_NoRelevantChange_NoExposureRecompute()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation sit = Sit();
			sit.GeometryVersion = 1;
			sit.KnowledgeVersion = 1;
			evaluator.Evaluate(in sit, DirectVsWall());
			int fills = evaluator.ExposureFillCount;
			Assert.Greater(fills, 0);
			evaluator.Evaluate(in sit, DirectVsWall());
			Assert.AreEqual(fills, evaluator.ExposureFillCount);
			Assert.IsTrue(TacticalLodMath.ExposureCacheValid(
				TacticalLodMath.Stamp(1, 1, 1, 1f, 2), 1, 1, 1));
		}
		#endregion

		#region J Lean
		[Test]
		public void J1_Background_SkipsMovingLean()
		{
			var overlay = new TacticalMovementOverlay();
			var scheduler = new TacticalUpdateScheduler();
			scheduler.BeginTick(0, 0f);
			overlay.BindScheduler(scheduler, 7);
			overlay.NotifyLod(IdleFar());
			TacticalMovingLeanSituation sit = FarLean();
			overlay.NotifyMovingLean(in sit);
			Assert.AreEqual(0, overlay.MovingLeanEvaluationCount);
		}

		[Test]
		public void J2_Corner_WakesLean()
		{
			var overlay = new TacticalMovementOverlay();
			var scheduler = new TacticalUpdateScheduler();
			scheduler.BeginTick(0, 0f);
			overlay.BindScheduler(scheduler, 8);
			overlay.NotifyLod(IdleFar());
			TacticalMovingLeanSituation sit = FarLean();
			overlay.NotifyMovingLean(in sit);
			sit.Approach = true;
			sit.DistanceToCornerMeters = 1.2f;
			sit.LeftSmallSufficient = true;
			sit.LeftVisibilityGain = 0.41f;
			overlay.NotifyMovingLean(in sit);
			Assert.AreEqual(TacticalLodTier.Full, overlay.LastLod.Tier);
			Assert.AreEqual(1, overlay.MovingLeanEvaluationCount);
		}
		#endregion

		#region K Navigation continuity
		[Test]
		public void K_LodChange_NavigationContinues()
		{
			var go = new GameObject("AI149_Nav");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				Vector3 dest = new Vector3(12f, 0f, 0f);
				TacticalMovementDecision committed = controller.TacticalMovement.Update(
					TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Normal, 1f));
				Assert.IsTrue(committed.HasRoute);
				controller.TacticalMovement.NotifyLod(IdleFar());
				Assert.AreEqual(TacticalLodTier.Background, controller.TacticalMovement.LastLod.Tier);
				Assert.IsTrue(controller.TacticalMovement.Last.HasRoute);
				var nav = new TacticalNavigationExecutor();
				nav.Begin();
				nav.Tick(
					controller,
					true,
					controller.LastTacticalMovement.CurrentHop,
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

		#region Overlay / Allows / Benchmark
		[Test]
		public void Overlay_DoesNotMove()
		{
			var go = new GameObject("AI149_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				var scheduler = new TacticalUpdateScheduler();
				scheduler.BeginTick(0, 1f);
				controller.TacticalMovement.BindScheduler(scheduler, 9);
				controller.TacticalMovement.NotifyLod(Combat());
				controller.TacticalMovement.Update(
					TacticalRouteMath.Goal(Vector3.zero, new Vector3(8f, 0f, 0f), TacticalMovementMode.Tactical, 1f));
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Allows_Background_PausesLeanAndExposure()
		{
			var gate = new TacticalLodGate();
			Assert.IsFalse(TacticalLodMath.Allows(
				TacticalLodTier.Background, TacticalLodOperation.MovingLean, in gate));
			Assert.IsFalse(TacticalLodMath.Allows(
				TacticalLodTier.Background, TacticalLodOperation.Exposure, in gate));
			Assert.IsTrue(TacticalLodMath.Allows(
				TacticalLodTier.Background, TacticalLodOperation.ArrivalValidation, in gate));
			gate.ApproachingCorner = true;
			Assert.IsTrue(TacticalLodMath.Allows(
				TacticalLodTier.Background, TacticalLodOperation.MovingLean, in gate));
		}

		[Test]
		public void Benchmark_WithoutVsWith_Scales()
		{
			int without10 = CountWithoutLod(10, 10);
			int with10 = CountWithLod(10, 10, 20);
			Assert.LessOrEqual(with10, without10);
			Assert.AreEqual(without10, with10);
			Assert.Less(CountWithLod(50, 10, 20), CountWithoutLod(50, 10));
			int with100 = CountWithLod(100, 10, 20);
			int without100 = CountWithoutLod(100, 10);
			Assert.Less(with100, without100);
			Assert.LessOrEqual(with100, 20 * 10);
			int with200 = CountWithLod(200, 10, 20);
			Assert.Less(with200, CountWithoutLod(200, 10));
			Assert.LessOrEqual(with200, 20 * 10);
		}
		#endregion

		#region Helpers
		private static TacticalLodSituation IdleFar()
		{
			return new TacticalLodSituation
			{
				Idle = true,
				HasPlayerDistance = true,
				DistanceToPlayerMeters = 80f,
				SecondsSinceSignificantEvent = 30f
			};
		}

		private static TacticalLodSituation Moving()
		{
			return new TacticalLodSituation
			{
				HasActiveTacticalMovement = true
			};
		}

		private static TacticalLodSituation Combat()
		{
			return new TacticalLodSituation
			{
				InCombat = true
			};
		}

		private static TacticalRouteSituation Sit()
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(20f, 0f, 0f),
				HasDestination = true,
				HasKnownThreat = true,
				Mode = TacticalMovementMode.Tactical,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed,
				Now = 1f,
				GeometryVersion = 1,
				KnowledgeVersion = 1
			};
		}

		private static TacticalRouteCandidate[] DirectVsWall()
		{
			var direct = new TacticalRouteCandidate();
			direct.SetDirect(1, Vector3.zero, new Vector3(20f, 0f, 0f));
			direct.UseAuthoredMetrics = true;
			direct.DistanceMeters = 20f;
			direct.TravelTimeSeconds = 13.3f;
			direct.Exposure01 = 0.82f;
			direct.Cover01 = 0.1f;
			direct.Danger01 = 0.7f;
			direct.MissionProgress01 = 0.5f;
			var wall = new TacticalRouteCandidate();
			wall.SetWaypoint(2, Vector3.zero, new Vector3(20f, 0f, 0f), new Vector3(10f, 0f, 4f));
			wall.UseAuthoredMetrics = true;
			wall.DistanceMeters = 24f;
			wall.TravelTimeSeconds = 16f;
			wall.Exposure01 = 0.18f;
			wall.Cover01 = 0.85f;
			wall.Danger01 = 0.2f;
			wall.MissionProgress01 = 0.5f;
			return new[] { direct, wall };
		}

		private static TacticalMovingLeanSituation FarLean()
		{
			return new TacticalMovingLeanSituation
			{
				Present = true,
				Moving = true,
				HasCorner = true,
				InCorridor = true,
				DistanceToCornerMeters = 12f,
				Approach = false,
				LeftAvailable = true,
				LeftVisibilityGain = 0.41f,
				LeftSmallSufficient = true,
				ExposureWithoutLean = 0.1f
			};
		}

		private static int CountWithoutLod(int _units, int _ticks)
		{
			return _units * _ticks;
		}

		private static int CountWithLod(int _units, int _ticks, int _budget)
		{
			var scheduler = new TacticalUpdateScheduler
			{
				MaxRouteEvaluationsPerTick = _budget,
				StaggerSlots = 1
			};
			int count = 0;
			for (int tick = 0; tick < _ticks; tick++)
			{
				scheduler.BeginTick(tick, tick);
				for (int i = 0; i < _units; i++)
				{
					if (scheduler.TryAdmit(i, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium))
						count++;
				}
			}

			return count;
		}
		#endregion
	}
}
