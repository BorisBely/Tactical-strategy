using NUnit.Framework;
using UnityEngine;

namespace VehicleNavigation.Tests
{
	public class NavigationPlannerTests
	{
		[Test]
		public void ArrivalAnalysis_FrontTarget_IsReachableForward()
		{
			var analysis = ArrivalAnalysis.Compute(
				Vector3.zero, 0f, 5.6f,
				new Vector3(0f, 0f, 8f), 0f);

			Assert.AreEqual(TargetSide.Front, analysis.Side);
			Assert.IsTrue(analysis.CanReachForward);
		}

		[Test]
		public void ArrivalAnalysis_CloseFrontTarget_NotInsideTurningCircleAt8m()
		{
			var analysis = ArrivalAnalysis.Compute(
				Vector3.zero, 0f, 5.6f,
				new Vector3(0f, 0f, 8f), 0f);

			Assert.IsFalse(analysis.TargetInsideTurningCircle);
		}

		[Test]
		public void ScoringSystem_ForwardBehindTarget_IsExpensive()
		{
			float forward = ScoringSystem.ScoreCandidate(
				DriverIntent.DriveForward, 10f, 0, FeasibilityResult.Valid, 150f, 5.6f);
			float reverse = ScoringSystem.ScoreCandidate(
				DriverIntent.Reverse, 10f, 0, FeasibilityResult.Valid, 150f, 5.6f);

			Assert.Greater(forward, reverse);
		}

		[Test]
		public void GoalPoseValidator_RequiresStableWindow()
		{
			var validator = new GoalPoseValidator();
			var criteria = new GoalPoseCriteria(0.5f, 5f, 1f, 0.4f);
			Vector3 goal = new Vector3(0f, 0f, 2f);

			bool first = validator.Evaluate(
				new Vector3(0f, 0f, 1.8f), 0f, 0.5f, goal, 0f, criteria, 0.1f,
				out float posErr, out float yawErr);
			Assert.IsFalse(first);
			Assert.LessOrEqual(posErr, 0.5f);

			bool second = validator.Evaluate(
				new Vector3(0f, 0f, 1.8f), 0f, 0.5f, goal, 0f, criteria, 0.5f,
				out _, out _);
			Assert.IsTrue(second);
		}

		[Test]
		public void TrajectoryPath_ProgressNeverMovesBackward()
		{
			var path = new TrajectoryPath();
			path.Build(new[]
			{
				Vector3.zero,
				new Vector3(0f, 0f, 5f),
				new Vector3(0f, 0f, 10f)
			});

			path.Project(new Vector3(0f, 0f, 3f), out int seg1, out _, out _);
			path.Project(new Vector3(0f, 0f, 6f), out int seg2, out _, out _);
			Assert.GreaterOrEqual(seg2, seg1);
		}

		[Test]
		public void VehicleKinematicsProfile_TurnRadiusMatchesSteer()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float expected = 3.5f / Mathf.Tan(32f * Mathf.Deg2Rad);
			Assert.AreEqual(expected, profile.MinTurningRadius, 0.01f);
		}

		[Test]
		public void ReedsSheppPlanner_ProducesNonEmptyPath()
		{
			var pts = ReedsSheppPlanner.PlanForwardArc(
				new ReedsSheppPlanner.Pose(Vector3.zero, 0f),
				new ReedsSheppPlanner.Pose(new Vector3(4f, 0f, 2f), 45f),
				5.6f);
			Assert.Greater(pts.Length, 1);
		}

		[Test]
		public void BicycleKinematics_ForwardStepAdvancesAlongYaw()
		{
			var prim = BicycleKinematics.Integrate(
				Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 2f, 3.5f, 0f);
			Assert.Greater(prim.EndPosition.z, 1.5f);
			Assert.AreEqual(TrajectoryGear.Forward, prim.Gear);
		}

		[Test]
		public void BicycleKinematics_ReverseStepMovesBackward()
		{
			var prim = BicycleKinematics.Integrate(
				Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 2f, 3.5f, 0f);
			Assert.Less(prim.EndPosition.z, -1.5f);
		}

