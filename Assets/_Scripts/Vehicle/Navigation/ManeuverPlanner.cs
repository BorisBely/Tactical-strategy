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

			for (int i = 0; i < _plan.Maneuvers.Count; i++)
			{
				Maneuver m = _plan.Maneuvers[i];
				bool isLast = i == _plan.Maneuvers.Count - 1;

				if (m is ReverseIntentManeuver)
					continue;

				switch (m.Type)
				{
					case VehicleManeuverType.Forward:
						m.SetWaypoints(corners.ToArray());
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
					m.SetWaypoints(m_Smoother.GenerateParkingWaypoints(
						_feedback.Position,
						_feedback.Yaw,
						_request.Destination,
						park.TargetHeadingYaw,
						_minRadius));
					break;

				case VehicleManeuverType.ApproachWithHeading:
					ApproachWithHeadingManeuver approach = (ApproachWithHeadingManeuver)m;
					m.SetWaypoints(m_Smoother.GenerateApproachWithHeadingArc(
						_feedback.Position,
						_feedback.Yaw,
						approach.Destination,
						approach.TargetHeadingYaw,
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
