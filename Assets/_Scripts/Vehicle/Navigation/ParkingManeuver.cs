using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ParkingManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Parking;

		public float TargetHeadingYaw { get; }

		public ParkingManeuver(float _targetHeadingYaw)
		{
			TargetHeadingYaw = _targetHeadingYaw;
			AllowReverse = true;
			SpeedScale = 0.28f;
			LookAheadOverride = 1.6f;
			IsArrivalManeuver = true;
		}

		public override bool IsComplete(ManeuverContext _ctx)
		{
			if (Waypoints == null || Waypoints.Count == 0)
				return true;

			Vector3 last = Waypoints[Waypoints.Count - 1];
			float dist = FlatDistance(_ctx.Position, last);
			float headingErr = Mathf.Abs(Mathf.DeltaAngle(
				Quaternion.LookRotation(_ctx.Forward, Vector3.up).eulerAngles.y,
				TargetHeadingYaw));
			return dist <= 0.5f && headingErr <= 5f && _ctx.SpeedKmh <= 1.5f;
		}
	}
}
