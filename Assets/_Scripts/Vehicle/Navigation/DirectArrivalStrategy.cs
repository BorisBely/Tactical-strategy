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
			if (_a.Distance < 0.2f)
				return ArrivalPlan.AtGoalPlan();

			if (_a.Side != TargetSide.Front && _a.Side != TargetSide.Rear)
				return ArrivalPlan.Invalid("target not in front/rear hemisphere");

			bool canForward = _a.CanReachForward && _a.Side == TargetSide.Front;
			bool canReverse = _a.CanReachReverse && _a.Side == TargetSide.Rear;
			if (!canForward && !canReverse)
				return ArrivalPlan.Invalid("target inside turning circle");

			if (Mathf.Abs(_a.HeadingError) > 45f)
				return ArrivalPlan.Invalid("heading error too large for direct");
			if (_a.LateralOffset > Mathf.Max(1.5f, _s.TurnRadius * 0.25f))
				return ArrivalPlan.Invalid("lateral offset too large for direct");

			var maneuvers = new List<Maneuver>();
			float targetYaw = _heading ?? _yaw;
			if (_heading.HasValue)
				maneuvers.Add(new ApproachWithHeadingManeuver(_target, targetYaw));
			else
				maneuvers.Add(new ParkingManeuver(targetYaw));

			float cost = _a.Distance * 1f + Mathf.Abs(_a.HeadingError) * 0.2f;
			return new ArrivalPlan(maneuvers, cost, Name) { PreferredSide = _a.Side };
		}
	}
}
