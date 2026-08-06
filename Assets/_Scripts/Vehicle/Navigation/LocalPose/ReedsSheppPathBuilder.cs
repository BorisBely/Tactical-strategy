using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Analytic curve-straight and Dubins CSC path builder for local pose planning.
	/// </summary>
	public static class ReedsSheppPathBuilder
	{
		private const float c_ReversePenalty = 1.35f;
		private const float c_GearSwitchPenalty = 2.5f;
		private const float c_BuildPoseTolerance = 0.12f;
		private const float c_PlanSnapTolerance = 0.25f;
		private static readonly float[] c_OneCuspPulls = { 0.3f, 0.5f, 0.8f, 1.0f, 1.2f, 1.6f, 2.0f, 2.5f, 3.0f };
		private static readonly float[] c_CuspArcSamples = { 0.4f, 0.8f, 1.2f, 1.6f, 2.0f, 2.8f, 3.6f, 4.5f, 5.5f, 7.0f, 9.0f, 11.0f };

		private static float GetMaxCuspArcLength(float _radius, float _goalDist)
		{
			float r = Mathf.Max(1f, _radius);
			float cap = r * Mathf.PI * 1.25f;
			if (_goalDist < r * 2.5f)
				cap = Mathf.Min(cap, Mathf.Max(r * 0.55f, _goalDist * 3.5f));
			return cap;
		}

		private static int DubinsSampleCount(float _segmentLength)
		{
			return Mathf.Max(4, Mathf.CeilToInt(_segmentLength / 0.08f));
		}

		private static float EndArcLength(BicycleKinematics.Primitive? _seg, float _fallback = 0f)
		{
			if (!_seg.HasValue || _seg.Value.Samples == null || _seg.Value.Samples.Count == 0)
				return _fallback;

			return _seg.Value.Samples[_seg.Value.Samples.Count - 1].ArcLength;
		}

		private static float EndPoseError(VehicleTrajectory _traj, GoalPose _goal)
		{
			if (_traj == null || !_traj.IsValid || _traj.PointCount < 1)
				return float.MaxValue;

			TrajectoryPoint end = _traj.Points[_traj.PointCount - 1];
			float err = BicycleKinematics.FlatDistance(end.Position, _goal.Position);
			if (_goal.RequiresPosePlanning)
				err += Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) * 0.02f;
			else if (_goal.HasAdvisoryHeading)
				err += Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) * 0.005f;
			return err;
		}

		private static VehicleTrajectory PickBestStage2(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase)
		{
			VehicleTrajectory best = null;
			float bestErr = float.MaxValue;

			void Consider(VehicleTrajectory _traj)
			{
				if (_traj == null || !_traj.IsValid)
					return;
				if (!ValidateEndPose(_traj, _goal, GetPlanPosTolerance(_goal)))
					return;
				float err = EndPoseError(_traj, _goal);
				if (err < bestErr)
				{
					bestErr = err;
					best = _traj;
				}
			}

			Consider(BuildDubinsBest(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Forward));
			for (int turn = -1; turn <= 1; turn += 2)
				Consider(BuildCSC(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Forward, turn));

			return best;
		}

		/// <summary>
		/// Cheap CS-only families for front/rear-oblique position goals. Safe to run before heavy RS.
		/// </summary>
		public static void AddLightCsCandidates(
			List<VehicleTrajectory> _out,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			bool _allowReverse)
		{
			if (_goal.RequiresPosePlanning)
				return;

			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			if (dist < 0.05f)
				return;

			float align = GetTravelAlignment(_from, _fromYaw, _goal.Position);
			if (align <= 12f || align >= 168f)
				return;

			VehicleTrajectory best = TryBuildBestCs(_from, _fromYaw, _goal.Position, _radius, _wheelBase, TrajectoryGear.Forward);
			TryAddValidated(_out, best, _goal, dist, _radius);
			if (_allowReverse)
			{
				TryAddValidated(_out,
					TryBuildBestCs(_from, _fromYaw, _goal.Position, _radius, _wheelBase, TrajectoryGear.Reverse),
					_goal, dist, _radius);
			}
		}

		/// <summary>
		/// Best CS over both turn signs and a few radius scales. Null if none validate.
		/// </summary>
		public static VehicleTrajectory TryBuildBestCs(
			Vector3 _from,
			float _fromYaw,
			Vector3 _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear)
		{
			VehicleTrajectory best = null;
			float bestScore = float.MaxValue;
			float[] radiusScales = { 1f, 1.25f, 1.6f, 2.0f };
			for (int si = 0; si < radiusScales.Length; si++)
			{
				float r = _radius * radiusScales[si];
				// Prefer gentler radius (≥1.25×) when free space — slightly shorter sharp
				// arcs lose unless clearly shorter.
				float gentlerBias = 0f;
				if (radiusScales[si] >= 1.25f)
					gentlerBias = 0.4f;
				if (radiusScales[si] >= 1.6f)
					gentlerBias = 0.55f;
				for (int turn = -1; turn <= 1; turn += 2)
				{
					VehicleTrajectory cs = BuildCS(_from, _fromYaw, _goal, r, _wheelBase, _gear, turn);
					if (cs == null || !cs.IsValid)
						continue;
					float score = cs.TotalLength - gentlerBias;
					if (score >= bestScore)
						continue;
					bestScore = score;
					best = cs;
				}
			}

			return best;
		}

		/// <summary>
		/// Cheap joint position+yaw families for explicit pose (Dubins/CSC). No RS cusp loops.
		/// </summary>
		public static void AddLightPoseCandidates(
			List<VehicleTrajectory> _out,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			bool _allowReverse)
		{
			if (!_goal.RequiresPosePlanning)
				return;

			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			if (dist < 0.05f)
				return;

			TryAddValidated(_out, BuildDubinsBest(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Forward), _goal, dist, _radius);
			if (_allowReverse)
				TryAddValidated(_out, BuildDubinsBest(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Reverse), _goal, dist, _radius);

			for (int turn = -1; turn <= 1; turn += 2)
			{
				TryAddValidated(_out, BuildCSC(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Forward, turn), _goal, dist, _radius);
				if (_allowReverse)
					TryAddValidated(_out, BuildCSC(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Reverse, turn), _goal, dist, _radius);
			}
		}

		/// <summary>
		/// Forward Dubins to a position-only goal by sampling terminal yaw. Reaches points
		/// inside the min-turn circle where plain CS fails (long-way CSC/CCC).
		/// </summary>
		public static void AddLightDubinsPositionCandidates(
			List<VehicleTrajectory> _out,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase)
		{
			if (_goal.RequiresPosePlanning)
				return;

			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			if (dist < 0.05f)
				return;

			VehicleTrajectory best = TryBuildBestDubinsToPosition(
				_from, _fromYaw, _goal.Position, _radius, _wheelBase, _goal.PositionTolerance);
			if (best != null)
				_out.Add(best);
		}

		/// <summary>
		/// Shortest forward Dubins path that reaches a position (terminal yaw free).
		/// </summary>
		public static VehicleTrajectory TryBuildBestDubinsToPosition(
			Vector3 _from,
			float _fromYaw,
			Vector3 _goalPos,
			float _radius,
			float _wheelBase,
			float _positionTolerance)
		{
			float dist = BicycleKinematics.FlatDistance(_from, _goalPos);
			if (dist < 0.05f)
				return null;

			float travelYaw = _fromYaw;
			Vector3 delta = _goalPos - _from;
			delta.y = 0f;
			if (delta.sqrMagnitude > 1e-4f)
				travelYaw = Quaternion.LookRotation(delta.normalized, Vector3.up).eulerAngles.y;

			VehicleTrajectory best = null;
			float bestLen = float.MaxValue;
			float maxLen = Mathf.Max(_radius * Mathf.PI * 1.8f + dist * 2f, dist * 5f + 10f);
			float posTol = Mathf.Max(c_PlanSnapTolerance, _positionTolerance, c_BuildPoseTolerance);

			for (int i = 0; i < 24; i++)
			{
				float endYaw = travelYaw + i * 15f;
				// Wide heading tolerance: we only care about position for this helper.
				var softGoal = new GoalPose(
					_goalPos, endYaw, GoalHeadingSource.RequiredExplicit, posTol, 179f);
				VehicleTrajectory dub = BuildDubinsBest(
					_from, _fromYaw, softGoal, _radius, _wheelBase, TrajectoryGear.Forward);
				if (dub == null || !dub.IsValid)
					continue;
				TrajectoryPoint end = dub.Points[dub.PointCount - 1];
				if (BicycleKinematics.FlatDistance(end.Position, _goalPos) > posTol)
					continue;
				if (dub.TotalLength > maxLen)
					continue;
				if (!TrajectoryKinematicsValidator.Validate(dub, _radius, out _))
					continue;
				if (dub.TotalLength >= bestLen)
					continue;
				bestLen = dub.TotalLength;
				best = dub;
			}

			return best;
		}

		public static void AddCandidates(
			List<VehicleTrajectory> _out,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			bool _allowReverse)
		{
			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			if (dist < 0.05f)
			{
				_out.Add(BuildTrivial(_from, _fromYaw, _goal));
				return;
			}

			Vector3 delta = _goal.Position - _from;
			delta.y = 0f;
			float travelYaw = delta.sqrMagnitude > 1e-4f
				? Quaternion.LookRotation(delta.normalized, Vector3.up).eulerAngles.y
				: _fromYaw;
			float align = Mathf.Abs(Mathf.DeltaAngle(_fromYaw, travelYaw));

			// Straight when aligned — prefer over CS for same direction.
			if (align <= 12f)
				TryAddValidated(_out, BuildStraight(_from, _fromYaw, _goal.Position, _wheelBase, TrajectoryGear.Forward), _goal, dist, _radius);
			if (_allowReverse && align >= 168f)
				TryAddValidated(_out, BuildStraight(_from, _fromYaw, _goal.Position, _wheelBase, TrajectoryGear.Reverse), _goal, dist, _radius);

			// CS — position-only and soft-tangent goals (CS cannot guarantee final heading).
			if (!_goal.RequiresPosePlanning && align > 12f && align < 168f)
				AddLightCsCandidates(_out, _from, _fromYaw, _goal, _radius, _wheelBase, _allowReverse);

			// CSC / Dubins when pose heading is required.
			if (_goal.RequiresPosePlanning)
			{
				for (int turn = -1; turn <= 1; turn += 2)
				{
					TryAddValidated(_out, BuildCSC(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Forward, turn), _goal, dist, _radius);
					if (_allowReverse)
						TryAddValidated(_out, BuildCSC(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Reverse, turn), _goal, dist, _radius);
				}

				TryAddValidated(_out, BuildDubinsBest(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Forward), _goal, dist, _radius);
				if (_allowReverse)
					TryAddValidated(_out, BuildDubinsBest(_from, _fromYaw, _goal, _radius, _wheelBase, TrajectoryGear.Reverse), _goal, dist, _radius);
			}

			// One-cusp and Reeds–Shepp cusp families when goal lies inside minimum turning circle.
			if (_allowReverse && dist < _radius * 2.8f)
			{
				for (int i = 0; i < c_OneCuspPulls.Length; i++)
				{
					for (int turn = -1; turn <= 1; turn += 2)
					{
						if (align >= 90f)
							TryAddValidated(_out, BuildOneCusp(_from, _fromYaw, _goal, _radius, _wheelBase, c_OneCuspPulls[i], turn, _straightReverse: true), _goal, dist, _radius);
						// Arc-reverse-first one-cusp only when goal is clearly behind (not front oblique).
						if (align >= 100f)
							TryAddValidated(_out, BuildOneCusp(_from, _fromYaw, _goal, _radius, _wheelBase, c_OneCuspPulls[i], turn, _straightReverse: false), _goal, dist, _radius);
					}
				}

				if (align >= 100f)
				{
					for (int turn = -1; turn <= 1; turn += 2)
					{
						TryAddValidated(_out, BuildCuspCC(_from, _fromYaw, _goal, _radius, _wheelBase, turn, dist), _goal, dist, _radius);
						TryAddValidated(_out, BuildCuspCSC(_from, _fromYaw, _goal, _radius, _wheelBase, turn, dist), _goal, dist, _radius);
						TryAddValidated(_out, BuildCuspSCC(_from, _fromYaw, _goal, _radius, _wheelBase, turn, dist), _goal, dist, _radius);
					}
				}
			}
		}

		public static void AddSymmetricCandidates(
			List<VehicleTrajectory> _out,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			bool _allowReverse)
		{
			AddCandidates(_out, _from, _fromYaw, _goal, _radius, _wheelBase, _allowReverse);

			float align = GetTravelAlignment(_from, _fromYaw, _goal.Position);
			if (align < 35f || align > 145f)
				return;

			GoalPose mirrored = MirrorGoalInStartFrame(_from, _fromYaw, _goal);
			if (BicycleKinematics.FlatDistance(_goal.Position, mirrored.Position) < 0.05f)
				return;

			int afterPrimary = _out.Count;
			AddCandidates(_out, _from, _fromYaw, mirrored, _radius, _wheelBase, _allowReverse);
			for (int i = afterPrimary; i < _out.Count; i++)
			{
				var t = _out[i];
				if (t != null && t.IsValid)
					_out[i] = MirrorTrajectoryInStartFrame(t, _from, _fromYaw);
			}
		}

		private static GoalPose MirrorGoalInStartFrame(Vector3 _from, float _fromYaw, GoalPose _goal)
		{
			Quaternion toLocal = Quaternion.Euler(0f, -_fromYaw, 0f);
			Quaternion toWorld = Quaternion.Euler(0f, _fromYaw, 0f);
			Vector3 local = toLocal * (_goal.Position - _from);
			local.x = -local.x;
			Vector3 mirroredPos = _from + toWorld * local;

			float? yaw = null;
			if (_goal.RequiresPosePlanning || _goal.HasAdvisoryHeading)
			{
				float localYaw = Mathf.DeltaAngle(_fromYaw, _goal.YawDegrees);
				float mirroredLocalYaw = -localYaw;
				yaw = BicycleKinematics.NormalizeYaw(_fromYaw + mirroredLocalYaw);
			}

			return new GoalPose(
				mirroredPos,
				yaw,
				_goal.HeadingSource,
				_goal.PositionTolerance,
				_goal.HeadingToleranceDeg);
		}

		private static VehicleTrajectory MirrorTrajectoryInStartFrame(
			VehicleTrajectory _source,
			Vector3 _from,
			float _fromYaw)
		{
			Quaternion toLocal = Quaternion.Euler(0f, -_fromYaw, 0f);
			Quaternion toWorld = Quaternion.Euler(0f, _fromYaw, 0f);
			var pts = new List<TrajectoryPoint>(_source.PointCount);
			for (int i = 0; i < _source.PointCount; i++)
			{
				TrajectoryPoint p = _source.Points[i];
				Vector3 local = toLocal * (p.Position - _from);
				local.x = -local.x;
				Vector3 world = _from + toWorld * local;
				float localYaw = Mathf.DeltaAngle(_fromYaw, p.YawDegrees);
				float mirroredYaw = BicycleKinematics.NormalizeYaw(_fromYaw - localYaw);
				pts.Add(new TrajectoryPoint(
					world, mirroredYaw, -p.Curvature, p.Gear, p.ArcLength, p.IsCusp));
			}

			var t = new VehicleTrajectory();
			t.Build(pts, _source.Cost, _source.ExpandedNodes, _source.DebugReason + "-mirror");
			return t;
		}
		public static VehicleTrajectory BuildDirectApproach(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _wheelBase,
			bool _allowReverse)
		{
			Vector3 delta = _goal.Position - _from;
			delta.y = 0f;
			if (delta.sqrMagnitude < 1e-4f)
			{
				if (TrajectoryKinematicsValidator.IsAtGoal(_from, _fromYaw, _goal))
					return BuildTrivial(_from, _fromYaw, _goal);
				return null;
			}

			float travelYaw = Quaternion.LookRotation(delta.normalized, Vector3.up).eulerAngles.y;
			float align = Mathf.Abs(Mathf.DeltaAngle(_fromYaw, travelYaw));

			VehicleTrajectory traj = null;
			if (align <= 20f)
				traj = BuildStraight(_from, _fromYaw, _goal.Position, _wheelBase, TrajectoryGear.Forward);
			else if (_allowReverse && align >= 160f)
				traj = BuildStraight(_from, _fromYaw, _goal.Position, _wheelBase, TrajectoryGear.Reverse);

			if (ValidateEndPose(traj, _goal, 0.12f))
				return traj;
			return null;
		}

		public static bool IsSanitary(VehicleTrajectory _traj, float _goalDist, float _turnRadius = 0f)
		{
			if (_traj == null || !_traj.IsValid)
				return false;

			if (_traj.GearSegmentCount > 5)
				return false;

			float maxLen = Mathf.Max(_goalDist * 2.5f + 6f, 12f);
			if (_turnRadius > 0f && _goalDist < _turnRadius * 2.5f)
			{
				// Inside min-turn circle: forward Dubins/CSC may need ~πR–1.5πR.
				// two-stage-side / rev-staging also need long reverse pulls at large trackable R.
				string reason = _traj.DebugReason ?? string.Empty;
				float arcBudget = reason.Contains("dubins") || reason.Contains("csc") ||
				                  reason.Contains("cs-fwd") || reason.Contains("arc-fwd") ||
				                  reason.Contains("two-stage-side") || reason.Contains("rev-staging") ||
				                  reason.Contains("front-oblique")
					? 1.6f
					: 0.9f;
				maxLen = Mathf.Max(maxLen, _turnRadius * Mathf.PI * arcBudget + _goalDist * 2.5f + 2f);
			}

			return _traj.TotalLength <= maxLen;
		}

		private static float GetPlanPosTolerance(GoalPose _goal)
		{
			return Mathf.Max(c_PlanSnapTolerance, _goal.PositionTolerance, c_BuildPoseTolerance);
		}

		public static float GetTravelAlignment(Vector3 _from, float _fromYaw, Vector3 _goalPos)
		{
			Vector3 delta = _goalPos - _from;
			delta.y = 0f;
			if (delta.sqrMagnitude < 1e-4f)
				return 0f;
			float travelYaw = Quaternion.LookRotation(delta.normalized, Vector3.up).eulerAngles.y;
			return Mathf.Abs(Mathf.DeltaAngle(_fromYaw, travelYaw));
		}

		/// <summary>Connect an intermediate pose to a heading goal via forward Dubins/CSC.</summary>
		public static VehicleTrajectory ConnectToGoalWithHeading(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase)
		{
			return PickBestStage2(_from, _fromYaw, _goal, _radius, _wheelBase);
		}

		/// <summary>Reverse arc staging then analytic close — for rear/side heading goals inside turning circle.</summary>
		public static VehicleTrajectory BuildReverseStagingWithHeading(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase)
		{
			if (!_goal.RequiresPosePlanning)
				return null;

			Vector3 toGoal = _goal.Position - _from;
			toGoal.y = 0f;
			float side = Mathf.Sign(Vector3.Dot(toGoal, BicycleKinematics.YawToForward(_fromYaw + 90f)));
			if (Mathf.Abs(side) < 0.1f)
				side = 1f;

			float r = Mathf.Max(1f, _radius);
			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;
			float[] pulls = { 0.4f, 0.6f, 0.8f, 1.0f, 1.2f, 1.6f, 2.0f, 2.5f, 3.0f, 3.5f };

			for (int pi = 0; pi < pulls.Length; pi++)
			{
				float pull = pulls[pi];
				for (int turn = -1; turn <= 1; turn += 2)
				{
					float curv = side * turn / r;
					var stage1 = BicycleKinematics.Integrate(
						_from, _fromYaw, curv, TrajectoryGear.Reverse, pull, _wheelBase, 0f);
					VehicleTrajectory stage2 = PickBestStage2(
						stage1.EndPosition, stage1.EndYawDegrees, _goal, r, _wheelBase);
					if (stage2 == null || !stage2.IsValid)
						continue;

					var pts = new List<TrajectoryPoint>(stage1.Samples);
					if (pts.Count > 0)
					{
						TrajectoryPoint cusp = pts[pts.Count - 1];
						pts[pts.Count - 1] = new TrajectoryPoint(
							cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
					}

					float baseArc = pts[pts.Count - 1].ArcLength;
					for (int i = 1; i < stage2.PointCount; i++)
					{
						TrajectoryPoint p = stage2.Points[i];
						pts.Add(new TrajectoryPoint(
							p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
					}

					TrySnapEndToGoal(pts, _goal, GetPlanPosTolerance(_goal), _goal.HeadingToleranceDeg);
					if (!ValidateIntegratedEnd(pts, _goal))
						continue;

					float cost = pull * c_ReversePenalty + stage2.Cost + c_GearSwitchPenalty;
					if (cost >= bestCost)
						continue;

					bestCost = cost;
					var t = new VehicleTrajectory();
					t.Build(pts, cost, 0, "rev-staging-hdg");
					best = t;
				}
			}

			return best;
		}

		public static void TryAddCandidate(
			List<VehicleTrajectory> _out,
			VehicleTrajectory _traj,
			GoalPose _goal,
			float _goalDist,
			float _radius)
		{
			TryAddValidated(_out, _traj, _goal, _goalDist, _radius);
		}

		private static void TryAddValidated(List<VehicleTrajectory> _out, VehicleTrajectory _traj, GoalPose _goal, float _goalDist, float _radius)
		{
			if (_traj == null || !_traj.IsValid)
				return;
			if (!ValidateEndPose(_traj, _goal, GetPlanPosTolerance(_goal)))
				return;
			if (_goalDist > 0f && _radius > 0f && !IsSanitary(_traj, _goalDist, _radius))
				return;
			if (!TrajectoryKinematicsValidator.Validate(_traj, _radius, out _))
				return;
			_out.Add(_traj);
		}

		private static void TrySnapEndToGoal(
			List<TrajectoryPoint> _pts,
			GoalPose _goal,
			float _posTol,
			float _headingTolDeg)
		{
			// No-op: never teleport the last sample to the goal. Endpoint must come from
			// integrated kinematics (or LocalPosePlanner.EnsureExecutionEndpoint refine).
		}

		private const float c_MinMotionForYaw = 0.02f;

		private static bool ValidateEndPose(VehicleTrajectory _traj, GoalPose _goal, float _posTol = c_BuildPoseTolerance)
		{
			if (_traj == null || !_traj.IsValid || _traj.PointCount < 1)
				return false;

			TrajectoryPoint end = _traj.Points[_traj.PointCount - 1];
			if (BicycleKinematics.FlatDistance(end.Position, _goal.Position) > _posTol)
				return false;

			if (_goal.RequiresPosePlanning &&
			    Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) > _goal.HeadingToleranceDeg)
				return false;

			return true;
		}

		private static VehicleTrajectory BuildTrivial(Vector3 _pos, float _yaw, GoalPose _goal)
		{
			if (!TrajectoryKinematicsValidator.IsAtGoal(_pos, _yaw, _goal))
				return VehicleTrajectory.Invalid("trivial rejected: goal not satisfied", 0);

			float endYaw = _goal.RequiresPosePlanning || _goal.HasAdvisoryHeading ? _goal.YawDegrees : _yaw;
			var pts = new List<TrajectoryPoint>
			{
				new TrajectoryPoint(_pos, _yaw, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(_goal.Position, endYaw, 0f, TrajectoryGear.Forward, 0.001f)
			};
			var t = new VehicleTrajectory();
			t.Build(pts, 0.001f, 0, "trivial");
			return t;
		}

		private static VehicleTrajectory BuildStraight(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			float _wheelBase,
			TrajectoryGear _gear)
		{
			Vector3 delta = _to - _from;
			delta.y = 0f;
			float len = delta.magnitude;
			if (len < 0.05f)
				return null;

			var prim = BicycleKinematics.Integrate(_from, _fromYaw, 0f, _gear, len, _wheelBase, 0f);
			if (BicycleKinematics.FlatDistance(prim.EndPosition, _to) > c_BuildPoseTolerance)
				return null;

			var t = new VehicleTrajectory();
			float cost = len * (_gear == TrajectoryGear.Reverse ? c_ReversePenalty : 1f);
			t.Build(prim.Samples, cost, 0, _gear == TrajectoryGear.Reverse ? "straight-rev" : "straight-fwd");
			return t;
		}

		private static VehicleTrajectory BuildCS(
			Vector3 _from,
			float _fromYaw,
			Vector3 _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear,
			float _turnSign)
		{
			float r = Mathf.Max(1f, _radius);
			Vector3 fwd = BicycleKinematics.YawToForward(_fromYaw);
			Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
			Vector3 center = _from + right * _turnSign * r;

			Vector3 toGoal = _goal - center;
			toGoal.y = 0f;
			float d = toGoal.magnitude;
			if (d < r * 0.98f)
				return null;

			float alpha = Mathf.Atan2(toGoal.x, toGoal.z);
			float beta = Mathf.Acos(Mathf.Clamp(r / d, -1f, 1f));
			float tanAngleA = alpha + beta;
			float tanAngleB = alpha - beta;

			Vector3 startRel = _from - center;
			float startAngle = Mathf.Atan2(startRel.x, startRel.z);

			float bestCost = float.MaxValue;
			List<TrajectoryPoint> bestPts = null;
			float bestCostVal = 0f;

			TryCSTangent(startAngle, tanAngleA, center, r, _from, _fromYaw, _goal, _wheelBase, _gear, _turnSign, ref bestPts, ref bestCost, ref bestCostVal);
			TryCSTangent(startAngle, tanAngleB, center, r, _from, _fromYaw, _goal, _wheelBase, _gear, _turnSign, ref bestPts, ref bestCost, ref bestCostVal);

			if (bestPts == null || bestPts.Count < 2)
				return null;

			var t = new VehicleTrajectory();
			t.Build(bestPts, bestCostVal, 0, _gear == TrajectoryGear.Reverse ? "cs-rev" : "cs-fwd");
			return t;
		}

		private static void TryCSTangent(
			float _startAngle,
			float _tanAngle,
			Vector3 _center,
			float _radius,
			Vector3 _from,
			float _fromYaw,
			Vector3 _goal,
			float _wheelBase,
			TrajectoryGear _gear,
			float _turnSign,
			ref List<TrajectoryPoint> _bestPts,
			ref float _bestCost,
			ref float _bestCostVal)
		{
			Vector3 tanPt = _center + new Vector3(Mathf.Sin(_tanAngle), 0f, Mathf.Cos(_tanAngle)) * _radius;
			Vector3 tanFwd = new Vector3(Mathf.Cos(_tanAngle), 0f, -Mathf.Sin(_tanAngle)) * _turnSign;
			if (_gear == TrajectoryGear.Reverse)
				tanFwd = -tanFwd;

			Vector3 toGoal = _goal - tanPt;
			toGoal.y = 0f;
			float along = Vector3.Dot(toGoal, tanFwd.normalized);
			if (along < 0.05f)
				return;

			float lateral = Vector3.Cross(tanFwd, toGoal).magnitude;
			if (lateral > c_BuildPoseTolerance)
				return;

			float arcDelta = SignedAngleDelta(_startAngle, _tanAngle, _turnSign);
			if (_gear == TrajectoryGear.Reverse)
				arcDelta = -arcDelta;
			if (Mathf.Abs(arcDelta) < 1e-3f && along < 0.1f)
				return;

			float curv = _turnSign / _radius;
			if (_gear == TrajectoryGear.Reverse)
				curv = -curv;

			float arcLen = _radius * Mathf.Abs(arcDelta);
			BicycleKinematics.Primitive? arc = arcLen > 0.05f
				? BicycleKinematics.Integrate(_from, _fromYaw, curv, _gear, arcLen, _wheelBase, 0f)
				: null;

			Vector3 lineStart = arc.HasValue ? arc.Value.EndPosition : _from;
			float lineYaw = arc.HasValue ? arc.Value.EndYawDegrees : _fromYaw;
			float baseArc = arc.HasValue ? arc.Value.Length : 0f;

			var line = BicycleKinematics.Integrate(lineStart, lineYaw, 0f, _gear, along, _wheelBase, baseArc);
			if (BicycleKinematics.FlatDistance(line.EndPosition, _goal) > c_BuildPoseTolerance)
				return;

			var pts = new List<TrajectoryPoint>();
			if (arc.HasValue)
				pts.AddRange(arc.Value.Samples);
			for (int i = pts.Count == 0 ? 0 : 1; i < line.Samples.Count; i++)
				pts.Add(line.Samples[i]);

			float cost = arcLen + along;
			if (_gear == TrajectoryGear.Reverse)
				cost *= c_ReversePenalty;

			if (cost < _bestCost)
			{
				_bestCost = cost;
				_bestCostVal = cost;
				_bestPts = pts;
			}
		}

		private static VehicleTrajectory BuildCSC(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear,
			float _firstTurnSign)
		{
			float r = Mathf.Max(1f, _radius);
			float endYawApproach = _goal.YawDegrees;
			Vector3 fwd = BicycleKinematics.YawToForward(_fromYaw);
			Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
			Vector3 center1 = _from + right * _firstTurnSign * r;

			Vector3 goalFwd = BicycleKinematics.YawToForward(endYawApproach);
			Vector3 goalRight = Vector3.Cross(Vector3.up, goalFwd).normalized;
			Vector3 goalBack = _goal.Position - goalFwd * 0.01f;
			Vector3 center2 = goalBack - goalRight * _firstTurnSign * r;

			Vector3 c1c2 = center2 - center1;
			c1c2.y = 0f;
			float D = c1c2.magnitude;
			if (D < 1e-3f)
				return null;

			float straight = D;
			Vector3 c1ToC2 = c1c2 / D;
			Vector3 midTangent1 = center1 + c1ToC2 * r * _firstTurnSign;
			Vector3 midTangent2 = center2 - c1ToC2 * r * _firstTurnSign;

			Vector3 startRel = _from - center1;
			float startAng = Mathf.Atan2(startRel.x, startRel.z);
			float tan1Ang = Mathf.Atan2((midTangent1 - center1).x, (midTangent1 - center1).z);
			float arc1Delta = SignedAngleDelta(startAng, tan1Ang, _firstTurnSign);

			Vector3 endRel = goalBack - center2;
			float endAng = Mathf.Atan2(endRel.x, endRel.z);
			float tan2Ang = Mathf.Atan2((midTangent2 - center2).x, (midTangent2 - center2).z);
			float arc2Delta = SignedAngleDelta(tan2Ang, endAng, _firstTurnSign);

			float curv = _firstTurnSign / r;
			if (_gear == TrajectoryGear.Reverse)
				curv = -curv;

			float arc1Len = r * Mathf.Abs(arc1Delta);
			BicycleKinematics.Primitive? seg1 = arc1Len > 0.05f
				? BicycleKinematics.Integrate(_from, _fromYaw, curv, _gear, arc1Len, _wheelBase, 0f)
				: null;

			Vector3 s2 = seg1.HasValue ? seg1.Value.EndPosition : _from;
			float y2 = seg1.HasValue ? seg1.Value.EndYawDegrees : _fromYaw;
			float baseArc = seg1.HasValue ? seg1.Value.Length : 0f;

			float lineLen = BicycleKinematics.FlatDistance(s2, midTangent2);
			if (lineLen < 0.02f)
				lineLen = straight;

			var seg2 = BicycleKinematics.Integrate(s2, y2, 0f, _gear, lineLen, _wheelBase, baseArc);
			float arc2Len = r * Mathf.Abs(arc2Delta);
			float seg2EndArc = EndArcLength(seg2, baseArc + lineLen);
			var seg3 = BicycleKinematics.Integrate(
				seg2.EndPosition, seg2.EndYawDegrees, curv, _gear, arc2Len, _wheelBase, seg2EndArc);

			var pts = MergeSegments(
				seg1.HasValue ? seg1.Value.Samples : null,
				seg2.Samples,
				seg3.Samples);

			float posTol = GetPlanPosTolerance(_goal);
			if (_goal.RequiresPosePlanning)
				TrySnapEndToGoal(pts, _goal, posTol, _goal.HeadingToleranceDeg);

			if (!ValidateIntegratedEnd(pts, _goal))
				return null;

			float cost = arc1Len + lineLen + arc2Len;
			if (_gear == TrajectoryGear.Reverse)
				cost *= c_ReversePenalty;

			var t = new VehicleTrajectory();
			t.Build(pts, cost, 0, "csc");
			return t;
		}

		private static VehicleTrajectory BuildDubinsBest(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear)
		{
			float r = Mathf.Max(1f, _radius);
			ToLocal(_from, _fromYaw, _goal.Position, _goal.YawDegrees, r,
				out float x, out float y, out float phi);

			VehicleTrajectory best = null;
			float bestLen = float.MaxValue;

			void ConsiderCsc(string _reason, float _firstSign, float _t, float _p, float _q)
			{
				var traj = IntegrateDubins(_from, _fromYaw, _goal, r, _wheelBase, _gear, _firstSign, _t, _p, _q, _reason);
				if (traj == null || !traj.IsValid || !ValidateEndPose(traj, _goal, GetPlanPosTolerance(_goal)))
					return;
				if (traj.TotalLength < bestLen)
				{
					bestLen = traj.TotalLength;
					best = traj;
				}
			}

			void ConsiderCcc(string _reason, float _firstSign, float _t, float _p, float _q)
			{
				var traj = IntegrateDubinsCcc(
					_from, _fromYaw, _goal, r, _wheelBase, _gear, _firstSign, _t, _p, _q, _reason);
				if (traj == null || !traj.IsValid || !ValidateEndPose(traj, _goal, GetPlanPosTolerance(_goal)))
					return;
				if (traj.TotalLength < bestLen)
				{
					bestLen = traj.TotalLength;
					best = traj;
				}
			}

			if (TryDubinsLSL(x, y, phi, out float t, out float p, out float q))
				ConsiderCsc("dubins-lsl", +1, t, p, q);
			if (TryDubinsLSL(-x, y, -phi, out t, out p, out q))
				ConsiderCsc("dubins-rsr", -1, t, p, q);
			if (TryDubinsLSR(x, y, phi, out t, out p, out q))
				ConsiderCsc("dubins-lsr", +1, t, p, -q);
			if (TryDubinsLSR(-x, y, -phi, out t, out p, out q))
				ConsiderCsc("dubins-rsl", -1, t, p, -q);
			// CCC (LRL/RLR): required for goals inside the min-turn circle. Middle segment is an arc.
			if (TryDubinsLRL(x, y, phi, out t, out p, out q))
				ConsiderCcc("dubins-lrl", +1, t, p, q);
			if (TryDubinsLRL(-x, y, -phi, out t, out p, out q))
				ConsiderCcc("dubins-rlr", -1, t, p, q);

			return best;
		}

		private static VehicleTrajectory BuildDubinsSingle(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear,
			float _firstTurnSign)
		{
			return BuildDubinsBest(_from, _fromYaw, _goal, _radius, _wheelBase, _gear);
		}

		private static VehicleTrajectory IntegrateDubins(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear,
			float _firstTurnSign,
			float _arc1,
			float _straight,
			float _arc2,
			string _reason)
		{
			float curv1 = _firstTurnSign / _radius;
			float curv2 = Mathf.Sign(_arc2) * _firstTurnSign / _radius;
			if (_gear == TrajectoryGear.Reverse)
			{
				curv1 = -curv1;
				curv2 = -curv2;
			}

			float a1 = _radius * Mathf.Abs(_arc1);
			float a2 = _radius * Mathf.Abs(_arc2);
			BicycleKinematics.Primitive? seg1 = a1 > 0.02f
				? BicycleKinematics.Integrate(_from, _fromYaw, curv1, _gear, a1, _wheelBase, 0f, DubinsSampleCount(a1))
				: null;

			Vector3 p2 = seg1.HasValue ? seg1.Value.EndPosition : _from;
			float y2 = seg1.HasValue ? seg1.Value.EndYawDegrees : _fromYaw;
			float baseArc = seg1.HasValue ? seg1.Value.Length : 0f;

			float straightLen = _radius * _straight;
			BicycleKinematics.Primitive? seg2 = _straight > 0.02f
				? BicycleKinematics.Integrate(p2, y2, 0f, _gear, straightLen, _wheelBase, baseArc, DubinsSampleCount(straightLen))
				: null;

			Vector3 p3 = seg2.HasValue ? seg2.Value.EndPosition : p2;
			float y3 = seg2.HasValue ? seg2.Value.EndYawDegrees : y2;
			float base2 = seg2.HasValue ? EndArcLength(seg2) : EndArcLength(seg1, baseArc);

			BicycleKinematics.Primitive? seg3 = a2 > 0.02f
				? BicycleKinematics.Integrate(p3, y3, curv2, _gear, a2, _wheelBase, base2, DubinsSampleCount(a2))
				: null;

			var pts = MergeSegments(
				seg1.HasValue ? seg1.Value.Samples : null,
				seg2.HasValue ? seg2.Value.Samples : null,
				seg3.HasValue ? seg3.Value.Samples : null);

			if (pts.Count < 2)
				return null;

			if (_goal.RequiresPosePlanning)
				TrySnapEndToGoal(pts, _goal, GetPlanPosTolerance(_goal), _goal.HeadingToleranceDeg);

			if (!ValidateIntegratedEnd(pts, _goal))
				return null;

			float cost = a1 + _radius * _straight + a2;
			if (_gear == TrajectoryGear.Reverse)
				cost *= c_ReversePenalty;

			var t = new VehicleTrajectory();
			t.Build(pts, cost, 0, _reason);
			return t;
		}

		/// <summary>
		/// Dubins CCC (LRL / RLR): three arcs, middle opposite turn. Not C-S-C.
		/// </summary>
		private static VehicleTrajectory IntegrateDubinsCcc(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			TrajectoryGear _gear,
			float _firstTurnSign,
			float _arc1,
			float _arcMid,
			float _arc3,
			string _reason)
		{
			float c1 = _firstTurnSign / _radius;
			float c2 = -_firstTurnSign / _radius;
			float c3 = _firstTurnSign / _radius;
			if (_gear == TrajectoryGear.Reverse)
			{
				c1 = -c1;
				c2 = -c2;
				c3 = -c3;
			}

			float a1 = _radius * Mathf.Abs(_arc1);
			float a2 = _radius * Mathf.Abs(_arcMid);
			float a3 = _radius * Mathf.Abs(_arc3);

			BicycleKinematics.Primitive? seg1 = a1 > 0.02f
				? BicycleKinematics.Integrate(_from, _fromYaw, c1, _gear, a1, _wheelBase, 0f, DubinsSampleCount(a1))
				: null;
			Vector3 p2 = seg1.HasValue ? seg1.Value.EndPosition : _from;
			float y2 = seg1.HasValue ? seg1.Value.EndYawDegrees : _fromYaw;
			float base1 = seg1.HasValue ? seg1.Value.Length : 0f;

			BicycleKinematics.Primitive? seg2 = a2 > 0.02f
				? BicycleKinematics.Integrate(p2, y2, c2, _gear, a2, _wheelBase, base1, DubinsSampleCount(a2))
				: null;
			Vector3 p3 = seg2.HasValue ? seg2.Value.EndPosition : p2;
			float y3 = seg2.HasValue ? seg2.Value.EndYawDegrees : y2;
			float base2 = seg2.HasValue ? EndArcLength(seg2) : base1;

			BicycleKinematics.Primitive? seg3 = a3 > 0.02f
				? BicycleKinematics.Integrate(p3, y3, c3, _gear, a3, _wheelBase, base2, DubinsSampleCount(a3))
				: null;

			var pts = MergeSegments(
				seg1.HasValue ? seg1.Value.Samples : null,
				seg2.HasValue ? seg2.Value.Samples : null,
				seg3.HasValue ? seg3.Value.Samples : null);

			if (pts.Count < 2)
				return null;

			if (_goal.RequiresPosePlanning)
				TrySnapEndToGoal(pts, _goal, GetPlanPosTolerance(_goal), _goal.HeadingToleranceDeg);

			if (!ValidateIntegratedEnd(pts, _goal))
				return null;

			float cost = a1 + a2 + a3;
			if (_gear == TrajectoryGear.Reverse)
				cost *= c_ReversePenalty;

			var traj = new VehicleTrajectory();
			traj.Build(pts, cost, 0, _reason);
			return traj;
		}

		private static VehicleTrajectory BuildOneCusp(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			float _pull,
			float _turnSign,
			bool _straightReverse)
		{
			BicycleKinematics.Primitive stage1;
			if (_straightReverse)
			{
				stage1 = BicycleKinematics.Integrate(
					_from, _fromYaw, 0f, TrajectoryGear.Reverse, _pull, _wheelBase, 0f);
			}
			else
			{
				float curv = -_turnSign / Mathf.Max(1f, _radius);
				stage1 = BicycleKinematics.Integrate(
					_from, _fromYaw, curv, TrajectoryGear.Reverse, _pull, _wheelBase, 0f);
			}

			var pts = new List<TrajectoryPoint>(stage1.Samples);
			if (pts.Count > 0)
			{
				TrajectoryPoint cusp = pts[pts.Count - 1];
				pts[pts.Count - 1] = new TrajectoryPoint(
					cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
			}

			VehicleTrajectory stage2 = _goal.RequiresPosePlanning
				? PickBestStage2(stage1.EndPosition, stage1.EndYawDegrees, _goal, _radius, _wheelBase)
				: BuildCS(stage1.EndPosition, stage1.EndYawDegrees, _goal.Position, _radius, _wheelBase, TrajectoryGear.Forward, _turnSign);

			if (stage2 == null || !stage2.IsValid)
				return null;

			float baseArc = pts[pts.Count - 1].ArcLength;
			for (int i = 1; i < stage2.PointCount; i++)
			{
				TrajectoryPoint p = stage2.Points[i];
				pts.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
			}

			if (_goal.RequiresPosePlanning)
				TrySnapEndToGoal(pts, _goal, GetPlanPosTolerance(_goal), _goal.HeadingToleranceDeg);
			else
				TrySnapEndPosition(pts, _goal.Position, GetPlanPosTolerance(_goal));

			if (!ValidateIntegratedEnd(pts, _goal))
				return null;

			var t = new VehicleTrajectory();
			t.Build(pts, _pull * c_ReversePenalty + stage2.Cost + c_GearSwitchPenalty, 0, "one-cusp");
			return t;
		}

		private static void TrySnapEndPosition(List<TrajectoryPoint> _pts, Vector3 _goalPos, float _posTol)
		{
			// No-op: position teleport disabled (see TrySnapEndToGoal).
		}

		private static bool ValidateIntegratedEnd(List<TrajectoryPoint> _pts, GoalPose _goal)
		{
			if (_pts == null || _pts.Count < 1)
				return false;

			float posTol = GetPlanPosTolerance(_goal);
			TrajectoryPoint end = _pts[_pts.Count - 1];
			if (BicycleKinematics.FlatDistance(end.Position, _goal.Position) > posTol)
				return false;

			if (_goal.RequiresPosePlanning &&
			    Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) > _goal.HeadingToleranceDeg)
				return false;

			return true;
		}

		private static VehicleTrajectory BuildCuspCC(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			float _turnSign,
			float _goalDist)
		{
			if (!_goal.RequiresPosePlanning)
				return null;

			float r = Mathf.Max(1f, _radius);
			float curv = _turnSign / r;
			float maxArc = GetMaxCuspArcLength(r, _goalDist);
			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;

			for (int i = 0; i < c_CuspArcSamples.Length; i++)
			{
				float s1 = c_CuspArcSamples[i];
				if (s1 > maxArc)
					continue;

				BicycleKinematics.Primitive stage1 = BicycleKinematics.Integrate(
					_from, _fromYaw, -curv, TrajectoryGear.Reverse, s1, _wheelBase, 0f);
				var merged = TryForwardArcTail(stage1, _goal, r, _wheelBase, curv, "cusp-cc", s1 * c_ReversePenalty + c_GearSwitchPenalty, maxArc);
				if (merged != null && merged.Cost < bestCost)
				{
					bestCost = merged.Cost;
					best = merged;
				}
			}

			return best;
		}

		private static VehicleTrajectory BuildCuspCSC(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			float _turnSign,
			float _goalDist)
		{
			if (!_goal.RequiresPosePlanning)
				return null;

			float r = Mathf.Max(1f, _radius);
			float curv = _turnSign / r;
			float maxArc = GetMaxCuspArcLength(r, _goalDist);
			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;

			for (int i = 0; i < c_CuspArcSamples.Length; i++)
			{
				float s1 = c_CuspArcSamples[i];
				if (s1 > maxArc)
					continue;

				BicycleKinematics.Primitive stage1 = BicycleKinematics.Integrate(
					_from, _fromYaw, -curv, TrajectoryGear.Reverse, s1, _wheelBase, 0f);
				var stage2 = BuildCSC(
					stage1.EndPosition, stage1.EndYawDegrees, _goal, r, _wheelBase, TrajectoryGear.Forward, _turnSign);
				var merged = MergeWithCusp(stage1.Samples, stage2, s1 * c_ReversePenalty + c_GearSwitchPenalty, "cusp-csc");
				if (merged != null && ValidateEndPose(merged, _goal, GetPlanPosTolerance(_goal)) && merged.Cost < bestCost)
				{
					bestCost = merged.Cost;
					best = merged;
				}
			}

			return best;
		}

		private static VehicleTrajectory BuildCuspSCC(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			float _turnSign,
			float _goalDist)
		{
			if (!_goal.RequiresPosePlanning)
				return null;

			float r = Mathf.Max(1f, _radius);
			float curv = _turnSign / r;
			float maxArc = GetMaxCuspArcLength(r, _goalDist);
			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;

			for (int i = 0; i < c_OneCuspPulls.Length; i++)
			{
				float pull = c_OneCuspPulls[i];
				BicycleKinematics.Primitive stage1 = BicycleKinematics.Integrate(
					_from, _fromYaw, 0f, TrajectoryGear.Reverse, pull, _wheelBase, 0f);
				var stage2 = BuildCSC(
					stage1.EndPosition, stage1.EndYawDegrees, _goal, r, _wheelBase, TrajectoryGear.Forward, _turnSign);
				var merged = MergeWithCusp(stage1.Samples, stage2, pull * c_ReversePenalty + c_GearSwitchPenalty, "cusp-scc");
				if (merged != null && ValidateEndPose(merged, _goal, GetPlanPosTolerance(_goal)) && merged.Cost < bestCost)
				{
					bestCost = merged.Cost;
					best = merged;
				}

				BicycleKinematics.Primitive revArc = BicycleKinematics.Integrate(
					_from, _fromYaw, -curv, TrajectoryGear.Reverse, pull, _wheelBase, 0f);
				merged = TryForwardArcTail(revArc, _goal, r, _wheelBase, curv, "cusp-sc-c", pull * c_ReversePenalty + c_GearSwitchPenalty, maxArc);
				if (merged != null && merged.Cost < bestCost)
				{
					bestCost = merged.Cost;
					best = merged;
				}
			}

			return best;
		}

		private static VehicleTrajectory TryForwardArcTail(
			BicycleKinematics.Primitive _stage1,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			float _curv,
			string _reason,
			float _baseCost,
			float _maxArcLen)
		{
			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;

			for (int i = 0; i < c_CuspArcSamples.Length; i++)
			{
				float s2 = c_CuspArcSamples[i];
				if (s2 > _maxArcLen)
					continue;

				BicycleKinematics.Primitive stage2 = BicycleKinematics.Integrate(
					_stage1.EndPosition, _stage1.EndYawDegrees, _curv, TrajectoryGear.Forward, s2, _wheelBase, _stage1.Length);
				var pts = MergeSegments(_stage1.Samples, stage2.Samples);
				MarkCusp(pts, _stage1.Samples.Count - 1);
				if (_goal.RequiresPosePlanning)
					TrySnapEndToGoal(pts, _goal, GetPlanPosTolerance(_goal), _goal.HeadingToleranceDeg);
				if (!ValidateIntegratedEnd(pts, _goal))
					continue;

				float cost = _baseCost + s2;
				if (cost >= bestCost)
					continue;

				bestCost = cost;
				var t = new VehicleTrajectory();
				t.Build(pts, cost, 0, _reason);
				best = t;
			}

			var dub = BuildDubinsBest(_stage1.EndPosition, _stage1.EndYawDegrees, _goal, _radius, _wheelBase, TrajectoryGear.Forward);
			if (dub != null && ValidateEndPose(dub, _goal, GetPlanPosTolerance(_goal)))
			{
				float cost = _baseCost + dub.TotalLength;
				if (cost < bestCost)
				{
					var merged = MergeWithCusp(_stage1.Samples, dub, cost, _reason + "+dub");
					if (merged != null)
						best = merged;
				}
			}

			return best;
		}

		private static VehicleTrajectory MergeWithCusp(
			List<TrajectoryPoint> _stage1,
			VehicleTrajectory _stage2,
			float _cost,
			string _reason)
		{
			if (_stage1 == null || _stage1.Count == 0 || _stage2 == null || !_stage2.IsValid)
				return null;

			var pts = new List<TrajectoryPoint>(_stage1);
			MarkCusp(pts, pts.Count - 1);
			float baseArc = pts[pts.Count - 1].ArcLength;
			for (int i = 1; i < _stage2.PointCount; i++)
			{
				TrajectoryPoint p = _stage2.Points[i];
				pts.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
			}

			var t = new VehicleTrajectory();
			t.Build(pts, _cost + _stage2.Cost, 0, _reason);
			return t.IsValid ? t : null;
		}

		private static void MarkCusp(List<TrajectoryPoint> _pts, int _index)
		{
			if (_pts == null || _index < 0 || _index >= _pts.Count)
				return;
			TrajectoryPoint cusp = _pts[_index];
			_pts[_index] = new TrajectoryPoint(
				cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
		}

		private static List<TrajectoryPoint> MergeSegments(
			List<TrajectoryPoint> _a,
			List<TrajectoryPoint> _b,
			List<TrajectoryPoint> _c = null)
		{
			var pts = new List<TrajectoryPoint>();
			AppendSegment(pts, _a, false);
			AppendSegment(pts, _b, pts.Count > 0);
			AppendSegment(pts, _c, pts.Count > 0);
			return pts;
		}

		private static void AppendSegment(List<TrajectoryPoint> _dst, List<TrajectoryPoint> _src, bool _skipFirst)
		{
			if (_src == null || _src.Count == 0)
				return;
			int start = _skipFirst ? 1 : 0;
			for (int i = start; i < _src.Count; i++)
				_dst.Add(_src[i]);
		}

		/// <summary>Dubins frame: x = forward, y = left (right is negative y).</summary>
		private static void ToLocal(
			Vector3 _from,
			float _fromYaw,
			Vector3 _goalPos,
			float _goalYaw,
			float _radius,
			out float _x,
			out float _y,
			out float _phi)
		{
			Vector3 delta = _goalPos - _from;
			delta.y = 0f;
			Vector3 fwd = BicycleKinematics.YawToForward(_fromYaw);
			Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
			float localFwd = Vector3.Dot(delta, fwd);
			float localRight = Vector3.Dot(delta, right);
			_x = localFwd / _radius;
			_y = -localRight / _radius;
			_phi = Mathf.DeltaAngle(_fromYaw, _goalYaw) * Mathf.Deg2Rad;
		}

		private static float SignedAngleDelta(float _from, float _to, float _turnSign)
		{
			float delta = Mathf.DeltaAngle(_from * Mathf.Rad2Deg, _to * Mathf.Rad2Deg) * Mathf.Deg2Rad;
			if (_turnSign < 0f)
				delta = -delta;
			if (_turnSign > 0f && delta < 0f)
				delta += 2f * Mathf.PI;
			if (_turnSign < 0f && delta > 0f)
				delta -= 2f * Mathf.PI;
			return delta;
		}

		private static float Mod2Pi(float _a)
		{
			while (_a < 0f) _a += 2f * Mathf.PI;
			while (_a >= 2f * Mathf.PI) _a -= 2f * Mathf.PI;
			return _a;
		}

		private static bool TryDubinsLSL(float _x, float _y, float _phi, out float _t, out float _p, out float _q)
		{
			_t = _p = _q = 0f;
			float u = _x - Mathf.Sin(_phi);
			float v = _y - 1f + Mathf.Cos(_phi);
			float t1 = Mathf.Atan2(v, u);
			float p1 = Mathf.Sqrt(u * u + v * v);
			if (p1 < 1e-4f)
				return false;
			_t = Mod2Pi(-t1);
			_p = p1;
			_q = Mod2Pi(_phi - t1);
			return _t >= 0f && _p >= 0f && _q >= 0f;
		}

		private static bool TryDubinsLSR(float _x, float _y, float _phi, out float _t, out float _p, out float _q)
		{
			_t = _p = _q = 0f;
			float u = _x + Mathf.Sin(_phi);
			float v = _y - 1f - Mathf.Cos(_phi);
			float pSq = u * u + v * v;
			if (pSq < 4f)
				return false;
			float p1 = Mathf.Sqrt(pSq) - 2f;
			if (p1 < 0f)
				return false;
			float t1 = Mathf.Atan2(v, u);
			float t2 = Mathf.Atan2(2f, p1 + 2f);
			_t = Mod2Pi(t1 - t2);
			_p = p1;
			_q = Mod2Pi(_t - _phi);
			return _t >= 0f && _p >= 0f && _q >= 0f;
		}

		internal static void ToLocalFrame(
			Vector3 _from,
			float _fromYaw,
			Vector3 _goalPos,
			float _goalYaw,
			float _radius,
			out float _x,
			out float _y,
			out float _phi)
		{
			ToLocal(_from, _fromYaw, _goalPos, _goalYaw, _radius, out _x, out _y, out _phi);
		}

		internal static bool ValidateTrajectoryEnd(VehicleTrajectory _traj, GoalPose _goal)
		{
			return ValidateEndPose(_traj, _goal, GetPlanPosTolerance(_goal));
		}

		internal static bool ValidateIntegratedPoints(List<TrajectoryPoint> _pts, GoalPose _goal)
		{
			return ValidateIntegratedEnd(_pts, _goal);
		}

		internal static void TrySnapTrajectoryEnd(List<TrajectoryPoint> _pts, GoalPose _goal)
		{
			TrySnapEndToGoal(_pts, _goal, GetPlanPosTolerance(_goal), _goal.HeadingToleranceDeg);
		}

		internal static void MarkCuspPoint(List<TrajectoryPoint> _pts, int _index)
		{
			MarkCusp(_pts, _index);
		}

		internal static void AppendTrajectorySegment(
			List<TrajectoryPoint> _dst,
			List<TrajectoryPoint> _src,
			bool _skipFirst)
		{
			AppendSegment(_dst, _src, _skipFirst);
		}

		private static bool TryDubinsLRL(float _x, float _y, float _phi, out float _t, out float _p, out float _q)
		{
			_t = _p = _q = 0f;
			float tmp0 = (6f - _x * _x - _y * _y + 2f * Mathf.Cos(_phi) * _x + 2f * Mathf.Sin(_phi) * _y) / 8f;
			if (Mathf.Abs(tmp0) > 1f)
				return false;

			_p = Mod2Pi(2f * Mathf.PI - Mathf.Acos(tmp0));
			_t = Mod2Pi(Mathf.Atan2(-_y, _x) - Mathf.Atan2(Mathf.Sin(_p), 1f - Mathf.Cos(_p)));
			_q = Mod2Pi(_t - _phi);
			return _t >= 0f && _p >= 0f && _q >= 0f;
		}
	}
}
