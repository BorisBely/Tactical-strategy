using NUnit.Framework;
using UnityEngine;

namespace VehicleNavigation.Tests
{
	public class NavigationStabilizationTests
	{
		[Test]
		public void PathResult_LastSegmentTangent_UsesFinalNonZeroSegment()
		{
			var path = new PathResult(
				new[]
				{
					Vector3.zero,
					new Vector3(0f, 0f, 4f),
					new Vector3(3f, 0f, 4f)
				},
				7f,
				true,
				false);

			Assert.IsTrue(path.TryGetLastSegmentTangent(out float yaw));
			Assert.AreEqual(90f, yaw, 0.1f);
		}

		[Test]
		public void NavigationRequest_PathTangentHeading_DoesNotOverrideExplicit()
		{
			var explicitReq = NavigationRequest.FromPositionAndHeading(
				new Vector3(1f, 0f, 2f), 45f, VehicleSpeedMode.Medium);
			var overridden = explicitReq.WithPathTangentHeading(180f);

			Assert.AreEqual(GoalHeadingSource.RequiredExplicit, overridden.HeadingSource);
			Assert.AreEqual(45f, overridden.HeadingYaw.Value, 0.01f);
		}

		[Test]
		public void NavigationRequest_PathTangentHeading_SetsSourceAndYaw()
		{
			var req = NavigationRequest.FromPosition(Vector3.one, VehicleSpeedMode.Medium);
			var tangent = req.WithPathTangentHeading(120f);

			Assert.AreEqual(GoalHeadingSource.SoftPathTangent, tangent.HeadingSource);
			Assert.IsTrue(tangent.HasAdvisoryHeading);
			Assert.AreEqual(120f, tangent.HeadingYaw.Value, 0.01f);
			Assert.IsFalse(tangent.RequiresPosePlanning);
		}

		[Test]
		public void VehicleParameters_ComfortDecel_IsBelowHardDecel()
		{
			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));

