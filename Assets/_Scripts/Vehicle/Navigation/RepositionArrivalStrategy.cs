using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class RepositionArrivalStrategy : IArrivalStrategy
	{
		public string Name => "Reposition";

		public ArrivalPlan Generate(ArrivalAnalysis _a, ArrivalPlanningSettings _s,
			Vector3 _pos, float _yaw, Vector3 _target, float? _heading)
		{
			if (_a.TargetInsideTurningCircle && _a.TargetInFront)
				return ArrivalPlan.Invalid("target too close in front");

			float step = Mathf.Max(_s.RepositionStep, _a.LateralOffset + 0.5f);
			Vector3 backDir = Quaternion.Euler(0f, _yaw + 180f, 0f) * Vector3.forward;
			Vector3 prePos = _pos + backDir * step;

			var maneuvers = new List<Maneuver>();
			var rev = new ReverseManeuver();
			rev.SetWaypoints(new[] { _pos, prePos });
			maneuvers.Add(rev);

			var arrival = _heading.HasValue
				? (Maneuver)new ApproachWithHeadingManeuver(_target, _heading.Value)
				: new ParkingManeuver(_heading ?? _yaw);
			maneuvers.Add(arrival);

			float cost = _a.Distance * 1.5f + 12f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
