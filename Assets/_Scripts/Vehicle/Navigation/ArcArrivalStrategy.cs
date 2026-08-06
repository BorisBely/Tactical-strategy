using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Smooth arc approach using a biarc/staging path instead of a full U-turn.
	/// </summary>
	public sealed class ArcArrivalStrategy : IArrivalStrategy
	{
		public string Name => "Arc";

		public ArrivalPlan Generate(ArrivalAnalysis _a, ArrivalPlanningSettings _s,
			Vector3 _pos, float _yaw, Vector3 _target, float? _heading)
		{
			float absAngle = Mathf.Abs(_a.HeadingError);
			if (absAngle < 15f || absAngle > 120f)
				return ArrivalPlan.Invalid("angle out of arc range");
			if (_a.LateralOffset < _s.SideOffsetThreshold * 0.5f)
				return ArrivalPlan.Invalid("too straight for arc");
			if (_a.Distance < _s.ArcMinDistance)
				return ArrivalPlan.Invalid($"too close for arc (min {_s.ArcMinDistance:F1}m)");

			float targetYaw = _heading ?? Quaternion.LookRotation((_target - _pos).normalized, Vector3.up).eulerAngles.y;
			var arc = ReedsSheppPlanner.PlanForwardArc(
				new ReedsSheppPlanner.Pose(_pos, _yaw),
				new ReedsSheppPlanner.Pose(_target, targetYaw),
				_s.TurnRadius);

			var maneuvers = new List<Maneuver>();
			var approach = new ApproachWithHeadingManeuver(_target, targetYaw);
			approach.SetWaypoints(arc);
			maneuvers.Add(approach);

			float cost = _a.Distance * 1.1f + absAngle * 0.15f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