			Assert.Less(p.ComfortBrakeDecelMs2, p.HardBrakeDecelMs2);
			Assert.GreaterOrEqual(p.ComfortBrakeDecelMs2, 0.8f);
		}

		[Test]
		public void TrajectoryTracker_GoalApproach_UsesComfortStopProfile()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, 8f), 0f, 0f, TrajectoryGear.Forward, 8f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 8f, 0, "straight-fwd");

			var goal = new GoalPose(new Vector3(0f, 0f, 8f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 3f);

			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));

			var output = tracker.Tick(new Vector3(0f, 0f, 5f), 0f, 18f, p, 1f);
			Assert.AreEqual(StopIntent.Goal, output.Command.StopIntent);
			Assert.Less(output.Command.DesiredSpeedKmh, 18f);
			Assert.Greater(output.Command.DesiredSpeedKmh, 0f);
		}

		[Test]
		public void TrajectoryTracker_CuspApproach_RequestsGearChangeStopIntent()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Reverse, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Forward, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, 4f), 0f, 0f, TrajectoryGear.Forward, 6f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 6f, 0, "cusp-test");

			var goal = new GoalPose(new Vector3(0f, 0f, 4f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));

			var output = tracker.Tick(new Vector3(0f, 0f, -0.2f), 0f, 8f, p, 1f);
			Assert.AreEqual(TrajectoryGear.Reverse, output.ActiveGear);
			Assert.AreEqual(StopIntent.GearChange, output.Command.StopIntent);
		}

		[Test]
		public void GoalPose_SoftTangent_CompletesOnPositionOnly()
		{
			var goal = new GoalPose(new Vector3(1f, 0f, 0f), 90f, GoalHeadingSource.SoftPathTangent, 0.5f, 5f);
			Assert.IsFalse(goal.RequiresPosePlanning);
			Assert.IsTrue(goal.HasAdvisoryHeading);
			Assert.IsTrue(goal.IsReached(new Vector3(1f, 0f, 0f), 0f));
		}

		[Test]
		public void ArrivalPositionBand_Oval_TightLongLooseLat()
		{
			Vector3 goal = new Vector3(0f, 0f, 5f);
			const float yaw = 0f; // forward +Z

			// 0.08m short along chassis — inside longitudinal 0.1
			Assert.IsTrue(ArrivalPositionBand.IsInside(
				new Vector3(0f, 0f, 4.92f), yaw, goal, 0.1f, 0.45f));

			// 0.20m short — outside longitudinal even though Euclidean < 0.45
			Assert.IsFalse(ArrivalPositionBand.IsInside(
				new Vector3(0f, 0f, 4.80f), yaw, goal, 0.1f, 0.45f));

			// 0.40m to the right — inside lateral 0.45
			Assert.IsTrue(ArrivalPositionBand.IsInside(
				new Vector3(-0.40f, 0f, 5f), yaw, goal, 0.1f, 0.45f));

			// 0.50m to the right — outside lateral
			Assert.IsFalse(ArrivalPositionBand.IsInside(
				new Vector3(-0.50f, 0f, 5f), yaw, goal, 0.1f, 0.45f));
		}

		[Test]
		public void GoalPose_Oval_IsReachedUsesVehicleFrame()
		{
			var goal = new GoalPose(
				new Vector3(0f, 0f, 10f),
				null,
				GoalHeadingSource.None,
				0.1f,
				0.45f,
				5f);

			Assert.AreEqual(0.45f, goal.PositionTolerance, 0.001f);
			Assert.IsTrue(goal.IsReached(new Vector3(0.3f, 0f, 10f), 0f));
			Assert.IsFalse(goal.IsReached(new Vector3(0f, 0f, 9.7f), 0f));
		}

		[Test]
		public void LocalPosePlanner_SideTwoMeters_MirrorPairFindPaths()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var planner = new LocalPosePlanner();

			var left = planner.Plan(Vector3.zero, 0f,
				new GoalPose(new Vector3(2f, 0f, 0f), null, 0.5f, 8f),
				profile, null, true, 0.55f, 0f);
			var right = planner.Plan(Vector3.zero, 0f,
				new GoalPose(new Vector3(-2f, 0f, 0f), null, 0.5f, 8f),
				profile, null, true, 0.55f, 0f);

			Assert.IsTrue(left.IsValid, left.DebugReason);
			Assert.IsTrue(right.IsValid, right.DebugReason);
			Assert.Greater(left.TotalLength, 2f);
			Assert.Greater(right.TotalLength, 2f);
		}

		[Test]
		public void LocalPlanningSession_SideGoal_CanResumeAfterBudget()
		{
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var planner = new LocalPosePlanner();
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), null, 0.5f, 8f);
			var session = planner.CreateSession(Vector3.zero, 0f, goal, profile, null, true, 0.55f, 350f);

			PlanStepResult result = PlanStepResult.Pending(0f, 0);
			int slices = 0;
			while (result.Status == PlanStepStatus.Pending && slices < 250)
			{
				result = planner.StepPlan(session, 1.5f);
				slices++;
			}

			Assert.AreNotEqual(PlanStepStatus.Pending, result.Status, $"still pending after {slices} slices");
			Assert.IsTrue(result.Trajectory != null && result.Trajectory.IsValid, planner.LastStats.Reason);
		}

		[Test]
		public void TrajectoryTracker_DoesNotSkipFutureCuspSegment()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>();
			for (int i = 0; i <= 40; i++)
				pts.Add(new TrajectoryPoint(new Vector3(0f, 0f, i * 0.2f), 0f, 0f, TrajectoryGear.Forward, i * 0.2f));
			pts.Add(new TrajectoryPoint(new Vector3(0f, 0f, 8f), 0f, 0f, TrajectoryGear.Reverse, 8f, true));
			for (int i = 1; i <= 30; i++)
				pts.Add(new TrajectoryPoint(new Vector3(0f, 0f, 8f - i * 0.2f), 180f, 0f, TrajectoryGear.Reverse, 8f + i * 0.2f));

			var traj = new VehicleTrajectory();
			traj.Build(pts, 14f, 0, "cusp-skip-test");

			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.5f);
			var p = new VehicleParameters(
				4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));

			int startIndex = tracker.CurrentIndex;
			for (int t = 0; t < 5; t++)
			{
				float z = 3f + t * 0.05f;
				var output = tracker.Tick(new Vector3(0f, 0f, z), 0f, 8f, p, 1f);
				Assert.Less(output.NearestIndex, traj.FindNextCusp(startIndex) + 2);
			}
		}
	}
}