		[Test]
		public void LocalPosePlanner_StraightTwoMeters_EndsNearGoalWithoutSnap()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			var end = traj.Points[traj.PointCount - 1];
			Assert.LessOrEqual(Vector3.Distance(end.Position, goal.Position), 0.11f);
		}

		[Test]
		public void LocalPosePlanner_StraightTwoMeters_IsValid()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid);
			Assert.Greater(traj.TotalLength, 1.0f);
			Assert.LessOrEqual(traj.TotalLength, 4.0f);
		}

		[Test]
		public void LocalPosePlanner_SideTwoMetersWithHeading_FindsPath()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), 90f, 0.5f, 8f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.LessOrEqual(traj.TotalLength, 14f, $"side 2m path too long: {traj.TotalLength:F1}m reason={traj.DebugReason}");
			var end = traj.Points[traj.PointCount - 1];
			Assert.LessOrEqual(Vector3.Distance(end.Position, goal.Position), 0.26f);
			if (goal.HasHeading)
				Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, goal.YawDegrees)), goal.HeadingToleranceDeg);
		}

		[Test]
		public void LocalPosePlanner_StraightReverseTwoMeters_HasSingleReverseSegment()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, -2f), null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual("straight-rev", traj.DebugReason);
			Assert.AreEqual(1, traj.GearSegmentCount);
			Assert.LessOrEqual(traj.TotalLength, 3f);
			Assert.GreaterOrEqual(traj.TotalLength, 1.5f);
			for (int i = 0; i < traj.PointCount; i++)
				Assert.Less(Mathf.Abs(traj.Points[i].Curvature), 0.02f);
		}

		[Test]
		public void SweptVolumeChecker_NullSnapshot_NoPhysicsQueries()
		{
			var checker = new SweptVolumeChecker();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var prim = BicycleKinematics.Integrate(
				Vector3.zero, 0f, 0.1f, TrajectoryGear.Forward, 1f, 3.5f, 0f);
			var traj = new VehicleTrajectory();
			traj.Build(prim.Samples, 1f, 0, "test");

			checker.ResetCounters();
			Assert.IsTrue(checker.IsPrimitiveSafe(prim, profile, null));
			Assert.IsTrue(checker.IsTrajectorySafe(traj, profile, null));
			Assert.AreEqual(0, checker.PhysicsQueries);
			Assert.AreEqual(0, checker.PrimitiveQueries);
			Assert.AreEqual(0, checker.TrajectoryQueries);

			var invalidSnapshot = new PlanningObstacleSnapshot();
			checker.ResetCounters();
			Assert.IsFalse(invalidSnapshot.IsValid);
			Assert.IsTrue(checker.IsPrimitiveSafe(prim, profile, invalidSnapshot));
			Assert.IsTrue(checker.IsTrajectorySafe(traj, profile, invalidSnapshot));
			Assert.AreEqual(0, checker.PhysicsQueries);
			Assert.AreEqual(0, checker.PrimitiveQueries);
			Assert.AreEqual(0, checker.TrajectoryQueries);
		}

		[Test]
		public void LocalPosePlanner_RearDiagonalTwoMeters_PrefersShortReverseFirstCusp()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float yaw = 135f;
			float rad = yaw * Mathf.Deg2Rad;
			var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
			var goal = new GoalPose(goalPos, yaw, 0.5f, 10f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.LessOrEqual(traj.TotalLength, 16f, $"rear diagonal too long: {traj.TotalLength:F1}m reason={traj.DebugReason}");
			Assert.GreaterOrEqual(traj.GearSegmentCount, 1);
			if (traj.GearSegmentCount > 1)
				Assert.AreEqual(TrajectoryGear.Reverse, traj.Points[0].Gear);

			TrajectoryPoint end = traj.Points[traj.PointCount - 1];
			Assert.LessOrEqual(
				BicycleKinematics.FlatDistance(end.Position, goalPos), goal.PositionTolerance);
			Assert.LessOrEqual(
				Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, yaw)), goal.HeadingToleranceDeg);

			for (int i = 1; i < traj.PointCount; i++)
			{
				float step = Vector3.Distance(traj.Points[i - 1].Position, traj.Points[i].Position);
				Assert.LessOrEqual(step, 0.35f);
			}
		}

		[Test]
		public void LocalPosePlanner_PathSamplesAreContinuous()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), 90f, 0.5f, 8f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);

			for (int i = 1; i < traj.PointCount; i++)
			{
				float step = Vector3.Distance(traj.Points[i - 1].Position, traj.Points[i].Position);
				Assert.LessOrEqual(step, 0.35f);
				Assert.GreaterOrEqual(traj.Points[i].ArcLength, traj.Points[i - 1].ArcLength);
			}
		}

		[Test]
		public void LocalPosePlanner_FrontObliqueTwoMeters_StartsForward()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float yaw = 45f;
			float rad = yaw * Mathf.Deg2Rad;
			var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
			var goal = new GoalPose(goalPos, null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			// 2 m at 45° lies inside the min-turn circle: short CS/arc is geometrically
			// impossible, so lattice/hybrid "merged" forward-first is acceptable.
			Assert.AreEqual(TrajectoryGear.Forward, traj.Points[0].Gear, traj.DebugReason);
			Assert.LessOrEqual(traj.TotalLength, profile.EffectiveTurnRadius * 2.5f + 4f);
			string reason = traj.DebugReason ?? string.Empty;
			Assert.IsFalse(reason.Contains("three-point"), $"short front-oblique must not use three-point: {reason}");
			Assert.IsFalse(
				reason.StartsWith("rs-") && traj.Points[0].Gear == TrajectoryGear.Reverse,
				$"front-oblique must not pick reverse-first RS: {reason}");
		}

		[Test]
		public void BicycleKinematics_SteerRamp_FirstMeterBelowTargetCurvature()
		{
			float wb = 3.5f;
			float κ = 0.179f;
			float rampLen = BicycleKinematics.ComputeSteerRampLength(wb, 0f, κ);
			Assert.Greater(rampLen, 0.4f, "full lock ramp should span a meaningful arc");

			var prim = BicycleKinematics.IntegrateWithSteerRamp(
				Vector3.zero, 0f, 0f, κ, TrajectoryGear.Forward, 3f, wb, 0f, 16);
			Assert.IsNotNull(prim.Samples);
			Assert.Greater(prim.Samples.Count, 4);
			Assert.AreEqual(0f, prim.Samples[0].Curvature, 1e-4f);

			// Sample near mid-ramp — still climbing toward target κ.
			float midArc = rampLen * 0.45f;
			float κAtMid = κ;
			for (int i = 0; i < prim.Samples.Count; i++)
			{
				if (prim.Samples[i].ArcLength >= midArc)
				{
					κAtMid = Mathf.Abs(prim.Samples[i].Curvature);
					break;
				}
			}
			Assert.Less(κAtMid, κ * 0.85f, $"clothoid should still be ramping at {midArc:F2}m (L_ramp={rampLen:F2}m)");
			Assert.Greater(κAtMid, κ * 0.2f);
		}

		[Test]
		public void BicycleKinematics_ResampleStraightRev_KeepsNearZeroCurvature()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, -2f), null, 0.5f, 5f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual("straight-rev", traj.DebugReason);

			var resampled = BicycleKinematics.ResampleWithSteerRamp(traj, profile.WheelBase);
			Assert.IsTrue(resampled.IsValid);
			for (int i = 0; i < resampled.PointCount; i++)
				Assert.Less(Mathf.Abs(resampled.Points[i].Curvature), 0.02f);
		}

		[Test]
		public void LocalPosePlanner_ForwardPoseTravelAligned_PrefersForwardFirst()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), 0f, 0.15f, 5f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual(TrajectoryGear.Forward, traj.Points[0].Gear);
			Assert.IsFalse((traj.DebugReason ?? "").StartsWith("rs-"), traj.DebugReason);
			Assert.Less(traj.TotalLength, 4.5f);
		}

		[Test]
		public void LocalPosePlanner_SideLongRange_StaysCompact()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			foreach (float dist in new[] { 10f, 15f })
			{
				var goal = new GoalPose(new Vector3(dist, 0f, 0f), null, 0.25f, 8f);
				var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
				Assert.IsTrue(traj.IsValid, $"dist={dist}: {planner.LastStats.Reason}");
				Assert.Less(traj.TotalLength / dist, 2.8f, $"dist={dist} detour {traj.DebugReason}");
				Assert.LessOrEqual(traj.GearSegmentCount, 3, $"dist={dist} segs");
			}
		}

		[Test]
		public void LocalPosePlanner_RearObliqueTwoMeters_StartsReverse()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float yaw = 225f;
			float rad = yaw * Mathf.Deg2Rad;
			var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
			var goal = new GoalPose(goalPos, null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual(TrajectoryGear.Reverse, traj.Points[0].Gear);
		}

		[Test]
		public void LocalPosePlanner_SideTwoMetersLeftRight_AreSymmetric()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var rightPlanner = new LocalPosePlanner();
			var leftPlanner = new LocalPosePlanner();
			var right = rightPlanner.Plan(Vector3.zero, 0f,
				new GoalPose(new Vector3(2f, 0f, 0f), 90f, 0.5f, 8f), profile, null, true, 0.55f);
			var left = leftPlanner.Plan(Vector3.zero, 0f,
				new GoalPose(new Vector3(-2f, 0f, 0f), 270f, 0.5f, 8f), profile, null, true, 0.55f);

			Assert.IsTrue(right.IsValid, rightPlanner.LastStats.Reason);
			Assert.IsTrue(left.IsValid, leftPlanner.LastStats.Reason);
			Assert.AreEqual(right.Points[0].Gear, left.Points[0].Gear,
				$"firstGear R={right.DebugReason} L={left.DebugReason}");
			Assert.AreEqual(right.GearSegmentCount, left.GearSegmentCount,
				$"segs R={right.GearSegmentCount}({right.DebugReason}) L={left.GearSegmentCount}({left.DebugReason})");
			Assert.Less(Mathf.Abs(right.TotalLength - left.TotalLength), 0.5f);
		}

		[Test]
		public void VehicleTrajectory_MarksGearCusps()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0.1f, TrajectoryGear.Reverse, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Forward, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, 0f), 0f, 0f, TrajectoryGear.Forward, 4f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 4f, 0, "test");
			Assert.IsTrue(traj.IsValid);
			Assert.AreEqual(2, traj.GearSegmentCount);
			Assert.Greater(traj.CuspIndices.Count, 0);
		}

		[Test]
		public void LocalPosePlanner_RearDiagonalHeading_StaysWithinBudget()
		{
			bool prevLog = LocalPosePlanner.DebugLog;
			LocalPosePlanner.DebugLog = false;
			try
			{
				var planner = new LocalPosePlanner();
				var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
				VehicleTrajectory rearRight = null;
				VehicleTrajectory rearLeft = null;
				foreach (float yaw in new[] { 135f, 225f })
				{
					float rad = yaw * Mathf.Deg2Rad;
					var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
					var goal = new GoalPose(goalPos, yaw, 0.5f, 10f);
					var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f);
					Assert.IsTrue(traj.IsValid, $"yaw={yaw}: {planner.LastStats.Reason}");
					Assert.IsFalse(planner.LastStats.BudgetTerminated, $"yaw={yaw} budget");
					Assert.LessOrEqual(planner.LastStats.PrimitiveCollisionQueries, 3200, $"yaw={yaw} primQ");
					Assert.LessOrEqual(planner.LastStats.TrajectoryCollisionQueries, 800, $"yaw={yaw} trajQ");
					Assert.LessOrEqual(planner.LastStats.PlanDurationMs, 5000f, $"yaw={yaw} planMs");
					Assert.LessOrEqual(planner.LastStats.AnalyticShots, 30, $"yaw={yaw} shots");

					TrajectoryPoint end = traj.Points[traj.PointCount - 1];
					Assert.LessOrEqual(
						BicycleKinematics.FlatDistance(end.Position, goalPos), goal.PositionTolerance);
					Assert.LessOrEqual(
						Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, yaw)), goal.HeadingToleranceDeg);

					for (int i = 1; i < traj.PointCount; i++)
					{
						float step = Vector3.Distance(traj.Points[i - 1].Position, traj.Points[i].Position);
						Assert.LessOrEqual(step, 0.35f, $"yaw={yaw} sample gap at {i}");
					}

					if (Mathf.Approximately(yaw, 135f))
						rearRight = traj;
					else
						rearLeft = traj;
				}

				Assert.NotNull(rearRight);
				Assert.NotNull(rearLeft);
				Assert.AreEqual(rearRight.GearSegmentCount, rearLeft.GearSegmentCount);
				Assert.Less(Mathf.Abs(rearRight.TotalLength - rearLeft.TotalLength), 0.75f);
			}
			finally
			{
				LocalPosePlanner.DebugLog = prevLog;
			}
		}

		[Test]
		public void ReedsSheppClosePoseSolver_RearDiagonal135_ReturnsValidReverseFirstPath()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float yaw = 135f;
			float rad = yaw * Mathf.Deg2Rad;
			var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
			var goal = new GoalPose(goalPos, yaw, 0.5f, 10f);

			var traj = ReedsSheppClosePoseSolver.Build(
				Vector3.zero, 0f, goal, profile.EffectiveTurnRadius, profile.WheelBase,
				out ReedsSheppClosePoseSolver.BuildStats stats);

			Assert.IsNotNull(traj, stats.ToSummary());
			Assert.IsTrue(traj.IsValid, stats.ToSummary());
			Assert.Greater(stats.FormulasGenerated, 0, stats.ToSummary());
			Assert.Greater(stats.ValidCandidates, 0, stats.ToSummary());
			Assert.LessOrEqual(traj.TotalLength, 16f);
			Assert.AreEqual(3, traj.GearSegmentCount);
			Assert.AreEqual(TrajectoryGear.Reverse, traj.Points[0].Gear);

			TrajectoryPoint end = traj.Points[traj.PointCount - 1];
			Assert.LessOrEqual(BicycleKinematics.FlatDistance(end.Position, goalPos), goal.PositionTolerance);
			Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, yaw)), goal.HeadingToleranceDeg);

			for (int i = 1; i < traj.PointCount; i++)
			{
				float step = Vector3.Distance(traj.Points[i - 1].Position, traj.Points[i].Position);
				Assert.LessOrEqual(step, 0.35f);
			}
		}

		[Test]
		public void ReedsSheppClosePoseSolver_RearDiagonal225_Mirrors135()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float yaw135 = 135f;
			float yaw225 = 225f;
			float rad135 = yaw135 * Mathf.Deg2Rad;
			float rad225 = yaw225 * Mathf.Deg2Rad;
			var goal135 = new GoalPose(
				new Vector3(Mathf.Sin(rad135) * 2f, 0f, Mathf.Cos(rad135) * 2f),
				yaw135, 0.5f, 10f);
			var goal225 = new GoalPose(
				new Vector3(Mathf.Sin(rad225) * 2f, 0f, Mathf.Cos(rad225) * 2f),
				yaw225, 0.5f, 10f);

			var right = ReedsSheppClosePoseSolver.Build(
				Vector3.zero, 0f, goal135, profile.EffectiveTurnRadius, profile.WheelBase, out _);
			var left = ReedsSheppClosePoseSolver.Build(
				Vector3.zero, 0f, goal225, profile.EffectiveTurnRadius, profile.WheelBase, out _);

			Assert.IsNotNull(right);
			Assert.IsNotNull(left);
			Assert.AreEqual(right.GearSegmentCount, left.GearSegmentCount);
			Assert.Less(Mathf.Abs(right.TotalLength - left.TotalLength), 0.75f);
			Assert.AreEqual(TrajectoryGear.Reverse, right.Points[0].Gear);
			Assert.AreEqual(TrajectoryGear.Reverse, left.Points[0].Gear);
		}

		[Test]
		public void GoalPoseValidator_PositionOnlyIgnoresHeading()
		{
			var validator = new GoalPoseValidator();
			var criteria = new GoalPoseCriteria(0.5f, 5f, 1f, 0.2f);
			bool ok = validator.Evaluate(
				new Vector3(0f, 0f, 2f), 90f, 0.2f,
				new Vector3(0f, 0f, 2f), 0f, false,
				criteria, 0.25f, out _, out float yawErr);
			Assert.IsTrue(ok);
			Assert.AreEqual(0f, yawErr, 0.001f);
		}

		[Test]
		public void PursuitController_TargetExactlyBehind_ProducesNonZeroCurvature()
		{
			var memory = new VehicleDriverMemory();
			var ctx = new NavigationContext(VehicleParameters.Default, memory);
			ctx.State = new FeedbackState(
				Vector3.zero,
				Vector3.forward,
				Vector3.right,
				0f,
				0f,
				0f,
				0f,
				false,
				true,
				false,
				false,
				true,
				default,
				memory);

			var maneuver = new ForwardManeuver();
			maneuver.SetWaypoints(new[]
			{
				Vector3.zero,
				new Vector3(0f, 0f, -20f)
			});
			ctx.Plan = new DrivingPlan(
				new System.Collections.Generic.List<Maneuver> { maneuver },
				"test",
				VehicleDrivingMode.Forward);
			ctx.CurrentManeuverIndex = 0;

			var pursuit = new PursuitController();
			PursuitController.Output output = pursuit.Tick(
				ctx, maneuver, 1f, 30f, 6f, null);

			Assert.IsTrue(output.TargetBehind);
			Assert.Greater(Mathf.Abs(output.Command.DesiredCurvature), 0.01f);
			Assert.LessOrEqual(Mathf.Abs(output.Command.DesiredSpeedKmh), 8.1f);
		}

		[Test]
		public void LocalPosePlanner_SideTwoMetersPositionOnly_UsesMultiGearCandidate()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			// Side goals inside the turning circle should reverse-first (1-seg arc-rev or 2-seg two-stage),
			// never a long forward U-turn.
			Assert.AreEqual(TrajectoryGear.Reverse, traj.Points[0].Gear,
				$"expected reverse-first side approach, got {traj.DebugReason}");
			Assert.Less(traj.TotalLength, 10f, $"side path too long: {traj.DebugReason} len={traj.TotalLength}");
			Assert.LessOrEqual(traj.GearSegmentCount, 3, $"too many gear segments: {traj.DebugReason}");
		}

		[Test]
		public void LocalPosePlanner_ObliqueTwoMeters_ProducesFeasiblePlan()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);

			foreach (float dir in new[] { 45f, 90f, 135f })
			{
				float rad = dir * Mathf.Deg2Rad;
				var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
				var goal = new GoalPose(goalPos, null, 0.5f, 5f);
				var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);
				Assert.IsTrue(traj.IsValid, $"{dir}°: {planner.LastStats.Reason}");
				Assert.Less(traj.TotalLength, 15f, $"{dir}° path too long: {traj.DebugReason}");
			}
		}

		[Test]
		public void LocalPosePlanner_CloseGoals_RuntimeSliceBudget_StillFindsAnalytic()
		{
			// Mirrors Play: 1.5ms slices, 350ms total, trackable R = Effective * 1.15 (~6.4m).
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);

			foreach (float dir in new[] { 45f, 90f, 270f, 315f })
			{
				float rad = dir * Mathf.Deg2Rad;
				var goalPos = new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f);
				var goal = new GoalPose(goalPos, null, 0.5f, 5f);
				var session = planner.CreateSession(
					Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);

				PlanStepResult result = default;
				int steps = 0;
				do
				{
					result = planner.StepPlan(session, LocalPosePlanner.RuntimeSliceBudgetMs);
					steps++;
				}
				while (result.Status == PlanStepStatus.Pending && steps < 400);

				Assert.AreEqual(PlanStepStatus.Ready, result.Status,
					$"{dir}°: status={result.Status} aGen={planner.LastStats.AnalyticGenerated} " +
					$"aVal={planner.LastStats.AnalyticValid} reason={planner.LastStats.Reason}");
				Assert.IsTrue(result.Trajectory != null && result.Trajectory.IsValid, $"{dir}° invalid traj");

				bool side = (dir >= 55f && dir <= 125f) || (dir >= 235f && dir <= 305f);
				if (side)
				{
					// Side goals must resolve via cheap two-stage, not lattice-only.
					Assert.Greater(planner.LastStats.AnalyticGenerated, 0,
						$"{dir}° side expected analytic candidates under runtime slice budget");
				}
				else
				{
					// Front-oblique @ trackable R often has no short forward CS; lattice may
					// starve on one mirror (315°) while the other (45°) lucks into a path.
					// Reverse-staging fallback after lattice is acceptable under slice budget.
					Assert.IsTrue(result.Trajectory.IsValid, $"{dir}° invalid");
					Assert.Less(result.Trajectory.TotalLength, 20f, $"{dir}° path too long");
				}
			}
		}

		[Test]
		public void TrajectoryKinematicsValidator_RejectsZeroLengthYawChange()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 0f, 0f, TrajectoryGear.Forward, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 90f, 0f, TrajectoryGear.Forward, 2f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 2f, 0, "yaw-snap");

			Assert.IsFalse(TrajectoryKinematicsValidator.Validate(traj, 6.5f, out string reason));
			Assert.IsTrue(reason.Contains("zero-length yaw"));
		}

		[Test]
		public void LocalPosePlanner_HeadingGoalTwoMeters_DoesNotYawSnap()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), 90f, 0.5f, 8f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f, 350f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.IsTrue(TrajectoryKinematicsValidator.Validate(traj, profile.EffectiveTurnRadius, out _));
			var end = traj.Points[traj.PointCount - 1];
			Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, 90f)), goal.HeadingToleranceDeg);
		}

		[Test]
		public void LocalPosePlanner_SideMirrors_90And270_PositionOnly()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal90 = new GoalPose(new Vector3(2f, 0f, 0f), null, 0.5f, 5f);
			var goal270 = new GoalPose(new Vector3(-2f, 0f, 0f), null, 0.5f, 5f);

			var right = planner.Plan(Vector3.zero, 0f, goal90, profile, null, true, 0.55f, 350f);
			var left = planner.Plan(Vector3.zero, 0f, goal270, profile, null, true, 0.55f, 350f);

			Assert.IsTrue(right.IsValid, planner.LastStats.Reason);
			Assert.IsTrue(left.IsValid, planner.LastStats.Reason);
			Assert.Less(Mathf.Abs(right.TotalLength - left.TotalLength), 1.5f);
		}

		[Test]
		public void TrajectoryKinematicsValidator_AcceptsMultiGearCuspPath()
		{
			var reverse = BicycleKinematics.Integrate(
				Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 2f, 3.5f, 0f);
			var forward = BicycleKinematics.Integrate(
				reverse.EndPosition, reverse.EndYawDegrees, 0f, TrajectoryGear.Forward, 2f, 3.5f, reverse.Length);

			var pts = new System.Collections.Generic.List<TrajectoryPoint>(reverse.Samples);
			TrajectoryPoint cusp = pts[pts.Count - 1];
			pts[pts.Count - 1] = new TrajectoryPoint(
				cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
			for (int i = 1; i < forward.Samples.Count; i++)
				pts.Add(forward.Samples[i]);

			var traj = new VehicleTrajectory();
			traj.Build(pts, 4f, 0, "cusp-test");

			Assert.IsTrue(TrajectoryKinematicsValidator.Validate(traj, 6.5f, out string reason), reason);
			Assert.GreaterOrEqual(traj.GearSegmentCount, 2);
			Assert.Greater(traj.CuspIndices.Count, 0);
		}

		[Test]
		public void ReedsSheppClosePoseSolver_RearDiagonal135_PassesKinematicValidation()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float yaw = 135f;
			float rad = yaw * Mathf.Deg2Rad;
			var goal = new GoalPose(
				new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f),
				yaw, 0.5f, 10f);

			var traj = ReedsSheppClosePoseSolver.Build(
				Vector3.zero, 0f, goal, profile.EffectiveTurnRadius, profile.WheelBase, out _);

			Assert.IsNotNull(traj);
			Assert.IsTrue(TrajectoryKinematicsValidator.Validate(traj, profile.EffectiveTurnRadius, out string reason), reason);
		}

		[Test]
		public void LocalPosePlanner_BudgetTerminatesWithinTotalCpu()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), 90f, 0.5f, 8f);
			var session = planner.CreateSession(
				Vector3.zero, 0f, goal, profile, null, true, 0.55f, 20f);

			int slices = 0;
			PlanStepResult result;
			do
			{
				result = planner.StepPlan(session, 1.5f);
				slices++;
			}
			while (result.Status == PlanStepStatus.Pending && slices < 500);

			// Allow one overrunning slice after the 20ms cap trips; wall-time must not dominate.
			Assert.LessOrEqual(planner.LastStats.PlanDurationMs, 50f);
			Assert.LessOrEqual(session.TotalPlanCpuMs, 50f);
			Assert.Less(slices, 40);
			Assert.IsTrue(
				result.Status == PlanStepStatus.Failed ||
				result.Status == PlanStepStatus.Ready ||
				session.BudgetTerminated);
			Assert.IsTrue(session.BudgetTerminated || result.Status != PlanStepStatus.Pending);
		}

		[Test]
		public void LocalPosePlanner_StraightReverseTwoMeters_SoftCpuBound()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, -2f), null, 0.5f, 5f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f, 350f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual("straight-rev", traj.DebugReason);
			Assert.AreEqual(TrajectoryGear.Reverse, traj.Points[0].Gear);
			Assert.LessOrEqual(traj.TotalLength, 3f);
			Assert.Less(planner.LastStats.PlanDurationMs, 40f);
		}

		[Test]
		public void LocalPosePlanner_PauseBetweenSlices_DoesNotBurnCpuBudget()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			// Forward pose goal keeps first slice light; assertion is about the Sleep gap, not absolute slice cost.
			var goal = new GoalPose(new Vector3(0f, 0f, 3f), 0f, 0.5f, 8f);
			var session = planner.CreateSession(
				Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);

			PlanStepResult first = planner.StepPlan(session, 1.5f);
			float cpuAfterFirst = session.TotalPlanCpuMs;
			Assert.Greater(cpuAfterFirst, 0f);

			System.Threading.Thread.Sleep(50);
			float cpuBeforeSecond = session.TotalPlanCpuMs;
			Assert.AreEqual(cpuAfterFirst, cpuBeforeSecond, 0.01f,
				"Sleep must not increase AccumulatedCpuMs");

			PlanStepResult second = planner.StepPlan(session, 1.5f);
			float cpuAfterSecond = session.TotalPlanCpuMs;

			Assert.Less(cpuAfterSecond - cpuAfterFirst, 40f,
				"second slice may add work, but not the 50ms wall-clock pause");
			Assert.IsTrue(
				first.Status == PlanStepStatus.Pending ||
				first.Status == PlanStepStatus.Ready ||
				second.Status == PlanStepStatus.Pending ||
				second.Status == PlanStepStatus.Ready ||
				second.Status == PlanStepStatus.Failed);
		}

		[Test]
		public void LocalPosePlanner_CheapCandidates_ExistUnderTightBudget()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, -2f), null, 0.5f, 5f);
			// <=25ms disables heavy analytic; cheap straight-rev must still appear.
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f, 20f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual("straight-rev", traj.DebugReason);
			Assert.Greater(planner.LastStats.CandidatesGenerated, 0);
		}

		[Test]
		public void LocalPosePlanner_ForceFinalize_CompletesWithoutRestart()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), 90f, 0.5f, 8f);
			var session = planner.CreateSession(
				Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);

			planner.StepPlan(session, 1.5f);
			Assert.IsTrue(session.IsActive);

			PlanStepResult finalized = planner.ForceFinalize(session, "wall timeout");
			Assert.IsFalse(session.IsActive);
			Assert.AreEqual("wall timeout", session.BudgetReason);
			Assert.IsTrue(
				finalized.Status == PlanStepStatus.Ready ||
				finalized.Status == PlanStepStatus.Failed);
			Assert.IsTrue(session.BudgetTerminated);
		}

		[Test]
		public void LocalPosePlanner_LatticeAcceptsGoalPositionTolerance()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			// Position tolerance wider than internal 0.25 m planning resolution.
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.5f, 5f);

			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			var end = traj.Points[traj.PointCount - 1];
			Assert.LessOrEqual(
				BicycleKinematics.FlatDistance(end.Position, goal.Position),
				goal.PositionTolerance);
		}

		[Test]
		public void ReedsSheppPathBuilder_EndpointCorrection_DoesNotYawTeleport()
		{
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), 5f, 0.5f, 8f);
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 0f, 0f, TrajectoryGear.Forward, 2f)
			};

			ReedsSheppPathBuilder.TrySnapTrajectoryEnd(pts, goal);

			float ds = BicycleKinematics.FlatDistance(pts[0].Position, pts[1].Position);
			float dyaw = Mathf.Abs(Mathf.DeltaAngle(pts[0].YawDegrees, pts[1].YawDegrees));
			Assert.IsFalse(ds < 0.02f && dyaw > 1f, "endpoint correction must not create zero-length yaw teleport");

			var traj = new VehicleTrajectory();
			traj.Build(pts, 2f, 0, "snap-test");
			Assert.IsTrue(TrajectoryKinematicsValidator.Validate(traj, 6.5f, out string reason), reason);
		}

		[Test]
		public void TrajectoryTracker_InsideStrictTolerance_BrakesWithoutCompleteOrReplan()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 0f, 0f, TrajectoryGear.Forward, 2f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 2f, 0, "straight-fwd");
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.1f, 5f);

			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.5f, 4f);
			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));
			const float speedKmh = 4f;
			var output = tracker.Tick(
				new Vector3(0f, 0f, 1.95f), 0f, speedKmh, p, 1f);

			Assert.IsTrue(output.RequestTerminalBrake);
			Assert.IsFalse(output.NeedPathReplan);
			Assert.IsFalse(output.IsComplete);
			Assert.AreEqual(StopIntent.Goal, output.Command.StopIntent);
			// Soft settle: creep/slew down instead of DesiredSpeed=0 slam.
			Assert.Less(output.Command.DesiredSpeedKmh, speedKmh);
			Assert.LessOrEqual(output.Command.DesiredSpeedKmh, 2.01f);
		}

		[Test]
		public void TrajectoryTracker_NearGoalPathEnd_CreepsWithoutFreezeOrReplan()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 0f, 0f, TrajectoryGear.Forward, 2f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 2f, 0, "straight-fwd");
			var goal = new GoalPose(new Vector3(0f, 0f, 2.32f), null, 0.1f, 5f);

			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.5f, 1f);
			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));
			// Path end at ~0.32 m — must creep, not hard-freeze or replan.
			var output = tracker.Tick(
				new Vector3(0f, 0f, 2f), 0f, 0.2f, p, 1f);

			Assert.IsFalse(output.NeedPathReplan);
			Assert.IsFalse(output.RequestTerminalBrake);
			Assert.IsFalse(output.IsComplete);
			Assert.Greater(output.Command.DesiredSpeedKmh, 0.5f);
		}

		[Test]
		public void TrajectoryTracker_LookAhead_InterpolatesAlongArc()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, 1f), 0f, 0f, TrajectoryGear.Forward, 1f),
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 0f, 0f, TrajectoryGear.Forward, 2f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 2f, 0, "straight-fwd");
			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.1f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 0.5f, 0f);
			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));
			var output = tracker.Tick(Vector3.zero, 0f, 2f, p, 1f);
			Assert.Greater(output.LookAheadPoint.z, 0.3f);
			Assert.Less(output.LookAheadPoint.z, 1.2f);
		}

		[Test]
		public void GoalPoseValidator_AbsSpeedAndStrictPositionResetTimer()
		{
			var validator = new GoalPoseValidator();
			var criteria = new GoalPoseCriteria(0.1f, 5f, 1f, 0.4f);
			Vector3 goal = new Vector3(0f, 0f, 2f);

			Assert.IsFalse(validator.Evaluate(
				new Vector3(0f, 0f, 1.95f), 0f, 0.5f, goal, 0f, false, criteria, 0.2f, out _, out _));

			// Outside position tolerance resets window.
			Assert.IsFalse(validator.Evaluate(
				new Vector3(0f, 0f, 1.89f), 0f, 0.5f, goal, 0f, false, criteria, 0.2f, out _, out _));

			validator.Reset();
			Assert.IsFalse(validator.Evaluate(
				new Vector3(0f, 0f, 1.95f), 0f, -2f, goal, 0f, false, criteria, 0.5f, out _, out _),
				"negative speed above max must fail Abs(speed) check");

			validator.Reset();
			Assert.IsTrue(validator.Evaluate(
				new Vector3(0f, 0f, 1.95f), 0f, 0.5f, goal, 0f, false, criteria, 0.5f, out _, out _));
		}

		[Test]
		public void LocalPosePlanner_ReverseOblique135And225_ProduceValidPlans()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);

			foreach (float yaw in new[] { 135f, 225f })
			{
				float rad = yaw * Mathf.Deg2Rad;
				var goal = new GoalPose(
					new Vector3(Mathf.Sin(rad) * 2f, 0f, Mathf.Cos(rad) * 2f),
					null, 0.25f, 5f);
				var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);
				Assert.IsTrue(traj.IsValid, $"{yaw}: {planner.LastStats.Reason}");
				var end = traj.Points[traj.PointCount - 1];
				Assert.LessOrEqual(
					BicycleKinematics.FlatDistance(end.Position, goal.Position),
					0.25f,
					$"{yaw} endpoint {traj.DebugReason}");
			}
		}

		[Test]
		public void LocalPosePlanner_RequiredHeading_NeverReturnsPartial()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), 90f, 0.25f, 8f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 40f);
			if (traj.IsValid)
				Assert.IsFalse(
					(traj.DebugReason ?? string.Empty).Contains("partial"),
					traj.DebugReason);
		}

		[Test]
		public void LocalPosePlanner_ForceFinalize_KeepsPriorValidatedCandidate()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, -2f), null, 0.25f, 5f);
			var session = planner.CreateSession(
				Vector3.zero, 0f, goal, profile, null, true, 0.6f, 350f);

			PlanStepResult step = planner.StepPlan(session, 5f);
			PlanStepResult finalized = planner.ForceFinalize(session, "wall timeout");
			Assert.IsFalse(session.IsActive);
			if (step.Status == PlanStepStatus.Ready)
			{
				Assert.AreEqual(PlanStepStatus.Ready, finalized.Status);
				Assert.IsNotNull(finalized.Trajectory);
				Assert.IsTrue(finalized.Trajectory.IsValid);
			}
			else
			{
				Assert.IsTrue(
					finalized.Status == PlanStepStatus.Ready ||
					finalized.Status == PlanStepStatus.Failed);
			}
		}

		[Test]
		public void LocalPosePlanner_SideTwentyMeters_PrefersRepositionOverShortArc()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(20f, 0f, 0f), null, 0.45f, 5f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			string reason = traj.DebugReason ?? string.Empty;
			Assert.IsTrue(
				IsRepositionFamily(reason, traj),
				$"expected reposition family, got {reason} len={traj.TotalLength:F1}");
			Assert.IsFalse(
				IsShortChordForwardArc(traj, 20f),
				$"short forward chord must not win side 20m ({reason})");
			float ratio = traj.TotalLength / 20f;
			Assert.Greater(ratio, 0.55f, $"path too short for side 20m: ratio={ratio:F2} ({reason})");
		}

		[Test]
		public void LocalPosePlanner_UTurnTwentyMeters_PrefersLoopOverShortArc()
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			float rad = 135f * Mathf.Deg2Rad;
			var goal = new GoalPose(
				new Vector3(Mathf.Sin(rad) * 20f, 0f, Mathf.Cos(rad) * 20f),
				null, 0.45f, 5f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			string reason = traj.DebugReason ?? string.Empty;
			Assert.IsTrue(
				IsRepositionFamily(reason, traj),
				$"expected loop/reposition, got {reason} len={traj.TotalLength:F1}");
			Assert.IsFalse(
				IsShortChordForwardArc(traj, 20f),
				$"short forward chord must not win UTurn 20m ({reason})");
			Assert.Greater(traj.TotalLength / 20f, 0.55f);
		}

		[Test]
		public void LocalPosePlanner_RearDiagonalFiveMeters_RuntimeSliceBudget_FindsPlan()
		{
			// Rear-oblique only (135/225). 315° is front-oblique — covered separately.
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);

			foreach (float yaw in new[] { 135f, 225f })
			{
				float rad = yaw * Mathf.Deg2Rad;
				var goalPos = new Vector3(Mathf.Sin(rad) * 5f, 0f, Mathf.Cos(rad) * 5f);
				var goal = new GoalPose(goalPos, null, 0.45f, 5f);
				var session = planner.CreateSession(
					Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);

				PlanStepResult result = default;
				int steps = 0;
				do
				{
					result = planner.StepPlan(session, LocalPosePlanner.RuntimeSliceBudgetMs);
					steps++;
				}
				while (result.Status == PlanStepStatus.Pending && steps < 400);

				Assert.AreEqual(PlanStepStatus.Ready, result.Status,
					$"{yaw}°: status={result.Status} aGen={planner.LastStats.AnalyticGenerated} " +
					$"reason={planner.LastStats.Reason}");
				Assert.IsTrue(result.Trajectory != null && result.Trajectory.IsValid, $"{yaw}° invalid");
				Assert.Greater(result.Trajectory.TotalLength, 0.5f, $"{yaw}° empty plan");
				Assert.Less(result.Trajectory.TotalLength, 25f, $"{yaw}° path too long");
			}
		}

		[Test]
		public void LocalPosePlanner_FrontObliqueFiveMeters_RuntimeSliceBudget_FindsPlan()
		{
			// Mirror of Play Forward_315deg_5m / Forward_45deg_5m under 1.5ms slices.
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);

			foreach (float yaw in new[] { 45f, 315f })
			{
				float rad = yaw * Mathf.Deg2Rad;
				var goalPos = new Vector3(Mathf.Sin(rad) * 5f, 0f, Mathf.Cos(rad) * 5f);
				var goal = new GoalPose(goalPos, null, 0.45f, 5f);
				var session = planner.CreateSession(
					Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);

				PlanStepResult result = default;
				int steps = 0;
				do
				{
					result = planner.StepPlan(session, LocalPosePlanner.RuntimeSliceBudgetMs);
					steps++;
				}
				while (result.Status == PlanStepStatus.Pending && steps < 400);

				Assert.AreEqual(PlanStepStatus.Ready, result.Status,
					$"{yaw}°: status={result.Status} aGen={planner.LastStats.AnalyticGenerated} " +
					$"reason={planner.LastStats.Reason}");
				Assert.IsTrue(result.Trajectory != null && result.Trajectory.IsValid, $"{yaw}° invalid");
				Assert.Greater(result.Trajectory.TotalLength, 0.5f, $"{yaw}° empty plan");
				Assert.Less(result.Trajectory.TotalLength, 30f, $"{yaw}° path too long");
			}
		}

		private static bool IsRepositionFamily(string _reason, VehicleTrajectory _traj)
		{
			if (_traj == null || !_traj.IsValid)
				return false;
			if (_reason.Contains("two-stage") || _reason.Contains("three-point") ||
			    _reason.Contains("one-cusp") || _reason.Contains("rev-staging") ||
			    _reason.StartsWith("rs-") || _reason.Contains("merged"))
				return true;
			return _traj.GearSegmentCount > 1;
		}

		private static bool IsShortChordForwardArc(VehicleTrajectory _traj, float _directDist)
		{
			if (_traj == null || !_traj.IsValid || _traj.GearSegmentCount > 1)
				return false;
			if (_traj.PointCount == 0 || _traj.Points[0].Gear != TrajectoryGear.Forward)
				return false;
			string reason = _traj.DebugReason ?? string.Empty;
			if (!(reason.Contains("arc-fwd") || reason.Contains("cs-fwd")))
				return false;
			return _traj.TotalLength / Mathf.Max(0.5f, _directDist) < 0.7f;
		}
	}
}
