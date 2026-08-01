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
			// Already at target — terminal, not error. Flag AtGoal to abort further planning.
			if (_a.Distance < 0.2f)
				return ArrivalPlan.AtGoalPlan();

			// Strictly front-hemisphere only, with tight heading/lateral bounds
			if (_a.Side != TargetSide.Front)
				return ArrivalPlan.Invalid("target not in front hemisphere");
			if (Mathf.Abs(_a.HeadingError) > 45f)
				return ArrivalPlan.Invalid("heading error too large for direct");
			if (_a.LateralOffset > 1.5f)
				return ArrivalPlan.Invalid("lateral offset too large for direct");

			var maneuvers = new List<Maneuver>();
			if (_heading.HasValue)
				maneuvers.Add(new ApproachWithHeadingManeuver(_target, _heading.Value));
			else
				maneuvers.Add(new ParkingManeuver(_heading ?? _yaw));

			float cost = _a.Distance * 1f + Mathf.Abs(_a.HeadingError) * 0.2f;
			return new ArrivalPlan(maneuvers, cost, Name) { PreferredSide = TargetSide.Front };
		}
	}
}
