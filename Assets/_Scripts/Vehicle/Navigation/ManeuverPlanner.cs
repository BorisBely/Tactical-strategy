using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Generates concrete waypoint sequences for each Maneuver in a DrivingPlan.
	/// Keeps *how* (DrivingPlanner) and *where* (ManeuverPlanner) separate.
	/// </summary>
	public sealed class ManeuverPlanner
	{
		private readonly PathSmoother m_Smoother;

		public ManeuverPlanner(PathSmoother _smoother)
		{
			m_Smoother = _smoother;
		}

		public void BuildWaypoints(
			DrivingPlan _plan,
			NavigationRequest _request,
			PathResult _path,
			FeedbackState _feedback,
			float _minRadius,
			float _vehicleLength)
		{
			if (_plan.Maneuvers == null || _plan.Maneuvers.Count == 0)
				return;

			List<Vector3> corners = ExtractPathCorners(_path, _feedback);
			float cornerCut = Mathf.Clamp(_minRadius * 0.08f, 0.15f, 0.35f);
			Vector3[] forwardPath = m_Smoother.SmoothCorners(corners.ToArray(), cornerCut);
			forwardPath = TrimPassedCorners(forwardPath, _feedback.Position);

			for (int i = 0; i < _plan.Maneuvers.Count; i++)
			{
				Maneuver m = _plan.Maneuvers[i];
				bool isLast = i == _plan.Maneuvers.Count - 1;

				if (m is ReverseIntentManeuver)
					continue;

				switch (m.Type)
				{
					case VehicleManeuverType.Forward:
						m.SetWaypoints(forwardPath);
						break;

					case VehicleManeuverType.Reverse:
						m.SetWaypoints(BuildReverseWaypoints(_feedback, corners, isLast));
						break;

					case VehicleManeuverType.TurnAround:
						TurnAroundManeuver turn = (TurnAroundManeuver)m;
						m.SetWaypoints(m_Smoother.GenerateTurnaroundTrajectory(
							_feedback.Position,
							_feedback.Yaw,
							turn.TurnSign,
							_minRadius,
							_vehicleLength));
						break;

					case VehicleManeuverType.ThreePointTurn:
						ThreePointTurnManeuver three = (ThreePointTurnManeuver)m;
						m.SetWaypoints(m_Smoother.GenerateThreePointWaypoints(
							_feedback.Position,
							_feedback.Yaw,
							three.TurnSign,
							_minRadius,
							_feedback.Geometry));
						break;

				case VehicleManeuverType.Parking:
					ParkingManeuver park = (ParkingManeuver)m;
					m.SetWaypoints(ReedsSheppPlanner.PlanStagingApproach(
						new ReedsSheppPlanner.Pose(_feedback.Position, _feedback.Yaw),
						new ReedsSheppPlanner.Pose(_request.Destination, park.TargetHeadingYaw),
						_minRadius));
					break;

				case VehicleManeuverType.ApproachWithHeading:
					ApproachWithHeadingManeuver approach = (ApproachWithHeadingManeuver)m;
					m.SetWaypoints(ReedsSheppPlanner.PlanStagingApproach(
						new ReedsSheppPlanner.Pose(_feedback.Position, _feedback.Yaw),
						new ReedsSheppPlanner.Pose(approach.Destination, approach.TargetHeadingYaw),
						_minRadius));
					break;

					case VehicleManeuverType.Unstuck:
						UnstuckManeuver unstuck = (UnstuckManeuver)m;
						m.SetWaypoints(m_Smoother.GenerateUnstuckWaypoints(
							_feedback.Position,
							_feedback.Yaw,
							unstuck.SteerSign,
							_minRadius));
						break;

					case VehicleManeuverType.Stop:
						m.SetWaypoints(new[] { _feedback.Position });
						break;

					case VehicleManeuverType.PostTurnAlignment:
						m.SetWaypoints(new[] { _feedback.Position, _request.Destination });
						break;
				}
			}
		}

		private static List<Vector3> ExtractPathCorners(PathResult _path, FeedbackState _feedback)
		{
			List<Vector3> corners = new List<Vector3>();
			if (_path.Corners != null && _path.Corners.Length > 0)
			{
				for (int i = 0; i < _path.Corners.Length; i++)
					corners.Add(_path.Corners[i]);
			}
			else
			{
				corners.Add(_feedback.Position);
			}

			return corners;
		}

		private static Vector3[] TrimPassedCorners(Vector3[] _corners, Vector3 _position)
		{
			if (_corners == null || _corners.Length <= 2)
				return _corners;

			int best = 0;
			float bestDist = float.MaxValue;
			for (int i = 0; i < _corners.Length; i++)
			{
				float d = FlatDistance(_position, _corners[i]);
				if (d < bestDist)
				{
					bestDist = d;
					best = i;
				}
			}

			if (best <= 0)
				return _corners;

			var trimmed = new Vector3[_corners.Length - best + 1];
			trimmed[0] = _position;
			for (int i = 1; i < trimmed.Length; i++)
				trimmed[i] = _corners[best + i - 1];
			return trimmed;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}

		private static Vector3[] BuildReverseWaypoints(
			FeedbackState _feedback,
			List<Vector3> _corners,
			bool _isLast)
		{
			if (_corners == null || _corners.Count == 0)
				return new[] { _feedback.Position };

			// Reverse simply aims at the first meaningful corner.
			Vector3 target = _corners.Count > 1 ? _corners[1] : _corners[0];
			return new[] { _feedback.Position, target };
		}
	}
}
