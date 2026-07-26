using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ReverseArrivalStrategy : IArrivalStrategy
	{
		public string Name => "Reverse";

		public ArrivalPlan Generate(ArrivalAnalysis _a, ArrivalPlanningSettings _s,
			Vector3 _pos, float _yaw, Vector3 _target, float? _heading)
		{
			float absAngle = Mathf.Abs(_a.HeadingError);

			// Only for rear hemisphere
			if (_a.Side == TargetSide.Front)
				return ArrivalPlan.Invalid("target is in front");
			if (_a.Side == TargetSide.Left || _a.Side == TargetSide.Right)
				return ArrivalPlan.Invalid("side target — use Arc/Reposition");

			var maneuvers = new List<Maneuver>();
			if (_a.TargetInsideRearTurningCircle)
			{
				Vector3 preGoalPos = _target + Quaternion.Euler(0f, _heading ?? (_yaw + 180f), 0f) * Vector3.forward * _s.PreGoalDistance;
				var rev = new ReverseManeuver();
				var segs = new List<Vector3> { _pos, preGoalPos, _target };
				rev.SetWaypoints(segs.ToArray());
				maneuvers.Add(rev);
			}
			else
			{
				maneuvers.Add(new ReverseManeuver());
			}

			if (_heading.HasValue)
				maneuvers.Add(new ApproachWithHeadingManeuver(_target, _heading.Value));
			else
				maneuvers.Add(new ParkingManeuver(_heading ?? _yaw));

			float cost = _a.Distance * 1.3f + 8f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
