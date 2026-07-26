using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class DirectArrivalStrategy : IArrivalStrategy
	{
		public string Name => "Direct";

		public ArrivalPlan Generate(ArrivalAnalysis _a, ArrivalPlanningSettings _s,
			Vector3 _pos, float _yaw, Vector3 _target, float? _heading)
		{
			if (!_a.TargetInFront || _a.Distance < 0.2f)
				return ArrivalPlan.Invalid("not in front");

			var maneuvers = new List<Maneuver>();
			if (_heading.HasValue)
				maneuvers.Add(new ApproachWithHeadingManeuver(_target, _heading.Value));
			else
				maneuvers.Add(new ParkingManeuver(_heading ?? _yaw));

			float cost = _a.Distance * 1f + Mathf.Abs(_a.HeadingError) * 0.2f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
