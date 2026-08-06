using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Foundation for tight-space navigation: wraps Reeds-Shepp candidates and future Hybrid A*.
	/// </summary>
	public sealed class TightSpacePlanner
	{
		private readonly float m_TurnRadius;

		public TightSpacePlanner(float _turnRadius)
		{
			m_TurnRadius = Mathf.Max(1f, _turnRadius);
		}

		public List<Maneuver> PlanParkingPose(
			Vector3 _position, float _yaw,
			Vector3 _target, float _targetYaw)
		{
			var maneuvers = new List<Maneuver>();
			var arc = ReedsSheppPlanner.PlanStagingApproach(
				new ReedsSheppPlanner.Pose(_position, _yaw),
				new ReedsSheppPlanner.Pose(_target, _targetYaw),
				m_TurnRadius);

			var approach = new ApproachWithHeadingManeuver(_target, _targetYaw);
			approach.SetWaypoints(arc);
			maneuvers.Add(approach);
			return maneuvers;
		}

		public bool CanFitCorridor(float _width, float _vehicleWidth, float _margin = 0.3f)
		{
			return _width >= _vehicleWidth + _margin * 2f;
		}
	}
}
