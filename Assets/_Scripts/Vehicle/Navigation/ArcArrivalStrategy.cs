using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Smooth arc approach: one continuous turn into the target.
	/// Best when target is off-center but not behind.
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

			var maneuvers = new List<Maneuver>();
			float sign = _a.HeadingError > 0f ? 1f : -1f;
			maneuvers.Add(new TurnAroundManeuver(sign));
			if (_heading.HasValue)
				maneuvers.Add(new ApproachWithHeadingManeuver(_target, _heading.Value));
			else
				maneuvers.Add(new ParkingManeuver(_heading ?? _yaw));

			float cost = _a.Distance * 1.1f + absAngle * 0.15f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
