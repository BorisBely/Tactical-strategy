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
			Vector3 rightDir = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

			float sideShift = (_a.Side == TargetSide.Left) ? _s.RepositionSideFactor * step
			                : (_a.Side == TargetSide.Right) ? -_s.RepositionSideFactor * step : 0f;
			Vector3 prePos = _pos + backDir * step + rightDir * sideShift;

			float approachHeading = _heading
				?? Quaternion.LookRotation((_target - prePos).normalized, Vector3.up).eulerAngles.y;

			var maneuvers = new List<Maneuver>();
			var tmpCtx = new DriverContext();
			tmpCtx.Position = _pos;
			tmpCtx.Forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
			tmpCtx.Right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
			tmpCtx.Yaw = _yaw;
			tmpCtx.Path = new PathResult(new[] { _pos, prePos, _target }, step * 2f, true, false);

			var reversePath = ReversePathBuilder.Build(tmpCtx.Path, tmpCtx);
			maneuvers.Add(new ReverseIntentManeuver(reversePath));
			maneuvers.Add(new ApproachWithHeadingManeuver(_target, approachHeading));

			float cost = _a.Distance * 1.5f + 12f;
			return new ArrivalPlan(maneuvers, cost, Name);
		}
	}
}
