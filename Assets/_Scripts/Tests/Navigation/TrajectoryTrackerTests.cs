using NUnit.Framework;
using UnityEngine;

namespace VehicleNavigation.Tests
{
	public class TrajectoryTrackerTests
	{
		private static VehicleParameters TestParams =>
			new VehicleParameters(4.8f, 2.4f, 3.5f, 30f, 15f, 32f, 120f, 5.5f, null,
				new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f));

		private static VehicleTrajectory PlanStraightReverse(float distanceMeters)
		{
			var planner = new LocalPosePlanner();
			var profile = new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
			var goal = new GoalPose(new Vector3(0f, 0f, -distanceMeters), null, 0.5f, 5f);
			var traj = planner.Plan(Vector3.zero, 0f, goal, profile, null, true, 0.6f);
			Assert.IsTrue(traj.IsValid, planner.LastStats.Reason);
			Assert.AreEqual("straight-rev", traj.DebugReason);
			return traj;
		}

		private static void SimulateBicycleStep(
			ref Vector3 _pos,
			ref float _yaw,
			ref float _speedKmh,
			MotionCommand _cmd,
			float _dt)
		{
			_speedKmh = Mathf.MoveTowards(_speedKmh, _cmd.DesiredSpeedKmh, 25f * _dt);
			float signedSpeedMs = _cmd.Reverse ? -Mathf.Abs(_speedKmh) : Mathf.Abs(_speedKmh);
			signedSpeedMs /= 3.6f;

			float yawRateDeg = signedSpeedMs * _cmd.DesiredCurvature * Mathf.Rad2Deg;
			_yaw = BicycleKinematics.NormalizeYaw(_yaw + yawRateDeg * _dt);

			float yawRad = _yaw * Mathf.Deg2Rad;
			_pos += new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad)) * signedSpeedMs * _dt;
		}

		[Test]
		public void TrajectoryTracker_StraightReverse_KeepsSteerNearZero()
		{
			var traj = PlanStraightReverse(5f);
			var goal = new GoalPose(new Vector3(0f, 0f, -5f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);

			Vector3 pos = Vector3.zero;
			float yaw = 0f;
			float speed = 0f;
			float maxAbsWheelCurv = 0f;
			float maxAbsSteer = 0f;
			var p = TestParams;

			for (int i = 0; i < 400; i++)
			{
				var output = tracker.Tick(pos, yaw, speed, p, 1f);
				maxAbsWheelCurv = Mathf.Max(maxAbsWheelCurv, Mathf.Abs(output.WheelCurvature));
				maxAbsSteer = Mathf.Max(maxAbsSteer, Mathf.Abs(Mathf.Atan(p.WheelBase * output.Command.DesiredCurvature) / p.MaxSteeringAngleRad));
				SimulateBicycleStep(ref pos, ref yaw, ref speed, output.Command, 0.05f);
				if (output.IsComplete)
					break;
			}

			Assert.Less(maxAbsWheelCurv, 0.08f, "reverse straight should not command large curvature");
			Assert.Less(maxAbsSteer, 0.15f, "reverse straight should not saturate steering");
			Assert.Less(Mathf.Abs(yaw), 2f, "yaw drift on straight reverse");
		}

		[Test]
		public void TrajectoryTracker_StraightReverse_PerturbationConverges()
		{
			var traj = PlanStraightReverse(5f);
			var goal = new GoalPose(new Vector3(0f, 0f, -5f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);

			Vector3 pos = new Vector3(0.25f, 0f, -0.5f);
			float yaw = 4f;
			float speed = 0f;
			float initialCross = float.MaxValue;
			var p = TestParams;

			for (int i = 0; i < 300; i++)
			{
				var output = tracker.Tick(pos, yaw, speed, p, 1f);
				if (i == 0)
					initialCross = Mathf.Abs(output.CrossTrack);
				SimulateBicycleStep(ref pos, ref yaw, ref speed, output.Command, 0.05f);
			}

			var final = tracker.Tick(pos, yaw, speed, p, 1f);
			Assert.Less(Mathf.Abs(final.CrossTrack), initialCross + 0.05f);
		}

		[Test]
		public void TrajectoryTracker_MirrorOffsets_CommandOppositeSteer()
		{
			var traj = PlanStraightReverse(5f);
			var goal = new GoalPose(new Vector3(0f, 0f, -5f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = TestParams;

			var left = tracker.Tick(new Vector3(-0.2f, 0f, 0f), 0f, 5f, p, 1f);
			tracker.Activate(traj, goal, 2f);
			var right = tracker.Tick(new Vector3(0.2f, 0f, 0f), 0f, 5f, p, 1f);

			Assert.Greater(Mathf.Abs(left.Command.DesiredCurvature), 0.0001f);
			Assert.Greater(Mathf.Abs(right.Command.DesiredCurvature), 0.0001f);
			Assert.Less(left.Command.DesiredCurvature * right.Command.DesiredCurvature, 0f);
		}

		[Test]
		public void TrajectoryTracker_AfterSteerReset_FirstCommandNearZeroOnStraightReverse()
		{
			var traj = PlanStraightReverse(5f);
			var goal = new GoalPose(new Vector3(0f, 0f, -5f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = TestParams;

			tracker.Tick(new Vector3(0.15f, 0f, 0f), 0f, 8f, p, 1f);
			var afterReset = tracker.Tick(Vector3.zero, 0f, 8f, p, 1f);
			Assert.Less(Mathf.Abs(afterReset.Command.DesiredCurvature), 0.05f);
		}

		[Test]
		public void TrajectoryTracker_LookaheadStaysInCurrentGearSegment()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Reverse, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Forward, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, 2f), 0f, 0f, TrajectoryGear.Forward, 6f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 6f, 0, "test-cusp");
			Assert.IsTrue(traj.IsValid);

			var goal = new GoalPose(new Vector3(0f, 0f, 2f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 3f);
			var p = TestParams;

			var output = tracker.Tick(new Vector3(0f, 0f, -0.5f), 0f, 4f, p, 1f);
			Assert.AreEqual(TrajectoryGear.Reverse, output.ActiveGear);
			Assert.LessOrEqual(output.LookAheadPoint.z, 0.05f, "lookahead should not jump into forward segment before cusp stop");
		}
		[Test]
		public void TrajectoryTracker_LoopPath_DoesNotJumpToSegmentEnd()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>();
			float arc = 0f;
			Vector3 loopPos = new Vector3(0f, 0f, 1.5f);
			for (int i = 0; i <= 10; i++)
			{
				pts.Add(new TrajectoryPoint(new Vector3(0f, 0f, i * 0.3f), 0f, 0f, TrajectoryGear.Forward, arc));
				arc += 0.3f;
			}

			for (int i = 1; i <= 30; i++)
			{
				float t = i / 30f;
				var samplePos = Vector3.Lerp(new Vector3(0f, 0f, 3f), loopPos, t);
				pts.Add(new TrajectoryPoint(samplePos, 0f, 0f, TrajectoryGear.Forward, arc));
				arc += 0.12f;
			}

			pts[pts.Count - 1] = new TrajectoryPoint(loopPos, 0f, 0f, TrajectoryGear.Forward, arc);

			var traj = new VehicleTrajectory();
			traj.Build(pts, arc, 0, "loop-test");
			Assert.IsTrue(traj.IsValid);

			var goal = new GoalPose(loopPos, null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = TestParams;

			Vector3 pos = Vector3.zero;
			float speed = 6f;
			for (int i = 0; i < 25; i++)
			{
				tracker.Tick(pos, 0f, speed, p, 1f);
				pos = new Vector3(0f, 0f, Mathf.Min(1.5f, i * 0.08f));
			}

			int idxBefore = tracker.CurrentIndex;
			Assert.LessOrEqual(idxBefore, 12, "setup: index should be in first half of path");

			var output = tracker.Tick(loopPos, 0f, speed, p, 1f);
			Assert.Less(output.NearestIndex, idxBefore + 8,
				"spatial duplicate at segment end must not pull index to path end");
			Assert.Less(output.NearestIndex, 20);
		}

		[Test]
		public void TrajectoryTracker_NeedPathReplan_DoesNotSetWaitingForStop()
		{
			var traj = PlanStraightReverse(5f);
			var goal = new GoalPose(new Vector3(0f, 0f, -5f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = TestParams;

			var output = tracker.Tick(new Vector3(3f, 0f, 0f), 0f, 0f, p, 1f);
			Assert.IsTrue(output.NeedPathReplan);
			Assert.IsFalse(output.WaitingForStop);
		}

		[Test]
		public void TrajectoryTracker_MicroCreepGear_DoesNotChatter()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 0f, false),
				new TrajectoryPoint(new Vector3(0.35f, 0f, 0f), 0f, 0f, TrajectoryGear.Forward, 0.35f, false)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 0.35f, 0, "test");

			var goal = new GoalPose(new Vector3(0.4f, 0f, 0f), 90f, 0.1f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.5f);
			var p = TestParams;

			TrajectoryGear latchedGear = TrajectoryGear.Forward;
			int flips = 0;
			for (int i = 0; i < 24; i++)
			{
				float yaw = i % 2 == 0 ? 88f : 92f;
				var output = tracker.Tick(new Vector3(0.36f, 0f, 0f), yaw, 0f, p, 1f);
				if (i == 0)
					latchedGear = output.ActiveGear;
				else if (output.ActiveGear != latchedGear)
					flips++;
			}

			Assert.AreEqual(0, flips, "micro-creep gear should stay latched across yaw noise");
		}

		[Test]
		public void TrajectoryTracker_PositionOnly_CompletesWithoutGearFlipAtGoal()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 0f, false),
				new TrajectoryPoint(new Vector3(2f, 0f, 0f), 35f, 0.1f, TrajectoryGear.Reverse, 2.2f, false)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 2.2f, 0, "two-stage-side");

			var goal = new GoalPose(new Vector3(2f, 0f, 0f), null, 0.1f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.5f);
			var p = TestParams;

			int reverseCmds = 0;
			bool completed = false;
			for (int i = 0; i < 20; i++)
			{
				var output = tracker.Tick(new Vector3(2.05f, 0f, 0.02f), 34f, 0.2f, p, 1f);
				if (output.Command.Reverse)
					reverseCmds++;
				if (output.IsComplete)
				{
					completed = true;
					break;
				}
			}

			Assert.IsTrue(completed, "position-only arrival must complete at tolerance");
			Assert.AreEqual(0, reverseCmds, "must not hunt reverse after position-only arrival");
		}

		private static VehicleTrajectory BuildStraightThenSharpTurn()
		{
			const float wb = 3.5f;
			var straight = BicycleKinematics.Integrate(
				Vector3.zero, 0f, 0f, TrajectoryGear.Forward, 1.5f, wb, 0f);
			float straightArc = straight.Samples[straight.Samples.Count - 1].ArcLength;
			var arc = BicycleKinematics.Integrate(
				straight.EndPosition, straight.EndYawDegrees, 0.2f, TrajectoryGear.Forward, 5f, wb,
				straightArc);
			var pts = new System.Collections.Generic.List<TrajectoryPoint>();
			for (int i = 0; i < straight.Samples.Count; i++)
				pts.Add(straight.Samples[i]);
			for (int i = 1; i < arc.Samples.Count; i++)
				pts.Add(arc.Samples[i]);
			var traj = new VehicleTrajectory();
			traj.Build(pts, 6.5f, 0, "test-sharp-entry");
			Assert.IsTrue(traj.IsValid);
			return traj;
		}

		[Test]
		public void TrajectoryTracker_MisalignedYaw_HoldsTurnEntry()
		{
			var traj = BuildStraightThenSharpTurn();
			int midTurn = -1;
			for (int i = 0; i < traj.PointCount; i++)
			{
				if (Mathf.Abs(traj.Points[i].Curvature) > 0.08f &&
				    Mathf.Abs(traj.Points[i].YawDegrees) > 15f)
				{
					midTurn = i;
					break;
				}
			}

			Assert.Greater(midTurn, 0, "path must contain a high-κ sample with yaw>15°");

			var goal = new GoalPose(traj.Points[traj.PointCount - 1].Position, null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = TestParams;

			// Body still at yaw=0 while path tangent has already rotated — classic early entry.
			Vector3 pos = traj.Points[midTurn].Position;
			float yaw = 0f;
			tracker.Tick(pos, yaw, 3f, p, 1f);
			var gated = tracker.Tick(pos, yaw, 3f, p, 1f);
			Assert.IsTrue(tracker.TurnEntryGateActive, "gate should hold while yaw lags path tangent");
			Assert.Less(tracker.CurrentIndex, midTurn + 1, "index must not advance past misaligned high-κ");
			Assert.LessOrEqual(gated.Command.DesiredSpeedKmh, 4.5f, "gate speed should be capped");

			int idxHeld = tracker.CurrentIndex;
			for (int i = 0; i < 12; i++)
				tracker.Tick(pos, yaw, 3f, p, 1f);

			Assert.IsTrue(tracker.TurnEntryGateActive, "gate remains while yaw stays misaligned");
			Assert.LessOrEqual(tracker.CurrentIndex, idxHeld + 2, "index hold while gated");
		}

		[Test]
		public void TrajectoryTracker_StraightReverse_TurnEntryGateStaysOff()
		{
			var traj = PlanStraightReverse(5f);
			var goal = new GoalPose(new Vector3(0f, 0f, -5f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 2f);
			var p = TestParams;

			Vector3 pos = Vector3.zero;
			float yaw = 0f;
			float speed = 0f;
			for (int i = 0; i < 80; i++)
			{
				var output = tracker.Tick(pos, yaw, speed, p, 1f);
				Assert.IsFalse(tracker.TurnEntryGateActive, "straight-rev must not engage turn-entry gate");
				SimulateBicycleStep(ref pos, ref yaw, ref speed, output.Command, 0.05f);
				if (output.IsComplete)
					break;
			}
		}

		[Test]
		public void TrajectoryTracker_HighCrossTrack_OnImprovingPath_SuppressesReplan()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>();
			for (int i = 0; i <= 20; i++)
			{
				float t = i / 20f;
				float yaw = t * 60f;
				float rad = yaw * Mathf.Deg2Rad;
				pts.Add(new TrajectoryPoint(
					new Vector3(Mathf.Sin(rad) * 3f, 0f, Mathf.Cos(rad) * 3f - 3f),
					yaw, 0.18f, TrajectoryGear.Forward, t * 4f, false));
			}
			var traj = new VehicleTrajectory();
			traj.Build(pts, 4f, 0, "rs-lrl2");
			var goal = new GoalPose(pts[pts.Count - 1].Position, 60f, 0.2f, 8f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.2f);
			var p = TestParams;

			// Offset laterally while still closer to goal than start.
			Vector3 pos = pts[8].Position + new Vector3(1.3f, 0f, 0f);
			var output = tracker.Tick(pos, pts[8].YawDegrees, 4f, p, 1f);
			Assert.IsFalse(output.NeedPathReplan,
				"improving multi-segment path should not immediate-replan on moderate xtrack");
		}

		[Test]
		public void TrajectoryTracker_SettleAtTolerance_CommandsZeroSpeed()
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
			tracker.Activate(traj, goal, 1.5f);
			var p = TestParams;

			var output = tracker.Tick(new Vector3(0f, 0f, 1.95f), 0f, 0.5f, p, 1f);
			Assert.AreEqual(0f, output.Command.DesiredSpeedKmh, 0.01f,
				"inside tolerance with heading OK should idle, not creep");
			Assert.IsTrue(output.RequestTerminalBrake);
		}

		[Test]
		public void TrajectoryTracker_SettleLookAhead_StaysOnPathNotGoal()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, 0f, TrajectoryGear.Reverse, 0f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Reverse, 2f),
				new TrajectoryPoint(new Vector3(0f, 0f, -2f), 0f, 0f, TrajectoryGear.Forward, 2f),
				new TrajectoryPoint(new Vector3(2f, 0f, -2f), 90f, 0.15f, TrajectoryGear.Forward, 4f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 4f, 0, "two-stage-side");
			var goal = new GoalPose(new Vector3(2f, 0f, -2f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.2f);
			var p = TestParams;

			var output = tracker.Tick(new Vector3(0f, 0f, -0.3f), 0f, 2f, p, 1f);
			float laDistGoal = Vector3.Distance(output.LookAheadPoint, goal.Position);
			float laDistPath = Vector3.Distance(output.LookAheadPoint, new Vector3(0f, 0f, -1.5f));
			Assert.Less(laDistPath, laDistGoal,
				"look-ahead should stay on current reverse segment, not jump to goal");
		}

		[Test]
		public void TrajectoryTracker_TwoStageSide_ApexDoesNotSettleTowardGoal()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, -0.12f, TrajectoryGear.Reverse, 0f),
				new TrajectoryPoint(new Vector3(1.2f, 0f, 0.3f), -25f, -0.12f, TrajectoryGear.Reverse, 1.6f),
				new TrajectoryPoint(new Vector3(1.2f, 0f, 0.3f), -25f, 0f, TrajectoryGear.Forward, 1.6f),
				new TrajectoryPoint(new Vector3(2f, 0f, 0f), 0f, 0.15f, TrajectoryGear.Forward, 3.2f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 3.2f, 0, "two-stage-side");
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.2f);
			var p = TestParams;

			// Near goal in Euclidean terms but still on staging reverse segment.
			var output = tracker.Tick(new Vector3(1.15f, 0f, 0.25f), -20f, 2f, p, 1f);
			Assert.AreEqual(TrajectoryGear.Reverse, output.ActiveGear);
			Assert.Greater(output.Command.DesiredSpeedKmh, 0.1f,
				"should follow staging path, not idle/creep toward goal");
			Assert.Less(Vector3.Distance(output.LookAheadPoint, goal.Position), 2.5f);
			float laDistPath = Vector3.Distance(output.LookAheadPoint, new Vector3(1.2f, 0f, 0.3f));
			Assert.Less(laDistPath, 1.5f, "look-ahead should stay on current segment");
		}

		[Test]
		public void TrajectoryTracker_MidPathWithinTolerance_ContinuesDriving()
		{
			var pts = new System.Collections.Generic.List<TrajectoryPoint>
			{
				new TrajectoryPoint(Vector3.zero, 0f, -0.12f, TrajectoryGear.Reverse, 0f),
				new TrajectoryPoint(new Vector3(1.2f, 0f, 0.4f), -30f, -0.12f, TrajectoryGear.Reverse, 1.8f, true),
				new TrajectoryPoint(new Vector3(2f, 0f, 0f), 0f, 0.15f, TrajectoryGear.Reverse, 4.5f)
			};
			var traj = new VehicleTrajectory();
			traj.Build(pts, 4.5f, 0, "two-stage-side");
			var goal = new GoalPose(new Vector3(2f, 0f, 0f), null, 0.5f, 5f);
			var tracker = new TrajectoryTracker();
			tracker.Activate(traj, goal, 1.2f);
			var p = TestParams;

			var output = tracker.Tick(new Vector3(1.95f, 0f, 0.05f), -15f, 2f, p, 1f);
			Assert.IsFalse(output.IsComplete, "within tolerance mid-path must not complete");
			Assert.AreEqual(TrajectoryGear.Reverse, output.ActiveGear);
			Assert.Greater(output.Command.DesiredSpeedKmh, 0.1f,
				"should keep following path when distToEnd > approach zone");
		}
	}
}
