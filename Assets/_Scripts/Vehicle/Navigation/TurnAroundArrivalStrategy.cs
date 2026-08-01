using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class TurnAroundArrivalStrategy : IArrivalStrategy
	{
		public string Name => "TurnAround";

		public ArrivalPlan Generate(ArrivalAnalysis _a, ArrivalPlanningSettings _s,
			Vector3 _pos, float _yaw, Vector3 _target, float? _heading)
		{
			if (!_a.TargetInsideTurningCircle && _a.TargetInFront)
				return ArrivalPlan.Invalid("target reachable forward");

			var maneuvers = new List<Maneuver>();
			float sign = _a.HeadingError > 0f ? 1f : -1f;
			maneuvers.Add(new TurnAroundManeuver(sign));

			// Short alignment only if there's distance to cover after turn
			if (_a.Distance > 4f)
				maneuvers.Add(new PostTurnAlignmentManeuver());

			if (_heading.HasValue)
				maneuvers.Add(new ApproachWithHeadingManeuver(_target, _heading.Value));
			else
				maneuvers.Add(new ParkingManeuver(_heading ?? _yaw));

			float cost = _a.Distance * 1.8f + 20f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
