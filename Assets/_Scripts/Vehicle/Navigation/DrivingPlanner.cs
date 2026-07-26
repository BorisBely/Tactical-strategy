using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class DrivingPlanner
	{
		public const float c_DefaultTurnRadius = 6.5f;

		private const float c_FrontSectorAngle = 90f;
		private const float c_RearSectorAngle = 135f;

		private readonly DecisionEvaluator m_DecisionEvaluator;
		private ManeuverFeasibilityChecker m_Feasibility;
		private ArrivalPlanner m_ArrivalPlanner;

		public DrivingPlanner(DecisionEvaluator _decisionEvaluator)
		{
			m_DecisionEvaluator = _decisionEvaluator;
		}

		public void SetFeasibility(ManeuverFeasibilityChecker _feasibility)
		{
			m_Feasibility = _feasibility;
		}

		public void SetArrivalPlanner(ArrivalPlanner _planner)
		{
			m_ArrivalPlanner = _planner;
		}

		public DrivingPlan BuildPlan(
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			float _reverseMaxSegment,
			float _reverseAngleDegrees,
			float _turnRadius = c_DefaultTurnRadius,
			DriverContext _ctx = null)
		{
			Vector3 forward = FlatDir(_feedback.Forward);

			Vector3 firstTarget = _request.Destination;
			if (_path.Corners != null && _path.Corners.Length > 1)
				firstTarget = _path.Corners[1];

			Vector3 toFirst = firstTarget - _feedback.Position;
			toFirst.y = 0f;
			float firstSegLen = toFirst.magnitude;
			float firstAngle = firstSegLen > 0.01f
				? Mathf.Abs(Vector3.SignedAngle(forward, toFirst / firstSegLen, Vector3.up))
				: 0f;

			float flatToDest = FlatDistance(_feedback.Position, _request.Destination);
			float reverseRange = Mathf.Max(_reverseMaxSegment * 1.25f, 3f);

			VehicleDrivingMode proposedMode = DecideBaseMode(
				firstAngle, firstSegLen, flatToDest,
				_reverseMaxSegment, reverseRange, _reverseAngleDegrees);

			VehicleDrivingMode safeMode = m_DecisionEvaluator != null
				? m_DecisionEvaluator.ChooseSafeMode(
					proposedMode, flatToDest, _turnRadius,
					_feedback.Geometry, _feedback.Memory)
				: proposedMode;

			// Build candidates
			var candidates = new List<DrivingCandidate>(3);
			candidates.Add(BuildForwardCandidate(_request, _path, _feedback, _ctx));

			if (m_Feasibility != null)
			{
				bool canReverse = _request.AllowReverse
					&& VehicleLocalGeometry.HasSafeBackingSpace(_feedback.Geometry, 1.8f);
				bool canTurn = _request.AllowTurnAround
					&& VehicleLocalGeometry.CanFitTurnRadius(_turnRadius, _feedback.Geometry);

				if (canReverse)
					candidates.Add(BuildReverseCandidate(_request, _path, _feedback, _turnRadius, _ctx));
				if (canTurn)
					candidates.Add(BuildTurnAroundCandidate(_request, _path, _feedback, _turnRadius, _ctx));
			}

			// Evaluate: check feasibility + score
			FeasibilityResult bestFeasibility = FeasibilityResult.Valid;
			for (int i = 0; i < candidates.Count; i++)
			{
				var c = candidates[i];
				c.Feasibility = m_Feasibility != null
					? m_Feasibility.CheckPlan(c.Plan, _feedback.Geometry, _turnRadius)
					: FeasibilityResult.Valid;
				c.Cost = ScoreCandidate(c, flatToDest, _ctx);
				if (c.Feasibility != null && c.Feasibility.IsValid)
					bestFeasibility = c.Feasibility;
			}

			// Pick best valid
			DrivingCandidate best = null;
			float bestCost = float.MaxValue;
			for (int i = 0; i < candidates.Count; i++)
			{
				var c = candidates[i];
				if (c.Feasibility != null && !c.Feasibility.IsValid)
					continue;
				if (c.Cost < bestCost)
				{
					bestCost = c.Cost;
					best = c;
				}
			}

			if (best == null)
			{
				best = candidates[0];
			}

			string reason = $"mode={best.Mode} cost={best.Cost:F1} candidates={candidates.Count} safe={safeMode}";
			var plan = new DrivingPlan(best.Maneuvers, reason, best.Mode, best.Cost, bestFeasibility);
			plan.BuildSegments();
			return plan;
		}

		private DrivingCandidate BuildForwardCandidate(
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			DriverContext _ctx)
		{
			var maneuvers = new List<Maneuver> { new ForwardManeuver() };
			AppendArrivalManeuver(_request, _path, _feedback, maneuvers);
			return new DrivingCandidate(VehicleDrivingMode.Forward, maneuvers);
		}

		private DrivingCandidate BuildReverseCandidate(
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			float _turnRadius,
			DriverContext _ctx)
		{
			var maneuvers = new List<Maneuver>();
			if (_ctx != null)
			{
				var reversePath = ReversePathBuilder.Build(_ctx.Path, _ctx);
				maneuvers.Add(new ReverseIntentManeuver(reversePath));
			}
			else
			{
				maneuvers.Add(new ReverseManeuver());
			}
			AppendArrivalManeuver(_request, _path, _feedback, maneuvers);
			if (maneuvers.Count == 0)
				maneuvers.Add(new ForwardManeuver());
			return new DrivingCandidate(VehicleDrivingMode.Reverse, maneuvers);
		}

		private DrivingCandidate BuildTurnAroundCandidate(
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			float _turnRadius,
			DriverContext _ctx)
		{
			float sign = ChooseTurnSign(_feedback.Geometry);
			var maneuvers = new List<Maneuver>
			{
				new TurnAroundManeuver(sign),
				new ForwardManeuver()
			};
			AppendArrivalManeuver(_request, _path, _feedback, maneuvers);
			return new DrivingCandidate(VehicleDrivingMode.TurnAround, maneuvers);
		}

		private static float ScoreCandidate(DrivingCandidate _candidate, float _flatToDest, DriverContext _ctx)
		{
			int turns = _candidate.Mode == VehicleDrivingMode.TurnAround ? 1 : 0;
			DriverIntent intent = _candidate.Mode switch
			{
				VehicleDrivingMode.Reverse => DriverIntent.Reverse,
				VehicleDrivingMode.TurnAround => DriverIntent.TurnAround,
				_ => DriverIntent.DriveForward
			};
			return ScoringSystem.ScoreCandidate(intent, _flatToDest, turns, _candidate.Feasibility);
		}

		private sealed class DrivingCandidate
		{
			public readonly VehicleDrivingMode Mode;
			public readonly IReadOnlyList<Maneuver> Maneuvers;
			public FeasibilityResult Feasibility;
			public float Cost;

			public DrivingPlan Plan => new DrivingPlan(Maneuvers, "candidate", Mode, Cost);

			public DrivingCandidate(VehicleDrivingMode _mode, List<Maneuver> _maneuvers)
			{
				Mode = _mode;
				Maneuvers = _maneuvers ?? new List<Maneuver>();
			}
		}

		private static VehicleDrivingMode DecideBaseMode(
			float _firstAngle,
			float _firstSegLen,
			float _flatToDest,
			float _reverseMaxSegment,
			float _reverseRange,
			float _reverseAngleDegrees)
		{
			if (_firstAngle <= c_FrontSectorAngle)
				return VehicleDrivingMode.Forward;

			if (_firstAngle >= Mathf.Min(c_RearSectorAngle, _reverseAngleDegrees * 0.85f) &&
				_firstSegLen <= _reverseMaxSegment &&
				_flatToDest <= _reverseRange)
			{
				return VehicleDrivingMode.Reverse;
			}

			if (_firstAngle > c_FrontSectorAngle && _flatToDest > _reverseMaxSegment * 0.5f)
				return VehicleDrivingMode.TurnAround;

			return VehicleDrivingMode.Forward;
		}

		private static void BuildManeuverSequence(
			VehicleDrivingMode _mode,
			FeedbackState _feedback,
			List<Maneuver> _maneuvers,
			float _turnRadius,
			DriverContext _ctx = null,
			NavigationRequest _request = default)
		{
			switch (_mode)
			{
				case VehicleDrivingMode.Reverse:
					if (_ctx != null)
					{
						var reversePath = ReversePathBuilder.Build(_ctx.Path, _ctx);
						_maneuvers.Add(new ReverseIntentManeuver(reversePath));
					}
					else
					{
						_maneuvers.Add(new ReverseManeuver());
					}
					break;

				case VehicleDrivingMode.TurnAround:
					float sign = ChooseTurnSign(_feedback.Geometry);
					_maneuvers.Add(new TurnAroundManeuver(sign));
					_maneuvers.Add(new ForwardManeuver());
					break;

				default:
					_maneuvers.Add(new ForwardManeuver());
					break;
			}
		}

		private static float ChooseTurnSign(VehicleLocalGeometry.Sample _geometry)
		{
			float sign = _geometry.PreferredTurnSign;
			if (Mathf.Abs(sign) < 0.1f)
				sign = _geometry.LeftClearance >= _geometry.RightClearance ? -1f : 1f;

			bool leftOk = _geometry.LeftClearance >= 3f;
			bool rightOk = _geometry.RightClearance >= 3f;
			if (sign < 0f && !leftOk && rightOk)
				return 1f;
			if (sign > 0f && !rightOk && leftOk)
				return -1f;

			return sign;
		}

		private void AppendArrivalManeuver(
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			List<Maneuver> _maneuvers)
		{
			Vector3 forward = FlatDir(_feedback.Forward);
			float currentYaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;

			// Try precision arrival planner first
			if (m_ArrivalPlanner != null)
			{
				float? heading = _request.HasHeading ? _request.HeadingYaw : (float?)null;
				var arrivalManeuvers = m_ArrivalPlanner.PlanArrival(
					_feedback.Position, currentYaw, _request.Destination, heading);
				if (arrivalManeuvers != null && arrivalManeuvers.Count > 0)
				{
					_maneuvers.AddRange(arrivalManeuvers);
					return;
				}
			}

			switch (_request.FacingMode)
			{
				case ArrivalFacingMode.FaceHeading when _request.HasHeading:
					_maneuvers.Add(new ApproachWithHeadingManeuver(
						_request.Destination,
						_request.HeadingYaw.Value));
					return;

				case ArrivalFacingMode.UsePathFacing:
					Vector3 pathDir = GetPathFinalDirection(_path, _feedback.Position);
					_maneuvers.Add(new ParkingManeuver(
						Quaternion.LookRotation(pathDir, Vector3.up).eulerAngles.y));
					return;

				case ArrivalFacingMode.KeepCurrent:
					return;

				case ArrivalFacingMode.None:
				default:
					if (_request.HasHeading)
					{
						float delta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, _request.HeadingYaw.Value));
						if (delta > 18f)
							_maneuvers.Add(new ParkingManeuver(_request.HeadingYaw.Value));
					}
					return;
			}
		}

		private static Vector3 GetPathFinalDirection(PathResult _path, Vector3 _position)
		{
			if (_path.Corners == null || _path.Corners.Length < 2)
				return Vector3.forward;
			Vector3 last = _path.Corners[_path.Corners.Length - 1];
			Vector3 prev = _path.Corners[_path.Corners.Length - 2];
			Vector3 dir = last - prev;
			dir.y = 0f;
			return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
		}

		private static Vector3 FlatDir(Vector3 _v)
		{
			_v.y = 0f;
			if (_v.sqrMagnitude < 0.0001f)
				return Vector3.forward;
			return _v.normalized;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
